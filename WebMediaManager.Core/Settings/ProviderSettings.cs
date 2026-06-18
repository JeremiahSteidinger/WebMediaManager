using WebMediaManager.Core.Providers;

namespace WebMediaManager.Core.Settings;

/// <summary>
/// Credentials and source preferences for the metadata providers. Entered in the Settings UI and
/// read at request time, so changing a key takes effect without a restart.
/// </summary>
public class ProviderSettings
{
    public string? TmdbApiKey { get; set; }

    public string? TvdbApiKey { get; set; }

    /// <summary>Optional subscriber PIN required by licensed TVDB v4 keys.</summary>
    public string? TvdbPin { get; set; }

    public MetadataSource PreferredMovieSource { get; set; } = MetadataSource.Tmdb;

    public MetadataSource PreferredTvSource { get; set; } = MetadataSource.Tmdb;
}
