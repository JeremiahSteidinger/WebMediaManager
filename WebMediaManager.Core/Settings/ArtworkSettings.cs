namespace WebMediaManager.Core.Settings;

/// <summary>Toggles and filenames for artwork downloaded next to media (Kodi conventions).</summary>
public class ArtworkSettings
{
    public bool DownloadPoster { get; set; } = true;

    public bool DownloadFanart { get; set; } = true;

    public string PosterFilename { get; set; } = "poster.jpg";

    public string FanartFilename { get; set; } = "fanart.jpg";
}
