using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using WebMediaManager.Core.Domain;
using WebMediaManager.Core.Providers;
using WebMediaManager.Services;

namespace WebMediaManager.Providers.Tmdb;

/// <summary>
/// TMDB provider over a typed <see cref="HttpClient"/>. Covers both movies and TV. The v4 bearer token
/// is read from settings per request, so changing it in the UI takes effect with no restart.
/// </summary>
public sealed class TmdbMetadataProvider(HttpClient http, ISettingsService settings) : IMetadataProvider
{
    public const string ImageBase = "https://image.tmdb.org/t/p/w342";
    private const string PosterBase = "https://image.tmdb.org/t/p/w500";
    private const string FanartBase = "https://image.tmdb.org/t/p/original";

    public MetadataSource Source => MetadataSource.Tmdb;

    public bool Supports(LibraryType type) => true;

    public async Task<IReadOnlyList<MetadataSearchResult>> SearchAsync(string query, int? year, LibraryType type, CancellationToken ct)
    {
        var escaped = Uri.EscapeDataString(query);

        if (type == LibraryType.Movies)
        {
            var url = $"search/movie?query={escaped}" + (year is not null ? $"&year={year}" : string.Empty);
            var resp = await GetAsync<TmdbSearchResponse<TmdbMovieSearchItem>>(url, ct);
            return resp?.Results?.Select(r => new MetadataSearchResult(
                MetadataSource.Tmdb, r.Id.ToString(), r.Title ?? string.Empty, YearOf(r.ReleaseDate), r.Overview, PosterUrl(r.PosterPath)))
                .ToList() ?? [];
        }

        var tvUrl = $"search/tv?query={escaped}" + (year is not null ? $"&first_air_date_year={year}" : string.Empty);
        var tv = await GetAsync<TmdbSearchResponse<TmdbTvSearchItem>>(tvUrl, ct);
        return tv?.Results?.Select(r => new MetadataSearchResult(
            MetadataSource.Tmdb, r.Id.ToString(), r.Name ?? string.Empty, YearOf(r.FirstAirDate), r.Overview, PosterUrl(r.PosterPath)))
            .ToList() ?? [];
    }

    public async Task<MovieMetadata?> GetMovieAsync(string providerId, CancellationToken ct)
    {
        var d = await GetAsync<TmdbMovieDetails>($"movie/{providerId}?append_to_response=external_ids", ct);
        if (d is null)
        {
            return null;
        }

        return new MovieMetadata
        {
            ProviderId = d.Id.ToString(),
            Title = d.Title ?? string.Empty,
            OriginalTitle = d.OriginalTitle,
            Year = YearOf(d.ReleaseDate),
            ReleaseDate = DateOnlyOf(d.ReleaseDate),
            Plot = d.Overview,
            Tagline = d.Tagline,
            Runtime = d.Runtime,
            Rating = d.VoteAverage,
            Votes = d.VoteCount,
            Genres = Names(d.Genres),
            Studios = Names(d.ProductionCompanies),
            TmdbId = d.Id.ToString(),
            ImdbId = d.ImdbId,
            PosterUrl = ImageUrl(PosterBase, d.PosterPath),
            FanartUrl = ImageUrl(FanartBase, d.BackdropPath),
        };
    }

    public async Task<TvShowMetadata?> GetTvShowAsync(string providerId, CancellationToken ct)
    {
        var d = await GetAsync<TmdbTvDetails>($"tv/{providerId}?append_to_response=external_ids", ct);
        if (d is null)
        {
            return null;
        }

        return new TvShowMetadata
        {
            ProviderId = d.Id.ToString(),
            Title = d.Name ?? string.Empty,
            OriginalTitle = d.OriginalName,
            Year = YearOf(d.FirstAirDate),
            Plot = d.Overview,
            Status = MapStatus(d.Status),
            Rating = d.VoteAverage,
            Votes = d.VoteCount,
            Genres = Names(d.Genres),
            Studios = Names(d.Networks),
            TmdbId = d.Id.ToString(),
            TvdbId = d.ExternalIds?.TvdbId?.ToString(),
            ImdbId = d.ExternalIds?.ImdbId,
            PosterUrl = ImageUrl(PosterBase, d.PosterPath),
            FanartUrl = ImageUrl(FanartBase, d.BackdropPath),
        };
    }

