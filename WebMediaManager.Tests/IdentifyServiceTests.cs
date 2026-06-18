using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using WebMediaManager.Core.Domain;
using WebMediaManager.Core.Providers;
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
            new NoopNfo(), NullLogger<IdentifyService>.Instance);

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
            new NoopNfo(), NullLogger<IdentifyService>.Instance);

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
        public Task DownloadForItemAsync(Guid itemId, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class NoopNfo : INfoFileService
    {
        public Task WriteForItemAsync(Guid itemId, CancellationToken ct = default) => Task.CompletedTask;
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
    }
}
