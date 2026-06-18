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
