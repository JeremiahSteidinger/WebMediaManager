using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using WebMediaManager.Core.Domain;
using WebMediaManager.Core.Providers;
using WebMediaManager.Services;

namespace WebMediaManager.Providers.Imdb;

/// <summary>
/// IMDb metadata via the OMDb API (omdbapi.com) — IMDb itself exposes no public API, so OMDb serves
/// its catalog keyed by IMDb ids (tt…). Covers both movies and TV. The API key is read from settings
/// per request (as a query parameter), so changing it in the UI takes effect without a restart.
/// </summary>
public sealed class ImdbMetadataProvider(HttpClient http, ISettingsService settings) : IMetadataProvider
{
    public MetadataSource Source => MetadataSource.Imdb;

    public bool Supports(LibraryType type) => true;

    public async Task<IReadOnlyList<MetadataSearchResult>> SearchAsync(string query, int? year, LibraryType type, CancellationToken ct)
    {
        var omdbType = type == LibraryType.Movies ? "movie" : "series";
        var url = $"?s={Uri.EscapeDataString(query)}&type={omdbType}" + (year is not null ? $"&y={year}" : string.Empty);

        var resp = await GetAsync<OmdbSearchResponse>(url, ct);
        // OMDb returns Response:"False" for an empty search (and search hits carry no plot/overview).
        return resp?.Search?
            .Where(r => !string.IsNullOrEmpty(r.ImdbId))
            .Select(r => new MetadataSearchResult(
                MetadataSource.Imdb, r.ImdbId!, r.Title ?? string.Empty, ParseYear(r.Year), null, Clean(r.Poster)))
            .ToList() ?? [];
    }

    public async Task<MovieMetadata?> GetMovieAsync(string providerId, CancellationToken ct)
    {
        var d = await GetTitleAsync(providerId, ct);
        if (d is null)
        {
            return null;
        }

        return new MovieMetadata
        {
            ProviderId = d.ImdbId ?? providerId,
            Title = d.Title ?? string.Empty,
            Year = ParseYear(d.Year),
            ReleaseDate = ParseDate(d.Released),
            Plot = Clean(d.Plot),
            Runtime = ParseRuntime(d.Runtime),
            Rating = ParseRating(d.ImdbRating),
            Votes = ParseVotes(d.ImdbVotes),
            Genres = SplitList(d.Genre),
            Studios = SplitList(d.Production),
            ImdbId = d.ImdbId ?? providerId,
            PosterUrl = Clean(d.Poster),
        };
    }

    public async Task<TvShowMetadata?> GetTvShowAsync(string providerId, CancellationToken ct)
    {
        var d = await GetTitleAsync(providerId, ct);
        if (d is null)
        {
            return null;
        }

        return new TvShowMetadata
        {
            ProviderId = d.ImdbId ?? providerId,
            Title = d.Title ?? string.Empty,
            Year = ParseYear(d.Year),
            Plot = Clean(d.Plot),
            Status = MapStatus(d.Year),
            Rating = ParseRating(d.ImdbRating),
            Votes = ParseVotes(d.ImdbVotes),
            Genres = SplitList(d.Genre),
            Studios = SplitList(d.Production),
            ImdbId = d.ImdbId ?? providerId,
            PosterUrl = Clean(d.Poster),
        };
    }

    public async Task<IReadOnlyList<EpisodeMetadata>> GetEpisodesAsync(string showProviderId, int seasonNumber, CancellationToken ct)
    {
        var season = await GetAsync<OmdbSeasonResponse>(
            $"?i={Uri.EscapeDataString(showProviderId)}&Season={seasonNumber}", ct);
        if (!IsOk(season?.Response))
        {
            return [];
        }

        // OMDb's season listing carries no per-episode plot; that needs a per-episode call we skip for now.
        return season!.Episodes?
            .Select(e => new EpisodeMetadata(
                seasonNumber, ParseInt(e.Episode) ?? 0, Clean(e.Title), null, ParseDate(e.Released), null, null))
            .Where(e => e.Episode > 0)
            .ToList() ?? [];
    }

    // Alternate episode orderings are a TMDB concept; OMDb/IMDb don't expose them.
    public Task<IReadOnlyList<EpisodeOrdering>> GetEpisodeOrderingsAsync(string showProviderId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<EpisodeOrdering>>([]);

    public Task<IReadOnlyList<EpisodeMetadata>> GetOrderedEpisodesAsync(string orderingId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<EpisodeMetadata>>([]);

    private async Task<OmdbTitle?> GetTitleAsync(string providerId, CancellationToken ct)
    {
        var d = await GetAsync<OmdbTitle>($"?i={Uri.EscapeDataString(providerId)}&plot=full", ct);
        return IsOk(d?.Response) ? d : null;
    }

    private async Task<T?> GetAsync<T>(string url, CancellationToken ct)
    {
        var key = (await settings.GetAsync(ct)).Providers.OmdbApiKey;
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new MetadataException("OMDb API key is not set. Add it in Settings → Providers.");
        }

        using var response = await http.GetAsync(url + "&apikey=" + Uri.EscapeDataString(key.Trim()), ct);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            throw new MetadataException("OMDb rejected the API key (401). Check Settings → Providers.");
        }
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<T>(ct);
    }

    private static bool IsOk(string? response) =>
        string.Equals(response, "True", StringComparison.OrdinalIgnoreCase);

    // OMDb returns the literal "N/A" for absent values; treat those (and blanks) as null.
    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) || value.Equals("N/A", StringComparison.OrdinalIgnoreCase)
            ? null
            : value.Trim();

    private static IReadOnlyList<string> SplitList(string? value) =>
        Clean(value)?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];

    private static int? ParseYear(string? value)
    {
        var v = Clean(value);
        return v is not null && v.Length >= 4 && int.TryParse(v.AsSpan(0, 4), out var y) ? y : null;
    }

    private static DateOnly? ParseDate(string? value) =>
        Clean(value) is { } v && DateOnly.TryParse(v, CultureInfo.InvariantCulture, out var d) ? d : null;

    private static int? ParseInt(string? value) =>
        int.TryParse(Clean(value), NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? i : null;

    // OMDb runtime is formatted like "148 min"; pull the leading integer.
    private static int? ParseRuntime(string? value)
    {
        var v = Clean(value);
        if (v is null)
        {
            return null;
        }
        var digits = new string(v.TakeWhile(char.IsDigit).ToArray());
        return int.TryParse(digits, out var minutes) ? minutes : null;
    }

    private static double? ParseRating(string? value) =>
        double.TryParse(Clean(value), NumberStyles.Float, CultureInfo.InvariantCulture, out var r) ? r : null;

    // OMDb vote counts are formatted with grouping commas, e.g. "2,300,000".
    private static int? ParseVotes(string? value)
    {
        var v = Clean(value);
        if (v is null)
        {
            return null;
        }
        var digits = new string(v.Where(char.IsDigit).ToArray());
        return int.TryParse(digits, out var votes) ? votes : null;
    }

    // Series year reads as "2008–2013" (ended), "2008–" (still running), or a single year (unknown).
    private static TvShowStatus MapStatus(string? year)
    {
        var v = Clean(year);
        if (v is null)
        {
            return TvShowStatus.Unknown;
        }
        var dash = v.IndexOfAny(['–', '-']);
        if (dash < 0)
        {
            return TvShowStatus.Unknown;
        }
        return v[(dash + 1)..].Trim().Length == 0 ? TvShowStatus.Continuing : TvShowStatus.Ended;
    }
}
