using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using WebMediaManager.Core.Domain;
using WebMediaManager.Core.Nfo;
using WebMediaManager.Data;

namespace WebMediaManager.Services;

public interface INfoFileService
{
    /// <summary>Writes NFO sidecars for an item per the NFO settings. Best-effort.</summary>
    Task WriteForItemAsync(Guid itemId, CancellationToken ct = default);
}

public sealed class NfoFileService(
    IDbContextFactory<MediaDbContext> factory,
    ISettingsService settings,
    INfoWriter writer,
    ILogger<NfoFileService> logger) : INfoFileService
{
    public async Task WriteForItemAsync(Guid itemId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var nfo = (await settings.GetAsync(ct)).Nfo;

        var movie = await db.Movies.FirstOrDefaultAsync(m => m.Id == itemId, ct);
        if (movie is not null)
        {
            if (nfo.WriteMovieNfo)
            {
                var library = await db.Libraries.FirstAsync(l => l.Id == movie.LibraryId, ct);
                WriteMovie(movie, library.RootPath);
            }
            return;
        }

        var show = await db.TvShows.Include(s => s.Seasons).ThenInclude(se => se.Episodes)
            .FirstOrDefaultAsync(s => s.Id == itemId, ct);
        if (show is null)
        {
            return;
        }

        var showLibrary = await db.Libraries.FirstAsync(l => l.Id == show.LibraryId, ct);
        if (nfo.WriteTvShowNfo)
        {
            Save(writer.BuildTvShowNfo(show), Path.Combine(showLibrary.RootPath, show.RelativePath, "tvshow.nfo"));
        }
        if (nfo.WriteEpisodeNfo)
        {
            foreach (var episode in show.Seasons.SelectMany(s => s.Episodes))
            {
                var nfoRel = Path.ChangeExtension(episode.VideoFilePath, ".nfo");
                Save(writer.BuildEpisodeNfo(show, episode), Path.Combine(showLibrary.RootPath, nfoRel));
            }
        }
    }

    private void WriteMovie(Movie movie, string root)
    {
        var full = Path.Combine(root, movie.RelativePath);
        var path = Directory.Exists(full)
            ? Path.Combine(full, "movie.nfo")                      // folder-based: movie.nfo in the folder
            : Path.ChangeExtension(Path.Combine(root, movie.RelativePath), ".nfo"); // loose: <basename>.nfo
        Save(writer.BuildMovieNfo(movie), path);
    }

    private void Save(XDocument doc, string fullPath)
    {
        try
        {
            var dir = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }
            doc.Save(fullPath);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to write NFO {Path}", fullPath);
        }
    }
}
