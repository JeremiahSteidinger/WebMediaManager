using WebMediaManager.Core.Domain;

namespace WebMediaManager.Core.Providers;

/// <summary>
/// A source of online metadata (TMDB, TVDB, …). Implementations translate provider-specific responses
/// into the neutral DTOs above so the rest of the app never sees provider JSON shapes.
/// </summary>
public interface IMetadataProvider
{
    MetadataSource Source { get; }

    bool Supports(LibraryType type);

    Task<IReadOnlyList<MetadataSearchResult>> SearchAsync(string query, int? year, LibraryType type, CancellationToken ct);

    Task<MovieMetadata?> GetMovieAsync(string providerId, CancellationToken ct);

    Task<TvShowMetadata?> GetTvShowAsync(string providerId, CancellationToken ct);

    /// <summary>Episodes for one season of a show; empty if the provider can't supply them.</summary>
    Task<IReadOnlyList<EpisodeMetadata>> GetEpisodesAsync(string showProviderId, int seasonNumber, CancellationToken ct);

    /// <summary>
    /// Alternate episode orderings the provider offers for a show (TMDB "episode groups").
    /// Empty when the provider has none or doesn't support them.
    /// </summary>
    Task<IReadOnlyList<EpisodeOrdering>> GetEpisodeOrderingsAsync(string showProviderId, CancellationToken ct);

    /// <summary>
    /// Episodes for a chosen alternate ordering, already mapped to that ordering's season/episode
    /// numbers so they line up with files laid out in that order. Empty if unsupported.
    /// </summary>
    Task<IReadOnlyList<EpisodeMetadata>> GetOrderedEpisodesAsync(string orderingId, CancellationToken ct);
}
