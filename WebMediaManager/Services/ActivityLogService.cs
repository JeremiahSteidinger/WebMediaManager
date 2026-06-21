using Microsoft.EntityFrameworkCore;
using WebMediaManager.Core.Domain;
using WebMediaManager.Data;

namespace WebMediaManager.Services;

public interface IActivityLogService
{
    /// <summary>Records one action and its outcome. Best-effort: never throws, so it can't break the action it logs.</summary>
    Task LogAsync(ActivityCategory category, ActivityStatus status, string subject, string message,
        Guid? itemId = null, CancellationToken ct = default);

    /// <summary>Most recent entries first, optionally filtered by status.</summary>
    Task<IReadOnlyList<ActivityLog>> GetRecentAsync(int take = 200, ActivityStatus? status = null, CancellationToken ct = default);

    /// <summary>Removes all entries.</summary>
    Task ClearAsync(CancellationToken ct = default);
}

/// <summary>
/// Append-only record of actions the app takes. Registered as a singleton (it depends only on the
/// singleton context factory), so the scoped feature services and the singleton scan worker can all log.
/// </summary>
public sealed class ActivityLogService(
    IDbContextFactory<MediaDbContext> factory,
    ILogger<ActivityLogService> logger) : IActivityLogService
{
    public async Task LogAsync(ActivityCategory category, ActivityStatus status, string subject, string message,
        Guid? itemId = null, CancellationToken ct = default)
    {
        try
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            db.ActivityLogs.Add(new ActivityLog
            {
                Id = Guid.NewGuid(),
                TimestampUtc = DateTimeOffset.UtcNow,
                Category = category,
                Status = status,
                ItemId = itemId,
                Subject = Truncate(subject, 500),
                Message = Truncate(message, 2000),
            });
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            // A failure to log must never surface to (or undo) the action being logged.
            logger.LogWarning(ex, "Failed to write activity log entry ({Category}/{Status})", category, status);
        }
    }

    public async Task<IReadOnlyList<ActivityLog>> GetRecentAsync(int take = 200, ActivityStatus? status = null, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var query = db.ActivityLogs.AsNoTracking();
        if (status is not null)
        {
            query = query.Where(a => a.Status == status);
        }
        return await query.OrderByDescending(a => a.TimestampUtc).Take(take).ToListAsync(ct);
    }

    public async Task ClearAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await db.ActivityLogs.ExecuteDeleteAsync(ct);
    }

    private static string Truncate(string? value, int max) =>
        string.IsNullOrEmpty(value) ? string.Empty : value.Length <= max ? value : value[..max];
}
