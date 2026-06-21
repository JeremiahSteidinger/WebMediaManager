using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using WebMediaManager.Core.Domain;
using WebMediaManager.Core.Renaming;
using WebMediaManager.Core.Settings;
using WebMediaManager.Data;
using WebMediaManager.Services;

namespace WebMediaManager.Tests;

/// <summary>Exercises the rename service end-to-end against a real temp folder tree.</summary>
public sealed class RenameServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly TestContextFactory _factory;
    private readonly string _root;
    private readonly AppSettings _settings = new();

    public RenameServiceTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<MediaDbContext>().UseSqlite(_connection).Options;
        using (var db = new MediaDbContext(options))
        {
            db.Database.EnsureCreated();
        }
        _factory = new TestContextFactory(options);
        _root = Path.Combine(Path.GetTempPath(), "wmm-rename-" + Guid.NewGuid().ToString("N"));

        _settings.RenamePatterns.MovieFolder = "{title} ({year}) <[tmdb-{tmdbid}]>";
        _settings.RenamePatterns.MovieFile = "{title} ({year})";
    }

    [Fact]
    public async Task Renames_movie_folder_and_file_then_updates_db()
    {
        var dir = Path.Combine(_root, "inception");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "inc.mkv"), "x");

        var movieId = Guid.NewGuid();
        await using (var db = _factory.CreateDbContext())
        {
            var libraryId = Guid.NewGuid();
            db.Libraries.Add(new Library { Id = libraryId, Name = "m", Type = LibraryType.Movies, RootPath = _root });
            db.Movies.Add(new Movie
            {
                Id = movieId,
                LibraryId = libraryId,
                Title = "Inception",
                Year = 2010,
                TmdbId = "27205",
                MatchState = MatchState.Matched,
                RelativePath = "inception",
                VideoFilePath = Path.Combine("inception", "inc.mkv"),
            });
            await db.SaveChangesAsync();
        }

        var service = NewService();

        var plan = await service.BuildPlanAsync(movieId);
        Assert.False(plan.HasConflicts);
        Assert.Equal(2, plan.Ops.Count); // file + folder

        var result = await service.ApplyAsync(movieId);
        Assert.True(result.Success, result.Error);

        var expectedFolder = Path.Combine(_root, "Inception (2010) [tmdb-27205]");
        var expectedFile = Path.Combine(expectedFolder, "Inception (2010).mkv");
        Assert.True(Directory.Exists(expectedFolder), "renamed folder should exist");
        Assert.True(File.Exists(expectedFile), "renamed file should exist");
        Assert.False(Directory.Exists(Path.Combine(_root, "inception")), "old folder should be gone");

        await using var verify = _factory.CreateDbContext();
        var movie = await verify.Movies.SingleAsync();
        Assert.Equal("Inception (2010) [tmdb-27205]", movie.RelativePath);
        Assert.Equal(Path.Combine("Inception (2010) [tmdb-27205]", "Inception (2010).mkv"), movie.VideoFilePath);
    }

    [Fact]
    public async Task Optional_group_collapses_when_id_missing()
    {
        var dir = Path.Combine(_root, "matrix");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "m.mkv"), "x");

        var movieId = Guid.NewGuid();
        await using (var db = _factory.CreateDbContext())
        {
            var libraryId = Guid.NewGuid();
            db.Libraries.Add(new Library { Id = libraryId, Name = "m", Type = LibraryType.Movies, RootPath = _root });
            db.Movies.Add(new Movie
            {
                Id = movieId,
                LibraryId = libraryId,
                Title = "The Matrix",
                Year = 1999,
                TmdbId = null, // no id -> the [tmdb-...] group should vanish
                MatchState = MatchState.Matched,
                RelativePath = "matrix",
                VideoFilePath = Path.Combine("matrix", "m.mkv"),
            });
            await db.SaveChangesAsync();
        }

        var result = await NewService().ApplyAsync(movieId);
        Assert.True(result.Success, result.Error);
        Assert.True(Directory.Exists(Path.Combine(_root, "The Matrix (1999)")));
    }

    [Fact]
    public async Task Blank_file_pattern_renames_only_the_folder()
    {
        _settings.RenamePatterns.MovieFolder = "{title} ({year})";
        _settings.RenamePatterns.MovieFile = string.Empty; // blank => leave files alone

        var dir = Path.Combine(_root, "inception");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "inception.mkv"), "x");

        var movieId = Guid.NewGuid();
        await using (var db = _factory.CreateDbContext())
        {
            var libraryId = Guid.NewGuid();
            db.Libraries.Add(new Library { Id = libraryId, Name = "m", Type = LibraryType.Movies, RootPath = _root });
            db.Movies.Add(new Movie
            {
                Id = movieId,
                LibraryId = libraryId,
                Title = "Inception",
                Year = 2010,
                MatchState = MatchState.Matched,
                RelativePath = "inception",
                VideoFilePath = Path.Combine("inception", "inception.mkv"),
            });
            await db.SaveChangesAsync();
        }

        var service = NewService();

        var plan = await service.BuildPlanAsync(movieId);
        Assert.Single(plan.Ops);                              // only the folder rename, no file rename
        Assert.Equal(RenameMoveKind.Folder, plan.Ops[0].Kind);

        var result = await service.ApplyAsync(movieId);
        Assert.True(result.Success, result.Error);
        // File moved with the folder but kept its original name.
        Assert.True(File.Exists(Path.Combine(_root, "Inception (2010)", "inception.mkv")));
    }

    private RenameService NewService() => new(
        _factory,
        new StubSettings(_settings),
        new TokenEngine(),
        new PhysicalFileSystem(),
        new RenamePlanner(),
        new NoopActivityLog(),
        NullLogger<RenameService>.Instance);

    public void Dispose()
    {
        _connection.Dispose();
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private sealed class TestContextFactory(DbContextOptions<MediaDbContext> options)
        : IDbContextFactory<MediaDbContext>
    {
        public MediaDbContext CreateDbContext() => new(options);
    }

    private sealed class StubSettings(AppSettings settings) : ISettingsService
    {
        public Task<AppSettings> GetAsync(CancellationToken ct = default) => Task.FromResult(settings);
        public Task SaveAsync(AppSettings s, CancellationToken ct = default) => Task.CompletedTask;
    }
}
