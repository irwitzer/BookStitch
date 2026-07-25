using BookStitch.Models;
using BookStitch.Services;
using BookStitch.Tests.TestHelpers;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class ConvertedTrackReconciliationServiceTests
{
    private readonly WorkManifestService _workManifestService = new();

    [Fact]
    public void ReconcileTrack_UsesMatchingManifestEntryForLocalProject()
    {
        using var folder = new TemporaryFolder();
        var sourcePath = Path.Combine(folder.Path, "track.flac");
        var convertedPath = Path.Combine(folder.Path, "track.m4a");
        var preset = new ExportPreset("AAC Stereo 96 kbps", 96, 2);
        var track = CreateTrack();
        var manifest = new ExportWorkManifest();

        File.WriteAllBytes(sourcePath, [1, 2, 3]);
        File.WriteAllBytes(convertedPath, [4, 5, 6]);
        _workManifestService.UpdateTrack(manifest, 0, track, sourcePath, convertedPath, preset);

        var service = new ConvertedTrackReconciliationService(_workManifestService);

        var result = service.ReconcileTrack(
            manifest,
            ProjectManifestTypes.FolderProject,
            0,
            track,
            sourcePath,
            convertedPath,
            preset);

        Assert.True(result.CanReuse);
        Assert.True(result.ManifestChanged);
        Assert.Single(manifest.Tracks);
        Assert.Equal(ProjectManifestTrackStatuses.Converted, manifest.Tracks[0].Status);
    }

    [Theory]
    [InlineData(ProjectManifestTypes.Mp3DiscProject)]
    [InlineData(ProjectManifestTypes.AudioCdProject)]
    public void ReconcileTrack_RebuildsStaleDiscManifestEntry(string projectType)
    {
        using var folder = new TemporaryFolder();
        var sourcePath = Path.Combine(folder.Path, "track.flac");
        var convertedPath = Path.Combine(folder.Path, "track.m4a");
        var preset = new ExportPreset("AAC Stereo 96 kbps", 96, 2);
        var track = CreateTrack();
        var manifest = new ExportWorkManifest();

        File.WriteAllBytes(sourcePath, [1, 2, 3]);
        File.SetLastWriteTimeUtc(sourcePath, DateTime.UtcNow.AddMinutes(-2));
        File.WriteAllBytes(convertedPath, [4, 5, 6]);
        File.SetLastWriteTimeUtc(convertedPath, DateTime.UtcNow.AddMinutes(-1));

        var service = new ConvertedTrackReconciliationService(_workManifestService);

        var result = service.ReconcileTrack(
            manifest,
            projectType,
            4,
            track,
            sourcePath,
            convertedPath,
            preset);

        Assert.True(result.CanReuse);
        Assert.True(result.ManifestChanged);

        var entry = Assert.Single(manifest.Tracks);
        Assert.Equal(5, entry.TrackIndex);
        Assert.Equal(Path.GetFullPath(sourcePath), entry.SourcePath);
        Assert.Equal(Path.GetFullPath(convertedPath), entry.ConvertedPath);
        Assert.Equal(preset.DisplayName, entry.Preset);
        Assert.Equal(ProjectManifestTrackStatuses.Converted, entry.Status);
        Assert.Equal(new FileInfo(convertedPath).Length, entry.ConvertedSizeBytes);
        Assert.Equal(5, manifest.State.LastCompletedTrackIndex);
    }

    [Fact]
    public void ReconcileTrack_RejectsUntrackedPreparedFileForLocalProject()
    {
        using var folder = new TemporaryFolder();
        var sourcePath = Path.Combine(folder.Path, "track.flac");
        var convertedPath = Path.Combine(folder.Path, "track.m4a");
        var preset = new ExportPreset("AAC Stereo 96 kbps", 96, 2);
        var track = CreateTrack();
        var manifest = new ExportWorkManifest();

        File.WriteAllBytes(sourcePath, [1]);
        File.SetLastWriteTimeUtc(sourcePath, DateTime.UtcNow.AddMinutes(-2));
        File.WriteAllBytes(convertedPath, [2]);
        File.SetLastWriteTimeUtc(convertedPath, DateTime.UtcNow.AddMinutes(-1));

        var service = new ConvertedTrackReconciliationService(_workManifestService);

        var result = service.ReconcileTrack(
            manifest,
            ProjectManifestTypes.FolderProject,
            0,
            track,
            sourcePath,
            convertedPath,
            preset);

        Assert.False(result.CanReuse);
        Assert.False(result.ManifestChanged);
        Assert.Empty(manifest.Tracks);
    }

    [Theory]
    [InlineData("track.m4a.part")]
    [InlineData("track.m4a.copying")]
    public void ReconcileTrack_RejectsTemporaryDiscOutput(string fileName)
    {
        using var folder = new TemporaryFolder();
        var sourcePath = Path.Combine(folder.Path, "track.flac");
        var convertedPath = Path.Combine(folder.Path, fileName);
        var preset = new ExportPreset("AAC Stereo 96 kbps", 96, 2);
        var track = CreateTrack();
        var manifest = new ExportWorkManifest();

        File.WriteAllBytes(sourcePath, [1]);
        File.WriteAllBytes(convertedPath, [2]);

        var service = new ConvertedTrackReconciliationService(_workManifestService);

        var result = service.ReconcileTrack(
            manifest,
            ProjectManifestTypes.AudioCdProject,
            0,
            track,
            sourcePath,
            convertedPath,
            preset);

        Assert.False(result.CanReuse);
        Assert.False(result.ManifestChanged);
        Assert.Empty(manifest.Tracks);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReconcileTrack_ResetsStaleConvertedEntryWhenPreparedFileIsMissingOrEmpty(bool createEmptyFile)
    {
        using var folder = new TemporaryFolder();
        var sourcePath = Path.Combine(folder.Path, "track.flac");
        var convertedPath = Path.Combine(folder.Path, "track.m4a");
        var preset = new ExportPreset("AAC Stereo 96 kbps", 96, 2);
        var track = CreateTrack();
        var manifest = new ExportWorkManifest();

        File.WriteAllBytes(sourcePath, [1, 2, 3]);
        File.WriteAllBytes(convertedPath, [4, 5, 6]);
        _workManifestService.UpdateTrack(manifest, 3, track, sourcePath, convertedPath, preset);

        File.Delete(convertedPath);
        if (createEmptyFile)
            File.WriteAllBytes(convertedPath, []);

        var service = new ConvertedTrackReconciliationService(_workManifestService);

        var result = service.ReconcileTrack(
            manifest,
            ProjectManifestTypes.AudioCdProject,
            1,
            track,
            sourcePath,
            convertedPath,
            preset);

        Assert.False(result.CanReuse);
        Assert.True(result.ManifestChanged);

        var entry = Assert.Single(manifest.Tracks);
        Assert.Equal(2, entry.TrackIndex);
        Assert.Equal(ProjectManifestTrackStatuses.Pending, entry.Status);
        Assert.Equal(0, entry.ConvertedSizeBytes);
        Assert.Equal(0, entry.ConvertedLastWriteUtcTicks);
        Assert.Null(entry.StartedUtc);
        Assert.Null(entry.CompletedUtc);
        Assert.Empty(entry.LastError);
        Assert.Equal(0, manifest.State.LastCompletedTrackIndex);
    }

    [Fact]
    public void ReconcileTrack_ResetsStaleLocalEntryWhenSourceChanged()
    {
        using var folder = new TemporaryFolder();
        var sourcePath = Path.Combine(folder.Path, "track.flac");
        var convertedPath = Path.Combine(folder.Path, "track.m4a");
        var preset = new ExportPreset("AAC Stereo 96 kbps", 96, 2);
        var track = CreateTrack();
        var manifest = new ExportWorkManifest();

        File.WriteAllBytes(sourcePath, [1, 2, 3]);
        File.WriteAllBytes(convertedPath, [4, 5, 6]);
        _workManifestService.UpdateTrack(manifest, 0, track, sourcePath, convertedPath, preset);

        File.WriteAllBytes(sourcePath, [1, 2, 3, 7]);
        File.SetLastWriteTimeUtc(sourcePath, DateTime.UtcNow.AddMinutes(1));

        var service = new ConvertedTrackReconciliationService(_workManifestService);

        var result = service.ReconcileTrack(
            manifest,
            ProjectManifestTypes.FolderProject,
            0,
            track,
            sourcePath,
            convertedPath,
            preset);

        Assert.False(result.CanReuse);
        Assert.True(result.ManifestChanged);

        var entry = Assert.Single(manifest.Tracks);
        Assert.Equal(ProjectManifestTrackStatuses.Pending, entry.Status);
        Assert.Equal(new FileInfo(sourcePath).Length, entry.SourceSizeBytes);
        Assert.Equal(0, entry.ConvertedSizeBytes);
        Assert.Equal(0, manifest.State.LastCompletedTrackIndex);
    }

    private static TrackInfo CreateTrack()
    {
        return new TrackInfo
        {
            Index = 1,
            FileName = "track.flac",
            Extension = ".flac",
            Codec = "FLAC",
            ProcessingAction = "Konvertieren",
            Duration = "00:01:00",
            DurationTicks = TimeSpan.FromMinutes(1).Ticks,
            ChapterTitle = "001 Kapitel"
        };
    }
}
