using BookStitch.Models;
using BookStitch.Services;
using BookStitch.Tests.TestHelpers;
using Xunit;

namespace BookStitch.Tests.Services;

public class BookMetadataServiceTests
{
    private readonly BookMetadataService _service = new();

    [Fact]
    public void GuessFromFolder_WithNoTracks_ReturnsEmptySuggestion()
    {
        using var folder = new TemporaryFolder();

        var suggestion = _service.GuessFromFolder(
            folder.Path,
            folder.Path,
            []);

        Assert.Equal("", suggestion.Title);
        Assert.Equal("", suggestion.Author);
        Assert.Equal("", suggestion.Narrator);
    }

    [Fact]
    public void GuessFromFolder_UsesFolderNameAsTitleWhenTagsAreMissing()
    {
        using var folder = new TemporaryFolder();

        var suggestion = _service.GuessFromFolder(
            folder.Path,
            System.IO.Path.Combine(folder.Path, "Der_große_Test   Band_01"),
            [TrackWithArtist("Autor")]);

        Assert.Equal("Der große Test Band 01", suggestion.Title);
    }

    [Fact]
    public void GuessFromFolder_ReturnsEmptyTitleWhenDisplayFolderPathIsEmpty()
    {
        using var folder = new TemporaryFolder();

        var suggestion = _service.GuessFromFolder(
            folder.Path,
            "",
            [TrackWithArtist("Autor")]);

        Assert.Equal("", suggestion.Title);
    }

    [Fact]
    public void GuessFromFolder_UsesDominantTrackArtistAsAuthor()
    {
        using var folder = new TemporaryFolder();

        var tracks = new[]
        {
            TrackWithArtist("Autor A"),
            TrackWithArtist("Autor A"),
            TrackWithArtist("Autor B")
        };

        var suggestion = _service.GuessFromFolder(
            folder.Path,
            folder.Path,
            tracks);

        Assert.Equal("Autor A", suggestion.Author);
    }

    [Fact]
    public void GuessFromFolder_IgnoresBlankArtistsWhenGuessingAuthor()
    {
        using var folder = new TemporaryFolder();

        var tracks = new[]
        {
            TrackWithArtist(""),
            TrackWithArtist("Autor A"),
            TrackWithArtist("Autor A")
        };

        var suggestion = _service.GuessFromFolder(
            folder.Path,
            folder.Path,
            tracks);

        Assert.Equal("Autor A", suggestion.Author);
    }

    [Fact]
    public void GuessFromFolder_ReturnsEmptyAuthorWhenNoDominantArtistExists()
    {
        using var folder = new TemporaryFolder();

        var tracks = new[]
        {
            TrackWithArtist("Autor A"),
            TrackWithArtist("Autor B"),
            TrackWithArtist("Autor C")
        };

        var suggestion = _service.GuessFromFolder(
            folder.Path,
            folder.Path,
            tracks);

        Assert.Equal("", suggestion.Author);
    }

    [Fact]
    public void GuessFromFolder_UsesArtistWhenOnlyOneTrackExists()
    {
        using var folder = new TemporaryFolder();

        var suggestion = _service.GuessFromFolder(
            folder.Path,
            folder.Path,
            [TrackWithArtist("Einzelautor")]);

        Assert.Equal("", suggestion.Author);
    }

    [Fact]
    public void GuessFromFolder_HandlesMissingScannedFolderAndStillUsesDisplayFolderTitleAndTrackAuthor()
    {
        using var folder = new TemporaryFolder();
        var missingFolder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        var tracks = new[]
        {
            TrackWithArtist("Autor A"),
            TrackWithArtist("Autor A")
        };

        var suggestion = _service.GuessFromFolder(
            missingFolder,
            System.IO.Path.Combine(folder.Path, "Mein_Hoerbuch"),
            tracks);

        Assert.Equal("Mein Hoerbuch", suggestion.Title);
        Assert.Equal("Autor A", suggestion.Author);
        Assert.Equal("", suggestion.Narrator);
    }

    [Fact]
    public void GuessFromFolder_IgnoresUnsupportedFilesWhenReadingTags()
    {
        using var folder = new TemporaryFolder();
        WriteFile(folder, "not-audio.txt", "not audio");

        var tracks = new[]
        {
            TrackWithArtist("Autor A"),
            TrackWithArtist("Autor A")
        };

        var suggestion = _service.GuessFromFolder(
            folder.Path,
            System.IO.Path.Combine(folder.Path, "Fallback_Title"),
            tracks);

        Assert.Equal("Fallback Title", suggestion.Title);
        Assert.Equal("Autor A", suggestion.Author);
    }

    private static TrackInfo TrackWithArtist(string artist)
    {
        return new TrackInfo
        {
            Artist = artist
        };
    }

    private static void WriteFile(TemporaryFolder folder, string relativePath, string content)
    {
        var fullPath = System.IO.Path.Combine(folder.Path, relativePath);
        var directory = System.IO.Path.GetDirectoryName(fullPath);

        if (!string.IsNullOrWhiteSpace(directory))
            System.IO.Directory.CreateDirectory(directory);

        System.IO.File.WriteAllText(fullPath, content);
    }
}
