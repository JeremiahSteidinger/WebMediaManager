using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using WebMediaManager.Core.Domain;
using WebMediaManager.Core.Nfo;
using WebMediaManager.Data;

namespace WebMediaManager.Services;

public interface INfoFileService
{
    /// <summary>Writes NFO sidecars for an item per the NFO settings. Best-effort.
    /// Returns the number of NFO files successfully written this call.</summary>
    Task<int> WriteForItemAsync(Guid itemId, CancellationToken ct = default);
}

public sealed class NfoFileService(
    IDbContextFactory<MediaDbContext> factory,
    ISettingsService settings,
    INfoWriter writer,
    ILogger<NfoFileService> logger) : INfoFileService
{
    public async Task<int> WriteForItemAsync(Guid itemId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var nfo = (await settings.GetAsync(ct)).Nfo;

        var movie = await db.Movies.FirstOrDefaultAsync(m => m.Id == itemId, ct);
        if (movie is not null)
        {
            if (nfo.WriteMovieNfo)
            {
                var library = await db.Libraries.FirstAsync(l => l.Id == movie.LibraryId, ct);
                return WriteMovie(movie, library.RootPath) ? 1 : 0;
            }
            return 0;
        }

        var show = await db.TvShows.Include(s => s.Seasons).ThenInclude(se => se.Episodes)
            .FirstOrDefaultAsync(s => s.Id == itemId, ct);
        if (show is null)
        {
            return 0;
        }

        var written = 0;
        var showLibrary = await db.Libraries.FirstAsync(l => l.Id == show.LibraryId, ct);
        if (nfo.WriteTvShowNfo)
        {
            if (Save(writer.BuildTvShowNfo(show), Path.Combine(showLibrary.RootPath, show.RelativePath, "tvshow.nfo")))
            {
                written++;
            }
        }
        if (nfo.WriteEpisodeNfo)
        {
            foreach (var episode in show.Seasons.SelectMany(s => s.Episodes))
            {
                var nfoRel = Path.ChangeExtension(episode.VideoFilePath, ".nfo");
                if (Save(writer.BuildEpisodeNfo(show, episode), Path.Combine(showLibrary.RootPath, nfoRel)))
                {
                    written++;
                }
            }
        }
        return written;
    }

    private bool WriteMovie(Movie movie, string root)
    {
        var full = Path.Combine(root, movie.RelativePath);
        var path = Directory.Exists(full)
            ? Path.Combine(full, "movie.nfo")                      // folder-based: movie.nfo in the folder
            : Path.ChangeExtension(Path.Combine(root, movie.RelativePath), ".nfo"); // loose: <basename>.nfo
        return Save(writer.BuildMovieNfo(movie), path);
    }

    private bool Save(XDocument doc, string fullPath)
    {
        try
        {
            var dir = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }
            doc.Save(fullPath);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to write NFO {Path}", fullPath);
            return false;
        }
    }
}
