namespace WebMediaManager.Services;

/// <summary>A single sub-folder shown in the folder picker.</summary>
public sealed record DirectoryEntry(string Name, string FullPath);

/// <summary>
/// The folders directly under <paramref name="CurrentPath"/>. <paramref name="CurrentPath"/> is null when
/// listing the roots ("Computer"); <paramref name="ParentPath"/> is null at a root (drive / "/").
/// </summary>
public sealed record DirectoryListing(
    string? CurrentPath,
    string? ParentPath,
    IReadOnlyList<DirectoryEntry> Directories);

public interface IDirectoryBrowserService
{
    /// <summary>
    /// Lists the immediate sub-folders of <paramref name="path"/>. A null/blank or non-existent path returns
    /// the filesystem roots. Folders that can't be read are skipped rather than throwing.
    /// </summary>
    DirectoryListing List(string? path);

    /// <summary>True if <paramref name="path"/> is a folder that currently exists on the server.</summary>
    bool Exists(string? path);
}

/// <summary>
/// Browses the server's filesystem for the folder picker. The app's "Root path" is a server-side path
/// (e.g. a container mount point), so picking has to happen here, not in the browser.
/// </summary>
public sealed class DirectoryBrowserService : IDirectoryBrowserService
{
    public DirectoryListing List(string? path)
    {
        // No path (or a typed path that doesn't exist) → start at the roots.
        if (string.IsNullOrWhiteSpace(path))
        {
            return ListRoots();
        }

        var full = Path.GetFullPath(path.Trim());
        if (!Directory.Exists(full))
        {
            return ListRoots();
        }

        var dirs = new List<DirectoryEntry>();
        try
        {
            foreach (var dir in Directory.EnumerateDirectories(full))
            {
                var name = Path.GetFileName(dir);
                dirs.Add(new DirectoryEntry(string.IsNullOrEmpty(name) ? dir : name, dir));
            }
        }
        catch (UnauthorizedAccessException)
        {
            // No permission to enumerate — show the folder with no children rather than failing.
        }
        catch (IOException)
        {
            // Drive not ready / transient I/O — same graceful fallback.
        }

        dirs.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));

        var parent = Directory.GetParent(full)?.FullName;
        return new DirectoryListing(full, parent, dirs);
    }

    public bool Exists(string? path) =>
        !string.IsNullOrWhiteSpace(path) && Directory.Exists(path.Trim());

    private static DirectoryListing ListRoots()
    {
        var roots = new List<DirectoryEntry>();
        if (OperatingSystem.IsWindows())
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                try
                {
                    if (drive.IsReady)
                    {
                        roots.Add(new DirectoryEntry(drive.Name, drive.RootDirectory.FullName));
                    }
                }
                catch (IOException)
                {
                    // Skip drives that fault on the readiness check.
                }
            }
            roots.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        }
        else
        {
            // On Linux/macOS (the Docker deployment) there's a single root the user drills down from.
            roots.Add(new DirectoryEntry("/", "/"));
        }

        return new DirectoryListing(null, null, roots);
    }
}
