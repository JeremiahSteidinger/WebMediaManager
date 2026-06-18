using WebMediaManager.Core.Renaming;

namespace WebMediaManager.Services;

/// <summary>Real filesystem backing for the rename planner/executor.</summary>
public sealed class PhysicalFileSystem : IFileSystem
{
    public bool FileExists(string absolutePath) => File.Exists(absolutePath);

    public bool DirectoryExists(string absolutePath) => Directory.Exists(absolutePath);

    public void MoveFile(string fromAbsolute, string toAbsolute)
    {
        var dir = Path.GetDirectoryName(toAbsolute);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }
        File.Move(fromAbsolute, toAbsolute, overwrite: false);
    }

    public void MoveDirectory(string fromAbsolute, string toAbsolute)
    {
        var parent = Path.GetDirectoryName(toAbsolute);
        if (!string.IsNullOrEmpty(parent))
        {
            Directory.CreateDirectory(parent);
        }
        Directory.Move(fromAbsolute, toAbsolute);
    }
}
