using WebMediaManager.Services.Scanning;

namespace WebMediaManager.Jobs;

/// <summary>
/// Single background consumer of the job queue. Runs scans off the Blazor circuit, in its own DI scope
/// (and thus its own DbContext), so it is the sole DB writer during a scan.
/// </summary>
public sealed class BackgroundJobWorker(
    IJobQueue queue,
    IServiceScopeFactory scopeFactory,
    IScanProgressService progress,
    ILogger<BackgroundJobWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var request in queue.Reader.ReadAllAsync(stoppingToken))
        {
            await RunScanAsync(request, stoppingToken);
        }
    }

    private async Task RunScanAsync(ScanRequest request, CancellationToken stoppingToken)
    {
        var libToken = progress.Register(request.LibraryId);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, libToken);
        var token = linked.Token;

        try
        {
            progress.Set(new ScanProgress(request.LibraryId, ScanState.Running, 0, "Starting…", null));

            using var scope = scopeFactory.CreateScope();
            var scanner = scope.ServiceProvider.GetRequiredService<IMediaScanner>();
            await scanner.ScanAsync(request.LibraryId, request.Reimport, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested && !stoppingToken.IsCancellationRequested)
        {
            progress.Set(progress.Get(request.LibraryId) with { State = ScanState.Cancelled, Current = null });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Scan failed for library {LibraryId}", request.LibraryId);
            progress.Set(progress.Get(request.LibraryId) with { State = ScanState.Failed, Error = ex.Message, Current = null });
        }
        finally
        {
            progress.Release(request.LibraryId);
        }
    }
}
