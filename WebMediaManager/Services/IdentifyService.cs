using Microsoft.EntityFrameworkCore;
using WebMediaManager.Core.Domain;
using WebMediaManager.Core.Providers;
using WebMediaManager.Data;
using WebMediaManager.Providers;

namespace WebMediaManager.Services;

public interface IIdentifyService
{
    Task<IReadOnlyList<MetadataSearchResult>> SearchAsync(
        MetadataSource source, string query, int? year, LibraryType type, CancellationToken ct = default);

    /// <summary>Looks up a single work by its exact provider ID (e.g. a known TMDB id), returning it as a result.</summary>
    Task<MetadataSearchResult?> LookupByIdAsync(
        MetadataSource source, string providerId, LibraryType type, CancellationToken ct = default);

    /// <summary>Links a chosen search result to an item: fetches details, applies metadata, marks it Matched.</summary>
    Task LinkAsync(Guid itemId, MetadataSearchResult result, CancellationToken ct = default);
}

public sealed class IdentifyService(
    IMetadataProviderResolver resolver,
    IDbContextFactory<MediaDbContext> factory,
    ISettingsService settings,
    IArtworkService artwork,
    INfoFileService nfo,
    ILogger<IdentifyService> logger) : IIdentifyService
{
    public async Task<IReadOnlyList<MetadataSearchResult>> SearchAsync(
        MetadataSource source, string query, int? year, LibraryType type, CancellationToken ct = default)
    {
        var provider = resolver.Get(source)
            ?? throw new MetadataException($"Provider {source} is not available.");
        return await provider.SearchAsync(query, year, type, ct);
    }

    public async Task<MetadataSearchResult?> LookupByIdAsync(
        MetadataSource source, string providerId, LibraryType type, CancellationToken ct = default)
    {
        var provider = resolver.Get(source)
            ?? throw new MetadataException($"Provider {source} is not available.");

        if (type == LibraryType.Movies)
        {
            var movie = await provider.GetMovieAsync(providerId, ct);
            return movie is null
                ? null
                : new MetadataSearchResult(source, movie.ProviderId, movie.Title, movie.Year, movie.Plot, movie.PosterUrl);
        }

        var show = await provider.GetTvShowAsync(providerId, ct);
        return show is null
            ? null
            : new MetadataSearchResult(source, show.ProviderId, show.Title, show.Year, show.Plot, show.PosterUrl);
    }

    public async Task LinkAsync(Guid itemId, MetadataSearchResult result, CancellationToken ct = default)
    {
        var provider = resolver.Get(result.Source)
            ?? throw new MetadataException($"Provider {result.Source} is not available.");

        await using var db = await factory.CreateDbContextAsync(ct);
        var item = await db.MediaItems.FirstOrDefaultAsync(m => m.Id == itemId, ct)
            ?? throw new InvalidOperationException("Item not found.");

        string? posterUrl = null;
        string? fanartUrl = null;
        switch (item)
        {
            case Movie movie:
                var movieMeta = await provider.GetMovieAsync(result.ProviderId, ct)
                    ?? throw new MetadataException("No movie details returned by the provider.");
                ApplyMovie(movie, movieMeta);
                posterUrl = movieMeta.PosterUrl;
                fanartUrl = movieMeta.FanartUrl;
                break;

            case TvShow show:
                var showMeta = await provider.GetTvShowAsync(result.ProviderId, ct)
                    ?? throw new MetadataException("No show details returned by the provider.");
                ApplyShow(show, showMeta);
                posterUrl = showMeta.PosterUrl;
                fanartUrl = showMeta.FanartUrl;
                await EnrichEpisodesAsync(db, show, provider, result.ProviderId, ct);
                break;
        }

        item.PrimaryProvider = result.Source;
        item.MatchState = MatchState.Matched;

        await RefreshArtworkRowsAsync(db, itemId, posterUrl, fanartUrl, ct);
        await db.SaveChangesAsync(ct);

        // Side effects below are best-effort: a failure must never undo the match.
        try
        {
            await artwork.DownloadForItemAsync(itemId, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Artwork download failed for item {ItemId}", itemId);
        }

        try
        {
            await nfo.WriteForItemAsync(itemId, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "NFO write failed for item {ItemId}", itemId);
        }
    }

    private async Task EnrichEpisodesAsync(MediaDbContext db, TvShow show, IMetadataProvider provider, string providerId, CancellationToken ct)
    {
        try
        {
            await db.Entry(show).Collection(s => s.Seasons).Query().Include(se => se.Episodes).LoadAsync(ct);

            foreach (var seasonNumber in show.Seasons.Select(s => s.SeasonNumber).Distinct())
            {
                var episodes = await provider.GetEpisodesAsync(providerId, seasonNumber, ct);
                if (episodes.Count == 0)
                {
                    continue;
                }

                var byNumber = episodes.ToDictionary(e => (e.Season, e.Episode));
                var scanned = show.Seasons.Where(s => s.SeasonNumber == seasonNumber).SelectMany(s => s.Episodes);
                foreach (var ep in scanned)
                {
                    if (byNumber.TryGetValue((ep.SeasonNumber, ep.EpisodeNumber), out var meta))
                    {
                        ep.Title = meta.Title ?? ep.Title;
                        ep.Plot = meta.Plot ?? ep.Plot;
                        ep.AirDate = meta.AirDate ?? ep.AirDate;
                        ep.TmdbId ??= meta.TmdbId;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Episode metadata enrichment failed for show {ShowId}", show.Id);
        }
    }

    private async Task RefreshArtworkRowsAsync(MediaDbContext db, Guid itemId, string? posterUrl, string? fanartUrl, CancellationToken ct)
    {
        var cfg = (await settings.GetAsync(ct)).Artwork;

        var existing = await db.Artworks.Where(a => a.MediaItemId == itemId).ToListAsync(ct);
        db.Artworks.RemoveRange(existing);

        if (cfg.DownloadPoster && posterUrl is not null)
        {
            db.Artworks.Add(new Artwork { Id = Guid.NewGuid(), MediaItemId = itemId, Kind = ArtworkKind.Poster, SourceUrl = posterUrl });
        }
        if (cfg.DownloadFanart && fanartUrl is not null)
        {
            db.Artworks.Add(new Artwork { Id = Guid.NewGuid(), MediaItemId = itemId, Kind = ArtworkKind.Fanart, SourceUrl = fanartUrl });
        }
    }

    private static void ApplyMovie(Movie movie, MovieMetadata meta)
    {
        movie.Title = meta.Title;
        movie.OriginalTitle = meta.OriginalTitle;
        movie.Year = meta.Year;
        movie.ReleaseDate = meta.ReleaseDate;
        movie.Plot = meta.Plot;
        movie.Tagline = meta.Tagline;
        movie.Runtime = meta.Runtime;
        movie.Rating = meta.Rating;
        movie.Votes = meta.Votes;
        movie.Genres = [.. meta.Genres];
        movie.Studios = [.. meta.Studios];
        movie.TmdbId = meta.TmdbId;
        movie.ImdbId = meta.ImdbId;
    }

    private static void ApplyShow(TvShow show, TvShowMetadata meta)
    {
        show.Title = meta.Title;
        show.OriginalTitle = meta.OriginalTitle;
        show.Year = meta.Year;
        show.Plot = meta.Plot;
        show.Status = meta.Status;
        show.Rating = meta.Rating;
        show.Votes = meta.Votes;
        show.Genres = [.. meta.Genres];
        show.Studios = [.. meta.Studios];
        show.TmdbId = meta.TmdbId;
        show.TvdbId = meta.TvdbId;
        show.ImdbId = meta.ImdbId;
    }
}
