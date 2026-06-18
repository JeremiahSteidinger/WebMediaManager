namespace WebMediaManager.Core.Domain;

public class Movie : MediaItem
{
    /// <summary>Main video file, relative to <see cref="Library.RootPath"/>.</summary>
    public string? VideoFilePath { get; set; }

    public int? Runtime { get; set; }

    public DateOnly? ReleaseDate { get; set; }
}
