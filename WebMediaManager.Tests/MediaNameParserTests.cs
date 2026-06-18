using WebMediaManager.Core.Scanning;

namespace WebMediaManager.Tests;

public class MediaNameParserTests
{
    [Theory]
    [InlineData("Inception (2010)", "Inception", 2010)]
    [InlineData("Inception 2010", "Inception", 2010)]
    [InlineData("The.Matrix.1999.1080p.BluRay.x264", "The Matrix", 1999)]
    [InlineData("Blade Runner 2049 (2017)", "Blade Runner 2049", 2017)]
    [InlineData("2001 A Space Odyssey (1968)", "2001 A Space Odyssey", 1968)]
    [InlineData("Some Movie Without A Year", "Some Movie Without A Year", null)]
    [InlineData("Quick.Brown.Fox.2021.2160p.HDR.x265", "Quick Brown Fox", 2021)]
    [InlineData("300 (2006)", "300", 2006)]
    public void ParseMovie_extracts_title_and_year(string input, string expectedTitle, int? expectedYear)
    {
        var result = MediaNameParser.ParseMovie(input);
        Assert.Equal(expectedTitle, result.Title);
        Assert.Equal(expectedYear, result.Year);
    }

    [Theory]
    [InlineData("Show - S01E02 - Title.mkv", 1, new[] { 2 })]
    [InlineData("show.s01e02.720p.mkv", 1, new[] { 2 })]
    [InlineData("Show 1x05.avi", 1, new[] { 5 })]
    [InlineData("Show S03E10E11.mkv", 3, new[] { 10, 11 })]
    [InlineData("Show.S02E03-E04.mkv", 2, new[] { 3, 4 })]
    [InlineData("Show - S00E01 - Special.mkv", 0, new[] { 1 })]
    [InlineData("Show - 1x2.mkv", 1, new[] { 2 })]
    public void ParseEpisode_extracts_season_and_episodes(string input, int season, int[] episodes)
    {
        var result = MediaNameParser.ParseEpisode(input);
        Assert.NotNull(result);
        Assert.Equal(season, result!.Season);
        Assert.Equal(episodes, result.Episodes);
    }

    [Theory]
    [InlineData("just-a-movie.mkv")]
    [InlineData("featurette.mp4")]
    public void ParseEpisode_returns_null_without_marker(string input)
    {
        Assert.Null(MediaNameParser.ParseEpisode(input));
    }
}
