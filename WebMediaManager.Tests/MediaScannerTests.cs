using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using WebMediaManager.Core.Domain;
using WebMediaManager.Core.Nfo;
using WebMediaManager.Core.Providers;
using WebMediaManager.Data;
using WebMediaManager.Jobs;
using WebMediaManager.Services.Scanning;

namespace WebMediaManager.Tests;

/// <summary>
/// Exercises the scanner against a real temp folder tree and in-memory SQLite, covering season grouping
/// and multi-episode file expansion — the parts most likely to regress.
/// </summary>
public sealed class MediaScannerTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly TestContextFactory _factory;
    private readonly string _root;

    public MediaScannerTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<MediaDbContext>().UseSqlite(_connection).Options;
        using (var db = new MediaDbContext(options))
        {
            db.Database.EnsureCreated();
        }
        _factory = new TestContextFactory(options);
        _root = Path.Combine(Path.GetTempPath(), "wmm-test-" + Guid.NewGuid().ToString("N"));
    }

    [Fact]
    public async Task Scans_movie_folders_into_unmatched_movies()
    {
        var dir = Path.Combine(_root, "Inception (2010)");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "Inception (2010).mkv"), "x");

        var libraryId = await SeedLibraryAsync(LibraryType.Movies);
        await new MediaScanner(_factory, new NoopProgress(), new NfoReader(), NullLogger<MediaScanner>.Instance).ScanAsync(libraryId, reimport: false, CancellationToken.None);

        await using var db = _factory.CreateDbContext();
        var movie = await db.Movies.SingleAsync();
        Assert.Equal("Inception", movie.Title);
        Assert.Equal(2010, movie.Year);
        Assert.Equal(MatchState.Unmatched, movie.MatchState);
        Assert.Equal(Path.Combine("Inception (2010)", "Inception (2010).mkv"), movie.VideoFilePath);
    }

    [Fact]
    public async Task Scans_tv_show_grouping_seasons_and_expanding_multi_episode_files()
    {
        var firefly = Path.Combine(_root, "Firefly", "Season 01");
        Directory.CreateDirectory(firefly);
        File.WriteAllText(Path.Combine(firefly, "Firefly 1x01.mkv"), "");
        File.WriteAllText(Path.Combine(firefly, "Firefly S01E02E03.mkv"), "");   // multi-episode

        var libraryId = await SeedLibraryAsync(LibraryType.TvShows);
        await new MediaScanner(_factory, new NoopProgress(), new NfoReader(), NullLogger<MediaScanner>.Instance).ScanAsync(libraryId, reimport: false, CancellationToken.None);

        await using var db = _factory.CreateDbContext();
        var show = await db.TvShows.SingleAsync();
        Assert.Equal("Firefly", show.Title);

        var episodes = await db.Episodes
            .Join(db.Seasons, e => e.SeasonId, s => s.Id, (e, s) => new { e, s.TvShowId })
            .Where(x => x.TvShowId == show.Id)
            .Select(x => x.e)
            .ToListAsync();

        // 1x01 -> S1E1; S01E02E03 -> S1E2 + S1E3
        Assert.Equal(3, episodes.Count);
        Assert.Contains(episodes, e => e.SeasonNumber == 1 && e.EpisodeNumber == 1);
        Assert.Contains(episodes, e => e.SeasonNumber == 1 && e.EpisodeNumber == 2);
        Assert.Contains(episodes, e => e.SeasonNumber == 1 && e.EpisodeNumber == 3);
        Assert.Equal(1, await db.Seasons.CountAsync(s => s.TvShowId == show.Id));
    }

    [Fact]
    public async Task Tv_seasons_derive_from_folder_when_filenames_lack_a_marker()
    {
        // Reproduces the reported Happy Tree Friends case: Season 03's files are named "NN - Title.mp4"
        // with no SxxExx marker, so the season must come from the folder or it's silently dropped.
        var show = Path.Combine(_root, "Happy Tree Friends (1999)");
        var marked = Path.Combine(show, "Season 02");
        var bare = Path.Combine(show, "Season 03");
        Directory.CreateDirectory(marked);
        Directory.CreateDirectory(bare);
        File.WriteAllText(Path.Combine(marked, "Happy Tree Friends - S02E01 - Eye Candy.mp4"), "");
        File.WriteAllText(Path.Combine(bare, "01 - The Wrong Side of the Tracks.mp4"), "");
        File.WriteAllText(Path.Combine(bare, "02 - From Hero to Eternity.mp4"), "");

        var libraryId = await SeedLibraryAsync(LibraryType.TvShows);
        await new MediaScanner(_factory, new NoopProgress(), new NfoReader(), NullLogger<MediaScanner>.Instance)
            .ScanAsync(libraryId, reimport: false, CancellationToken.None);

        await using var db = _factory.CreateDbContext();
        var tvShow = await db.TvShows.SingleAsync();

        var seasons = await db.Seasons.Where(s => s.TvShowId == tvShow.Id).Select(s => s.SeasonNumber).ToListAsync();
        Assert.Equal([2, 3], seasons.OrderBy(n => n));

        var s3 = await db.Episodes
            .Join(db.Seasons, e => e.SeasonId, s => s.Id, (e, s) => new { e, s.TvShowId, s.SeasonNumber })
            .Where(x => x.TvShowId == tvShow.Id && x.SeasonNumber == 3)
            .Select(x => x.e.EpisodeNumber)
            .ToListAsync();
        Assert.Equal([1, 2], s3.OrderBy(n => n));
    }

    [Fact]
    public async Task Reimport_adds_a_newly_parseable_season_to_an_existing_show_without_duplicating()
    {
        // The show is already in the DB with Season 02 only. After Season 03's files are renamed into a
        // parseable form on disk, a reimport should pull that season in — not require removing the show.
        var show = Path.Combine(_root, "Happy Tree Friends (1999)");
        var s2 = Path.Combine(show, "Season 02");
        Directory.CreateDirectory(s2);
        File.WriteAllText(Path.Combine(s2, "Happy Tree Friends - S02E01 - Eye Candy.mp4"), "");

        var libraryId = await SeedLibraryAsync(LibraryType.TvShows);
        var scanner = new MediaScanner(_factory, new NoopProgress(), new NfoReader(), NullLogger<MediaScanner>.Instance);
        await scanner.ScanAsync(libraryId, reimport: false, CancellationToken.None);

        await using (var db = _factory.CreateDbContext())
        {
            Assert.Equal(1, await db.Seasons.CountAsync());
        }

        // Season 03 appears with bare-numbered files (no SxxExx marker).
        var s3 = Path.Combine(show, "Season 03");
        Directory.CreateDirectory(s3);
        File.WriteAllText(Path.Combine(s3, "01 - The Wrong Side of the Tracks.mp4"), "");
        File.WriteAllText(Path.Combine(s3, "02 - From Hero to Eternity.mp4"), "");

        await scanner.ScanAsync(libraryId, reimport: true, CancellationToken.None);

        await using (var db = _factory.CreateDbContext())
        {
            var tvShow = await db.TvShows.SingleAsync();
            var seasons = await db.Seasons.Where(s => s.TvShowId == tvShow.Id)
                .Select(s => s.SeasonNumber).ToListAsync();
            Assert.Equal([2, 3], seasons.OrderBy(n => n));            // season 3 pulled in, season 2 not duplicated
            Assert.Equal(3, await db.Episodes.CountAsync());          // S02E01 + S03E01 + S03E02

            // A second reimport with no on-disk changes must not duplicate the episodes it just added.
            await scanner.ScanAsync(libraryId, reimport: true, CancellationToken.None);
        }

        await using (var db = _factory.CreateDbContext())
        {
            Assert.Equal(3, await db.Episodes.CountAsync());
            Assert.Equal(2, await db.Seasons.CountAsync());
        }
    }

    [Fact]
    public async Task Plain_rescan_picks_up_a_newly_downloaded_episode_of_an_existing_show()
    {
        // The reported case: a season is partly downloaded, then a new episode lands. A plain scan (no
        // reimport) must pull the new episode into the existing show without duplicating what's there.
        var seasonDir = Path.Combine(_root, "Firefly", "Season 01");
        Directory.CreateDirectory(seasonDir);
        File.WriteAllText(Path.Combine(seasonDir, "Firefly S01E01.mkv"), "");

        var libraryId = await SeedLibraryAsync(LibraryType.TvShows);
        var scanner = new MediaScanner(_factory, new NoopProgress(), new NfoReader(), NullLogger<MediaScanner>.Instance);
        await scanner.ScanAsync(libraryId, reimport: false, CancellationToken.None);

        await using (var db = _factory.CreateDbContext())
        {
            Assert.Equal(1, await db.Episodes.CountAsync());
        }

        // A new episode is downloaded into the same season.
        File.WriteAllText(Path.Combine(seasonDir, "Firefly S01E02.mkv"), "");
        await scanner.ScanAsync(libraryId, reimport: false, CancellationToken.None);

        await using (var db = _factory.CreateDbContext())
        {
            Assert.Equal(1, await db.TvShows.CountAsync());   // not duplicated
            Assert.Equal(1, await db.Seasons.CountAsync());
            var episodes = await db.Episodes.Select(e => e.EpisodeNumber).OrderBy(n => n).ToListAsync();
            Assert.Equal([1, 2], episodes);                   // the new episode was picked up

            // A third scan with no on-disk change must not duplicate.
            await scanner.ScanAsync(libraryId, reimport: false, CancellationToken.None);
        }

        await using (var db = _factory.CreateDbContext())
        {
            Assert.Equal(2, await db.Episodes.CountAsync());
        }
    }

    [Fact]
    public async Task Plain_rescan_picks_up_a_whole_new_season_of_an_existing_show()
    {
        var s1 = Path.Combine(_root, "Firefly", "Season 01");
        Directory.CreateDirectory(s1);
        File.WriteAllText(Path.Combine(s1, "Firefly S01E01.mkv"), "");

        var libraryId = await SeedLibraryAsync(LibraryType.TvShows);
        var scanner = new MediaScanner(_factory, new NoopProgress(), new NfoReader(), NullLogger<MediaScanner>.Instance);
        await scanner.ScanAsync(libraryId, reimport: false, CancellationToken.None);

        var s2 = Path.Combine(_root, "Firefly", "Season 02");
        Directory.CreateDirectory(s2);
        File.WriteAllText(Path.Combine(s2, "Firefly S02E01.mkv"), "");
        await scanner.ScanAsync(libraryId, reimport: false, CancellationToken.None);

        await using var db = _factory.CreateDbContext();
        var tvShow = await db.TvShows.SingleAsync();
        var seasons = await db.Seasons.Where(s => s.TvShowId == tvShow.Id).Select(s => s.SeasonNumber).ToListAsync();
        Assert.Equal([1, 2], seasons.OrderBy(n => n));
    }

    [Fact]
    public async Task Scan_reports_incremental_progress_for_a_show_with_many_episodes()
    {
        // A show with far more episodes than the report interval must publish steady running updates while
        // it works, rather than going silent until the whole show is done (the "looks frozen" complaint).
        var seasonDir = Path.Combine(_root, "Big Show", "Season 01");
        Directory.CreateDirectory(seasonDir);
        for (var i = 1; i <= 60; i++)
        {
            File.WriteAllText(Path.Combine(seasonDir, $"Big Show - S01E{i:D2}.mkv"), "");
        }

        var capture = new CapturingProgress();
        var libraryId = await SeedLibraryAsync(LibraryType.TvShows);
        await new MediaScanner(_factory, capture, new NfoReader(), NullLogger<MediaScanner>.Instance)
            .ScanAsync(libraryId, reimport: false, CancellationToken.None);

        var running = capture.Snapshots.Where(p => p.State == ScanState.Running && p.Current is not null).ToList();
        Assert.True(running.Count >= 3, $"expected several running updates, got {running.Count}");
        Assert.Contains(running, p => p.Current!.Contains("imported"));   // mid-show, not just the final report
    }

    [Fact]
    public async Task Rescan_does_not_duplicate_items()
    {
        var dir = Path.Combine(_root, "The Matrix (1999)");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "The Matrix (1999).mkv"), "x");

        var libraryId = await SeedLibraryAsync(LibraryType.Movies);
        var scanner = new MediaScanner(_factory, new NoopProgress(), new NfoReader(), NullLogger<MediaScanner>.Instance);
        await scanner.ScanAsync(libraryId, reimport: false, CancellationToken.None);
        await scanner.ScanAsync(libraryId, reimport: false, CancellationToken.None);

        await using var db = _factory.CreateDbContext();
        Assert.Equal(1, await db.Movies.CountAsync());
    }

    [Fact]
    public async Task Imports_existing_movie_nfo_and_poster()
    {
        var dir = Path.Combine(_root, "Emilia Perez (2024)");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "Emilia Perez (2024).mkv"), "x");
        File.WriteAllText(Path.Combine(dir, "poster.jpg"), "img");
        File.WriteAllText(Path.Combine(dir, "movie.nfo"),
            """
            <movie>
              <title>Emilia Pérez</title>
              <year>2024</year>
              <uniqueid type="tmdb" default="true">974950</uniqueid>
              <imdbid>tt20221436</imdbid>
            </movie>
            """);

        var libraryId = await SeedLibraryAsync(LibraryType.Movies);
        await new MediaScanner(_factory, new NoopProgress(), new NfoReader(), NullLogger<MediaScanner>.Instance)
            .ScanAsync(libraryId, reimport: false, CancellationToken.None);

        await using var db = _factory.CreateDbContext();
        var movie = await db.Movies.SingleAsync();
        Assert.Equal("Emilia Pérez", movie.Title);          // title comes from the NFO, not the folder name
        Assert.Equal(MatchState.Matched, movie.MatchState); // NFO present => matched externally
        Assert.True(movie.HasNfo);
        Assert.Equal("974950", movie.TmdbId);
        Assert.Equal(MetadataSource.Tmdb, movie.PrimaryProvider);

        var poster = await db.Artworks.SingleAsync(a => a.MediaItemId == movie.Id && a.Kind == ArtworkKind.Poster);
        Assert.Equal("img", await File.ReadAllTextAsync(Path.Combine(_root, poster.LocalRelativePath!)));
        Assert.NotNull(poster.DownloadedUtc);
    }

    [Fact]
    public async Task Movie_folder_with_multiple_versions_yields_one_movie_using_the_largest_file()
    {
        var dir = Path.Combine(_root, "Blade Runner 2049 (2017)");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "Blade Runner 2049 (2017) - HD.mkv"), new string('x', 1_000));
        File.WriteAllText(Path.Combine(dir, "Blade Runner 2049 (2017) - UHD.mkv"), new string('x', 5_000)); // larger

        var libraryId = await SeedLibraryAsync(LibraryType.Movies);
        await new MediaScanner(_factory, new NoopProgress(), new NfoReader(), NullLogger<MediaScanner>.Instance)
            .ScanAsync(libraryId, reimport: false, CancellationToken.None);

        await using var db = _factory.CreateDbContext();
        var movie = await db.Movies.SingleAsync();                 // a single entry for the folder
        Assert.EndsWith("UHD.mkv", movie.VideoFilePath);           // largest file chosen as the primary
    }

    [Fact]
    public async Task Reimport_reads_new_nfo_and_artwork_into_existing_item_without_duplicating()
    {
        var dir = Path.Combine(_root, "Dune (2021)");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "Dune (2021).mkv"), "x");

        var libraryId = await SeedLibraryAsync(LibraryType.Movies);
        var scanner = new MediaScanner(_factory, new NoopProgress(), new NfoReader(), NullLogger<MediaScanner>.Instance);

        // First scan: no NFO yet, so it stays Unmatched.
        await scanner.ScanAsync(libraryId, reimport: false, CancellationToken.None);
        await using (var db = _factory.CreateDbContext())
        {
            var m = await db.Movies.SingleAsync();
            Assert.Equal(MatchState.Unmatched, m.MatchState);
            Assert.False(m.HasNfo);
        }

        // Organized externally afterwards: NFO + poster appear on disk.
        File.WriteAllText(Path.Combine(dir, "poster.jpg"), "img");
        File.WriteAllText(Path.Combine(dir, "movie.nfo"),
            """<movie><title>Dune</title><year>2021</year><uniqueid type="tmdb">438631</uniqueid></movie>""");

        await scanner.ScanAsync(libraryId, reimport: true, CancellationToken.None);

        await using (var db = _factory.CreateDbContext())
        {
            Assert.Equal(1, await db.Movies.CountAsync()); // re-read in place, not duplicated
            var m = await db.Movies.SingleAsync();
            Assert.Equal(MatchState.Matched, m.MatchState);
            Assert.True(m.HasNfo);
            Assert.Equal("Dune", m.Title);
            Assert.Equal("438631", m.TmdbId);
            Assert.Equal(1, await db.Artworks.CountAsync(a => a.MediaItemId == m.Id && a.Kind == ArtworkKind.Poster));
        }
    }

    [Fact]
    public async Task Scan_removes_movie_whose_folder_no_longer_exists()
    {
        var keep = Path.Combine(_root, "Keeper (2000)");
        var gone = Path.Combine(_root, "Gone (1999)");
        Directory.CreateDirectory(keep);
        Directory.CreateDirectory(gone);
        File.WriteAllText(Path.Combine(keep, "Keeper (2000).mkv"), "x");
        File.WriteAllText(Path.Combine(gone, "Gone (1999).mkv"), "x");

        var libraryId = await SeedLibraryAsync(LibraryType.Movies);
        var scanner = new MediaScanner(_factory, new NoopProgress(), new NfoReader(), NullLogger<MediaScanner>.Instance);
        await scanner.ScanAsync(libraryId, reimport: false, CancellationToken.None);

        await using (var db = _factory.CreateDbContext())
        {
            Assert.Equal(2, await db.Movies.CountAsync());
        }

        // The folder disappears from disk (renamed/removed out-of-band by another instance).
        Directory.Delete(gone, recursive: true);
        await scanner.ScanAsync(libraryId, reimport: false, CancellationToken.None);

        await using (var db = _factory.CreateDbContext())
        {
            var movie = await db.Movies.SingleAsync();
            Assert.Equal("Keeper", movie.Title);
        }
    }

    [Fact]
    public async Task Reimport_prunes_renamed_folder_leaving_only_the_current_one()
    {
        // Reproduces the production case: a folder identified+renamed on another instance leaves the old
        // record orphaned. The new folder is picked up as a fresh item; the stale record must be pruned.
        var oldDir = Path.Combine(_root, "Movie");
        Directory.CreateDirectory(oldDir);
        File.WriteAllText(Path.Combine(oldDir, "Movie.mkv"), "x");

        var libraryId = await SeedLibraryAsync(LibraryType.Movies);
        var scanner = new MediaScanner(_factory, new NoopProgress(), new NfoReader(), NullLogger<MediaScanner>.Instance);
        await scanner.ScanAsync(libraryId, reimport: false, CancellationToken.None);

        var newDir = Path.Combine(_root, "Movie (2010)");
        Directory.Move(oldDir, newDir);

        await scanner.ScanAsync(libraryId, reimport: true, CancellationToken.None);

        await using var db = _factory.CreateDbContext();
        var movie = await db.Movies.SingleAsync();
        Assert.Equal(Path.GetRelativePath(_root, newDir), movie.RelativePath);
    }

    [Fact]
    public async Task Scan_removes_tv_show_whose_folder_no_longer_exists_and_cascades_children()
    {
        var keepDir = Path.Combine(_root, "Firefly", "Season 01");
        var goneDir = Path.Combine(_root, "Angel", "Season 01");
        Directory.CreateDirectory(keepDir);
        Directory.CreateDirectory(goneDir);
        File.WriteAllText(Path.Combine(keepDir, "Firefly S01E01.mkv"), "x");
        File.WriteAllText(Path.Combine(goneDir, "Angel S01E01.mkv"), "x");

        var libraryId = await SeedLibraryAsync(LibraryType.TvShows);
        var scanner = new MediaScanner(_factory, new NoopProgress(), new NfoReader(), NullLogger<MediaScanner>.Instance);
        await scanner.ScanAsync(libraryId, reimport: false, CancellationToken.None);

        await using (var db = _factory.CreateDbContext())
        {
            Assert.Equal(2, await db.TvShows.CountAsync());
        }

        Directory.Delete(Path.Combine(_root, "Angel"), recursive: true);
        await scanner.ScanAsync(libraryId, reimport: false, CancellationToken.None);

        await using (var db = _factory.CreateDbContext())
        {
            Assert.Equal("Firefly", (await db.TvShows.SingleAsync()).Title);
            Assert.Equal(1, await db.Seasons.CountAsync());   // Angel's season cascaded away
            Assert.Equal(1, await db.Episodes.CountAsync());  // Angel's episode cascaded away
        }
    }

    [Fact]
    public async Task Scan_refuses_to_prune_when_nothing_is_found_on_disk()
    {
        // Guards the catastrophic case: the library root resolves but is empty (e.g. a network share
        // mounted to a bare mount point). The records must survive and the scan must fail loudly.
        var dir = Path.Combine(_root, "Inception (2010)");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "Inception (2010).mkv"), "x");

        var libraryId = await SeedLibraryAsync(LibraryType.Movies);
        var scanner = new MediaScanner(_factory, new NoopProgress(), new NfoReader(), NullLogger<MediaScanner>.Instance);
        await scanner.ScanAsync(libraryId, reimport: false, CancellationToken.None);

        Directory.Delete(dir, recursive: true); // root still exists, but now has no media

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => scanner.ScanAsync(libraryId, reimport: false, CancellationToken.None));

        await using var db = _factory.CreateDbContext();
        Assert.Equal(1, await db.Movies.CountAsync()); // not pruned
    }

    private async Task<Guid> SeedLibraryAsync(LibraryType type)
    {
        var id = Guid.NewGuid();
        await using var db = _factory.CreateDbContext();
        db.Libraries.Add(new Library { Id = id, Name = "test", Type = type, RootPath = _root });
        await db.SaveChangesAsync();
        return id;
    }

    public void Dispose()
    {
        _connection.Dispose();
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private sealed class TestContextFactory(DbContextOptions<MediaDbContext> options)
        : IDbContextFactory<MediaDbContext>
    {
        public MediaDbContext CreateDbContext() => new(options);
    }

    private sealed class CapturingProgress : IScanProgressService
    {
        public List<ScanProgress> Snapshots { get; } = [];
        public event Action<ScanProgress>? Changed;
        public ScanProgress Get(Guid libraryId) =>
            Snapshots.LastOrDefault(p => p.LibraryId == libraryId) ?? ScanProgress.Idle(libraryId);
        public void Set(ScanProgress progress) { Snapshots.Add(progress); Changed?.Invoke(progress); }
        public bool IsRunning(Guid libraryId) => false;
        public CancellationToken Register(Guid libraryId) => CancellationToken.None;
        public void RequestCancel(Guid libraryId) { }
        public void Release(Guid libraryId) { }
    }

    private sealed class NoopProgress : IScanProgressService
    {
#pragma warning disable CS0067 // event is required by the interface but unused in tests
        public event Action<ScanProgress>? Changed;
#pragma warning restore CS0067
        public ScanProgress Get(Guid libraryId) => ScanProgress.Idle(libraryId);
        public void Set(ScanProgress progress) { }
        public bool IsRunning(Guid libraryId) => false;
        public CancellationToken Register(Guid libraryId) => CancellationToken.None;
        public void RequestCancel(Guid libraryId) { }
        public void Release(Guid libraryId) { }
    }
}
