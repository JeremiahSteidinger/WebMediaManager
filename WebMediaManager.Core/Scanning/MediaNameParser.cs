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

    /// <summary>Parses an episode file name into season + episode number(s), or null if it has no marker.</summary>
    public static ParsedEpisode? ParseEpisode(string fileName)
    {
        var work = StripVideoExtension(fileName);

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

        var alt = AltSeasonEpisode().Match(work);
        if (alt.Success)
        {
            return new ParsedEpisode(int.Parse(alt.Groups[1].Value), [int.Parse(alt.Groups[2].Value)]);
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
