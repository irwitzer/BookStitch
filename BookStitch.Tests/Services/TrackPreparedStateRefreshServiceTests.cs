using BookStitch.Models;
using BookStitch.Services;
using BookStitch.Tests.TestHelpers;
using System.Text.Json;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class TrackPreparedStateRefreshServiceTests
{
    [Theory]
    [InlineData("null")]
    [InlineData("[null]")]
    public void Read_only_refresh_handles_null_track_state_without_repairing_the_file(string tracksJson)
    {
        using var folder = new TemporaryFolder();
        ProjectFolderLayout.EnsureProjectFolders(folder.Path);
        var path = ProjectFolderLayout.GetExportManifestPath(folder.Path);
        File.WriteAllText(path, "{\"Tracks\":" + tracksJson + "}");
        var bytes = File.ReadAllBytes(path);
        var track = new TrackInfo();
        new TrackPreparedStateRefreshService().RefreshForCurrentPreset([track], folder.Path, "AAC Stereo 192 kbps");
        Assert.False(track.HasReusableConvertedFile);
        Assert.Equal(bytes, File.ReadAllBytes(path));
    }

    [Fact]
    public void Refresh_never_normalizes_or_changes_manifest_when_selected_preset_differs()
    {
        using var folder = new TemporaryFolder();
        ProjectFolderLayout.EnsureProjectFolders(folder.Path);
        var path = ProjectFolderLayout.GetExportManifestPath(folder.Path);
        var manifest = new ExportWorkManifest
        {
            SourceFolder = "actual-source", SelectedPreset = "AAC Mono 64 kbps",
            ProjectWorkFolder = folder.Path,
            Tracks = [new ExportWorkManifestTrack { Action = "legacy action" }]
        };
        File.WriteAllText(path, JsonSerializer.Serialize(manifest));
        var bytes = File.ReadAllBytes(path);
        var stamp = File.GetLastWriteTimeUtc(path);
        for (var index = 0; index < 3; index++)
            new TrackPreparedStateRefreshService().RefreshForCurrentPreset(
                [new TrackInfo()], folder.Path, "AAC Stereo 192 kbps");
        Assert.Equal(bytes, File.ReadAllBytes(path));
        Assert.Equal(stamp, File.GetLastWriteTimeUtc(path));
        var saved = new WorkManifestService().ReadOnly(path);
        Assert.Equal("actual-source", saved.SourceFolder);
        Assert.Equal("AAC Mono 64 kbps", saved.SelectedPreset);
        Assert.Equal("legacy action", Assert.Single(saved.Tracks).Action);
        Assert.Equal(Guid.Empty, saved.Tracks[0].PreparationId);
    }

    [Theory]
    [InlineData("AAC Stereo 192 kbps", true)]
    [InlineData("AAC Mono 64 kbps", false)]
    public void Refresh_projects_only_the_selected_valid_manifest_preset(
        string selectedPreset, bool expectedReusable)
    {
        using var folder = new TemporaryFolder();
        var sourcePath = Path.Combine(folder.Path, "source.flac");
        var convertedPath = Path.Combine(folder.Path, "source.m4a");
        File.WriteAllBytes(sourcePath, [1, 2, 3]);
        File.SetLastWriteTimeUtc(sourcePath, DateTime.UtcNow.AddMinutes(-2));
        File.WriteAllBytes(convertedPath, new byte[512 * 1024]);
        WriteExportManifest(folder.Path, sourcePath, convertedPath, "AAC Stereo 192 kbps");
        var track = new TrackInfo { FilePath = sourcePath };

        new TrackPreparedStateRefreshService().RefreshForCurrentPreset(
            [track], folder.Path, selectedPreset);

        Assert.Equal(expectedReusable ? convertedPath : "", track.PreparedConvertedPath);
        Assert.Equal(expectedReusable, track.HasReusableConvertedFile);
        Assert.Equal(expectedReusable, track.ConvertedSizeAvailable);
    }

    [Fact]
    public void RefreshForCurrentPreset_MarksReusablePreparedTrack()
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
            ConvertedSizeAvailable = true
        };

        new TrackPreparedStateRefreshService().RefreshForCurrentPreset(
            [track],
            folder.Path,
            "AAC Stereo 192 kbps");

        Assert.Equal("", track.PreparedConvertedPath);
        Assert.Equal("", track.PreparedConvertedPreset);
        Assert.False(track.HasReusableConvertedFile);
        Assert.False(track.ConvertedSizeAvailable);
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

    [Fact]
    public void GetPreparationProgress_CountsReusableTracksForTheSelectedPreset()
    {
        using var folder = new TemporaryFolder();
        var reusable = CreatePreparedTrack(folder.Path, "reusable", "selected");
        var otherPreset = CreatePreparedTrack(folder.Path, "other-preset", "other");
        var pending = new TrackInfo
        {
            FilePath = Path.Combine(folder.Path, "pending.flac")
        };
        File.WriteAllText(pending.FilePath, "source");

        var progress = new TrackPreparedStateRefreshService().GetPreparationProgress(
            [reusable, otherPreset, pending],
            "selected");

        Assert.Equal(new PresetPreparationProgress(1, 3, 2, 33), progress);
    }

    [Fact]
    public void GetPreparationProgress_EmptyTrackSetReturnsZeroProgress()
    {
        var progress = new TrackPreparedStateRefreshService().GetPreparationProgress([], "selected");

        Assert.Equal(new PresetPreparationProgress(0, 0, 0, 0), progress);
    }

    [Theory]
    [InlineData(ProjectManifestTypes.FolderProject)]
    [InlineData(ProjectManifestTypes.Mp3DiscProject)]
    [InlineData(ProjectManifestTypes.AudioCdProject)]
    public void Rebuilt_tracks_project_selected_cache_and_progressively_completed_files(string projectType)
    {
        using var folder = new TemporaryFolder();
        const string selected = "AAC Mono 64 kbps";
        var tracks = new[] { "disc1.mp3", "disc2.mp3" }.Select(name => new TrackInfo
        {
            FilePath = Path.Combine(folder.Path, name), FileName = name
        }).ToArray();
        foreach (var track in tracks)
        {
            File.WriteAllText(track.FilePath, "source");
            File.SetLastWriteTimeUtc(track.FilePath, DateTime.UtcNow.AddMinutes(-2));
        }
        var cache = ProjectFolderLayout.GetConvertedPresetFolder(folder.Path, ExportPreset.Parse(selected).GetFolderName());
        Directory.CreateDirectory(cache);
        string Converted(TrackInfo track) => ConvertedTrackPathService.GetConvertedTrackPath(cache, track.FilePath, track);
        var store = new WorkManifestService();
        var manifest = new ExportWorkManifest { ProjectType = projectType, ProjectWorkFolder = folder.Path };
        var manifestPath = ProjectFolderLayout.GetExportManifestPath(folder.Path);
        File.WriteAllText(Converted(tracks[0]), "prepared");
        store.UpdateTrack(manifest, 0, tracks[0], tracks[0].FilePath, Converted(tracks[0]), ExportPreset.Parse(selected));
        store.Save(manifestPath, manifest);
        var service = new TrackPreparedStateRefreshService();
        service.RefreshForCurrentPreset(tracks, folder.Path, selected);
        Assert.Equal(64, tracks[0].DisplayPreparedBitrateKbps);
        Assert.Equal("Mono", tracks[0].DisplayPreparedChannelLayout);
        Assert.Null(tracks[1].DisplayPreparedBitrateKbps);
        service.RefreshForCurrentPreset(tracks, folder.Path, "AAC Stereo 128 kbps");
        Assert.All(tracks, track => Assert.Null(track.DisplayPreparedBitrateKbps));
        File.WriteAllText(Converted(tracks[1]), "prepared");
        store.UpdateTrack(manifest, 1, tracks[1], tracks[1].FilePath, Converted(tracks[1]), ExportPreset.Parse(selected));
        store.Save(manifestPath, manifest);
        service.RefreshForCurrentPreset(tracks, folder.Path, selected);
        Assert.All(tracks, track => Assert.Equal(64, track.DisplayPreparedBitrateKbps));
        service.RefreshForCurrentPreset(tracks, null, selected);
        Assert.All(tracks, track => Assert.Null(track.DisplayPreparedBitrateKbps));
    }

    private static TrackInfo CreatePreparedTrack(string root, string name, string preset)
    {
        var sourcePath = Path.Combine(root, $"{name}.flac");
        var convertedPath = Path.Combine(root, $"{name}.m4a");
        File.WriteAllText(sourcePath, "source");
        File.SetLastWriteTimeUtc(sourcePath, DateTime.UtcNow.AddMinutes(-2));
        File.WriteAllText(convertedPath, "prepared");
        File.SetLastWriteTimeUtc(convertedPath, DateTime.UtcNow.AddMinutes(-1));
        return new TrackInfo
        {
            FilePath = sourcePath,
            PreparedConvertedPath = convertedPath,
            PreparedConvertedPreset = preset, HasReusableConvertedFile = true
        };
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
                    SourceSizeBytes = new FileInfo(sourcePath).Length,
                    SourceLastWriteUtcTicks = File.GetLastWriteTimeUtc(sourcePath).Ticks,
                    ConvertedSizeBytes = new FileInfo(convertedPath).Length,
                    ConvertedLastWriteUtcTicks = File.GetLastWriteTimeUtc(convertedPath).Ticks,
                    PreparationVersion = 1, PreparationId = Guid.NewGuid(),
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
