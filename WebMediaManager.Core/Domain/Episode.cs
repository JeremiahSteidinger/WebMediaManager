namespace WebMediaManager.Core.Domain;

public class Episode
{
    public Guid Id { get; set; }

    public Guid SeasonId { get; set; }

    /// <summary>Denormalized for sort/lookup; mirrors the owning <see cref="Season.SeasonNumber"/>.</summary>
    public int SeasonNumber { get; set; }

    public int EpisodeNumber { get; set; }

    public string? Title { get; set; }

    public string? Plot { get; set; }

    public DateOnly? AirDate { get; set; }

    /// <summary>Episode video file, relative to <see cref="Library.RootPath"/>.</summary>
    public string VideoFilePath { get; set; } = string.Empty;

    public string? TmdbId { get; set; }

    public string? TvdbId { get; set; }

    public double? Rating { get; set; }
}
