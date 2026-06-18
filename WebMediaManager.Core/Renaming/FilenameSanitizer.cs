using System.Text;
using System.Text.RegularExpressions;

namespace WebMediaManager.Core.Renaming;

/// <summary>
/// Makes a single path segment safe as a file/folder name. Defaults to the cross-platform-safe set
/// (illegal on Windows too) since media often ends up on NAS/Windows shares.
/// </summary>
public static partial class FilenameSanitizer
{
    [GeneratedRegex(@"\s+", RegexOptions.Compiled)]
    private static partial Regex Whitespace();

    public static string Sanitize(string segment)
    {
        if (string.IsNullOrEmpty(segment))
        {
            return segment;
        }

        // Keep a readable separator where a colon was (e.g. "Title: Sub" -> "Title - Sub").
        var work = segment.Replace(":", " -").Replace('/', '-').Replace('\\', '-');

        var sb = new StringBuilder(work.Length);
        foreach (var c in work)
        {
            if (c is '<' or '>' or '"' or '|' or '?' or '*')
            {
                continue;
            }
            if (char.IsControl(c))
            {
                continue;
            }
            sb.Append(c);
        }

        var result = Whitespace().Replace(sb.ToString(), " ").Trim();
        // Windows disallows trailing dots/spaces on names.
        return result.TrimEnd('.', ' ');
    }
}
