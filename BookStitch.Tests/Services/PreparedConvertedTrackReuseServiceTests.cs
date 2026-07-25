using BookStitch.Models;
using BookStitch.Services;
using BookStitch.Tests.TestHelpers;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class PreparedConvertedTrackReuseServiceTests
{
    [Theory]
    [InlineData(ProjectManifestTypes.Mp3DiscProject)]
    [InlineData(ProjectManifestTypes.AudioCdProject)]
    public void CanReuseForDiscProject_AcceptsCurrentPreparedTrack(string projectType)
    {
        using var folder = new TemporaryFolder();
        var sourcePath = Path.Combine(folder.Path, "track.flac");
        var convertedPath = Path.Combine(folder.Path, "track.m4a");

        File.WriteAllBytes(sourcePath, [1, 2, 3]);
        File.SetLastWriteTimeUtc(sourcePath, DateTime.UtcNow.AddMinutes(-2));
        File.WriteAllBytes(convertedPath, [4, 5, 6]);
        File.SetLastWriteTimeUtc(convertedPath, DateTime.UtcNow.AddMinutes(-1));

        Assert.True(PreparedConvertedTrackReuseService.CanReuseForDiscProject(
            projectType,
            sourcePath,
            convertedPath));
    }

    [Fact]
    public void CanReuseForDiscProject_RejectsLocalProject()
    {
        using var folder = new TemporaryFolder();
        var sourcePath = Path.Combine(folder.Path, "track.flac");
        var convertedPath = Path.Combine(folder.Path, "track.m4a");

        File.WriteAllBytes(sourcePath, [1]);
        File.WriteAllBytes(convertedPath, [2]);

        Assert.False(PreparedConvertedTrackReuseService.CanReuseForDiscProject(
            ProjectManifestTypes.FolderProject,
            sourcePath,
            convertedPath));
    }

    [Theory]
    [InlineData("track.m4a.part")]
    [InlineData("track.m4a.copying")]
    public void CanReuseForDiscProject_RejectsTemporaryOutput(string fileName)
    {
        using var folder = new TemporaryFolder();
        var sourcePath = Path.Combine(folder.Path, "track.flac");
        var convertedPath = Path.Combine(folder.Path, fileName);

        File.WriteAllBytes(sourcePath, [1]);
        File.WriteAllBytes(convertedPath, [2]);

        Assert.False(PreparedConvertedTrackReuseService.CanReuseForDiscProject(
            ProjectManifestTypes.AudioCdProject,
            sourcePath,
            convertedPath));
    }

    [Fact]
    public void CanReuseForDiscProject_RejectsOutputOlderThanSource()
    {
        using var folder = new TemporaryFolder();
        var sourcePath = Path.Combine(folder.Path, "track.flac");
        var convertedPath = Path.Combine(folder.Path, "track.m4a");

        File.WriteAllBytes(convertedPath, [2]);
        File.SetLastWriteTimeUtc(convertedPath, DateTime.UtcNow.AddMinutes(-2));
        File.WriteAllBytes(sourcePath, [1]);
        File.SetLastWriteTimeUtc(sourcePath, DateTime.UtcNow.AddMinutes(-1));

        Assert.False(PreparedConvertedTrackReuseService.CanReuseForDiscProject(
            ProjectManifestTypes.AudioCdProject,
            sourcePath,
            convertedPath));
    }
}
