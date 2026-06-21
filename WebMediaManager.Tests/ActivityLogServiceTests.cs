using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using WebMediaManager.Core.Domain;
using WebMediaManager.Data;
using WebMediaManager.Services;

namespace WebMediaManager.Tests;

public sealed class ActivityLogServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly TestContextFactory _factory;

    public ActivityLogServiceTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<MediaDbContext>().UseSqlite(_connection).Options;
        using (var db = new MediaDbContext(options))
        {
            db.Database.EnsureCreated();
        }
        _factory = new TestContextFactory(options);
    }

    private ActivityLogService NewService() => new(_factory, NullLogger<ActivityLogService>.Instance);

    [Fact]
    public async Task Logs_then_returns_newest_first()
    {
        var svc = NewService();
        await svc.LogAsync(ActivityCategory.Identify, ActivityStatus.Success, "Inception", "first");
        await svc.LogAsync(ActivityCategory.Rename, ActivityStatus.Failure, "Inception", "second");

        var entries = await svc.GetRecentAsync();

        Assert.Equal(2, entries.Count);
        Assert.Equal("second", entries[0].Message); // most recent first
        Assert.Equal("first", entries[1].Message);
    }

    [Fact]
    public async Task Filters_by_status()
    {
        var svc = NewService();
        await svc.LogAsync(ActivityCategory.Identify, ActivityStatus.Success, "A", "ok");
        await svc.LogAsync(ActivityCategory.Rename, ActivityStatus.Failure, "B", "boom");

        var failures = await svc.GetRecentAsync(status: ActivityStatus.Failure);

        Assert.Single(failures);
        Assert.Equal("B", failures[0].Subject);
        Assert.Equal(ActivityStatus.Failure, failures[0].Status);
    }

    [Fact]
    public async Task Take_caps_the_result_count()
    {
        var svc = NewService();
        for (var i = 0; i < 5; i++)
        {
            await svc.LogAsync(ActivityCategory.Scan, ActivityStatus.Success, "lib", $"scan {i}");
        }

        Assert.Equal(3, (await svc.GetRecentAsync(take: 3)).Count);
    }

    [Fact]
    public async Task Clear_removes_all()
    {
        var svc = NewService();
        await svc.LogAsync(ActivityCategory.Scan, ActivityStatus.Success, "lib", "done");

        await svc.ClearAsync();

        Assert.Empty(await svc.GetRecentAsync());
    }

    public void Dispose() => _connection.Dispose();

    private sealed class TestContextFactory(DbContextOptions<MediaDbContext> options)
        : IDbContextFactory<MediaDbContext>
    {
        public MediaDbContext CreateDbContext() => new(options);
    }
}
