using BookStitch.Models;
using BookStitch.Services;
using BookStitch.Tests.TestHelpers;
using System.IO;
using Xunit;
namespace BookStitch.Tests.Services;

public sealed class LiveOutputColumnTests
{
    [Fact]
    public void Manual_visibility_priority_survives_settings_reload_and_default_reset()
    {
        using var folder = new TemporaryFolder();
        var service = new SettingsService(Path.Combine(folder.Path, "app"), Path.Combine(folder.Path, "projects"));
        service.Save(new AppSettings { WorkingFolder = Path.Combine(folder.Path, "projects"), TrackGridCustomColumnVisibility = true,
            TrackGridColumns = [new() { Key = "Channels", IsVisible = false }, new() { Key = "DisplayPreparedProfile", IsVisible = true }] });
        var reloaded = new SettingsService(Path.Combine(folder.Path, "app"), Path.Combine(folder.Path, "projects")).Load();
        Assert.True(reloaded.TrackGridCustomColumnVisibility);
        Assert.False(reloaded.TrackGridColumns.Single(c => c.Key == "Channels").IsVisible);
        reloaded.TrackGridCustomColumnVisibility = false; reloaded.TrackGridColumns.Clear(); service.Save(reloaded);
        Assert.False(service.Load().TrackGridCustomColumnVisibility);
    }

    [Fact]
    public void Lossless_completed_output_preview_uses_source_properties_instead_of_nominal_bitrate()
    {
        using var folder = new TemporaryFolder(); ProjectFolderLayout.EnsureProjectFolders(folder.Path);
        var source = Path.Combine(folder.Path, "source.m4a"); var output = Path.Combine(folder.Path, "copy.m4a");
        File.WriteAllText(source, "source"); File.WriteAllText(output, "completed");
        var track = new TrackInfo { SourceId = Guid.NewGuid(), FilePath = source, SampleRateHz = 48000, TargetSampleRateHz = 48000,
            Channels = 1, AudioProfile = "LC", AudioBitrateBitsPerSecond = 62000m, ProcessingAction = ProcessingActionIds.Reuse };
        var preset = ExportPreset.Parse("AAC Mono 64 kbps"); var store = new WorkManifestService();
        var path = ProjectFolderLayout.GetExportManifestPath(folder.Path);
        var manifest = store.LoadOrCreate(path, folder.Path, folder.Path, preset.DisplayName);
        store.UpdateTrack(manifest, 0, track, source, output, preset); store.Save(path, manifest);
        TrackStateUpdateQueueService.ApplyUpdates(new([track], folder.Path, preset.DisplayName, true, PreparedOnly: true));
        Assert.Equal(62, track.DisplayPreparedBitrateKbps); Assert.Equal("48.000", track.DisplayPreparedSampleRateHz);
        Assert.Equal("Mono", track.DisplayPreparedChannelLayout); Assert.Equal("AAC-LC", track.DisplayPreparedProfile);
    }

    [Fact]
    public void Completed_checkpoints_fill_live_and_pause_resume_fills_the_gap_without_probes()
    {
        using var folder = new TemporaryFolder();
        ProjectFolderLayout.EnsureProjectFolders(folder.Path);
        const string preset = "AAC Stereo 96 kbps";
        var tracks = Enumerable.Range(0, 3).Select(i => new TrackInfo { SourceId = Guid.NewGuid(), FilePath = Path.Combine(folder.Path, $"{i}.flac"),
            SampleRateHz = 48000, TargetSampleRateHz = 44100, Channels = 1 }).ToArray();
        foreach (var track in tracks) File.WriteAllText(track.FilePath, "source");
        var store = new WorkManifestService(); var path = ProjectFolderLayout.GetExportManifestPath(folder.Path);
        var manifest = store.LoadOrCreate(path, folder.Path, folder.Path, preset);
        void Complete(int i)
        {
            var target = Path.Combine(folder.Path, $"{i}.m4a"); File.WriteAllText(target, "completed");
            store.UpdateTrack(manifest, i, tracks[i], tracks[i].FilePath, target, ExportPreset.Parse(preset)); store.Save(path, manifest);
        }
        var context = new TrackStateUpdateContext(tracks, folder.Path, preset, true, PreparedOnly: true);
        Complete(0); Complete(2);
        Assert.True(TrackStateUpdateQueueService.ApplyUpdates(context));
        Assert.All(tracks, t => Assert.False(t.SourceSizeAvailable)); // Disc refresh does not re-inspect live source presentation.
        foreach (var i in new[] { 0, 2 })
        {
            Assert.Equal(96, tracks[i].DisplayPreparedBitrateKbps); Assert.Equal("44.100", tracks[i].DisplayPreparedSampleRateHz);
            Assert.Equal("Stereo", tracks[i].DisplayPreparedChannelLayout); Assert.Equal("AAC-LC", tracks[i].DisplayPreparedProfile);
            Assert.Null(tracks[i].PreparedAudio); // No stream or packet probe is required for live display.
        }
        Assert.Null(tracks[1].DisplayPreparedBitrateKbps); // Interrupted middle worker has no successful checkpoint.
        TrackStateUpdateQueueService.ApplyUpdates(context with { IsWorkflowActive = false });
        Complete(1); Assert.True(TrackStateUpdateQueueService.ApplyUpdates(context));
        Assert.All(tracks, t => Assert.Equal(96, t.DisplayPreparedBitrateKbps));
        var entry = manifest.Tracks.Single(e => e.SourceId == tracks[1].SourceId);
        entry.AacTiming = new(1, entry.ConvertedSizeBytes, entry.ConvertedLastWriteUtcTicks, 44100, 1, 44100)
            { Audio = new("aac", "LC", 44100, 2, 95123m) };
        store.Save(path, manifest);
        Assert.True(TrackStateUpdateQueueService.ApplyUpdates(context));
        Assert.Equal(95, tracks[1].DisplayPreparedBitrateKbps); // Measured evidence supersedes the nominal live value.
        File.Delete(entry.ConvertedPath);
        TrackStateUpdateQueueService.ApplyUpdates(context);
        Assert.Empty(tracks[1].DisplayPreparedProfile); Assert.Null(tracks[1].DisplayPreparedBitrateKbps);
        Assert.Equal(96, tracks[2].DisplayPreparedBitrateKbps);
    }
}
