namespace WebMediaManager.Core.Renaming;

/// <summary>
/// Turns requested moves into a validated, ordered plan: drops no-ops, orders deepest path first (so
/// children are renamed before their parent folders), and flags conflicts — targets escaping the root,
/// two moves colliding on one name, or a target that already exists and isn't itself being moved.
/// </summary>
public sealed class RenamePlanner
{
    public RenamePlan BuildPlan(string root, IEnumerable<PlannedMove> moves, IFileSystem fs)
    {
        var rootFull = Normalize(Path.GetFullPath(root));

        var effective = moves
            .Where(m => !string.Equals(m.FromRelative, m.ToRelative, StringComparison.Ordinal))
            .OrderByDescending(m => SegmentCount(m.FromRelative))
            .ToList();

        var sources = effective
            .Select(m => Combine(rootFull, m.FromRelative))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var claimedTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ops = new List<RenameOp>(effective.Count);

        foreach (var move in effective)
        {
            var fromAbs = Combine(rootFull, move.FromRelative);
            var toAbs = Combine(rootFull, move.ToRelative);

            string? conflict = null;
            if (!IsUnderRoot(rootFull, toAbs))
            {
                conflict = "Target escapes the library root.";
            }
            else if (!claimedTargets.Add(toAbs))
            {
                conflict = "Two items map to the same name.";
            }
            else if ((fs.FileExists(toAbs) || fs.DirectoryExists(toAbs)) && !sources.Contains(toAbs))
            {
                conflict = "Target already exists.";
            }

            ops.Add(new RenameOp(move.FromRelative, move.ToRelative, fromAbs, toAbs, move.Kind, conflict));
        }

        return new RenamePlan(ops);
    }

    private static int SegmentCount(string relative) =>
        relative.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries).Length;

    private static string Combine(string rootFull, string relative) =>
        Normalize(Path.GetFullPath(Path.Combine(rootFull, relative)));

    private static string Normalize(string path) =>
        path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static bool IsUnderRoot(string rootFull, string candidate) =>
        candidate.Equals(rootFull, StringComparison.OrdinalIgnoreCase)
        || candidate.StartsWith(rootFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
}
