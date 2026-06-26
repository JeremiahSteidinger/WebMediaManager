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

    /// <summary>Alternate episode orderings (TMDB episode groups) a source offers for a show; empty when none.</summary>
    Task<IReadOnlyList<EpisodeOrdering>> GetEpisodeOrderingsAsync(
        MetadataSource source, string providerId, CancellationToken ct = default);

    /// <summary>
    /// Links a chosen search result to an item: fetches details, applies metadata, marks it Matched.
    /// For a TV show, <paramref name="ordering"/> selects an alternate episode order (null = aired order).
    /// </summary>
    Task LinkAsync(Guid itemId, MetadataSearchResult result, EpisodeOrdering? ordering = null, CancellationToken ct = default);

    /// <summary>
    /// Re-fetches metadata for one season's episodes of an already-matched show (matched by
    /// season/episode number against the show's existing provider link and episode order) and
    /// writes only those episodes' NFOs. Leaves episodes with no provider match untouched.
    /// </summary>
    Task RefreshSeasonAsync(Guid seasonId, CancellationToken ct = default);

    /// <summary>
    /// Re-fetches metadata for a single episode of an already-matched show (matched by season/episode
    /// number) and writes only that episode's NFO. Useful for a freshly added episode of an ongoing
    /// season, so a new file gets its metadata and sidecar without re-touching the rest of the show.
    /// </summary>
    Task RefreshEpisodeAsync(Guid episodeId, CancellationToken ct = default);
}

