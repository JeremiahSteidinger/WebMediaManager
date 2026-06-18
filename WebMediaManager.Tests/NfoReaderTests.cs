using System.Xml.Linq;
using WebMediaManager.Core.Domain;
using WebMediaManager.Core.Nfo;

namespace WebMediaManager.Tests;

public class NfoReaderTests
{
    private readonly NfoReader _reader = new();

    [Fact]
    public void Reads_movie_nfo_including_uniqueid_and_legacy_ids()
    {
        var doc = XDocument.Parse(
            """
            <movie>
              <title>Inception</title>
              <originaltitle>Inception</originaltitle>
              <year>2010</year>
              <plot>A thief…</plot>
              <runtime>148</runtime>
              <rating>8.3</rating>
              <premiered>2010-07-15</premiered>
              <genre>Action</genre>
              <genre>Science Fiction</genre>
              <uniqueid type="tmdb" default="true">27205</uniqueid>
              <imdbid>tt1375666</imdbid>
            </movie>
            """);
        var movie = new Movie { Title = "scanned" };

        _reader.ReadMovie(doc, movie);

        Assert.Equal("Inception", movie.Title);
        Assert.Equal(2010, movie.Year);
        Assert.Equal(148, movie.Runtime);
        Assert.Equal(8.3, movie.Rating);
        Assert.Equal(new DateOnly(2010, 7, 15), movie.ReleaseDate);
        Assert.Equal(["Action", "Science Fiction"], movie.Genres);
        Assert.Equal("27205", movie.TmdbId);   // from uniqueid
        Assert.Equal("tt1375666", movie.ImdbId); // from legacy element
    }

    [Fact]
    public void Reads_imdb_from_legacy_id_element()
    {
        var doc = XDocument.Parse("<movie><title>X</title><id>tt0111161</id></movie>");
        var movie = new Movie();

        _reader.ReadMovie(doc, movie);

        Assert.Equal("tt0111161", movie.ImdbId);
    }

    [Fact]
    public void Reads_tvshow_status_and_ids()
    {
        var doc = XDocument.Parse(
            """
            <tvshow>
              <title>Breaking Bad</title>
              <year>2008</year>
              <status>Ended</status>
              <uniqueid type="tvdb">81189</uniqueid>
              <tmdbid>1396</tmdbid>
            </tvshow>
            """);
        var show = new TvShow();

        _reader.ReadTvShow(doc, show);

        Assert.Equal("Breaking Bad", show.Title);
        Assert.Equal(TvShowStatus.Ended, show.Status);
        Assert.Equal("81189", show.TvdbId);
        Assert.Equal("1396", show.TmdbId);
    }

    [Fact]
    public void Reads_episode_details()
    {
        var doc = XDocument.Parse(
            """
            <episodedetails>
              <title>Pilot</title>
              <season>1</season>
              <episode>1</episode>
              <aired>2008-01-20</aired>
              <rating>9.0</rating>
            </episodedetails>
            """);
        var episode = new Episode { SeasonNumber = 1, EpisodeNumber = 1 };

        _reader.ReadEpisode(doc, episode);

        Assert.Equal("Pilot", episode.Title);
        Assert.Equal(new DateOnly(2008, 1, 20), episode.AirDate);
        Assert.Equal(9.0, episode.Rating);
    }

    [Fact]
    public void Round_trips_with_the_writer()
    {
        var original = new Movie
        {
            Title = "Inception",
            Year = 2010,
            TmdbId = "27205",
            ImdbId = "tt1375666",
            Genres = ["Action"],
        };
        var doc = new NfoWriter().BuildMovieNfo(original);

        var parsed = new Movie();
        _reader.ReadMovie(doc, parsed);

        Assert.Equal("Inception", parsed.Title);
        Assert.Equal(2010, parsed.Year);
        Assert.Equal("27205", parsed.TmdbId);
        Assert.Equal("tt1375666", parsed.ImdbId);
        Assert.Equal(["Action"], parsed.Genres);
    }
}
