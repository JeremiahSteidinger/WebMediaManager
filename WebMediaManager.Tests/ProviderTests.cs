using System.Net;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using WebMediaManager.Core.Domain;
using WebMediaManager.Core.Providers;
using WebMediaManager.Core.Settings;
using WebMediaManager.Providers.Tmdb;
using WebMediaManager.Providers.Tvdb;
using WebMediaManager.Services;

namespace WebMediaManager.Tests;

/// <summary>Verifies provider request/response handling against canned HTTP (no network or real keys).</summary>
public class ProviderTests
{
    [Fact]
    public async Task Tmdb_movie_search_parses_results_and_sends_bearer_token()
    {
        string? sentAuth = null;
        var provider = NewTmdb(new StubHandler(req =>
        {
            sentAuth = req.Headers.Authorization?.ToString();
            return (HttpStatusCode.OK,
                """{"results":[{"id":27205,"title":"Inception","release_date":"2010-07-15","overview":"A thief","poster_path":"/p.jpg"}]}""");
        }));

        var results = await provider.SearchAsync("Inception", null, LibraryType.Movies, default);

        var r = Assert.Single(results);
        Assert.Equal("Inception", r.Title);
        Assert.Equal(2010, r.Year);
        Assert.Equal("27205", r.ProviderId);
        Assert.Equal(MetadataSource.Tmdb, r.Source);
        Assert.Equal("https://image.tmdb.org/t/p/w342/p.jpg", r.PosterUrl);
        Assert.Equal("Bearer KEY", sentAuth);
    }

    [Fact]
    public async Task Tmdb_movie_details_parses_metadata()
    {
        var provider = NewTmdb(new StubHandler(_ => (HttpStatusCode.OK,
            """
            {"id":27205,"title":"Inception","original_title":"Inception","release_date":"2010-07-15",
             "overview":"A thief","tagline":"Your mind is the scene","runtime":148,
             "vote_average":8.3,"vote_count":34000,
             "genres":[{"name":"Action"},{"name":"Science Fiction"}],
             "production_companies":[{"name":"Legendary"}],"imdb_id":"tt1375666"}
            """)));

        var movie = await provider.GetMovieAsync("27205", default);

        Assert.NotNull(movie);
        Assert.Equal("Inception", movie!.Title);
        Assert.Equal(2010, movie.Year);
        Assert.Equal(new DateOnly(2010, 7, 15), movie.ReleaseDate);
        Assert.Equal(148, movie.Runtime);
        Assert.Equal("tt1375666", movie.ImdbId);
        Assert.Equal("27205", movie.TmdbId);
        Assert.Equal(["Action", "Science Fiction"], movie.Genres);
    }

    [Fact]
    public async Task Tmdb_unauthorized_throws_metadata_exception()
    {
        var provider = NewTmdb(new StubHandler(_ => (HttpStatusCode.Unauthorized, "{}")));
        await Assert.ThrowsAsync<MetadataException>(() => provider.SearchAsync("x", null, LibraryType.Movies, default));
    }

    [Fact]
    public async Task Tmdb_missing_key_throws_metadata_exception()
    {
        var provider = NewTmdb(new StubHandler(_ => (HttpStatusCode.OK, "{}")), key: null);
        await Assert.ThrowsAsync<MetadataException>(() => provider.SearchAsync("x", null, LibraryType.Movies, default));
    }

    [Fact]
    public async Task Tvdb_logs_in_then_searches_series()
    {
        var handler = new StubHandler(req =>
        {
            var path = req.RequestUri!.AbsolutePath;
            if (req.Method == HttpMethod.Post && path.EndsWith("/login"))
            {
                return (HttpStatusCode.OK, """{"data":{"token":"JWT"}}""");
            }
            if (path.EndsWith("/search"))
            {
                Assert.Equal("Bearer JWT", req.Headers.Authorization?.ToString());
                return (HttpStatusCode.OK,
                    """{"data":[{"tvdb_id":"81189","name":"Breaking Bad","year":"2008","overview":"Chemistry","image_url":"http://img/x.jpg"}]}""");
            }
            return (HttpStatusCode.NotFound, "{}");
        });

        var settings = SettingsWith(s => s.Providers.TvdbApiKey = "TVKEY");
        var sp = new ServiceCollection()
            .AddSingleton<ISettingsService>(new StubSettings(settings))
            .AddSingleton<IHttpClientFactory>(new StubHttpClientFactory(handler, "https://api4.thetvdb.com/v4/"))
            .BuildServiceProvider();

        var tokens = new TvdbTokenProvider(
            sp.GetRequiredService<IHttpClientFactory>(), sp.GetRequiredService<IServiceScopeFactory>());
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api4.thetvdb.com/v4/") };
        var provider = new TvdbMetadataProvider(http, tokens);

        var results = await provider.SearchAsync("Breaking Bad", null, LibraryType.TvShows, default);

        var r = Assert.Single(results);
        Assert.Equal("Breaking Bad", r.Title);
        Assert.Equal(2008, r.Year);
        Assert.Equal("81189", r.ProviderId);
        Assert.Equal(MetadataSource.Tvdb, r.Source);
    }

    private static TmdbMetadataProvider NewTmdb(StubHandler handler, string? key = "KEY")
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.themoviedb.org/3/") };
        var settings = SettingsWith(s => s.Providers.TmdbApiKey = key);
        return new TmdbMetadataProvider(http, new StubSettings(settings));
    }

    private static AppSettings SettingsWith(Action<AppSettings> configure)
    {
        var s = new AppSettings();
        configure(s);
        return s;
    }

    private sealed class StubHandler(Func<HttpRequestMessage, (HttpStatusCode Code, string Json)> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var (code, json) = responder(request);
            return Task.FromResult(new HttpResponseMessage(code)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
                RequestMessage = request,
            });
        }
    }

    private sealed class StubSettings(AppSettings settings) : ISettingsService
    {
        public Task<AppSettings> GetAsync(CancellationToken ct = default) => Task.FromResult(settings);
        public Task SaveAsync(AppSettings s, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class StubHttpClientFactory(HttpMessageHandler handler, string baseUrl) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false) { BaseAddress = new Uri(baseUrl) };
    }
}
