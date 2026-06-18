using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using WebMediaManager.Core.Domain;
using WebMediaManager.Core.Nfo;
using WebMediaManager.Core.Providers;
using WebMediaManager.Core.Scanning;
using WebMediaManager.Data;
using WebMediaManager.Jobs;

namespace WebMediaManager.Services.Scanning;

/// <summary>
/// Folder-convention scanner. Movies: one folder per movie (largest video is the main file) plus loose
/// videos in the root. TV: one folder per show, episodes discovered recursively and grouped by season.
/// New items are written as <see cref="MatchState.Unmatched"/>; existing paths are skipped (re-scan safe).
/// </summary>
public sealed class MediaScanner(
    IDbContextFactory<MediaDbContext> factory,
    IScanProgressService progress,
    INfoReader nfoReader,
    ILogger<MediaScanner> logger) : IMediaScanner
{
    private static readonly string[] IgnoredFolders =
    [
        "sample", "samples", "extras", "featurettes", "trailers", "behind the scenes", "deleted scenes",
    ];

    public async Task ScanAsync(Guid libraryId, bool reimport, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        var library = await db.Libraries.FirstOrDefaultAsync(l => l.Id == libraryId, ct)
            ?? throw new InvalidOperationException($"Library {libraryId} not found.");

        if (!Directory.Exists(library.RootPath))
        {
            throw new DirectoryNotFoundException($"Library root not found: {library.RootPath}");
        }

        var (added, skipped) = library.Type == LibraryType.Movies
            ? await ScanMoviesAsync(db, library, reimport, ct)
            : await ScanTvAsync(db, library, reimport, ct);

        var note = skipped > 0 ? $"{skipped} folder(s) skipped — see logs" : null;
        progress.Set(new ScanProgress(libraryId, ScanState.Completed, added, note, null));
    }

    private async Task<(int Added, int Skipped)> ScanMoviesAsync(MediaDbContext db, Library library, bool reimport, CancellationToken ct)
    {
        var added = 0;
        var skipped = 0;

        // Reimport loads existing movies tracked so we can re-read NFO/artwork into them in place.
        Dictionary<string, Movie>? existing = null;
        HashSet<string> seen;
        if (reimport)
        {
            var movies = await db.Movies.Where(m => m.LibraryId == library.Id).ToListAsync(ct);
            existing = movies.ToDictionary(m => m.RelativePath, StringComparer.OrdinalIgnoreCase);
            seen = new HashSet<string>(existing.Keys, StringComparer.OrdinalIgnoreCase);
        }
        else
        {
            seen = (await db.Movies.Where(m => m.LibraryId == library.Id).Select(m => m.RelativePath).ToListAsync(ct))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        // One folder per movie.
        foreach (var dir in Directory.EnumerateDirectories(library.RootPath))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var name = Path.GetFileName(dir);
                if (IsIgnored(name))
                {
                    continue;
                }

                var videos = EnumerateVideos(dir).ToList();
                if (videos.Count == 0)
                {
                    continue;
                }

                var rel = Path.GetRelativePath(library.RootPath, dir);
                var main = videos.MaxBy(f => new FileInfo(f).Length)!;

                if (!seen.Add(rel))
                {
                    // Already in the DB: only reimport re-reads its sidecars.
                    if (reimport && existing!.TryGetValue(rel, out var existingMovie))
                    {
                        await ClearArtworkAsync(db, existingMovie.Id, ct);
                        ImportMovieSidecars(db, library, existingMovie, dir, main, folderBased: true);
                        added++;
                        Report(library.Id, added, name);
                    }
                    continue;
                }

                var movie = AddMovie(db, library, name, rel, Path.GetRelativePath(library.RootPath, main));
                ImportMovieSidecars(db, library, movie, dir, main, folderBased: true);
                added++;
                Report(library.Id, added, name);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                skipped++;
                logger.LogWarning(ex, "Skipped folder during scan: {Dir}", dir);
            }
        }

        // Loose video files directly in the root.
        foreach (var file in Directory.EnumerateFiles(library.RootPath))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if (!VideoExtensions.IsVideo(file))
                {
                    continue;
                }

                var rel = Path.GetRelativePath(library.RootPath, file);
                var fileName = Path.GetFileName(file);

                if (!seen.Add(rel))
                {
                    if (reimport && existing!.TryGetValue(rel, out var existingMovie))
                    {
                        await ClearArtworkAsync(db, existingMovie.Id, ct);
                        ImportMovieSidecars(db, library, existingMovie, library.RootPath, file, folderBased: false);
                        added++;
                        Report(library.Id, added, fileName);
                    }
                    continue;
                }

                var movie = AddMovie(db, library, fileName, rel, rel);
                ImportMovieSidecars(db, library, movie, library.RootPath, file, folderBased: false);
                added++;
                Report(library.Id, added, fileName);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                skipped++;
                logger.LogWarning(ex, "Skipped file during scan: {File}", file);
            }
        }

        await db.SaveChangesAsync(ct);
        return (added, skipped);
    }

    private async Task<(int Added, int Skipped)> ScanTvAsync(MediaDbContext db, Library library, bool reimport, CancellationToken ct)
    {
        var added = 0;
        var skipped = 0;

        Dictionary<string, TvShow>? existing = null;
        HashSet<string> seen;
        if (reimport)
        {
            var shows = await db.TvShows
                .Include(s => s.Seasons).ThenInclude(se => se.Episodes)
                .Where(s => s.LibraryId == library.Id).ToListAsync(ct);
            existing = shows.ToDictionary(s => s.RelativePath, StringComparer.OrdinalIgnoreCase);
            seen = new HashSet<string>(existing.Keys, StringComparer.OrdinalIgnoreCase);
        }
        else
        {
            seen = (await db.TvShows.Where(s => s.LibraryId == library.Id).Select(s => s.RelativePath).ToListAsync(ct))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        foreach (var showDir in Directory.EnumerateDirectories(library.RootPath))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
            var showName = Path.GetFileName(showDir);
            if (IsIgnored(showName))
            {
                continue;
            }

            var rel = Path.GetRelativePath(library.RootPath, showDir);
            if (!seen.Add(rel))
            {
                // Already in the DB: only reimport re-reads tvshow.nfo, show artwork, and episode NFOs.
                if (reimport && existing!.TryGetValue(rel, out var existingShow))
                {
                    await ClearArtworkAsync(db, existingShow.Id, ct);
                    ImportShowSidecars(db, library, existingShow, showDir);
                    ReimportEpisodeNfos(library, existingShow);
                    added++;
                    Report(library.Id, added, showName);
                }
                continue;
            }

            var episodes = new List<(string File, ParsedEpisode Info)>();
            foreach (var file in EnumerateVideos(showDir))
            {
                var info = MediaNameParser.ParseEpisode(Path.GetFileName(file));
                if (info is not null)
                {
                    episodes.Add((file, info));
                }
            }

            if (episodes.Count == 0)
            {
                continue;
            }

            var parsed = MediaNameParser.ParseMovie(showName);
            var now = DateTimeOffset.UtcNow;
            var show = new TvShow
            {
                Id = Guid.NewGuid(),
                LibraryId = library.Id,
                Title = string.IsNullOrWhiteSpace(parsed.Title) ? showName : parsed.Title,
                Year = parsed.Year,
                RelativePath = rel,
                MatchState = MatchState.Unmatched,
                DateAdded = now,
                DateScanned = now,
            };

            foreach (var seasonGroup in episodes.GroupBy(e => e.Info.Season).OrderBy(g => g.Key))
            {
                var season = new Season
                {
                    Id = Guid.NewGuid(),
                    TvShowId = show.Id,
                    SeasonNumber = seasonGroup.Key,
                };

                foreach (var (file, info) in seasonGroup.OrderBy(e => e.Info.Episodes[0]))
                {
                    var relFile = Path.GetRelativePath(library.RootPath, file);
                    // A multi-episode file (S01E01E02) yields one Episode row per number, sharing the file.
                    foreach (var episodeNumber in info.Episodes)
                    {
                        var episode = new Episode
                        {
                            Id = Guid.NewGuid(),
                            SeasonId = season.Id,
                            SeasonNumber = info.Season,
                            EpisodeNumber = episodeNumber,
                            VideoFilePath = relFile,
                        };
                        season.Episodes.Add(episode);

                        // Import an existing single-episode NFO sidecar (multi-episode NFOs aren't single-root XML).
                        if (info.Episodes.Count == 1)
                        {
                            var epNfo = Path.ChangeExtension(file, ".nfo");
                            if (File.Exists(epNfo) && TryLoadNfo(epNfo, out var epDoc))
                            {
                                nfoReader.ReadEpisode(epDoc, episode);
                            }
                        }
                    }
                }

                show.Seasons.Add(season);
            }

            ImportShowSidecars(db, library, show, showDir);
            db.TvShows.Add(show);
            added++;
            Report(library.Id, added, showName);
            await db.SaveChangesAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                skipped++;
                logger.LogWarning(ex, "Skipped show folder during scan: {Dir}", showDir);
            }
        }

        return (added, skipped);
    }

    private static Movie AddMovie(MediaDbContext db, Library library, string name, string rel, string videoRel)
    {
        var parsed = MediaNameParser.ParseMovie(name);
        var now = DateTimeOffset.UtcNow;
        var movie = new Movie
        {
            Id = Guid.NewGuid(),
            LibraryId = library.Id,
            Title = string.IsNullOrWhiteSpace(parsed.Title) ? name : parsed.Title,
            Year = parsed.Year,
            RelativePath = rel,
            VideoFilePath = videoRel,
            MatchState = MatchState.Unmatched,
            DateAdded = now,
            DateScanned = now,
        };
        db.Movies.Add(movie);
        return movie;
    }

    private void ImportMovieSidecars(MediaDbContext db, Library library, Movie movie, string dirFull, string mainVideoFull, bool folderBased)
    {
        var baseName = Path.GetFileNameWithoutExtension(mainVideoFull);

        var nfoCandidates = folderBased
            ? new[] { Path.Combine(dirFull, "movie.nfo"), Path.ChangeExtension(mainVideoFull, ".nfo") }
            : new[] { Path.ChangeExtension(mainVideoFull, ".nfo") };
        var nfoPath = nfoCandidates.FirstOrDefault(File.Exists);
        if (nfoPath is not null && TryLoadNfo(nfoPath, out var doc))
        {
            nfoReader.ReadMovie(doc, movie);
            movie.MatchState = MatchState.Matched;
            movie.HasNfo = true;
            movie.PrimaryProvider = InferProvider(movie.TmdbId, movie.TvdbId, movie.ImdbId);
        }

        var posterNames = folderBased
            ? new[] { "poster.jpg", "poster.png", "folder.jpg", baseName + "-poster.jpg" }
            : new[] { baseName + "-poster.jpg", baseName + "-poster.png" };
        var fanartNames = folderBased
            ? new[] { "fanart.jpg", "fanart.png", baseName + "-fanart.jpg" }
            : new[] { baseName + "-fanart.jpg" };
        AddArtworkIfExists(db, library, movie.Id, ArtworkKind.Poster, dirFull, posterNames);
        AddArtworkIfExists(db, library, movie.Id, ArtworkKind.Fanart, dirFull, fanartNames);
    }

    private void ImportShowSidecars(MediaDbContext db, Library library, TvShow show, string showDir)
    {
        var nfoPath = Path.Combine(showDir, "tvshow.nfo");
        if (File.Exists(nfoPath) && TryLoadNfo(nfoPath, out var doc))
        {
            nfoReader.ReadTvShow(doc, show);
            show.MatchState = MatchState.Matched;
            show.HasNfo = true;
            show.PrimaryProvider = InferProvider(show.TmdbId, show.TvdbId, show.ImdbId);
        }

        AddArtworkIfExists(db, library, show.Id, ArtworkKind.Poster, showDir, ["poster.jpg", "poster.png", "folder.jpg"]);
        AddArtworkIfExists(db, library, show.Id, ArtworkKind.Fanart, showDir, ["fanart.jpg", "fanart.png"]);
        AddArtworkIfExists(db, library, show.Id, ArtworkKind.Banner, showDir, ["banner.jpg", "banner.png"]);
    }

    private static void AddArtworkIfExists(
        MediaDbContext db, Library library, Guid mediaItemId, ArtworkKind kind, string dirFull, IEnumerable<string> fileNames)
    {
        foreach (var fileName in fileNames)
        {
            var full = Path.Combine(dirFull, fileName);
            if (File.Exists(full))
            {
                db.Artworks.Add(new Artwork
                {
                    Id = Guid.NewGuid(),
                    MediaItemId = mediaItemId,
                    Kind = kind,
                    LocalRelativePath = Path.GetRelativePath(library.RootPath, full),
                    DownloadedUtc = DateTimeOffset.UtcNow,
                });
                return; // first match wins
            }
        }
    }

    private void ReimportEpisodeNfos(Library library, TvShow show)
    {
        foreach (var ep in show.Seasons.SelectMany(s => s.Episodes))
        {
            var epNfo = Path.ChangeExtension(Path.Combine(library.RootPath, ep.VideoFilePath), ".nfo");
            if (File.Exists(epNfo) && TryLoadNfo(epNfo, out var doc))
            {
                nfoReader.ReadEpisode(doc, ep);
            }
        }
    }

    private static async Task ClearArtworkAsync(MediaDbContext db, Guid mediaItemId, CancellationToken ct)
    {
        var old = await db.Artworks.Where(a => a.MediaItemId == mediaItemId).ToListAsync(ct);
        if (old.Count > 0)
        {
            db.Artworks.RemoveRange(old);
        }
    }

    private static bool TryLoadNfo(string path, out XDocument doc)
    {
        try
        {
            doc = XDocument.Load(path);
            return true;
        }
        catch
        {
            doc = null!;
            return false;
        }
    }

    private static MetadataSource InferProvider(string? tmdb, string? tvdb, string? imdb) =>
        !string.IsNullOrEmpty(tmdb) ? MetadataSource.Tmdb
        : !string.IsNullOrEmpty(tvdb) ? MetadataSource.Tvdb
        : MetadataSource.None;

    private void Report(Guid libraryId, int added, string current) =>
        progress.Set(new ScanProgress(libraryId, ScanState.Running, added, current, null));

    private static IEnumerable<string> EnumerateVideos(string root)
    {
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            if (!VideoExtensions.IsVideo(file))
            {
                continue;
            }

            var parent = Path.GetFileName(Path.GetDirectoryName(file) ?? string.Empty);
            if (IsIgnored(parent))
            {
                continue;
            }

            yield return file;
        }
    }

    private static bool IsIgnored(string folderName) =>
        IgnoredFolders.Contains(folderName, StringComparer.OrdinalIgnoreCase);
}
