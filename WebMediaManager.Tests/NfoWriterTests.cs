using System.Xml.Linq;
using WebMediaManager.Core.Domain;
using WebMediaManager.Core.Nfo;

namespace WebMediaManager.Tests;

public class NfoWriterTests
{
    private readonly NfoWriter _writer = new();

    [Fact]
    public void Movie_nfo_has_kodi_shape_with_dual_written_ids()
    {
        var movie = new Movie
        {
            Title = "Inception",
            OriginalTitle = "Inception",
            Year = 2010,
            Plot = "A thief…",
            Runtime = 148,
            Rating = 8.3,
            ReleaseDate = new DateOnly(2010, 7, 15),
            Genres = ["Action", "Science Fiction"],
            TmdbId = "27205",
            ImdbId = "tt1375666",
        };

        var root = _writer.BuildMovieNfo(movie).Root!;

        Assert.Equal("movie", root.Name.LocalName);
        Assert.Equal("Inception", root.Element("title")!.Value);
        Assert.Equal("2010", root.Element("year")!.Value);
        Assert.Equal("148", root.Element("runtime")!.Value);
        Assert.Equal("2010-07-15", root.Element("premiered")!.Value);
        Assert.Equal(2, root.Elements("genre").Count());

        // Modern uniqueid with tmdb marked default.
        var tmdbUnique = root.Elements("uniqueid").Single(e => (string?)e.Attribute("type") == "tmdb");
        Assert.Equal("27205", tmdbUnique.Value);
        Assert.Equal("true", (string?)tmdbUnique.Attribute("default"));

        // Legacy elements present too.
        Assert.Equal("27205", root.Element("tmdbid")!.Value);
        Assert.Equal("tt1375666", root.Element("imdbid")!.Value);
        Assert.Equal("tt1375666", root.Element("id")!.Value);
    }

    [Fact]
    public void Empty_values_are_omitted()
    {
        var movie = new Movie { Title = "X" };
        var root = _writer.BuildMovieNfo(movie).Root!;

        Assert.Null(root.Element("plot"));
        Assert.Null(root.Element("year"));
        Assert.Empty(root.Elements("uniqueid"));
        Assert.Null(root.Element("tagline"));
    }

    [Fact]
    public void Tvshow_nfo_prefers_tvdb_as_default_id()
    {
        var show = new TvShow
        {
            Title = "Breaking Bad",
            Year = 2008,
            Status = TvShowStatus.Ended,
            TvdbId = "81189",
            TmdbId = "1396",
            ImdbId = "tt0903747",
        };

        var root = _writer.BuildTvShowNfo(show).Root!;

        Assert.Equal("tvshow", root.Name.LocalName);
        Assert.Equal("Ended", root.Element("status")!.Value);
        var def = root.Elements("uniqueid").Single(e => (string?)e.Attribute("default") == "true");
        Assert.Equal("tvdb", (string?)def.Attribute("type"));
    }

    [Fact]
    public void Episode_nfo_has_season_and_episode()
    {
        var show = new TvShow { Title = "Breaking Bad" };
        var episode = new Episode
        {
            SeasonNumber = 1,
            EpisodeNumber = 2,
            Title = "Cat's in the Bag...",
            AirDate = new DateOnly(2008, 1, 27),
        };

        var root = _writer.BuildEpisodeNfo(show, episode).Root!;

        Assert.Equal("episodedetails", root.Name.LocalName);
        Assert.Equal("1", root.Element("season")!.Value);
        Assert.Equal("2", root.Element("episode")!.Value);
        Assert.Equal("2008-01-27", root.Element("aired")!.Value);
        Assert.Equal("Breaking Bad", root.Element("showtitle")!.Value);
    }
}
