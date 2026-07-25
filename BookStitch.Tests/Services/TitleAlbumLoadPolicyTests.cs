using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class TitleAlbumLoadPolicyTests
{
    [Fact]
    public void Resolve_UsesAlbumForBothFieldsWhenLinkingIsEnabled()
    {
        var result = TitleAlbumLoadPolicy.Resolve(
            "Einzeltitel",
            "Gesamtes Hörbuch",
            keepLinked: true);

        Assert.Equal("Gesamtes Hörbuch", result.Title);
        Assert.Equal("Gesamtes Hörbuch", result.Album);
    }

    [Theory]
    [InlineData("Billy Summers", "Billy Summers", "Billy Summers")]
    [InlineData("Billy Summers", "", "Billy Summers")]
    [InlineData("Billy Summers", null, "Billy Summers")]
    [InlineData("", "Gesamtes Hörbuch", "Gesamtes Hörbuch")]
    public void Resolve_KeepsLinkedFieldsOnOneCanonicalValue(
        string? title,
        string? album,
        string expected)
    {
        var result = TitleAlbumLoadPolicy.Resolve(title, album, keepLinked: true);

        Assert.Equal(expected, result.Title);
        Assert.Equal(expected, result.Album);
    }

    [Fact]
    public void Resolve_PreservesDistinctValuesWhenGlobalLinkingIsDisabled()
    {
        var result = TitleAlbumLoadPolicy.Resolve(
            "Einzeltitel",
            "Gesamtes Hörbuch",
            keepLinked: false);

        Assert.Equal("Einzeltitel", result.Title);
        Assert.Equal("Gesamtes Hörbuch", result.Album);
    }

    [Theory]
    [InlineData("Billy Summers", "", "Billy Summers", "Billy Summers")]
    [InlineData("", "Gesamtes Hörbuch", "Gesamtes Hörbuch", "Gesamtes Hörbuch")]
    public void Resolve_FillsMissingCounterpartWhenGlobalLinkingIsDisabled(
        string title,
        string album,
        string expectedTitle,
        string expectedAlbum)
    {
        var result = TitleAlbumLoadPolicy.Resolve(title, album, keepLinked: false);

        Assert.Equal(expectedTitle, result.Title);
        Assert.Equal(expectedAlbum, result.Album);
    }
}
