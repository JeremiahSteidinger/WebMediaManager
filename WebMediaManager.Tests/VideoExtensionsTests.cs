using WebMediaManager.Core.Scanning;

namespace WebMediaManager.Tests;

public class VideoExtensionsTests
{
    [Theory]
    [InlineData("movie.mp4", "video/mp4")]
    [InlineData("movie.m4v", "video/mp4")]
    [InlineData("clip.webm", "video/webm")]
    [InlineData("film.mkv", "video/x-matroska")]
    [InlineData("old.avi", "video/x-msvideo")]
    [InlineData("home.mov", "video/quicktime")]
    [InlineData("stream.ts", "video/mp2t")]
    [InlineData("mystery.bin", "application/octet-stream")]
    public void ContentType_maps_known_containers(string path, string expected) =>
        Assert.Equal(expected, VideoExtensions.ContentType(path));

    [Theory]
    [InlineData("movie.mp4", true)]
    [InlineData("movie.m4v", true)]
    [InlineData("clip.webm", true)]
    [InlineData("film.mkv", false)]
    [InlineData("old.avi", false)]
    [InlineData("home.mov", false)]
    [InlineData("clip.wmv", false)]
    public void IsBrowserPlayable_flags_web_native_containers(string path, bool expected) =>
        Assert.Equal(expected, VideoExtensions.IsBrowserPlayable(path));
}
