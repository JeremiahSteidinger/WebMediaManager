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
    [InlineData("01 - The Wrong Side of the Tracks.mp4")] // leading number is ambiguous without a season folder
    public void ParseEpisode_returns_null_without_marker(string input)
    {
        Assert.Null(MediaNameParser.ParseEpisode(input));
    }

    [Theory]
    [InlineData("Season 1", 1)]
    [InlineData("Season 01", 1)]
    [InlineData("Season.03", 3)]
    [InlineData("S1", 1)]
    [InlineData("S01", 1)]
    [InlineData("Series 2", 2)]
    [InlineData("3", 3)]
    [InlineData("Specials", 0)]
    [InlineData("Extras", null)]
    [InlineData("Happy Tree Friends (1999)", null)]
    public void ParseSeasonFolder_extracts_season_number(string input, int? expected)
    {
        Assert.Equal(expected, MediaNameParser.ParseSeasonFolder(input));
    }

    [Theory]
    // A leading number resolves to the season folder's number (the reported Happy Tree Friends case).
    [InlineData("01 - The Wrong Side of the Tracks.mp4", "Season 03", 3, new[] { 1 })]
    [InlineData("39 - Autopsy Turvy.mp4", "Season 03", 3, new[] { 39 })]
    [InlineData("02 From Hero to Eternity.mp4", "S3", 3, new[] { 2 })]
    [InlineData("01-02 Double Length.mp4", "Season 03", 3, new[] { 1, 2 })] // bare range
    // Episode-only markers also borrow the folder's season.
    [InlineData("E02 - Eye Candy.mp4", "Season 02", 2, new[] { 2 })]
    [InlineData("Episode 5.mkv", "Season 01", 1, new[] { 5 })]
    [InlineData("Show E02E03.mkv", "Season 04", 4, new[] { 2, 3 })]
    // An explicit filename marker still wins over the folder.
    [InlineData("Happy Tree Friends - S05E01 - The Wrong Side.mp4", "Season 03", 5, new[] { 1 })]
    public void ParseEpisode_uses_season_folder_when_filename_lacks_one(
        string fileName, string seasonFolder, int season, int[] episodes)
    {
        var result = MediaNameParser.ParseEpisode(fileName, seasonFolder);
        Assert.NotNull(result);
        Assert.Equal(season, result!.Season);
        Assert.Equal(episodes, result.Episodes);
    }
}
