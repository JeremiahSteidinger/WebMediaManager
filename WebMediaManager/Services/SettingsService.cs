using Microsoft.EntityFrameworkCore;
using WebMediaManager.Core.Settings;
using WebMediaManager.Data;

namespace WebMediaManager.Services;

public sealed class SettingsService(IDbContextFactory<MediaDbContext> factory) : ISettingsService
{
    public async Task<AppSettings> GetAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var settings = await db.Settings.FirstOrDefaultAsync(s => s.Id == AppSettings.SingletonId, ct);
        if (settings is null)
        {
            settings = new AppSettings();
            db.Settings.Add(settings);
            await db.SaveChangesAsync(ct);
        }
        return settings;
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        settings.Id = AppSettings.SingletonId;
        db.Settings.Update(settings);
        await db.SaveChangesAsync(ct);
    }
}
