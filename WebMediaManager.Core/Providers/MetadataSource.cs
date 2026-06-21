namespace WebMediaManager.Core.Providers;

/// <summary>
/// An online metadata source. IMDb has no public API, so that source is served by the OMDb API
/// (omdbapi.com), which returns IMDb's catalog keyed by IMDb ids.
/// </summary>
public enum MetadataSource
{
    None = 0,
    Tmdb = 1,
    Tvdb = 2,
    Imdb = 3,
}
