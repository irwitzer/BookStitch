using BookStitch.Models;
using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public class TrackPathServiceTests
{
    [Fact]
    public void GetTrackPath_WithoutRelativeFolder_CombinesFolderAndFileName()
    {
        var track = new TrackInfo
        {
            FileName = "Track 01.mp3",
            RelativeFolder = ""
        };

        var path = TrackPathService.GetTrackPath(@"C:\Books\Test", track);

        Assert.Equal(System.IO.Path.Combine(@"C:\Books\Test", "Track 01.mp3"), path);
    }

    [Fact]
    public void GetTrackPath_WithDotRelativeFolder_CombinesFolderAndFileName()
    {
        var track = new TrackInfo
        {
            FileName = "Track 01.mp3",
            RelativeFolder = "."
        };

        var path = TrackPathService.GetTrackPath(@"C:\Books\Test", track);

        Assert.Equal(System.IO.Path.Combine(@"C:\Books\Test", "Track 01.mp3"), path);
    }

    [Fact]
    public void GetTrackPath_WithRelativeFolder_CombinesFolderRelativeFolderAndFileName()
    {
        var track = new TrackInfo
        {
            FileName = "Track 01.mp3",
            RelativeFolder = "CD 01"
        };

        var path = TrackPathService.GetTrackPath(@"C:\Books\Test", track);

        Assert.Equal(System.IO.Path.Combine(@"C:\Books\Test", "CD 01", "Track 01.mp3"), path);
    }

    [Fact]
    public void PathEquals_ReturnsTrueForSameNormalizedPath()
    {
        var first = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "BookStitch", "Test");
        var second = first + System.IO.Path.DirectorySeparatorChar;

        Assert.True(TrackPathService.PathEquals(first, second));
    }

    [Fact]
    public void PathEquals_ReturnsFalseForBlankValues()
    {
        Assert.False(TrackPathService.PathEquals("", @"C:\Books"));
        Assert.False(TrackPathService.PathEquals(@"C:\Books", ""));
    }

    [Fact]
    public void PathEquals_ReturnsFalseForDifferentPaths()
    {
        Assert.False(TrackPathService.PathEquals(@"C:\Books\A", @"C:\Books\B"));
    }

    [Fact]
    public void IsPathInsideFolder_ReturnsTrueForChildPath()
    {
        var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "BookStitchTests", Guid.NewGuid().ToString("N"));
        var child = System.IO.Path.Combine(root, "Sub", "Track 01.mp3");

        Assert.True(TrackPathService.IsPathInsideFolder(child, root));
    }

    [Fact]
    public void IsPathInsideFolder_ReturnsFalseForSiblingPath()
    {
        var baseRoot = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "BookStitchTests", Guid.NewGuid().ToString("N"));
        var root = System.IO.Path.Combine(baseRoot, "Root");
        var sibling = System.IO.Path.Combine(baseRoot, "RootOther", "Track 01.mp3");

        Assert.False(TrackPathService.IsPathInsideFolder(sibling, root));
    }

    [Fact]
    public void IsPathInsideFolder_ReturnsFalseForInvalidPaths()
    {
        Assert.False(TrackPathService.IsPathInsideFolder("", ""));
    }
}
