namespace WebMediaManager.Core.Providers;

/// <summary>
/// An online metadata source. v1 ships TMDB and TVDB; the enum leaves room for IMDb/OMDb later.
/// </summary>
public enum MetadataSource
{
    None = 0,
    Tmdb = 1,
    Tvdb = 2,
}
