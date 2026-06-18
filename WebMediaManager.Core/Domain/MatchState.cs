namespace WebMediaManager.Core.Domain;

/// <summary>
/// Tracks whether a scanned item has been identified against an online source yet.
/// Scanning creates <see cref="Unmatched"/> rows; identifying flips them to <see cref="Matched"/>.
/// </summary>
public enum MatchState
{
    Unmatched = 0,
    Matched = 1,
    Ignored = 2,
}
