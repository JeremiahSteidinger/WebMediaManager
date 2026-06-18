namespace WebMediaManager.Jobs;

public enum ScanState
{
    Idle,
    Queued,
    Running,
    Completed,
    Failed,
    Cancelled,
}

/// <summary>Immutable snapshot of a library's scan, published to the UI as it changes.</summary>
public sealed record ScanProgress(
    Guid LibraryId,
    ScanState State,
    int Found,
    string? Current,
    string? Error)
{
    public static ScanProgress Idle(Guid libraryId) => new(libraryId, ScanState.Idle, 0, null, null);

    public bool IsActive => State is ScanState.Queued or ScanState.Running;
}
