using BookStitch.Models;
using BookStitch.Services;
using BookStitch.Tests.TestHelpers;
using System.Text.Json;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class TrackPreparedStateRefreshServiceTests
{
    [Fact]
    public void RefreshForCurrentPreset_MarksReusablePreparedTrackAndUpdatesConvertedSize()
    {
        using var folder = new TemporaryFolder();
        var sourcePath = Path.Combine(folder.Path, "source.flac");
        var convertedPath = Path.Combine(folder.Path, "source.m4a");
        File.WriteAllBytes(sourcePath, [1, 2, 3]);
        File.SetLastWriteTimeUtc(sourcePath, DateTime.UtcNow.AddMinutes(-2));
        File.WriteAllBytes(convertedPath, new byte[512 * 1024]);
        File.SetLastWriteTimeUtc(convertedPath, DateTime.UtcNow.AddMinutes(-1));
        WriteExportManifest(folder.Path, sourcePath, convertedPath, "AAC Stereo 192 kbps");

        var track = new TrackInfo { FilePath = sourcePath };

        new TrackPreparedStateRefreshService().RefreshForCurrentPreset(
            [track],
            folder.Path,
            "AAC Stereo 192 kbps");

        Assert.Equal(convertedPath, track.PreparedConvertedPath);
        Assert.Equal("AAC Stereo 192 kbps", track.PreparedConvertedPreset);
        Assert.True(track.HasReusableConvertedFile);
        Assert.True(track.ConvertedSizeAvailable);
        Assert.Equal(0.5d, track.ConvertedSizeMb);
    }

    [Fact]
    public void RefreshForCurrentPreset_ClearsPreparedStateWhenSourceIsNotInPlan()
    {
        using var folder = new TemporaryFolder();
        var manifestSourcePath = Path.Combine(folder.Path, "manifest.flac");
        var convertedPath = Path.Combine(folder.Path, "manifest.m4a");
        var otherSourcePath = Path.Combine(folder.Path, "other.flac");
        File.WriteAllBytes(manifestSourcePath, [1]);
        File.WriteAllBytes(convertedPath, [2]);
        File.WriteAllBytes(otherSourcePath, [3]);
        WriteExportManifest(folder.Path, manifestSourcePath, convertedPath, "AAC Stereo 192 kbps");

        var track = new TrackInfo
        {
            FilePath = otherSourcePath,
            PreparedConvertedPath = convertedPath,
            PreparedConvertedPreset = "AAC Stereo 192 kbps",
            HasReusableConvertedFile = true,
            ConvertedSizeAvailable = true,
            ConvertedSizeMb = 1d
        };

        new TrackPreparedStateRefreshService().RefreshForCurrentPreset(
            [track],
            folder.Path,
            "AAC Stereo 192 kbps");

        Assert.Equal("", track.PreparedConvertedPath);
        Assert.Equal("", track.PreparedConvertedPreset);
        Assert.False(track.HasReusableConvertedFile);
        Assert.False(track.ConvertedSizeAvailable);
        Assert.Equal(0d, track.ConvertedSizeMb);
    }

    [Fact]
    public void IsReusablePreparedConvertedTrack_RejectsDifferentPreset()
    {
        using var folder = new TemporaryFolder();
        var sourcePath = Path.Combine(folder.Path, "source.flac");
        var convertedPath = Path.Combine(folder.Path, "source.m4a");
        File.WriteAllBytes(sourcePath, [1]);
        File.WriteAllBytes(convertedPath, [2]);

        var track = new TrackInfo
        {
            FilePath = sourcePath,
            PreparedConvertedPath = convertedPath,
            PreparedConvertedPreset = "AAC Mono 64 kbps"
        };

        Assert.False(new TrackPreparedStateRefreshService().IsReusablePreparedConvertedTrack(
            track,
            "AAC Stereo 192 kbps"));
    }

    private static void WriteExportManifest(
        string projectFolder,
        string sourcePath,
        string convertedPath,
        string preset)
    {
        var manifest = new ExportWorkManifest
        {
            ProjectWorkFolder = projectFolder,
            ProjectType = ProjectManifestTypes.FolderProject,
            SelectedPreset = preset,
            Tracks =
            [
                new ExportWorkManifestTrack
                {
                    SourcePath = sourcePath,
                    ConvertedPath = convertedPath,
                    Preset = preset,
                    Status = "Converted"
                }
            ]
        };

        File.WriteAllText(
            Path.Combine(projectFolder, "project.json"),
            JsonSerializer.Serialize(manifest));
    }
}
