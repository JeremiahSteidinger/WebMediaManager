using Microsoft.EntityFrameworkCore;
using WebMediaManager.Core.Domain;
using WebMediaManager.Core.Renaming;
using WebMediaManager.Core.Settings;
using WebMediaManager.Data;

namespace WebMediaManager.Services;

public sealed record RenameResult(bool Success, int Applied, string? Error);

public interface IRenameService
{
    /// <summary>Computes the dry-run plan for an item (no disk changes).</summary>
    Task<RenamePlan> BuildPlanAsync(Guid itemId, CancellationToken ct = default);

    /// <summary>Applies the plan to disk and updates stored paths. No-op if the plan has conflicts.</summary>
    Task<RenameResult> ApplyAsync(Guid itemId, CancellationToken ct = default);
}

public sealed class RenameService(
    IDbContextFactory<MediaDbContext> factory,
    ISettingsService settings,
    ITokenEngine engine,
    IFileSystem fs,
    RenamePlanner planner,
    ILogger<RenameService> logger) : IRenameService
{
    public async Task<RenamePlan> BuildPlanAsync(Guid itemId, CancellationToken ct = default)
    {
        var (root, computed) = await ComputeAsync(itemId, ct);
        return planner.BuildPlan(root, computed.Moves, fs);
    }

    public async Task<RenameResult> ApplyAsync(Guid itemId, CancellationToken ct = default)
    {
        var (root, computed) = await ComputeAsync(itemId, ct);
        var plan = planner.BuildPlan(root, computed.Moves, fs);

        if (plan.HasConflicts)
        {
            return new RenameResult(false, 0, "The plan has conflicts; resolve them before renaming.");
        }
        if (plan.IsEmpty)
        {
            return new RenameResult(true, 0, null);
        }

        var applied = 0;
        foreach (var op in plan.Ops)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if (op.Kind == RenameMoveKind.File)
                {
                    fs.MoveFile(op.FromAbsolute, op.ToAbsolute);
                }
                else
                {
                    fs.MoveDirectory(op.FromAbsolute, op.ToAbsolute);
                }
                applied++;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Rename op failed: {From} -> {To}", op.FromAbsolute, op.ToAbsolute);
                return new RenameResult(false, applied,
                    $"Renamed {applied} item(s), then failed on '{op.FromRelative}': {ex.Message}. Re-scan the library to resync.");
            }
        }

        await PersistAsync(itemId, computed, ct);
        return new RenameResult(true, applied, null);
    }

    private async Task<(string Root, ComputedRename Computed)> ComputeAsync(Guid itemId, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var patterns = (await settings.GetAsync(ct)).RenamePatterns;

        var movie = await db.Movies.FirstOrDefaultAsync(m => m.Id == itemId, ct);
        if (movie is not null)
        {
            var library = await RequireLibrary(db, movie.LibraryId, ct);
            return (library.RootPath, ComputeMovie(movie, library.RootPath, patterns));
        }

        var show = await db.TvShows
            .Include(s => s.Seasons).ThenInclude(se => se.Episodes)
            .FirstOrDefaultAsync(s => s.Id == itemId, ct);
        if (show is not null)
        {
            var library = await RequireLibrary(db, show.LibraryId, ct);
            return (library.RootPath, ComputeShow(show, library.RootPath, patterns));
        }

        throw new InvalidOperationException("Item not found.");
    }

    private static async Task<Library> RequireLibrary(MediaDbContext db, Guid libraryId, CancellationToken ct) =>
        await db.Libraries.FirstOrDefaultAsync(l => l.Id == libraryId, ct)
        ?? throw new InvalidOperationException("Library not found.");

    private ComputedRename ComputeMovie(Movie m, string root, RenamePatternSettings p)
    {
        var tokens = RenameTokens.ForMovie(m);
        var newFolder = FilenameSanitizer.Sanitize(engine.Render(p.MovieFolder, tokens));
        var newFileBase = FilenameSanitizer.Sanitize(engine.Render(p.MovieFile, tokens));

        var moves = new List<PlannedMove>();
        var folderBased = fs.DirectoryExists(Path.Combine(root, m.RelativePath));

        string finalItemPath = m.RelativePath;
        string? finalVideoPath = m.VideoFilePath;

        if (folderBased)
        {
            var parent = Dir(m.RelativePath);
            var finalFolder = newFolder.Length > 0 ? CombineRel(parent, newFolder) : m.RelativePath;
            finalItemPath = finalFolder;

            if (!string.IsNullOrEmpty(m.VideoFilePath) && newFileBase.Length > 0)
            {
                var ext = Path.GetExtension(m.VideoFilePath);
                var fileDir = Dir(m.VideoFilePath);
                var toFile = CombineRel(fileDir, newFileBase + ext);
                moves.Add(new PlannedMove(m.VideoFilePath, toFile, RenameMoveKind.File));
                // The file ends up inside the (possibly renamed) folder.
                finalVideoPath = CombineRel(finalFolder, newFileBase + ext);
            }

            if (newFolder.Length > 0)
            {
                moves.Add(new PlannedMove(m.RelativePath, finalFolder, RenameMoveKind.Folder));
            }
        }
        else if (newFileBase.Length > 0)
        {
            // Loose file directly in the root.
            var ext = Path.GetExtension(m.RelativePath);
            var dir = Dir(m.RelativePath);
            var toFile = CombineRel(dir, newFileBase + ext);
            moves.Add(new PlannedMove(m.RelativePath, toFile, RenameMoveKind.File));
            finalItemPath = toFile;
            finalVideoPath = toFile;

            // Carry a matching .nfo sidecar along with the file.
            var nfoFrom = Path.ChangeExtension(m.RelativePath, ".nfo");
            var nfoTo = CombineRel(dir, newFileBase + ".nfo");
            if (!string.Equals(nfoFrom, nfoTo, StringComparison.Ordinal) && fs.FileExists(Path.Combine(root, nfoFrom)))
            {
                moves.Add(new PlannedMove(nfoFrom, nfoTo, RenameMoveKind.File));
            }
        }

        return new ComputedRename(moves, finalItemPath, finalVideoPath, [], []);
    }

    private ComputedRename ComputeShow(TvShow show, string root, RenamePatternSettings p)
    {
        var newShowFolder = FilenameSanitizer.Sanitize(engine.Render(p.ShowFolder, RenameTokens.ForShow(show)));
        var parent = Dir(show.RelativePath);
        var finalShow = newShowFolder.Length > 0 ? CombineRel(parent, newShowFolder) : show.RelativePath;

        var moves = new List<PlannedMove>();
        var seasonPaths = new List<(Guid, string?)>();
        var episodePaths = new List<(Guid, string)>();

        foreach (var season in show.Seasons)
        {
            var newSeasonFolder = FilenameSanitizer.Sanitize(engine.Render(p.SeasonFolder, RenameTokens.ForSeason(show, season.SeasonNumber)));

            var currentDirs = season.Episodes
                .Select(e => Dir(e.VideoFilePath))
                .Distinct(StringComparer.Ordinal)
                .ToList();
            var hasSeasonSubfolder = currentDirs.Count == 1
                && !string.Equals(currentDirs[0], show.RelativePath, StringComparison.Ordinal)
                && newSeasonFolder.Length > 0;

            var finalSeasonDir = hasSeasonSubfolder ? CombineRel(finalShow, newSeasonFolder) : finalShow;
            seasonPaths.Add((season.Id, hasSeasonSubfolder ? finalSeasonDir : null));

            foreach (var ep in season.Episodes)
            {
                var ext = Path.GetExtension(ep.VideoFilePath);
                var epDir = Dir(ep.VideoFilePath);
                var newEpBase = FilenameSanitizer.Sanitize(engine.Render(p.EpisodeFile, RenameTokens.ForEpisode(show, ep)));
                if (newEpBase.Length > 0)
                {
                    moves.Add(new PlannedMove(ep.VideoFilePath, CombineRel(epDir, newEpBase + ext), RenameMoveKind.File));

                    // Carry a matching <basename>.nfo sidecar along with the video.
                    var nfoFrom = Path.ChangeExtension(ep.VideoFilePath, ".nfo");
                    var nfoTo = CombineRel(epDir, newEpBase + ".nfo");
                    if (!string.Equals(nfoFrom, nfoTo, StringComparison.Ordinal) && fs.FileExists(Path.Combine(root, nfoFrom)))
                    {
                        moves.Add(new PlannedMove(nfoFrom, nfoTo, RenameMoveKind.File));
                    }
                }
                var finalName = newEpBase.Length > 0 ? newEpBase + ext : Path.GetFileName(ep.VideoFilePath);
                episodePaths.Add((ep.Id, CombineRel(finalSeasonDir, finalName)));
            }

            if (hasSeasonSubfolder)
            {
                moves.Add(new PlannedMove(currentDirs[0], CombineRel(show.RelativePath, newSeasonFolder), RenameMoveKind.Folder));
            }
        }

        if (newShowFolder.Length > 0)
        {
            moves.Add(new PlannedMove(show.RelativePath, finalShow, RenameMoveKind.Folder));
        }

        return new ComputedRename(moves, finalShow, null, seasonPaths, episodePaths);
    }

    private async Task PersistAsync(Guid itemId, ComputedRename computed, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        var movie = await db.Movies.FirstOrDefaultAsync(m => m.Id == itemId, ct);
        if (movie is not null)
        {
            movie.RelativePath = computed.FinalItemPath;
            movie.VideoFilePath = computed.FinalVideoPath;
            await db.SaveChangesAsync(ct);
            return;
        }

        var show = await db.TvShows.Include(s => s.Seasons).ThenInclude(se => se.Episodes)
            .FirstOrDefaultAsync(s => s.Id == itemId, ct);
        if (show is not null)
        {
            show.RelativePath = computed.FinalItemPath;
            var seasonMap = computed.SeasonPaths.ToDictionary(x => x.Item1, x => x.Item2);
            var episodeMap = computed.EpisodePaths.ToDictionary(x => x.Item1, x => x.Item2);
            foreach (var season in show.Seasons)
            {
                if (seasonMap.TryGetValue(season.Id, out var sp))
                {
                    season.RelativePath = sp;
                }
                foreach (var ep in season.Episodes)
                {
                    if (episodeMap.TryGetValue(ep.Id, out var epPath))
                    {
                        ep.VideoFilePath = epPath;
                    }
                }
            }
            await db.SaveChangesAsync(ct);
        }
    }

    private static string Dir(string relative) => Path.GetDirectoryName(relative) ?? string.Empty;

    private static string CombineRel(string dir, string name) =>
        string.IsNullOrEmpty(dir) ? name : Path.Combine(dir, name);

    private sealed record ComputedRename(
        IReadOnlyList<PlannedMove> Moves,
        string FinalItemPath,
        string? FinalVideoPath,
        IReadOnlyList<(Guid, string?)> SeasonPaths,
        IReadOnlyList<(Guid, string)> EpisodePaths);
}
