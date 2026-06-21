using Microsoft.EntityFrameworkCore;
using WebMediaManager.Core.Domain;
using WebMediaManager.Data;
using WebMediaManager.Services;
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
    IActivityLogService activityLog,
    IDbContextFactory<MediaDbContext> factory,
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
        var libraryName = await ResolveLibraryNameAsync(request.LibraryId);

        try
        {
            progress.Set(new ScanProgress(request.LibraryId, ScanState.Running, 0, "Starting…", null));

            using var scope = scopeFactory.CreateScope();
            var scanner = scope.ServiceProvider.GetRequiredService<IMediaScanner>();
            await scanner.ScanAsync(request.LibraryId, request.Reimport, token);

            // Outcome logs use CancellationToken.None so they still record even when the scan was cancelled.
            await activityLog.LogAsync(ActivityCategory.Scan, ActivityStatus.Success, libraryName, "Scan completed");
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested && !stoppingToken.IsCancellationRequested)
        {
            progress.Set(progress.Get(request.LibraryId) with { State = ScanState.Cancelled, Current = null });
            await activityLog.LogAsync(ActivityCategory.Scan, ActivityStatus.Warning, libraryName, "Scan cancelled");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Scan failed for library {LibraryId}", request.LibraryId);
            progress.Set(progress.Get(request.LibraryId) with { State = ScanState.Failed, Error = ex.Message, Current = null });
            await activityLog.LogAsync(ActivityCategory.Scan, ActivityStatus.Failure, libraryName, $"Scan failed: {ex.Message}");
        }
        finally
        {
            progress.Release(request.LibraryId);
        }
    }

    private async Task<string> ResolveLibraryNameAsync(Guid libraryId)
    {
        try
        {
            await using var db = await factory.CreateDbContextAsync();
            var lib = await db.Libraries.AsNoTracking().FirstOrDefaultAsync(l => l.Id == libraryId);
            return lib?.Name ?? "library";
        }
        catch
        {
            return "library";
        }
    }
}
