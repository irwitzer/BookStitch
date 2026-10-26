using BookStitch.Services;
using BookStitch.Tests.TestHelpers;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class TrackWorkspaceFilterServiceTests
{
    [Theory]
    [InlineData("Mein.Hoerbuch.part.01.mp3")]
    [InlineData("Mein.Hoerbuch.part.01.m4a")]
    public void Legitimate_source_with_part_marker_survives_scan_and_workspace_filter(string name)
    {
        using var folder = new TemporaryFolder();
        var path = folder.CreateFile(name);
        var tracks = new FolderScanner().Scan(folder.Path);
        Assert.Equal(path, Assert.Single(tracks).FilePath);
        Assert.Empty(new TrackWorkspaceFilterService().GetGeneratedOrWorkTracks(folder.Path, tracks, "", null));
        Assert.False(ConvertedFileCleanupService.IsPartFilePath(path));
    }

    [Theory]
    [InlineData(".m4a")]
    [InlineData(".m4b")]
    public void SingleContainerSourceMatchingOutputName_RemainsOneTrack(string extension)
    {
        using var folder = new TemporaryFolder();
        var path = folder.CreateFile("Book" + extension);
        var tracks = new FolderScanner().Scan(folder.Path);
        var generated = new TrackWorkspaceFilterService().GetGeneratedOrWorkTracks(
            folder.Path, tracks, path, null);
        Assert.Empty(generated);
        Assert.Equal(path, Assert.Single(tracks).FilePath);
        Assert.Equal(1, tracks[0].Index);
        Assert.True(AudioInputFormatCatalog.IsLocalImportExtension(extension));
    }

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
