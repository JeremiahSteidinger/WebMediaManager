using WebMediaManager.Core.Providers;

namespace WebMediaManager.Core.Domain;

/// <summary>
/// Base type for a managed work (a movie or a TV show), stored table-per-hierarchy. Paths are kept
/// relative to the owning <see cref="Library"/>'s root so they survive the volume being remounted.
/// </summary>
public abstract class MediaItem
{
    public Guid Id { get; set; }

    public Guid LibraryId { get; set; }

    public string Title { get; set; } = string.Empty;

    public string? OriginalTitle { get; set; }

    public string? SortTitle { get; set; }

    public int? Year { get; set; }

    public string? Plot { get; set; }

    public string? Tagline { get; set; }

    /// <summary>Item folder, relative to <see cref="Library.RootPath"/>. Canonical location.</summary>
    public string RelativePath { get; set; } = string.Empty;

    public MatchState MatchState { get; set; } = MatchState.Unmatched;

    /// <summary>True when an NFO file was found on disk for this item (identified outside this app).</summary>
    public bool HasNfo { get; set; }

    public MetadataSource PrimaryProvider { get; set; } = MetadataSource.None;

    public string? TmdbId { get; set; }

    public string? TvdbId { get; set; }

    public string? ImdbId { get; set; }

    public double? Rating { get; set; }

    public int? Votes { get; set; }

    public List<string> Genres { get; set; } = [];

    public List<string> Studios { get; set; } = [];

    public DateTimeOffset DateAdded { get; set; }

    public DateTimeOffset DateScanned { get; set; }
}
