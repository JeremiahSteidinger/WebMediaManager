using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using WebMediaManager.Core.Domain;
using WebMediaManager.Core.Providers;
using WebMediaManager.Core.Renaming;
using WebMediaManager.Core.Settings;
using WebMediaManager.Data;
using WebMediaManager.Providers;
using WebMediaManager.Services;

namespace WebMediaManager.Tests;

public sealed class IdentifyServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly TestContextFactory _factory;

    public IdentifyServiceTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<MediaDbContext>().UseSqlite(_connection).Options;
        using (var db = new MediaDbContext(options))
        {
            db.Database.EnsureCreated();
        }
        _factory = new TestContextFactory(options);
    }

    [Fact]
    public async Task Link_applies_movie_metadata_and_marks_matched()
    {
        var movieId = Guid.NewGuid();
        await using (var db = _factory.CreateDbContext())
        {
            var libraryId = Guid.NewGuid();
            db.Libraries.Add(new Library { Id = libraryId, Name = "m", Type = LibraryType.Movies, RootPath = "/x" });
            db.Movies.Add(new Movie
            {
                Id = movieId,
                LibraryId = libraryId,
                Title = "inception",                 // sloppy scanned title
                RelativePath = "Inception",
                MatchState = MatchState.Unmatched,
            });
            await db.SaveChangesAsync();
        }

        var meta = new MovieMetadata
        {
            ProviderId = "27205",
            Title = "Inception",
            Year = 2010,
            ImdbId = "tt1375666",
            TmdbId = "27205",
            Runtime = 148,
            Genres = ["Action", "Science Fiction"],
        };
        var service = new IdentifyService(
            new FakeResolver(new FakeProvider(meta)), _factory, new StubSettings(), new NoopArtwork(),
            new NoopNfo(), new NoopRename(), new NoopActivityLog(), NullLogger<IdentifyService>.Instance);

        await service.LinkAsync(movieId, new MetadataSearchResult(MetadataSource.Tmdb, "27205", "Inception", 2010, null, null));

        await using var verify = _factory.CreateDbContext();
        var movie = await verify.Movies.SingleAsync();
        Assert.Equal(MatchState.Matched, movie.MatchState);
        Assert.Equal(MetadataSource.Tmdb, movie.PrimaryProvider);
        Assert.Equal("Inception", movie.Title);
        Assert.Equal(2010, movie.Year);
        Assert.Equal(148, movie.Runtime);
        Assert.Equal("tt1375666", movie.ImdbId);
        Assert.Equal(["Action", "Science Fiction"], movie.Genres);
    }

    [Fact]
    public async Task LookupById_returns_the_work_for_a_known_provider_id()
    {
        var meta = new MovieMetadata { ProviderId = "27205", Title = "Inception", Year = 2010, PosterUrl = "http://img/p.jpg" };
        var service = new IdentifyService(
            new FakeResolver(new FakeProvider(meta)), _factory, new StubSettings(), new NoopArtwork(),
            new NoopNfo(), new NoopRename(), new NoopActivityLog(), NullLogger<IdentifyService>.Instance);

        var hit = await service.LookupByIdAsync(MetadataSource.Tmdb, "27205", LibraryType.Movies);

        Assert.NotNull(hit);
        Assert.Equal("27205", hit!.ProviderId);
        Assert.Equal("Inception", hit.Title);
        Assert.Equal(2010, hit.Year);
        Assert.Equal(MetadataSource.Tmdb, hit.Source);
        Assert.Equal("http://img/p.jpg", hit.PosterUrl);
    }

    public void Dispose() => _connection.Dispose();

    private sealed class TestContextFactory(DbContextOptions<MediaDbContext> options)
        : IDbContextFactory<MediaDbContext>
    {
        public MediaDbContext CreateDbContext() => new(options);
    }

    private sealed class StubSettings : ISettingsService
    {
        public Task<AppSettings> GetAsync(CancellationToken ct = default) => Task.FromResult(new AppSettings());
        public Task SaveAsync(AppSettings settings, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class NoopArtwork : IArtworkService
    {
        public Task<int> DownloadForItemAsync(Guid itemId, CancellationToken ct = default) => Task.FromResult(0);
    }

    private sealed class NoopNfo : INfoFileService
    {
        public Task<int> WriteForItemAsync(Guid itemId, CancellationToken ct = default) => Task.FromResult(0);
        public Task<int> WriteForSeasonAsync(Guid seasonId, CancellationToken ct = default) => Task.FromResult(0);
        public Task<int> WriteForEpisodeAsync(Guid episodeId, CancellationToken ct = default) => Task.FromResult(0);
    }

    private sealed class NoopRename : IRenameService
    {
        public Task<RenamePlan> BuildPlanAsync(Guid itemId, CancellationToken ct = default) =>
            Task.FromResult(new RenamePlan([]));
        public Task<RenameResult> ApplyAsync(Guid itemId, CancellationToken ct = default) =>
            Task.FromResult(new RenameResult(true, 0, null));
    }

    private sealed class FakeResolver(IMetadataProvider provider) : IMetadataProviderResolver
    {
        public IReadOnlyList<IMetadataProvider> ProvidersFor(LibraryType type) => [provider];
        public IMetadataProvider? Get(MetadataSource source) => provider;
    }

    private sealed class FakeProvider(MovieMetadata movie) : IMetadataProvider
    {
        public MetadataSource Source => MetadataSource.Tmdb;
        public bool Supports(LibraryType type) => true;
        public Task<IReadOnlyList<MetadataSearchResult>> SearchAsync(string query, int? year, LibraryType type, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<MetadataSearchResult>>([]);
        public Task<MovieMetadata?> GetMovieAsync(string providerId, CancellationToken ct) => Task.FromResult<MovieMetadata?>(movie);
        public Task<TvShowMetadata?> GetTvShowAsync(string providerId, CancellationToken ct) => Task.FromResult<TvShowMetadata?>(null);
        public Task<IReadOnlyList<EpisodeMetadata>> GetEpisodesAsync(string showProviderId, int seasonNumber, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<EpisodeMetadata>>([]);
        public Task<IReadOnlyList<EpisodeOrdering>> GetEpisodeOrderingsAsync(string showProviderId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<EpisodeOrdering>>([]);
        public Task<IReadOnlyList<EpisodeMetadata>> GetOrderedEpisodesAsync(string orderingId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<EpisodeMetadata>>([]);
    }

    [Fact]
    public async Task Link_with_chosen_ordering_applies_ordered_episodes_and_records_the_choice()
    {
        var showId = Guid.NewGuid();
        var seasonId = Guid.NewGuid();
        await using (var db = _factory.CreateDbContext())
        {
            var libraryId = Guid.NewGuid();
            db.Libraries.Add(new Library { Id = libraryId, Name = "tv", Type = LibraryType.TvShows, RootPath = "/x" });
            db.TvShows.Add(new TvShow
            {
                Id = showId,
                LibraryId = libraryId,
                Title = "firefly",
                RelativePath = "Firefly",
                MatchState = MatchState.Unmatched,
                Seasons =
                [
                    new Season
                    {
                        Id = seasonId,
                        TvShowId = showId,
                        SeasonNumber = 1,
                        Episodes =
                        [
                            new Episode { Id = Guid.NewGuid(), SeasonId = seasonId, SeasonNumber = 1, EpisodeNumber = 1, VideoFilePath = "Firefly/S01E01.mkv" },
                            new Episode { Id = Guid.NewGuid(), SeasonId = seasonId, SeasonNumber = 1, EpisodeNumber = 2, VideoFilePath = "Firefly/S01E02.mkv" },
                        ],
                    },
                ],
            });
            await db.SaveChangesAsync();
        }

        var showMeta = new TvShowMetadata { ProviderId = "1437", Title = "Firefly", Year = 2002 };
        var ordering = new EpisodeOrdering("grp-fox", "Fox Order", "Original broadcast order", 2);
        var ordered = new List<EpisodeMetadata>
        {
            new(1, 1, "The Train Job", "Fox-order opener", new DateOnly(2002, 9, 20), "1", null),
            new(1, 2, "Bushwhacked", "Fox-order second", new DateOnly(2002, 9, 27), "2", null),
        };
        var provider = new FakeTvProvider(showMeta, [ordering], ordered);
        var service = new IdentifyService(
            new FakeResolver(provider), _factory, new StubSettings(), new NoopArtwork(),
            new NoopNfo(), new NoopRename(), new NoopActivityLog(), NullLogger<IdentifyService>.Instance);

        await service.LinkAsync(
            showId, new MetadataSearchResult(MetadataSource.Tmdb, "1437", "Firefly", 2002, null, null), ordering);

        await using var verify = _factory.CreateDbContext();
        var show = await verify.TvShows.SingleAsync();
        Assert.Equal(MatchState.Matched, show.MatchState);
        Assert.Equal("grp-fox", show.EpisodeGroupId);
        Assert.Equal("Fox Order", show.EpisodeGroupName);

        var episodes = await verify.Episodes.OrderBy(e => e.EpisodeNumber).ToListAsync();
        Assert.Equal("The Train Job", episodes[0].Title);
        Assert.Equal("Bushwhacked", episodes[1].Title);
    }

    private sealed class FakeTvProvider(
        TvShowMetadata show, IReadOnlyList<EpisodeOrdering> orderings, IReadOnlyList<EpisodeMetadata> orderedEpisodes,
        IReadOnlyList<EpisodeMetadata>? airedEpisodes = null)
        : IMetadataProvider
    {
        public MetadataSource Source => MetadataSource.Tmdb;
        public bool Supports(LibraryType type) => true;
        public Task<IReadOnlyList<MetadataSearchResult>> SearchAsync(string query, int? year, LibraryType type, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<MetadataSearchResult>>([]);
        public Task<MovieMetadata?> GetMovieAsync(string providerId, CancellationToken ct) => Task.FromResult<MovieMetadata?>(null);
        public Task<TvShowMetadata?> GetTvShowAsync(string providerId, CancellationToken ct) => Task.FromResult<TvShowMetadata?>(show);
        // Aired-order path returns the given season's episodes (filtered by season number).
        public Task<IReadOnlyList<EpisodeMetadata>> GetEpisodesAsync(string showProviderId, int seasonNumber, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<EpisodeMetadata>>(
                (airedEpisodes ?? []).Where(e => e.Season == seasonNumber).ToList());
        public Task<IReadOnlyList<EpisodeOrdering>> GetEpisodeOrderingsAsync(string showProviderId, CancellationToken ct) =>
            Task.FromResult(orderings);
        public Task<IReadOnlyList<EpisodeMetadata>> GetOrderedEpisodesAsync(string orderingId, CancellationToken ct) =>
            Task.FromResult(orderedEpisodes);
    }

    [Fact]
    public async Task RefreshEpisode_enriches_only_the_target_episode_by_number()
    {
        var showId = Guid.NewGuid();
        var seasonId = Guid.NewGuid();
        var newEpisodeId = Guid.NewGuid();
        await using (var db = _factory.CreateDbContext())
        {
            var libraryId = Guid.NewGuid();
            db.Libraries.Add(new Library { Id = libraryId, Name = "tv", Type = LibraryType.TvShows, RootPath = "/x" });
            db.TvShows.Add(new TvShow
            {
                Id = showId,
                LibraryId = libraryId,
                Title = "Firefly",
                RelativePath = "Firefly",
                MatchState = MatchState.Matched,        // already identified
                PrimaryProvider = MetadataSource.Tmdb,
                TmdbId = "1437",
                Seasons =
                [
                    new Season
                    {
                        Id = seasonId,
                        TvShowId = showId,
                        SeasonNumber = 1,
                        Episodes =
                        [
                            new Episode { Id = Guid.NewGuid(), SeasonId = seasonId, SeasonNumber = 1, EpisodeNumber = 1, Title = "Serenity", VideoFilePath = "Firefly/S01E01.mkv" },
                            new Episode { Id = newEpisodeId, SeasonId = seasonId, SeasonNumber = 1, EpisodeNumber = 2, VideoFilePath = "Firefly/S01E02.mkv" },
                        ],
                    },
                ],
            });
            await db.SaveChangesAsync();
        }

        var showMeta = new TvShowMetadata { ProviderId = "1437", Title = "Firefly", Year = 2002 };
        var aired = new List<EpisodeMetadata>
        {
            new(1, 1, "Aired E1", "should not be applied", new DateOnly(2002, 9, 20), "1", null),
            new(1, 2, "The Train Job", "the new episode", new DateOnly(2002, 9, 27), "2", null),
        };
        var provider = new FakeTvProvider(showMeta, [], [], aired);
        var service = new IdentifyService(
            new FakeResolver(provider), _factory, new StubSettings(), new NoopArtwork(),
            new NoopNfo(), new NoopRename(), new NoopActivityLog(), NullLogger<IdentifyService>.Instance);

        await service.RefreshEpisodeAsync(newEpisodeId);

        await using var verify = _factory.CreateDbContext();
        var episodes = await verify.Episodes.OrderBy(e => e.EpisodeNumber).ToListAsync();
        Assert.Equal("Serenity", episodes[0].Title);     // untouched
        Assert.Equal("The Train Job", episodes[1].Title); // enriched by number
        Assert.Equal("the new episode", episodes[1].Plot);
    }

    [Fact]
    public async Task RefreshSeason_throws_when_show_is_not_yet_matched()
    {
        var showId = Guid.NewGuid();
        var seasonId = Guid.NewGuid();
        await using (var db = _factory.CreateDbContext())
        {
            var libraryId = Guid.NewGuid();
            db.Libraries.Add(new Library { Id = libraryId, Name = "tv", Type = LibraryType.TvShows, RootPath = "/x" });
            db.TvShows.Add(new TvShow
            {
                Id = showId,
                LibraryId = libraryId,
                Title = "Firefly",
                RelativePath = "Firefly",
                MatchState = MatchState.Unmatched,       // never identified — no provider id
                Seasons = [new Season { Id = seasonId, TvShowId = showId, SeasonNumber = 1 }],
            });
            await db.SaveChangesAsync();
        }

        var provider = new FakeTvProvider(new TvShowMetadata { ProviderId = "1437", Title = "Firefly" }, [], []);
        var service = new IdentifyService(
            new FakeResolver(provider), _factory, new StubSettings(), new NoopArtwork(),
            new NoopNfo(), new NoopRename(), new NoopActivityLog(), NullLogger<IdentifyService>.Instance);

        await Assert.ThrowsAsync<MetadataException>(() => service.RefreshSeasonAsync(seasonId));
    }
}
