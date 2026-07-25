using BookStitch.Services;
using BookStitch.Tests.TestHelpers;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class TrackWorkspaceFilterServiceTests
{
    [Theory]
    [InlineData(@"converted\AAC Stereo 128 kbps\001.m4a")]
    [InlineData(@"merge\parts\001.m4a")]
    public void IsMp3DiscGeneratedPath_Recognizes_Generated_Project_Folders(string relativePath)
    {
        using var folder = new TemporaryFolder();
        var path = folder.CreateFile(relativePath);

        var result = new TrackWorkspaceFilterService().IsMp3DiscGeneratedPath(folder.Path, path);

        Assert.True(result);
    }

    [Fact]
    public void IsMp3DiscGeneratedPath_Does_Not_Exclude_Imported_Disc_Source()
    {
        using var folder = new TemporaryFolder();
        var path = folder.CreateFile(@"CD 01\001 Original.mp3");

        var result = new TrackWorkspaceFilterService().IsMp3DiscGeneratedPath(folder.Path, path);

        Assert.False(result);
    }
}
