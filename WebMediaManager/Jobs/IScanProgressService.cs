using System.Collections.Concurrent;

namespace WebMediaManager.Jobs;

/// <summary>
/// Singleton hub for scan progress. The worker publishes snapshots; Blazor components subscribe to
/// <see cref="Changed"/> and re-render. Also owns the per-library cancellation tokens.
/// </summary>
public interface IScanProgressService
{
    event Action<ScanProgress>? Changed;

    ScanProgress Get(Guid libraryId);

    void Set(ScanProgress progress);

    bool IsRunning(Guid libraryId);

    /// <summary>Creates and stores a cancellation token for a starting scan.</summary>
    CancellationToken Register(Guid libraryId);

    void RequestCancel(Guid libraryId);

    /// <summary>Disposes the token source for a finished scan.</summary>
    void Release(Guid libraryId);
}

public sealed class ScanProgressService : IScanProgressService
{
    private readonly ConcurrentDictionary<Guid, ScanProgress> _progress = new();
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _tokens = new();

    public event Action<ScanProgress>? Changed;

    public ScanProgress Get(Guid libraryId) =>
        _progress.TryGetValue(libraryId, out var p) ? p : ScanProgress.Idle(libraryId);

    public void Set(ScanProgress progress)
    {
        _progress[progress.LibraryId] = progress;
        Changed?.Invoke(progress);
    }

    public bool IsRunning(Guid libraryId) => Get(libraryId).IsActive;

    public CancellationToken Register(Guid libraryId)
    {
        var cts = new CancellationTokenSource();
        if (_tokens.TryRemove(libraryId, out var old))
        {
            old.Dispose();
        }
        _tokens[libraryId] = cts;
        return cts.Token;
    }

    public void RequestCancel(Guid libraryId)
    {
        if (_tokens.TryGetValue(libraryId, out var cts))
        {
            cts.Cancel();
        }
    }

    public void Release(Guid libraryId)
    {
        if (_tokens.TryRemove(libraryId, out var cts))
        {
            cts.Dispose();
        }
    }
}
