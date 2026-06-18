namespace WebMediaManager.Core.Domain;

public class TvShow : MediaItem
{
    public TvShowStatus Status { get; set; } = TvShowStatus.Unknown;

    public List<Season> Seasons { get; set; } = [];
}
