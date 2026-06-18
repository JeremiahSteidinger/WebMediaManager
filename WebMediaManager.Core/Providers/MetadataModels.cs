using WebMediaManager.Core.Domain;

namespace WebMediaManager.Core.Providers;

/// <summary>A single search hit from a provider, provider-neutral.</summary>
public sealed record MetadataSearchResult(
    MetadataSource Source,
    string ProviderId,
    string Title,
    int? Year,
    string? Overview,
    string? PosterUrl);

/// <summary>Per-episode metadata, provider-neutral.</summary>
public sealed record EpisodeMetadata(
    int Season,
    int Episode,
    string? Title,
    string? Plot,
    DateOnly? AirDate,
    string? TmdbId,
    string? TvdbId);

/// <summary>Full movie metadata, provider-neutral.</summary>
public sealed record MovieMetadata
{
    public required string ProviderId { get; init; }
    public required string Title { get; init; }
    public string? OriginalTitle { get; init; }
    public int? Year { get; init; }
    public DateOnly? ReleaseDate { get; init; }
    public string? Plot { get; init; }
    public string? Tagline { get; init; }
    public int? Runtime { get; init; }
    public double? Rating { get; init; }
    public int? Votes { get; init; }
    public IReadOnlyList<string> Genres { get; init; } = [];
    public IReadOnlyList<string> Studios { get; init; } = [];
    public string? TmdbId { get; init; }
    public string? ImdbId { get; init; }
    public string? PosterUrl { get; init; }
    public string? FanartUrl { get; init; }
}

/// <summary>Full TV show metadata (show level), provider-neutral.</summary>
public sealed record TvShowMetadata
{
    public required string ProviderId { get; init; }
    public required string Title { get; init; }
    public string? OriginalTitle { get; init; }
    public int? Year { get; init; }
    public string? Plot { get; init; }
    public TvShowStatus Status { get; init; } = TvShowStatus.Unknown;
    public double? Rating { get; init; }
    public int? Votes { get; init; }
    public IReadOnlyList<string> Genres { get; init; } = [];
    public IReadOnlyList<string> Studios { get; init; } = [];
    public string? TmdbId { get; init; }
    public string? TvdbId { get; init; }
    public string? ImdbId { get; init; }
    public string? PosterUrl { get; init; }
    public string? FanartUrl { get; init; }
}
