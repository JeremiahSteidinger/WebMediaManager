namespace WebMediaManager.Core.Domain;

/// <summary>
/// The kind of media a <see cref="Library"/> contains. A library holds either movies or
/// TV shows, never both, because scanning and renaming rules differ between the two.
/// </summary>
public enum LibraryType
{
    Movies = 0,
    TvShows = 1,
}
