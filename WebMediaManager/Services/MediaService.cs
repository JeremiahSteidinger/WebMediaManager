using Microsoft.EntityFrameworkCore;
using WebMediaManager.Core.Domain;
using WebMediaManager.Data;

namespace WebMediaManager.Services;

public sealed class MediaService(IDbContextFactory<MediaDbContext> factory) : IMediaService
{
    public async Task<IReadOnlyList<MediaItem>> GetItemsAsync(Guid libraryId, MatchState? state = null, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var query = db.MediaItems.AsNoTracking().Where(m => m.LibraryId == libraryId);
        if (state is not null)
        {
            query = query.Where(m => m.MatchState == state);
        }
        return await query.OrderBy(m => m.Title).ToListAsync(ct);
    }

    public async Task<MediaItem?> GetItemAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        var movie = await db.Movies.AsNoTracking().FirstOrDefaultAsync(m => m.Id == id, ct);
        if (movie is not null)
        {
            return movie;
        }

        return await db.TvShows.AsNoTracking()
            .Include(s => s.Seasons.OrderBy(se => se.SeasonNumber))
            .ThenInclude(se => se.Episodes.OrderBy(e => e.EpisodeNumber))
            .FirstOrDefaultAsync(s => s.Id == id, ct);
    }

    public async Task<IReadOnlyDictionary<Guid, int>> GetCountsByLibraryAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var counts = await db.MediaItems
            .GroupBy(m => m.LibraryId)
            .Select(g => new { LibraryId = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        return counts.ToDictionary(x => x.LibraryId, x => x.Count);
    }

    public async Task<(int Movies, int Shows)> GetTotalsAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var movies = await db.Movies.CountAsync(ct);
        var shows = await db.TvShows.CountAsync(ct);
        return (movies, shows);
    }
}
