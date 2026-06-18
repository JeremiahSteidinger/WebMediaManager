using WebMediaManager.Core.Settings;

namespace WebMediaManager.Services;

public interface ISettingsService
{
    /// <summary>Returns the singleton settings row, creating it with defaults on first access.</summary>
    Task<AppSettings> GetAsync(CancellationToken ct = default);

    Task SaveAsync(AppSettings settings, CancellationToken ct = default);
}
