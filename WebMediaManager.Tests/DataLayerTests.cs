using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using WebMediaManager.Core.Domain;
using WebMediaManager.Core.Providers;
using WebMediaManager.Core.Settings;
using WebMediaManager.Data;

namespace WebMediaManager.Tests;

/// <summary>
/// Exercises the EF Core mapping against a real (in-memory) SQLite database: entity round-trips,
/// the enum-to-text conversion, and the JSON-backed settings columns.
/// </summary>
public sealed class DataLayerTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<MediaDbContext> _options;

    public DataLayerTests()
    {
        // A single kept-open in-memory connection shared by every context instance.
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<MediaDbContext>()
            .UseSqlite(_connection)
            .Options;

        using var db = new MediaDbContext(_options);
        db.Database.EnsureCreated();
    }

    private MediaDbContext NewContext() => new(_options);

    [Fact]
    public async Task Library_round_trips()
    {
        var id = Guid.NewGuid();
        await using (var db = NewContext())
        {
            db.Libraries.Add(new Library
            {
                Id = id,
                Name = "Movies",
                Type = LibraryType.Movies,
                RootPath = "/media/movies",
                CreatedUtc = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        await using (var db = NewContext())
        {
            var lib = await db.Libraries.SingleAsync();
            Assert.Equal(id, lib.Id);
            Assert.Equal("Movies", lib.Name);
            Assert.Equal(LibraryType.Movies, lib.Type);
            Assert.Equal("/media/movies", lib.RootPath);
        }
    }

    [Fact]
    public async Task LibraryType_is_persisted_as_text()
    {
        await using (var db = NewContext())
        {
            db.Libraries.Add(new Library
            {
                Id = Guid.NewGuid(),
                Name = "Shows",
                Type = LibraryType.TvShows,
                RootPath = "/media/tv",
            });
            await db.SaveChangesAsync();
        }

        var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT Type FROM Libraries LIMIT 1";
        var value = (string?)await cmd.ExecuteScalarAsync();
        Assert.Equal("TvShows", value);
    }

    [Fact]
    public async Task AppSettings_json_columns_round_trip()
    {
        await using (var db = NewContext())
        {
            var s = new AppSettings();
            s.Providers.TmdbApiKey = "tmdb-key";
            s.Providers.TvdbPin = "1234";
            s.Providers.PreferredTvSource = MetadataSource.Tvdb;
            s.RenamePatterns.MovieFolder = "{title} [tmdb-{tmdbid}]";
            s.Nfo.WriteEpisodeNfo = false;
            s.Artwork.PosterFilename = "cover.jpg";
            db.Settings.Add(s);
            await db.SaveChangesAsync();
        }

        await using (var db = NewContext())
        {
            var s = await db.Settings.SingleAsync();
            Assert.Equal(AppSettings.SingletonId, s.Id);
            Assert.Equal("tmdb-key", s.Providers.TmdbApiKey);
            Assert.Equal("1234", s.Providers.TvdbPin);
            Assert.Equal(MetadataSource.Tvdb, s.Providers.PreferredTvSource);
            Assert.Equal("{title} [tmdb-{tmdbid}]", s.RenamePatterns.MovieFolder);
            Assert.False(s.Nfo.WriteEpisodeNfo);
            Assert.Equal("cover.jpg", s.Artwork.PosterFilename);
        }
    }

    public void Dispose() => _connection.Dispose();
}
