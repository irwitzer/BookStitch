using BookStitch.Models;
using BookStitch.Services;
using BookStitch.Tests.TestHelpers;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed partial class RealMediaIntegrationTests
{
    // Deliberately opt-in: thousands of real ffmpeg/ffprobe processes are a load
    // reproduction, not appropriate for every unit-test invocation.
    public sealed class LargePreparationTheoryAttribute : TheoryAttribute
    {
        public LargePreparationTheoryAttribute()
        {
            if (Environment.GetEnvironmentVariable("BOOKSTITCH_LARGE_PREPARATION_TEST") != "1")
                Skip = "Set BOOKSTITCH_LARGE_PREPARATION_TEST=1 for the 1308-track real-media reproduction.";
        }
    }

    [LargePreparationTheory]
    [Trait("Category", "LargePreparedLifecycleIntegration")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Folder_1308_real_tracks_reach_review_with_read_only_polling_and_optional_pause(bool pause)
    {
        var tools = ResolveFfmpegToolchain();
        Assert.NotNull(tools);
        using var temp = new TemporaryFolder();
        var sources = Path.Combine(temp.Path, "input");
        var project = Path.Combine(temp.Path, "project");
        Directory.CreateDirectory(sources);
        var seed = Path.Combine(temp.Path, "seed.wav");
        await RunProcessAsync(tools.FfmpegPath,
            ["-y", "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=44100", "-t", "0.25", "-ac", "2", seed]);
        var paths = Enumerable.Range(1, 1308).Select(index => Path.Combine(sources, $"{index:0000}.wav")).ToArray();
        foreach (var path in paths) File.Copy(seed, path);
        var preset = ExportPreset.Parse("AAC Stereo 192 kbps");
        var parallel = Math.Clamp(Environment.ProcessorCount + 4, 2, 32); // Current Folder Auto contract.
        var originals = ProjectFolderLayout.GetOriginalsFolder(project);
        var tracks = paths.Select((path, index) => new TrackInfo
        {
            Index = index, FileName = Path.GetFileName(path), FilePath = Path.Combine(originals, Path.GetFileName(path)),
            Extension = "wav", Codec = "pcm_s16le", SampleRateHz = 44100,
            DurationTicks = TimeSpan.FromSeconds(0.25).Ticks, Duration = "00:00:00.250",
            ProcessingAction = ProcessingActionIds.Convert
        }).ToArray();
        var store = new WorkManifestService();
        var audio = new AacExportProcessingService();
        var manifestPath = ProjectFolderLayout.GetWorkManifestPath(project);
        ProjectFolderLayout.EnsureProjectFolders(project);
        var manifest = store.LoadOrCreate(manifestPath, project, originals, preset.DisplayName);
        manifest.State.Status = ProjectManifestStatuses.AcquiringSources;
        store.Save(manifestPath, manifest);
        var convertedFolder = ProjectFolderLayout.GetConvertedPresetFolder(project, preset.GetFolderName());
        Directory.CreateDirectory(convertedFolder);
        var targets = ConvertedTrackPathService.GetConvertedTrackPaths(convertedFolder, project, tracks);
        var sync = new object();
        var importer = new LocalProjectLivePreparationService(new LocalProjectImportService());
        using var pollingStop = new CancellationTokenSource();
        var polling = Task.Run(async () =>
        {
            while (!pollingStop.IsCancellationRequested)
            {
                new TrackPreparedStateRefreshService().RefreshForCurrentPreset(tracks, project, preset.DisplayName);
                await Task.Delay(25);
            }
        });
        try
        {
            async Task<LocalProjectLivePreparationResult> RunPreparation(bool requestPause)
            {
                using var cancel = new CancellationTokenSource();
                await using var lifecycle = new PreparedTrackLifecycleSession(store, audio, manifest,
                    manifestPath, sync, tools.FfprobePath, parallel);
                lifecycle.QueuePreset(preset, cancel.Token);
                var progress = new ImmediatePreparationProgress(snapshot =>
                {
                    if (requestPause && snapshot.PreparedFiles >= 12) cancel.Cancel();
                });
                return await importer.RunAsync(new LocalProjectLivePreparationRequest(sources, paths, project,
                    parallel, lifecycle.DrainAsync), async (copied, token) =>
                {
                    var index = int.Parse(Path.GetFileNameWithoutExtension(copied.SourceFile), System.Globalization.CultureInfo.InvariantCulture) - 1;
                    var track = tracks[index];
                    track.SourceId = copied.SourceIdentity!.SourceId;
                    if (lifecycle.CanReuse(track.FilePath, targets[index], preset)) return;
                    lifecycle.MarkStarted(index, track, track.FilePath, targets[index], preset, token);
                    await audio.PrepareTrackForExportAsync(track, track.FilePath, targets[index], preset,
                        tools.FfmpegPath, token, _ => { }, ffprobePath: tools.FfprobePath);
                    lifecycle.Complete(index, track, track.FilePath, targets[index], preset, token);
                }, progress, cancel.Token);
            }

            var first = await RunPreparation(pause);
            if (pause)
            {
                Assert.True(first.WasCanceled);
                var paused = store.ReadOnly(manifestPath);
                var prepared = paused.Tracks.Where(entry => WorkManifestService.IsReusableConvertedManifestEntry(entry, preset.DisplayName))
                    .ToDictionary(entry => entry.ConvertedPath, entry => entry.PreparationId);
                Assert.NotEmpty(prepared);
                new ConvertedFileCleanupService().DeletePartFiles(project);
                await Task.Delay(2000);
                manifest = store.LoadOrCreate(manifestPath, project, originals, preset.DisplayName);
                Assert.False((await RunPreparation(false)).WasCanceled);
                foreach (var entry in manifest.Tracks.Where(entry => prepared.ContainsKey(entry.ConvertedPath)))
                    Assert.Equal(prepared[entry.ConvertedPath], entry.PreparationId);
            }
            else Assert.False(first.WasCanceled);
            Assert.Equal(1308, manifest.Tracks.Count);
            Assert.All(manifest.Tracks, entry =>
            {
                Assert.True(WorkManifestService.IsReusableConvertedManifestEntry(entry, preset.DisplayName));
                Assert.True(entry.AacTiming?.IsValid);
            });
            // Exercise the real common review boundary too; Auto-Merge is deliberately off.
            var output = Path.Combine(temp.Path, "output", "book.m4a");
            var plan = new ExportPlanService().Create(new ExportPlanRequest(tracks, originals, temp.Path,
                output, preset.DisplayName, parallel, project));
            var snapshot = new ProjectSnapshotUiState(preset.DisplayName, "Auto", ".m4a",
                Path.GetDirectoryName(output)!, "{Titel}", "Load test", "Author", "Album", "", "", "", "", "book.m4a");
            var workflow = new ExportWorkflowService(store,
                new ProjectSnapshotService(new Mp3DiscProjectService(), new AudioDiscProjectService(), store),
                audio, new ConvertedFileCleanupService(),
                new ConvertedTrackPreparationPlanService(new ConvertedTrackReconciliationService(store)),
                new ConvertedTrackPreparationWorkflowService(store, audio), new ExportChapterService(),
                new FinalTagService(), new FinalOutputStorageService());
            var states = new List<ProjectPipelineState>();
            var result = await workflow.RunAsync(new ExportWorkflowRequest(plan, originals, tools.FfmpegPath,
                snapshot, new FinalAudioTagData { Title = "Load test" }, FfprobePath: tools.FfprobePath),
                new ExportWorkflowCallbacks { MergeAutomaticallyAfterConversion = () => false,
                    SetPipelineState = state => states.Add(state) }, CancellationToken.None);
            Assert.Equal(ExportWorkflowResultStatus.PausedBeforeMerge, result.Status);
            Assert.Contains(ProjectPipelineState.ReviewBeforeMerge, states);
            Assert.Equal(ProjectManifestStatuses.ReviewBeforeMerge, store.ReadOnly(plan.ManifestPath).State.Status);
        }
        finally
        {
            pollingStop.Cancel();
            await polling;
        }
    }

    private sealed class ImmediatePreparationProgress(Action<LocalProjectLivePreparationProgress> callback)
        : IProgress<LocalProjectLivePreparationProgress>
    {
        public void Report(LocalProjectLivePreparationProgress value) => callback(value);
    }
}
