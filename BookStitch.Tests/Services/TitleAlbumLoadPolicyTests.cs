using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class TitleAlbumLoadPolicyTests
{
    [Fact]
    public void Resolve_PreservesDistinctValues()
    {
        var result = TitleAlbumLoadPolicy.Resolve("Einzeltitel", "Gesamtes Hörbuch");

        Assert.Equal("Einzeltitel", result.Title);
        Assert.Equal("Gesamtes Hörbuch", result.Album);
    }

    [Theory]
    [InlineData("Billy Summers", "", "Billy Summers", "Billy Summers")]
    [InlineData("Billy Summers", null, "Billy Summers", "Billy Summers")]
    [InlineData("", "Gesamtes Hörbuch", "Gesamtes Hörbuch", "Gesamtes Hörbuch")]
    public void Resolve_FillsOnlyMissingCounterpart(
        string? title,
        string? album,
        string expectedTitle,
        string expectedAlbum)
    {
        var result = TitleAlbumLoadPolicy.Resolve(title, album);

        Assert.Equal(expectedTitle, result.Title);
        Assert.Equal(expectedAlbum, result.Album);
    }
}
