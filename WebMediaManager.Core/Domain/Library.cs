namespace WebMediaManager.Core.Domain;

/// <summary>
/// A root folder of media (movies or TV shows) that the app scans and manages.
/// <see cref="RootPath"/> is a path inside the running process — under Docker that is the
/// container-side mount point (e.g. "/media/movies"), configured by the user in Settings.
/// </summary>
public class Library
{
    public Guid Id { get; set; }

    /// <summary>Display name shown in the UI.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Whether this library holds movies or TV shows.</summary>
    public LibraryType Type { get; set; }

    /// <summary>
    /// Absolute path to the library root as seen by this process. Item paths are stored
    /// relative to this so the library survives the volume being remounted elsewhere.
    /// </summary>
    public string RootPath { get; set; } = string.Empty;

    public DateTimeOffset CreatedUtc { get; set; }
}
