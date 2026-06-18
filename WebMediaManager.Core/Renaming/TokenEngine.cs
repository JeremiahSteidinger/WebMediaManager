using System.Text.RegularExpressions;

namespace WebMediaManager.Core.Renaming;

public interface ITokenEngine
{
    /// <summary>Expands a pattern against token values. Pure — no filesystem, no IO.</summary>
    string Render(string pattern, IReadOnlyDictionary<string, string?> tokens);
}

/// <summary>
/// Expands <c>{token}</c> placeholders. Text wrapped in <c>&lt;...&gt;</c> is an optional group: if any
/// token inside it is empty/missing, the whole group (including its literal text) is dropped — so a
/// pattern like <c>{title} &lt;[tmdb-{tmdbid}]&gt;</c> leaves no dangling <c>[tmdb-]</c> when the id is
/// unknown. Angle brackets are illegal in filenames, so they never collide with literal text.
/// </summary>
public sealed partial class TokenEngine : ITokenEngine
{
    [GeneratedRegex(@"<([^<>]*)>", RegexOptions.Compiled)]
    private static partial Regex OptionalGroup();

    [GeneratedRegex(@"\{(\w+)\}", RegexOptions.Compiled)]
    private static partial Regex Token();

    [GeneratedRegex(@"\s+", RegexOptions.Compiled)]
    private static partial Regex Whitespace();

    public string Render(string pattern, IReadOnlyDictionary<string, string?> tokens)
    {
        if (string.IsNullOrEmpty(pattern))
        {
            return string.Empty;
        }

        var withGroups = OptionalGroup().Replace(pattern, match =>
        {
            var inner = match.Groups[1].Value;
            var hasToken = false;
            var anyEmpty = false;
            foreach (Match token in Token().Matches(inner))
            {
                hasToken = true;
                if (string.IsNullOrEmpty(Lookup(tokens, token.Groups[1].Value)))
                {
                    anyEmpty = true;
                    break;
                }
            }
            return hasToken && anyEmpty ? string.Empty : Substitute(inner, tokens);
        });

        return Collapse(Substitute(withGroups, tokens));
    }

    private static string Substitute(string text, IReadOnlyDictionary<string, string?> tokens) =>
        Token().Replace(text, m => Lookup(tokens, m.Groups[1].Value) ?? string.Empty);

    private static string? Lookup(IReadOnlyDictionary<string, string?> tokens, string name) =>
        tokens.TryGetValue(name, out var value) ? value : string.Empty;

    private static string Collapse(string text) => Whitespace().Replace(text, " ").Trim();
}
