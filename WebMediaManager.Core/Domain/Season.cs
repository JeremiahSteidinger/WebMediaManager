namespace WebMediaManager.Core.Domain;

public class Season
{
    public Guid Id { get; set; }

    public Guid TvShowId { get; set; }

    public int SeasonNumber { get; set; }

    public string? Title { get; set; }

    /// <summary>Season folder, relative to <see cref="Library.RootPath"/> (null if episodes live in the show root).</summary>
    public string? RelativePath { get; set; }

    public List<Episode> Episodes { get; set; } = [];
}
