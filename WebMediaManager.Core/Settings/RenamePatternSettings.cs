namespace WebMediaManager.Core.Settings;

/// <summary>
/// User-editable filename/folder templates, one per context. Tokens (e.g. <c>{title}</c>, <c>{year}</c>,
/// <c>{tmdbid}</c>, <c>{seasonNr2}</c>) are expanded by the rename engine — none of this is hardcoded.
/// </summary>
public class RenamePatternSettings
{
    public string MovieFolder { get; set; } = "{title} ({year})";

    public string MovieFile { get; set; } = "{title} ({year})";

    public string ShowFolder { get; set; } = "{showtitle} ({year})";

    public string SeasonFolder { get; set; } = "Season {seasonNr2}";

    public string EpisodeFile { get; set; } = "{showtitle} - S{seasonNr2}E{episodeNr2} - {title}";
}
