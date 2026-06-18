namespace WebMediaManager.Core.Renaming;

public enum RenameMoveKind
{
    File,
    Folder,
}

/// <summary>A requested move, with both paths relative to the library root.</summary>
public sealed record PlannedMove(string FromRelative, string ToRelative, RenameMoveKind Kind);

/// <summary>A planned move resolved to absolute paths, with any blocking conflict noted.</summary>
public sealed record RenameOp(
    string FromRelative,
    string ToRelative,
    string FromAbsolute,
    string ToAbsolute,
    RenameMoveKind Kind,
    string? Conflict)
{
    public bool HasConflict => Conflict is not null;
}

public sealed record RenamePlan(IReadOnlyList<RenameOp> Ops)
{
    public bool HasConflicts => Ops.Any(o => o.HasConflict);

    public bool IsEmpty => Ops.Count == 0;
}

/// <summary>Minimal filesystem surface the planner/executor need; abstracted for testing.</summary>
public interface IFileSystem
{
    bool FileExists(string absolutePath);

    bool DirectoryExists(string absolutePath);

    void MoveFile(string fromAbsolute, string toAbsolute);

    void MoveDirectory(string fromAbsolute, string toAbsolute);
}
