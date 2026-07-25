using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public class OutputFolderLayoutServiceTests
{
    private readonly OutputFolderLayoutService _service = new();

    [Fact]
    public void BuildOutputPath_ByDefault_CreatesAuthorAndTitleFolders()
    {
        var result = _service.BuildOutputPath(
            @"C:\Export",
            "Terry Pratchett",
            "Wachen! Wachen!",
            "Terry Pratchett - Wachen! Wachen!.m4b");

        Assert.Equal(
            System.IO.Path.Combine(@"C:\Export", "Terry Pratchett", "Wachen! Wachen!", "Terry Pratchett - Wachen! Wachen!.m4b"),
            result);
    }

    [Fact]
    public void BuildRelativeOutputPath_WithBlankMetadata_UsesFallbackFolders()
    {
        var result = _service.BuildRelativeOutputPath("", "", "Hoerbuch.m4a");

        Assert.Equal(
            System.IO.Path.Combine("Autor", "Titel", "Hoerbuch.m4a"),
            result);
    }

    [Fact]
    public void BuildRelativeOutputPath_CleansInvalidFolderCharacters()
    {
        var result = _service.BuildRelativeOutputPath("Au:tor", "Ti*tel", "Buch.m4b");

        Assert.Equal(
            System.IO.Path.Combine("Au tor", "Ti tel", "Buch.m4b"),
            result);
    }

    [Fact]
    public void BuildRelativeOutputPath_CanUseOnlyAuthorFolder()
    {
        var result = _service.BuildRelativeOutputPath(
            "Autor Name",
            "Titel Name",
            "Buch.m4b",
            includeAuthorFolder: true,
            includeTitleFolder: false);

        Assert.Equal(
            System.IO.Path.Combine("Autor Name", "Buch.m4b"),
            result);
    }

    [Fact]
    public void BuildRelativeOutputPath_CanUseOnlyTitleFolder()
    {
        var result = _service.BuildRelativeOutputPath(
            "Autor Name",
            "Titel Name",
            "Buch.m4b",
            includeAuthorFolder: false,
            includeTitleFolder: true);

        Assert.Equal(
            System.IO.Path.Combine("Titel Name", "Buch.m4b"),
            result);
    }
}

public class OutputFolderLayoutServiceAdditionalTests
{
    private readonly OutputFolderLayoutService _service = new();

    [Fact]
    public void BuildRelativeOutputPath_WithNoFolderLayout_UsesOnlyFileName()
    {
        var result = _service.BuildRelativeOutputPath(
            "Autor Name",
            "Titel Name",
            "Buch.m4b",
            OutputFolderLayoutService.LayoutNone);

        Assert.Equal("Buch.m4b", result);
    }

    [Fact]
    public void NormalizeLayout_MapsRemovedSingleAuthorTitleFolderToDefault()
    {
        Assert.Equal(
            OutputFolderLayoutService.DefaultLayout,
            OutputFolderLayoutService.NormalizeLayout(OutputFolderLayoutService.LayoutAuthorTitleSingle));
    }

    [Fact]
    public void BuildRelativeOutputPath_CanUseAuthorAlbumAndTitleFolders()
    {
        var result = _service.BuildRelativeOutputPath(
            "Autor Name",
            "Titel Name",
            "Buch.m4b",
            OutputFolderLayoutService.LayoutAuthorAlbumTitleNested,
            album: "Album Name");

        Assert.Equal(
            System.IO.Path.Combine("Autor Name", "Album Name", "Titel Name", "Buch.m4b"),
            result);
    }

    [Fact]
    public void BuildRelativeOutputPath_CanUseSeriesAuthorAndTitleFolders()
    {
        var result = _service.BuildRelativeOutputPath(
            "Autor Name",
            "Titel Name",
            "Buch.m4b",
            OutputFolderLayoutService.LayoutSeriesAuthorTitleNested,
            series: "Reihe Name");

        Assert.Equal(
            System.IO.Path.Combine("Reihe Name", "Autor Name", "Titel Name", "Buch.m4b"),
            result);
    }
}