    public async Task<IReadOnlyList<EpisodeMetadata>> GetEpisodesAsync(string showProviderId, int seasonNumber, CancellationToken ct)
    {
        var season = await GetAsync<TmdbSeasonResponse>($"tv/{showProviderId}/season/{seasonNumber}", ct);
        return season?.Episodes?
            .Select(e => new EpisodeMetadata(
                e.SeasonNumber, e.EpisodeNumber, e.Name, e.Overview, DateOnlyOf(e.AirDate), e.Id.ToString(), null))
            .ToList() ?? [];
    }

    public async Task<IReadOnlyList<EpisodeOrdering>> GetEpisodeOrderingsAsync(string showProviderId, CancellationToken ct)
    {
        var resp = await GetAsync<TmdbEpisodeGroupsResponse>($"tv/{showProviderId}/episode_groups", ct);
        return resp?.Results?
            .Where(g => !string.IsNullOrEmpty(g.Id))
            .Select(g => new EpisodeOrdering(g.Id!, OrderingName(g.Name, g.Type), g.Description, g.EpisodeCount))
            .ToList() ?? [];
    }

    public async Task<IReadOnlyList<EpisodeMetadata>> GetOrderedEpisodesAsync(string orderingId, CancellationToken ct)
    {
        var detail = await GetAsync<TmdbEpisodeGroupDetails>($"tv/episode_group/{orderingId}", ct);
        if (detail?.Groups is null)
        {
            return [];
        }

        // Re-number to the ordering: each group becomes a season, episodes are numbered by their position.
        // We assign sequential 1-based season/episode numbers (groups/episodes sorted by TMDB's `order`)
        // rather than trusting the raw `order` values, which are 0-based — this way the result lines up with
        // files laid out in this order as S01E01, S01E02, … regardless of TMDB's indexing base.
        var episodes = new List<EpisodeMetadata>();
        var seasonNumber = 1;
        foreach (var group in detail.Groups.OrderBy(g => g.Order))
        {
            var episodeNumber = 1;
            foreach (var ep in (group.Episodes ?? []).OrderBy(e => e.Order))
            {
                episodes.Add(new EpisodeMetadata(
                    seasonNumber, episodeNumber, ep.Name, ep.Overview, DateOnlyOf(ep.AirDate), ep.Id.ToString(), null));
                episodeNumber++;
            }
            seasonNumber++;
        }
        return episodes;
    }

    private async Task<T?> GetAsync<T>(string url, CancellationToken ct)
    {
        var key = (await settings.GetAsync(ct)).Providers.TmdbApiKey;
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new MetadataException("TMDB API key is not set. Add it in Settings → Providers.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key.Trim());

        using var response = await http.SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            throw new MetadataException("TMDB rejected the API key (401). Check Settings → Providers.");
        }
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<T>(ct);
    }

    private static IReadOnlyList<string> Names(List<TmdbNamed>? items) =>
        items?.Select(i => i.Name ?? string.Empty).Where(n => n.Length > 0).ToList() ?? [];

    private static string? PosterUrl(string? posterPath) =>
        string.IsNullOrEmpty(posterPath) ? null : ImageBase + posterPath;

    private static string? ImageUrl(string baseUrl, string? path) =>
        string.IsNullOrEmpty(path) ? null : baseUrl + path;

    private static int? YearOf(string? date) =>
        !string.IsNullOrEmpty(date) && date.Length >= 4 && int.TryParse(date.AsSpan(0, 4), out var y) ? y : null;

    private static DateOnly? DateOnlyOf(string? date) =>
        DateOnly.TryParse(date, CultureInfo.InvariantCulture, out var d) ? d : null;

    // TMDB names most groups descriptively ("DVD Order", "Fox Order"); fall back to the type when blank.
    private static string OrderingName(string? name, int type) =>
        string.IsNullOrWhiteSpace(name) ? OrderingTypeLabel(type) : name.Trim();

    // TMDB episode-group type enum (https://developer.themoviedb.org/reference/tv-episode-group-details).
    private static string OrderingTypeLabel(int type) => type switch
    {
        1 => "Original air date",
        2 => "Absolute",
        3 => "DVD",
        4 => "Digital",
        5 => "Story arc",
        6 => "Production",
        7 => "TV",
        _ => "Alternate order",
    };

    private static TvShowStatus MapStatus(string? status) => status switch
    {
        "Returning Series" or "In Production" or "Planned" or "Pilot" => TvShowStatus.Continuing,
        "Ended" or "Canceled" => TvShowStatus.Ended,
        _ => TvShowStatus.Unknown,
    };
}
