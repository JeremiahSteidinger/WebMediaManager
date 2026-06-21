using Microsoft.EntityFrameworkCore;
using WebMediaManager.Core.Domain;
using WebMediaManager.Data;

namespace WebMediaManager.Services;

public interface IArtworkService
{
    /// <summary>Downloads any not-yet-fetched artwork for an item to disk next to the media. Best-effort.
    /// Returns the number of images successfully written this call.</summary>
    Task<int> DownloadForItemAsync(Guid itemId, CancellationToken ct = default);
}

public sealed class ArtworkService(
    HttpClient http,
    IDbContextFactory<MediaDbContext> factory,
    ISettingsService settings,
    ILogger<ArtworkService> logger) : IArtworkService
{
    public async Task<int> DownloadForItemAsync(Guid itemId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        var item = await db.MediaItems.FirstOrDefaultAsync(m => m.Id == itemId, ct);
        if (item is null)
        {
            return 0;
        }

        var library = await db.Libraries.FirstOrDefaultAsync(l => l.Id == item.LibraryId, ct);
        if (library is null)
        {
            return 0;
        }

        var pending = await db.Artworks
            .Where(a => a.MediaItemId == itemId && a.DownloadedUtc == null && a.SourceUrl != null)
            .ToListAsync(ct);
        if (pending.Count == 0)
        {
            return 0;
        }

        var cfg = (await settings.GetAsync(ct)).Artwork;
        var targetDir = ResolveItemDirectory(library.RootPath, item.RelativePath);

        try
        {
            Directory.CreateDirectory(targetDir);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Cannot create artwork directory {Dir}", targetDir);
            return 0;
        }

        var downloaded = 0;
        foreach (var art in pending)
        {
            ct.ThrowIfCancellationRequested();
            var fileName = art.Kind switch
            {
                ArtworkKind.Poster => cfg.PosterFilename,
                ArtworkKind.Fanart => cfg.FanartFilename,
                _ => $"{art.Kind.ToString().ToLowerInvariant()}.jpg",
            };

            try
            {
                var bytes = await http.GetByteArrayAsync(art.SourceUrl!, ct);
                var fullPath = Path.Combine(targetDir, fileName);
                await File.WriteAllBytesAsync(fullPath, bytes, ct);
                art.LocalRelativePath = Path.GetRelativePath(library.RootPath, fullPath);
                art.DownloadedUtc = DateTimeOffset.UtcNow;
                downloaded++;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to download artwork {Url}", art.SourceUrl);
            }
        }

        await db.SaveChangesAsync(ct);
        return downloaded;
    }

    /// <summary>Returns the on-disk directory for an item — its folder, or the parent of a loose file.</summary>
    private static string ResolveItemDirectory(string root, string relativePath)
    {
        var full = Path.Combine(root, relativePath);
        return Directory.Exists(full) ? full : Path.GetDirectoryName(full) ?? root;
    }
}
