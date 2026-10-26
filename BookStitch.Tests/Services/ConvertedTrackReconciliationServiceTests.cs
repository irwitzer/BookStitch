using BookStitch.Models;
using BookStitch.Services;
using BookStitch.Tests.TestHelpers;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class ConvertedTrackReconciliationServiceTests
{
    private readonly WorkManifestService _workManifestService = new();

    [Fact]
    public void Repeated_source_preparation_states_remain_independent_before_completion()
    {
        using var folder = new TemporaryFolder();
        var source = Path.Combine(folder.Path, "source.flac"); File.WriteAllBytes(source, [1, 2, 3]);
        var firstPath = Path.Combine(folder.Path, "first.m4a");
        var secondPath = Path.Combine(folder.Path, "second.m4a");
        var track = CreateTrack(); var preset = ExportPreset.Parse("AAC Stereo 96 kbps");
        var manifest = new ExportWorkManifest();
        _workManifestService.MarkTrackStarted(manifest, 0, track, source, firstPath, preset, ProjectManifestTrackStatuses.Converting);
        _workManifestService.MarkTrackStarted(manifest, 1, track, source, secondPath, preset, ProjectManifestTrackStatuses.Converting);
        Assert.Equal(2, manifest.Tracks.Count);
        _workManifestService.MarkTrackFailed(manifest, 0, track, source, firstPath, preset, "first failed");
        Assert.Equal(ProjectManifestTrackStatuses.Failed, manifest.Tracks.Single(entry => entry.ConvertedPath == firstPath).Status);
        Assert.Equal(ProjectManifestTrackStatuses.Converting, manifest.Tracks.Single(entry => entry.ConvertedPath == secondPath).Status);
        _workManifestService.MarkTrackCanceled(manifest, 1, track, source, secondPath, preset);
        Assert.Equal(ProjectManifestTrackStatuses.Canceled, manifest.Tracks.Single(entry => entry.ConvertedPath == secondPath).Status);
    }

    [Fact]
    public void Legacy_cache_without_identity_is_not_reusable_before_migration()
    {
        using var folder = new TemporaryFolder();
        var source = Path.Combine(folder.Path, "source.flac");
        var converted = Path.Combine(folder.Path, "converted.m4a");
        File.WriteAllBytes(source, [1, 2, 3]); File.WriteAllBytes(converted, [4, 5, 6]);
        var track = CreateTrack(); var preset = ExportPreset.Parse("AAC Stereo 96 kbps");
        var manifest = new ExportWorkManifest();
        _workManifestService.UpdateTrack(manifest, 0, track, source, converted, preset);
        manifest.Tracks[0].PreparationVersion = 0;
        manifest.Tracks[0].PreparationId = Guid.Empty;
        Assert.False(_workManifestService.CanReuseConvertedTrack(manifest, 0, track, source, converted, preset));
        Assert.True(File.Exists(source)); Assert.True(File.Exists(converted));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Reuse_trusts_structural_state_and_detects_changed_timestamp(bool restoreTimestamp)
    {
        using var folder = new TemporaryFolder();
        var source = Path.Combine(folder.Path, "source.flac");
        var converted = Path.Combine(folder.Path, "converted.m4a");
        File.WriteAllBytes(source, [1, 2, 3]);
        File.WriteAllBytes(converted, [4, 5, 6]);
        var track = CreateTrack();
        var preset = ExportPreset.Parse("AAC Stereo 96 kbps");
        var manifest = new ExportWorkManifest();
        _workManifestService.UpdateTrack(manifest, 0, track, source, converted, preset);
        var timestamp = File.GetLastWriteTimeUtc(converted);
        File.WriteAllBytes(converted, [4, 9, 6]);
        File.SetLastWriteTimeUtc(converted, restoreTimestamp ? timestamp : timestamp.AddSeconds(2));
        var result = new ConvertedTrackReconciliationService(_workManifestService).ReconcileTrack(
            manifest, ProjectManifestTypes.FolderProject, 0, track, source, converted, preset);
        Assert.Equal(restoreTimestamp, result.CanReuse);
        Assert.Equal(restoreTimestamp ? ProjectManifestTrackStatuses.Converted : ProjectManifestTrackStatuses.Pending, Assert.Single(manifest.Tracks).Status);
    }

    [Theory]
    [InlineData(ProjectManifestTypes.Mp3DiscProject)]
    [InlineData(ProjectManifestTypes.AudioCdProject)]
    public void Action_change_requires_preparation_and_keeps_one_physical_cache_entry(string projectType)
    {
        using var folder = new TemporaryFolder();
        var source = Path.Combine(folder.Path, "track.m4a");
        var converted = Path.Combine(folder.Path, "converted.m4a");
        File.WriteAllText(source, "source"); File.WriteAllText(converted, "converted");
        var track = CreateTrack(); track.ProcessingAction = ProcessingActionIds.Reuse;
        var preset = ExportPreset.Parse("AAC Stereo 96 kbps");
        var manifest = new ExportWorkManifest();
        _workManifestService.UpdateTrack(manifest, 0, track, source, converted, preset);
        track.ProcessingAction = ProcessingActionIds.Convert;
        var result = new ConvertedTrackReconciliationService(_workManifestService).ReconcileTrack(
            manifest, projectType, 0, track, source, converted, preset);
        Assert.False(result.CanReuse);
        Assert.Equal(ProjectManifestTrackStatuses.Pending, Assert.Single(manifest.Tracks).Status);
        _workManifestService.UpdateTrack(manifest, 0, track, source, converted, preset);
        var entry = manifest.Tracks.Single(entry => entry.ConvertedPath == Path.GetFullPath(converted));
        Assert.Equal(ProcessingActionIds.Convert, entry.Action);
        Assert.True(_workManifestService.CanReuseConvertedTrack(manifest, 0, track, source, converted, preset));
    }

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
    public void ReconcileTrack_RejectsUntrackedDiscCacheWithoutProcessingEvidence(string projectType)
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

        Assert.False(result.CanReuse);
        Assert.False(result.ManifestChanged);
        Assert.Empty(manifest.Tracks);
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
