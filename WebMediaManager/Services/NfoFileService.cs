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

    /// <summary>Writes the episode NFO for every episode of a single season (subject to the
    /// episode-NFO setting). Returns the number written. Best-effort.</summary>
    Task<int> WriteForSeasonAsync(Guid seasonId, CancellationToken ct = default);

    /// <summary>Writes the episode NFO for a single episode (subject to the episode-NFO setting).
    /// Returns 1 if written, else 0. Best-effort.</summary>
    Task<int> WriteForEpisodeAsync(Guid episodeId, CancellationToken ct = default);
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
                if (WriteEpisode(show, episode, showLibrary.RootPath))
                {
                    written++;
                }
            }
        }
        return written;
    }

    public async Task<int> WriteForSeasonAsync(Guid seasonId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        if (!(await settings.GetAsync(ct)).Nfo.WriteEpisodeNfo)
        {
            return 0;
        }

        var season = await db.Seasons.Include(s => s.Episodes).FirstOrDefaultAsync(s => s.Id == seasonId, ct);
        if (season is null)
        {
            return 0;
        }

        var show = await db.TvShows.FirstAsync(s => s.Id == season.TvShowId, ct);
        var root = (await db.Libraries.FirstAsync(l => l.Id == show.LibraryId, ct)).RootPath;
        var written = 0;
        foreach (var episode in season.Episodes)
        {
            if (WriteEpisode(show, episode, root))
            {
                written++;
            }
        }
        return written;
    }

    public async Task<int> WriteForEpisodeAsync(Guid episodeId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        if (!(await settings.GetAsync(ct)).Nfo.WriteEpisodeNfo)
        {
            return 0;
        }

        var episode = await db.Episodes.FirstOrDefaultAsync(e => e.Id == episodeId, ct);
        if (episode is null)
        {
            return 0;
        }

        var season = await db.Seasons.FirstAsync(s => s.Id == episode.SeasonId, ct);
        var show = await db.TvShows.FirstAsync(s => s.Id == season.TvShowId, ct);
        var root = (await db.Libraries.FirstAsync(l => l.Id == show.LibraryId, ct)).RootPath;
        return WriteEpisode(show, episode, root) ? 1 : 0;
    }

    private bool WriteEpisode(TvShow show, Episode episode, string root)
    {
        var nfoRel = Path.ChangeExtension(episode.VideoFilePath, ".nfo");
        return Save(writer.BuildEpisodeNfo(show, episode), Path.Combine(root, nfoRel));
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
