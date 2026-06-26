using System.Text.RegularExpressions;

namespace WebMediaManager.Core.Scanning;

public readonly record struct ParsedMovie(string Title, int? Year);

public sealed record ParsedEpisode(int Season, IReadOnlyList<int> Episodes);

/// <summary>
/// Pure, filesystem-free parsing of release-style file and folder names. Best-effort heuristics — the
/// user corrects anything wrong via the Identify flow — but covers the common conventions.
/// </summary>
public static partial class MediaNameParser
{
    [GeneratedRegex(@"\((19|20)\d{2}\)", RegexOptions.Compiled)]
    private static partial Regex ParenYear();

    [GeneratedRegex(@"(?<![0-9])(19|20)\d{2}(?![0-9])", RegexOptions.Compiled)]
    private static partial Regex BareYear();

    // S01E02, s1e2, with optional separators, plus trailing E03/E04 for multi-episode files.
    [GeneratedRegex(@"[Ss](\d{1,2})[\s._-]*[Ee](\d{1,3})((?:[\s._-]*[Ee]\d{1,3})*)", RegexOptions.Compiled)]
    private static partial Regex SeasonEpisode();

    // 1x02 style.
    [GeneratedRegex(@"(?<!\d)(\d{1,2})x(\d{1,3})(?!\d)", RegexOptions.Compiled)]
    private static partial Regex AltSeasonEpisode();

    [GeneratedRegex(@"[Ee](\d{1,3})", RegexOptions.Compiled)]
    private static partial Regex ExtraEpisode();

    // Season-less episode marker: E02, e02, Ep 2, Episode 2, with optional trailing E03/E04 for multi-files.
    // The leading boundary stops it firing on a letter mid-word (the 'e' in "Code 9").
    [GeneratedRegex(@"(?<![A-Za-z0-9])[Ee](?:p(?:isode)?)?[\s._-]*(\d{1,3})((?:[\s._-]*[Ee]\d{1,3})*)", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex EpisodeMarker();

    // Bare leading episode number: "01 - Title", "01.Title", "01-02 Title" (range). Must be followed by a
    // separator or end so a 4-digit year ("2009 ...") can't masquerade as an episode.
    [GeneratedRegex(@"^(\d{1,3})(?:[\s._-]*-[\s._-]*(\d{1,3}))?(?=[\s._-]|$)", RegexOptions.Compiled)]
    private static partial Regex LeadingEpisode();

    // Season folder names: "Season 1", "Season 01", "S1", "S01", "Series 2", "Season.3".
    [GeneratedRegex(@"^(?:season|series|s)[\s._-]*(\d{1,4})$", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex SeasonFolder();

    // A folder named with nothing but a number ("1", "03") is treated as that season.
    [GeneratedRegex(@"^\d{1,4}$", RegexOptions.Compiled)]
    private static partial Regex BareNumberFolder();

    /// <summary>Parses a movie folder or file name into a title and (optional) year.</summary>
    public static ParsedMovie ParseMovie(string name)
    {
        var work = StripVideoExtension(name);

        // A parenthesized year is the strongest signal and wins over digits in the title.
        var paren = ParenYear().Match(work);
        if (paren.Success)
        {
            var year = int.Parse(paren.Value.Trim('(', ')'));
            return new ParsedMovie(CleanTitle(work[..paren.Index]), year);
        }

        // Otherwise the first standalone 19xx/20xx token marks the boundary between title and tags.
        var bare = BareYear().Match(work);
        if (bare.Success && bare.Index > 0)
        {
            var year = int.Parse(bare.Value);
            return new ParsedMovie(CleanTitle(work[..bare.Index]), year);
        }

        return new ParsedMovie(CleanTitle(work), null);
    }

    /// <summary>Parses the number out of a season folder name, or null if it isn't one.</summary>
    /// <remarks>Recognizes "Season 1", "Season 01", "S1", "S01", "Series 2", a bare number ("3"),
    /// and "Specials" (season 0) — the conventions Jellyfin/Kodi libraries use.</remarks>
    public static int? ParseSeasonFolder(string folderName)
    {
        var name = folderName.Trim();
        if (name.Equals("specials", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        var m = SeasonFolder().Match(name);
        if (m.Success)
        {
            return int.Parse(m.Groups[1].Value);
        }

        return BareNumberFolder().IsMatch(name) ? int.Parse(name) : null;
    }

    /// <summary>Parses an episode file name into season + episode number(s), or null if it has no marker.</summary>
    /// <param name="seasonFolderName">
    /// Name of the containing season folder (e.g. "Season 03"), used to supply the season for files that
    /// don't carry one themselves ("01 - Title.mp4", "E02.mkv"). Without it, only filenames with an explicit
    /// season marker (S03E01, 3x01) are recognized — a leading number in a bare series root is too ambiguous.
    /// </param>
    public static ParsedEpisode? ParseEpisode(string fileName, string? seasonFolderName = null)
    {
        var work = StripVideoExtension(fileName);

        // 1. Explicit SxxExx — authoritative; the season comes straight from the filename.
        var m = SeasonEpisode().Match(work);
        if (m.Success)
        {
            var season = int.Parse(m.Groups[1].Value);
            var episodes = new List<int> { int.Parse(m.Groups[2].Value) };
            foreach (Match extra in ExtraEpisode().Matches(m.Groups[3].Value))
            {
                episodes.Add(int.Parse(extra.Groups[1].Value));
            }
            return new ParsedEpisode(season, episodes);
        }

        // 2. 1x02 style — also self-contained.
        var alt = AltSeasonEpisode().Match(work);
        if (alt.Success)
        {
            return new ParsedEpisode(int.Parse(alt.Groups[1].Value), [int.Parse(alt.Groups[2].Value)]);
        }

        // The remaining forms carry no season, so they only fire when a season folder anchors one.
        if (seasonFolderName is null || ParseSeasonFolder(seasonFolderName) is not int folderSeason)
        {
            return null;
        }

        // 3. Episode-only marker inside the season folder: "E02", "Episode 2", "Ep.2" (+ trailing E03 for multi).
        var em = EpisodeMarker().Match(work);
        if (em.Success)
        {
            var episodes = new List<int> { int.Parse(em.Groups[1].Value) };
            foreach (Match extra in ExtraEpisode().Matches(em.Groups[2].Value))
            {
                episodes.Add(int.Parse(extra.Groups[1].Value));
            }
            return new ParsedEpisode(folderSeason, episodes);
        }

        // 4. Bare leading episode number: "01 - Title", "01.Title", "01-02 Title".
        var lead = LeadingEpisode().Match(work);
        if (lead.Success)
        {
            var episodes = new List<int> { int.Parse(lead.Groups[1].Value) };
            if (lead.Groups[2].Success)
            {
                episodes.Add(int.Parse(lead.Groups[2].Value));
            }
            return new ParsedEpisode(folderSeason, episodes);
        }

        return null;
    }

    private static string StripVideoExtension(string name)
    {
        var ext = Path.GetExtension(name);
        return VideoExtensions.All.Contains(ext) ? Path.GetFileNameWithoutExtension(name) : name;
    }

    private static string CleanTitle(string raw)
    {
        // Separators used in release names become spaces; brackets/punctuation at the edges are trimmed.
        var spaced = raw.Replace('.', ' ').Replace('_', ' ');
        spaced = Regex.Replace(spaced, @"\s+", " ");
        return spaced.Trim().Trim('-', '(', ')', '[', ']', ' ');
    }
}
