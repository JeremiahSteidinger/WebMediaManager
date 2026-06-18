namespace WebMediaManager.Core.Settings;

/// <summary>Toggles for Kodi/Jellyfin-compatible NFO sidecar generation.</summary>
public class NfoSettings
{
    public bool WriteMovieNfo { get; set; } = true;

    public bool WriteTvShowNfo { get; set; } = true;

    public bool WriteEpisodeNfo { get; set; } = true;
}
