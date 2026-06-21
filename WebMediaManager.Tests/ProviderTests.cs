using System.Net;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using WebMediaManager.Core.Domain;
using WebMediaManager.Core.Providers;
using WebMediaManager.Core.Settings;
using WebMediaManager.Providers.Imdb;
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

    [Fact]
    public async Task Imdb_movie_search_parses_results_and_sends_api_key()
    {
        Uri? sentUri = null;
        var provider = NewImdb(new StubHandler(req =>
        {
            sentUri = req.RequestUri;
            return (HttpStatusCode.OK,
                """{"Search":[{"Title":"Inception","Year":"2010","imdbID":"tt1375666","Type":"movie","Poster":"http://img/p.jpg"}],"totalResults":"1","Response":"True"}""");
        }));

        var results = await provider.SearchAsync("Inception", 2010, LibraryType.Movies, default);

        var r = Assert.Single(results);
        Assert.Equal("Inception", r.Title);
        Assert.Equal(2010, r.Year);
        Assert.Equal("tt1375666", r.ProviderId);
        Assert.Equal(MetadataSource.Imdb, r.Source);
        Assert.Equal("http://img/p.jpg", r.PosterUrl);
        Assert.Contains("apikey=KEY", sentUri!.Query);
        Assert.Contains("type=movie", sentUri.Query);
        Assert.Contains("y=2010", sentUri.Query);
    }

    [Fact]
    public async Task Imdb_movie_details_parses_metadata()
    {
        var provider = NewImdb(new StubHandler(_ => (HttpStatusCode.OK,
            """
            {"Title":"Inception","Year":"2010","Released":"16 Jul 2010","Runtime":"148 min",
             "Genre":"Action, Adventure, Sci-Fi","Plot":"A thief","Production":"Legendary",
             "Poster":"http://img/p.jpg","imdbRating":"8.8","imdbVotes":"2,300,000",
             "imdbID":"tt1375666","Type":"movie","Response":"True"}
            """)));

        var movie = await provider.GetMovieAsync("tt1375666", default);

        Assert.NotNull(movie);
        Assert.Equal("Inception", movie!.Title);
        Assert.Equal(2010, movie.Year);
        Assert.Equal(new DateOnly(2010, 7, 16), movie.ReleaseDate);
        Assert.Equal(148, movie.Runtime);
        Assert.Equal(8.8, movie.Rating);
        Assert.Equal(2300000, movie.Votes);
        Assert.Equal("tt1375666", movie.ImdbId);
        Assert.Equal(["Action", "Adventure", "Sci-Fi"], movie.Genres);
        Assert.Equal(["Legendary"], movie.Studios);
    }

    [Fact]
    public async Task Imdb_handles_not_found_response()
    {
        // OMDb returns HTTP 200 with Response:"False" when a title is missing.
        var provider = NewImdb(new StubHandler(_ => (HttpStatusCode.OK,
            """{"Response":"False","Error":"Incorrect IMDb ID."}""")));

        var movie = await provider.GetMovieAsync("tt0000000", default);

        Assert.Null(movie);
    }

    [Fact]
    public async Task Imdb_series_status_derived_from_year_range()
    {
        var ended = NewImdb(new StubHandler(_ => (HttpStatusCode.OK,
            """{"Title":"Breaking Bad","Year":"2008–2013","imdbID":"tt0903747","Type":"series","Response":"True"}""")));
        var running = NewImdb(new StubHandler(_ => (HttpStatusCode.OK,
            """{"Title":"The Show","Year":"2021–","imdbID":"tt9999999","Type":"series","Response":"True"}""")));

        Assert.Equal(TvShowStatus.Ended, (await ended.GetTvShowAsync("tt0903747", default))!.Status);
        Assert.Equal(TvShowStatus.Continuing, (await running.GetTvShowAsync("tt9999999", default))!.Status);
    }

    [Fact]
    public async Task Imdb_unauthorized_throws_metadata_exception()
    {
        var provider = NewImdb(new StubHandler(_ => (HttpStatusCode.Unauthorized, """{"Response":"False","Error":"Invalid API key!"}""")));
        await Assert.ThrowsAsync<MetadataException>(() => provider.SearchAsync("x", null, LibraryType.Movies, default));
    }

    [Fact]
    public async Task Imdb_missing_key_throws_metadata_exception()
    {
        var provider = NewImdb(new StubHandler(_ => (HttpStatusCode.OK, "{}")), key: null);
        await Assert.ThrowsAsync<MetadataException>(() => provider.SearchAsync("x", null, LibraryType.Movies, default));
    }

    private static ImdbMetadataProvider NewImdb(StubHandler handler, string? key = "KEY")
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://www.omdbapi.com/") };
        var settings = SettingsWith(s => s.Providers.OmdbApiKey = key);
        return new ImdbMetadataProvider(http, new StubSettings(settings));
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
