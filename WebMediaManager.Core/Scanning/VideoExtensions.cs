namespace WebMediaManager.Core.Scanning;

/// <summary>Recognized video container extensions used to identify media files during scanning.</summary>
public static class VideoExtensions
{
    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".mkv", ".mp4", ".avi", ".m4v", ".mov", ".wmv", ".ts", ".m2ts", ".mpg", ".mpeg", ".flv", ".webm",
    };

    public static bool IsVideo(string path) => All.Contains(Path.GetExtension(path));
}
