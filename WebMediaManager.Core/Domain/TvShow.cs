namespace WebMediaManager.Core.Domain;

public class TvShow : MediaItem
{
    public TvShowStatus Status { get; set; } = TvShowStatus.Unknown;

    /// <summary>
    /// Provider id of the chosen alternate episode ordering (a TMDB "episode group"), or null to use
    /// the default aired order. Recorded so the choice survives and re-identifies can default to it.
    /// </summary>
    public string? EpisodeGroupId { get; set; }

    /// <summary>Human-readable name of the chosen ordering (e.g. "DVD Order", "Fox Order"); null for aired order.</summary>
    public string? EpisodeGroupName { get; set; }

    public List<Season> Seasons { get; set; } = [];
}
