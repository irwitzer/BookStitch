using BookStitch.Models;
using BookStitch.Services;
using BookStitch.Tests.TestHelpers;
using System.IO;
using Xunit;
namespace BookStitch.Tests.Services;
public sealed partial class RealMediaIntegrationTests
{
    [Fact]
    public async Task Local_acquisition_publishes_real_prepared_tracks_before_run_end_and_fills_paused_gap_on_resume()
    {
        var tools = ResolveFfmpegToolchain(); Assert.NotNull(tools);
        using var folder = new TemporaryFolder();
        var sourceFolder = Path.Combine(folder.Path, "external"); Directory.CreateDirectory(sourceFolder);
        var sources = Enumerable.Range(0, 2).Select(i => Path.Combine(sourceFolder, $"{i}.mp3")).ToArray();
        foreach (var source in sources)
            await RunProcessAsync(tools.FfmpegPath, ["-v", "error", "-f", "lavfi", "-i", "anoisesrc=sample_rate=44100:seed=42", "-t", "1", "-ac", "2", "-c:a", "libmp3lame", source]);
        var originals = sources.Select(File.ReadAllBytes).ToArray();
        var project = new LocalProjectImportService().CreateProjectFolder(Path.Combine(folder.Path, "projects"), sourceFolder);
        var preset = ExportPreset.Parse("AAC Stereo 160 kbps");
        var tracks = sources.Select(source => new TrackInfo { SourceId = Guid.NewGuid(), FilePath = source, FileName = Path.GetFileName(source), Codec = "MP3", TargetSampleRateHz = 44100 }).ToArray();
        var store = new WorkManifestService(); var manifestPath = ProjectFolderLayout.GetExportManifestPath(project);
        var manifest = store.LoadOrCreate(manifestPath, project, ProjectFolderLayout.GetOriginalsFolder(project), preset.DisplayName);
        var firstReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<LocalProjectLivePreparationResult> Run(bool pauseSecond, CancellationToken token)
        {
            await using var lifecycle = new PreparedTrackLifecycleSession(store, new AacExportProcessingService(), manifest, manifestPath, new object(), tools.FfprobePath, 2);
            return await new LocalProjectLivePreparationService(new LocalProjectImportService()).RunAsync(
                new(sourceFolder, sources, project, 2, lifecycle.DrainAsync), async (copy, progress, cancellation) =>
                {
                    var index = Array.IndexOf(sources, copy.SourceFile); var track = tracks[index];
                    track.SourceId = copy.SourceIdentity!.SourceId;
                    if (pauseSecond && index == 1) await Task.Delay(Timeout.Infinite, cancellation);
                    var output = Path.Combine(project, $"prepared-{index}.m4a");
                    ExportWorkManifestTrack completed;
                    if (!lifecycle.CanReuse(copy.TargetFile, output, preset))
                    {
                        lifecycle.MarkStarted(index, track, copy.TargetFile, output, preset, cancellation);
                        await new AacExportProcessingService().PrepareTrackForExportAsync(track, copy.TargetFile, output, preset, tools.FfmpegPath, cancellation, _ => { });
                        completed = lifecycle.Complete(index, track, copy.TargetFile, output, preset, cancellation);
                    }
                    else completed = manifest.Tracks.Single(entry => entry.TrackIndex == index + 1);
                    Assert.True(TrackPreparedStateRefreshService.ApplyCompletedCheckpoint(track, completed, preset.DisplayName));
                    if (index == 0) firstReady.TrySetResult();
                }, null, token);
        }
        using var cancellation = new CancellationTokenSource();
        var running = Run(true, cancellation.Token);
        try
        {
            await firstReady.Task.WaitAsync(TimeSpan.FromSeconds(20));
            Assert.False(running.IsCompleted); Assert.True(tracks[0].HasReusableConvertedFile); Assert.False(tracks[1].HasReusableConvertedFile);
            Assert.True(tracks[0].DisplayPreparedBitrateKbps > 0); Assert.Equal("AAC-LC", tracks[0].DisplayPreparedProfile);
            Assert.Equal(sources[0], tracks[0].FilePath); // The production acquisition path has not rebound the row yet.
            TrackStateUpdateQueueService.ApplyUpdates(new(tracks, project, preset.DisplayName, true));
            Assert.True(tracks[0].HasReusableConvertedFile);
        }
        finally { cancellation.Cancel(); }
        Assert.True((await running).WasCanceled);
        var firstId = manifest.Tracks.Single(entry => entry.TrackIndex == 1).PreparationId;
        Assert.False((await Run(false, default)).WasCanceled);
        TrackStateUpdateQueueService.ApplyUpdates(new(tracks, project, preset.DisplayName, true));
        Assert.All(tracks, track => { Assert.True(track.HasReusableConvertedFile); Assert.Equal("AAC-LC", track.DisplayPreparedProfile); });
        Assert.Equal(firstId, manifest.Tracks.Single(entry => entry.TrackIndex == 1).PreparationId);
        for (var i = 0; i < sources.Length; i++) Assert.Equal(originals[i], File.ReadAllBytes(sources[i]));
    }
}