public sealed class IdentifyService(
    IMetadataProviderResolver resolver,
    IDbContextFactory<MediaDbContext> factory,
    ISettingsService settings,
    IArtworkService artwork,
    INfoFileService nfo,
    IRenameService rename,
    IActivityLogService activityLog,
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

    public async Task<IReadOnlyList<EpisodeOrdering>> GetEpisodeOrderingsAsync(
        MetadataSource source, string providerId, CancellationToken ct = default)
    {
        var provider = resolver.Get(source)
            ?? throw new MetadataException($"Provider {source} is not available.");
        return await provider.GetEpisodeOrderingsAsync(providerId, ct);
    }

    public async Task LinkAsync(Guid itemId, MetadataSearchResult result, EpisodeOrdering? ordering = null, CancellationToken ct = default)
    {
        // Falls back to a generic subject if we fail before the item is loaded; refined to the title otherwise.
        var subject = "item";
        try
        {
            var provider = resolver.Get(result.Source)
                ?? throw new MetadataException($"Provider {result.Source} is not available.");

            await using var db = await factory.CreateDbContextAsync(ct);
            var item = await db.MediaItems.FirstOrDefaultAsync(m => m.Id == itemId, ct)
                ?? throw new InvalidOperationException("Item not found.");
            subject = item.Title;

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
                    show.EpisodeGroupId = ordering?.Id;
                    show.EpisodeGroupName = ordering?.Name;
                    posterUrl = showMeta.PosterUrl;
                    fanartUrl = showMeta.FanartUrl;
                    await EnrichEpisodesAsync(db, show, provider, result.ProviderId, ordering?.Id, ct);
                    break;
            }

            item.PrimaryProvider = result.Source;
            item.MatchState = MatchState.Matched;
            subject = item.Title;

            await RefreshArtworkRowsAsync(db, itemId, posterUrl, fanartUrl, ct);
            await db.SaveChangesAsync(ct);

            var orderNote = ordering is null ? string.Empty : $" · {ordering.Name} order";
            await activityLog.LogAsync(ActivityCategory.Identify, ActivityStatus.Success, subject,
                $"Matched to {result.Source} #{result.ProviderId}{orderNote}", itemId, ct);
        }
        catch (Exception ex)
        {
            await activityLog.LogAsync(ActivityCategory.Identify, ActivityStatus.Failure, subject,
                $"Identify failed: {ex.Message}", itemId, ct);
            throw;
        }

        // Side effects below are best-effort: a failure must never undo the match.
        try
        {
            var images = await artwork.DownloadForItemAsync(itemId, ct);
            if (images > 0)
            {
                await activityLog.LogAsync(ActivityCategory.Artwork, ActivityStatus.Success, subject,
                    $"Downloaded {images} image(s)", itemId, ct);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Artwork download failed for item {ItemId}", itemId);
            await activityLog.LogAsync(ActivityCategory.Artwork, ActivityStatus.Failure, subject,
                $"Artwork download failed: {ex.Message}", itemId, ct);
        }

        try
        {
            var files = await nfo.WriteForItemAsync(itemId, ct);
            if (files > 0)
            {
                await activityLog.LogAsync(ActivityCategory.Nfo, ActivityStatus.Success, subject,
                    $"Wrote {files} NFO file(s)", itemId, ct);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "NFO write failed for item {ItemId}", itemId);
            await activityLog.LogAsync(ActivityCategory.Nfo, ActivityStatus.Failure, subject,
                $"NFO write failed: {ex.Message}", itemId, ct);
        }

        // Auto-rename runs last so the NFO/artwork written above are carried along by the folder move.
        // RenameService records its own outcome to the activity log.
        try
        {
            if ((await settings.GetAsync(ct)).RenamePatterns.AutoRenameAfterMatch)
            {
                var renameResult = await rename.ApplyAsync(itemId, ct);
                if (!renameResult.Success)
                {
                    logger.LogWarning("Auto-rename skipped for item {ItemId}: {Error}", itemId, renameResult.Error);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Auto-rename failed for item {ItemId}", itemId);
        }
    }

    public async Task RefreshSeasonAsync(Guid seasonId, CancellationToken ct = default)
    {
        var subject = "season";
        try
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            var season = await db.Seasons.Include(s => s.Episodes).FirstOrDefaultAsync(s => s.Id == seasonId, ct)
                ?? throw new InvalidOperationException("Season not found.");
            var show = await db.TvShows.FirstAsync(s => s.Id == season.TvShowId, ct);
            subject = $"{show.Title} · Season {season.SeasonNumber}";

            var (provider, providerId) = ResolveShowProvider(show);
            var byNumber = await FetchEpisodeMetadataAsync(provider, providerId, show.EpisodeGroupId, [season.SeasonNumber], ct);

            var matched = 0;
            foreach (var ep in season.Episodes)
            {
                if (byNumber.TryGetValue((ep.SeasonNumber, ep.EpisodeNumber), out var meta))
                {
                    ApplyEpisode(ep, meta);
                    matched++;
                }
            }
            await db.SaveChangesAsync(ct);

            await activityLog.LogAsync(ActivityCategory.Identify, ActivityStatus.Success, subject,
                $"Matched {matched}/{season.Episodes.Count} episode(s) by number", show.Id, ct);
        }
        catch (Exception ex)
        {
            await activityLog.LogAsync(ActivityCategory.Identify, ActivityStatus.Failure, subject,
                $"Season match failed: {ex.Message}", null, ct);
            throw;
        }

        await WriteEpisodeNfoAsync(() => nfo.WriteForSeasonAsync(seasonId, ct), subject);
    }

    public async Task RefreshEpisodeAsync(Guid episodeId, CancellationToken ct = default)
    {
        var subject = "episode";
        Guid showId;
        try
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            var episode = await db.Episodes.FirstOrDefaultAsync(e => e.Id == episodeId, ct)
                ?? throw new InvalidOperationException("Episode not found.");
            var season = await db.Seasons.FirstAsync(s => s.Id == episode.SeasonId, ct);
            var show = await db.TvShows.FirstAsync(s => s.Id == season.TvShowId, ct);
            showId = show.Id;
            subject = $"{show.Title} · S{episode.SeasonNumber:00}E{episode.EpisodeNumber:00}";

            var (provider, providerId) = ResolveShowProvider(show);
            var byNumber = await FetchEpisodeMetadataAsync(provider, providerId, show.EpisodeGroupId, [episode.SeasonNumber], ct);

            if (byNumber.TryGetValue((episode.SeasonNumber, episode.EpisodeNumber), out var meta))
            {
                ApplyEpisode(episode, meta);
                await db.SaveChangesAsync(ct);
                await activityLog.LogAsync(ActivityCategory.Identify, ActivityStatus.Success, subject,
                    "Matched by number", showId, ct);
            }
            else
            {
                await activityLog.LogAsync(ActivityCategory.Identify, ActivityStatus.Success, subject,
                    "No provider match for this number — wrote NFO from existing data", showId, ct);
            }
        }
        catch (Exception ex)
        {
            await activityLog.LogAsync(ActivityCategory.Identify, ActivityStatus.Failure, subject,
                $"Episode match failed: {ex.Message}", null, ct);
            throw;
        }

        await WriteEpisodeNfoAsync(() => nfo.WriteForEpisodeAsync(episodeId, ct), subject);
    }

    private async Task EnrichEpisodesAsync(
        MediaDbContext db, TvShow show, IMetadataProvider provider, string providerId, string? episodeGroupId, CancellationToken ct)
    {
        try
        {
            await db.Entry(show).Collection(s => s.Seasons).Query().Include(se => se.Episodes).LoadAsync(ct);

            var byNumber = await FetchEpisodeMetadataAsync(
                provider, providerId, episodeGroupId, show.Seasons.Select(s => s.SeasonNumber), ct);
            if (byNumber.Count == 0)
            {
                return;
            }

            foreach (var ep in show.Seasons.SelectMany(s => s.Episodes))
            {
                if (byNumber.TryGetValue((ep.SeasonNumber, ep.EpisodeNumber), out var meta))
                {
                    ApplyEpisode(ep, meta);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Episode metadata enrichment failed for show {ShowId}", show.Id);
        }
    }

    /// <summary>
    /// Pulls a show's episode metadata keyed by (season, episode). A chosen ordering returns every
    /// season's episodes in one call, re-numbered to that order; otherwise each requested season is
    /// pulled in the provider's default (aired) order.
    /// </summary>
    private async Task<Dictionary<(int Season, int Episode), EpisodeMetadata>> FetchEpisodeMetadataAsync(
        IMetadataProvider provider, string providerId, string? episodeGroupId, IEnumerable<int> seasonNumbers, CancellationToken ct)
    {
        IReadOnlyList<EpisodeMetadata> episodes;
        if (!string.IsNullOrEmpty(episodeGroupId))
        {
            episodes = await provider.GetOrderedEpisodesAsync(episodeGroupId, ct);
        }
        else
        {
            var collected = new List<EpisodeMetadata>();
            foreach (var seasonNumber in seasonNumbers.Distinct())
            {
                collected.AddRange(await provider.GetEpisodesAsync(providerId, seasonNumber, ct));
            }
            episodes = collected;
        }

        // First entry wins on the off chance an ordering repeats a (season, episode) slot.
        return episodes
            .GroupBy(e => (e.Season, e.Episode))
            .ToDictionary(g => g.Key, g => g.First());
    }

    private static void ApplyEpisode(Episode ep, EpisodeMetadata meta)
    {
        ep.Title = meta.Title ?? ep.Title;
        ep.Plot = meta.Plot ?? ep.Plot;
        ep.AirDate = meta.AirDate ?? ep.AirDate;
        ep.TmdbId ??= meta.TmdbId;
    }

    /// <summary>Resolves the provider and the show's id for it; throws if the show isn't matched yet.</summary>
    private (IMetadataProvider Provider, string ProviderId) ResolveShowProvider(TvShow show)
    {
        var providerId = show.PrimaryProvider switch
        {
            MetadataSource.Tmdb => show.TmdbId,
            MetadataSource.Tvdb => show.TvdbId,
            MetadataSource.Imdb => show.ImdbId,
            _ => null,
        };
        if (string.IsNullOrEmpty(providerId))
        {
            throw new MetadataException("Identify the show before matching individual seasons or episodes.");
        }

        var provider = resolver.Get(show.PrimaryProvider)
            ?? throw new MetadataException($"Provider {show.PrimaryProvider} is not available.");
        return (provider, providerId);
    }

    /// <summary>Best-effort episode-NFO write that records its own activity-log outcome; never throws.</summary>
    private async Task WriteEpisodeNfoAsync(Func<Task<int>> write, string subject)
    {
        try
        {
            var files = await write();
            if (files > 0)
            {
                await activityLog.LogAsync(ActivityCategory.Nfo, ActivityStatus.Success, subject,
                    $"Wrote {files} NFO file(s)");
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "NFO write failed for {Subject}", subject);
            await activityLog.LogAsync(ActivityCategory.Nfo, ActivityStatus.Failure, subject,
                $"NFO write failed: {ex.Message}");
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
