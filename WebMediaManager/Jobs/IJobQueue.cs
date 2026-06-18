using System.Threading.Channels;

namespace WebMediaManager.Jobs;

/// <summary>A request to scan one library's root folder. <paramref name="Reimport"/> re-reads NFO/artwork into existing items.</summary>
public sealed record ScanRequest(Guid LibraryId, bool Reimport = false);

/// <summary>
/// Hands long-running work off the Blazor circuit to the background worker. UI enqueues and returns
/// immediately; the worker is the single DB writer for scans, which keeps SQLite off SQLITE_BUSY.
/// </summary>
public interface IJobQueue
{
    void EnqueueScan(Guid libraryId, bool reimport = false);

    ChannelReader<ScanRequest> Reader { get; }
}

public sealed class JobQueue : IJobQueue
{
    private readonly Channel<ScanRequest> _channel =
        Channel.CreateUnbounded<ScanRequest>(new UnboundedChannelOptions { SingleReader = true });

    public ChannelReader<ScanRequest> Reader => _channel.Reader;

    public void EnqueueScan(Guid libraryId, bool reimport = false) =>
        _channel.Writer.TryWrite(new ScanRequest(libraryId, reimport));
}
