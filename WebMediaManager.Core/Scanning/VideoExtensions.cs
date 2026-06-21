namespace WebMediaManager.Core.Scanning;

/// <summary>Recognized video container extensions used to identify media files during scanning.</summary>
public static class VideoExtensions
{
    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".mkv", ".mp4", ".avi", ".m4v", ".mov", ".wmv", ".ts", ".m2ts", ".mpg", ".mpeg", ".flv", ".webm",
    };

    /// <summary>Containers an HTML5 &lt;video&gt; element plays natively across mainstream browsers (no transcoding).</summary>
    private static readonly IReadOnlySet<string> BrowserPlayable = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".m4v", ".webm",
    };

    public static bool IsVideo(string path) => All.Contains(Path.GetExtension(path));

    /// <summary>True when the container generally plays inline in a browser without transcoding.</summary>
    public static bool IsBrowserPlayable(string path) => BrowserPlayable.Contains(Path.GetExtension(path));

    /// <summary>Best-guess MIME type for the <c>Content-Type</c> header and the &lt;video&gt; source.</summary>
    public static string ContentType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".mp4" or ".m4v" => "video/mp4",
        ".webm" => "video/webm",
        ".mov" => "video/quicktime",
        ".mkv" => "video/x-matroska",
        ".avi" => "video/x-msvideo",
        ".wmv" => "video/x-ms-wmv",
        ".flv" => "video/x-flv",
        ".ts" or ".m2ts" => "video/mp2t",
        ".mpg" or ".mpeg" => "video/mpeg",
        _ => "application/octet-stream",
    };
}
