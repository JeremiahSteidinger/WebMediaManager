using WebMediaManager.Core.Domain;
using WebMediaManager.Services;

namespace WebMediaManager.Tests;

/// <summary>No-op activity log for tests that don't assert on logging.</summary>
internal sealed class NoopActivityLog : IActivityLogService
{
    public Task LogAsync(ActivityCategory category, ActivityStatus status, string subject, string message,
        Guid? itemId = null, CancellationToken ct = default) => Task.CompletedTask;

    public Task<IReadOnlyList<ActivityLog>> GetRecentAsync(int take = 200, ActivityStatus? status = null, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<ActivityLog>>([]);

    public Task ClearAsync(CancellationToken ct = default) => Task.CompletedTask;
}
