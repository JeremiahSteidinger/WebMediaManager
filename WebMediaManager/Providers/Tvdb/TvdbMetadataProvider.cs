using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using WebMediaManager.Core.Domain;
using WebMediaManager.Core.Providers;

namespace WebMediaManager.Providers.Tvdb;

/// <summary>
/// TVDB v4 provider (TV only). Attaches the cached bearer token; on a 401 it invalidates the token,
/// re-logs-in once, and retries.
/// </summary>
public sealed class TvdbMetadataProvider(HttpClient http, TvdbTokenProvider tokens) : IMetadataProvider
{
    public MetadataSource Source => MetadataSource.Tvdb;

    public bool Supports(LibraryType type) => type == LibraryType.TvShows;

    public async Task<IReadOnlyList<MetadataSearchResult>> SearchAsync(string query, int? year, LibraryType type, CancellationToken ct)
    {
        if (type != LibraryType.TvShows)
        {
            return [];
        }

        var url = $"search?query={Uri.EscapeDataString(query)}&type=series" + (year is not null ? $"&year={year}" : string.Empty);
        var resp = await SendAsync<TvdbSearchResponse>(url, ct);
        return resp?.Data?
            .Where(d => !string.IsNullOrEmpty(d.TvdbId))
            .Select(d => new MetadataSearchResult(
                MetadataSource.Tvdb, d.TvdbId!, d.Name ?? string.Empty, ParseYear(d.Year), d.Overview, d.ImageUrl))
            .ToList() ?? [];
    }

    // TVDB movies are not supported in v1; movie identification goes through TMDB.
    public Task<MovieMetadata?> GetMovieAsync(string providerId, CancellationToken ct) =>
        Task.FromResult<MovieMetadata?>(null);

    // Per-episode metadata from TVDB is a later enhancement; episodes come from TMDB for now.
    public Task<IReadOnlyList<EpisodeMetadata>> GetEpisodesAsync(string showProviderId, int seasonNumber, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<EpisodeMetadata>>([]);

    public async Task<TvShowMetadata?> GetTvShowAsync(string providerId, CancellationToken ct)
    {
        var resp = await SendAsync<TvdbSeriesResponse>($"series/{providerId}/extended", ct);
        var d = resp?.Data;
        if (d is null)
        {
            return null;
        }

        var id = d.Id?.ToString() ?? providerId;
        return new TvShowMetadata
        {
            ProviderId = id,
            Title = d.Name ?? string.Empty,
            Year = ParseYear(d.Year) ?? ParseYear(d.FirstAired),
            Plot = d.Overview,
            Status = MapStatus(d.Status?.Name),
            Genres = d.Genres?.Select(g => g.Name ?? string.Empty).Where(n => n.Length > 0).ToList() ?? [],
            TvdbId = id,
        };
    }

    private async Task<T?> SendAsync<T>(string url, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var token = await tokens.GetTokenAsync(forceRefresh: attempt > 0, ct);
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var response = await http.SendAsync(request, ct);
            if (response.StatusCode == HttpStatusCode.Unauthorized && attempt == 0)
            {
                tokens.Invalidate();
                continue;
            }
            if (!response.IsSuccessStatusCode)
            {
                throw new MetadataException($"TVDB request failed ({(int)response.StatusCode}).");
            }

            return await response.Content.ReadFromJsonAsync<T>(ct);
        }

        return default;
    }

    private static int? ParseYear(string? value) =>
        !string.IsNullOrEmpty(value) && value.Length >= 4 && int.TryParse(value.AsSpan(0, 4), out var y) ? y : null;

    private static TvShowStatus MapStatus(string? status) => status switch
    {
        "Continuing" or "Upcoming" => TvShowStatus.Continuing,
        "Ended" => TvShowStatus.Ended,
        _ => TvShowStatus.Unknown,
    };
}
