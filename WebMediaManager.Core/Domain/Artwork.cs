namespace WebMediaManager.Core.Domain;

public enum ArtworkKind
{
    Poster = 0,
    Fanart = 1,
    Banner = 2,
}

/// <summary>
/// One artwork image for a <see cref="MediaItem"/>. <see cref="SourceUrl"/> is the remote location;
/// once fetched, <see cref="LocalRelativePath"/> (relative to the library root) points at the saved file.
/// </summary>
public class Artwork
{
    public Guid Id { get; set; }

    public Guid MediaItemId { get; set; }

    public ArtworkKind Kind { get; set; }

    public string? SourceUrl { get; set; }

    public string? LocalRelativePath { get; set; }

    public DateTimeOffset? DownloadedUtc { get; set; }
}
