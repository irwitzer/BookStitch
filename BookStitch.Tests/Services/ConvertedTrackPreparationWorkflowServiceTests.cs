using BookStitch.Models;
using BookStitch.Services;
using BookStitch.Tests.TestHelpers;
using System.IO;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class ConvertedTrackPreparationWorkflowServiceTests : IDisposable
{
    [Theory]
    [InlineData("Success")]
    [InlineData("Failed")]
    [InlineData("Canceled")]
    public async Task Start_has_no_commit_and_terminal_boundary_is_durable(string outcome)
    {
        Directory.CreateDirectory(_root);
        var source = Path.Combine(_root, "source.mp3");
        var target = Path.Combine(_root, "converted", "source.m4a");
        var path = Path.Combine(_root, "project.json");
        File.WriteAllText(source, "source");
        var track = new TrackInfo { Index = 1, SourceId = Guid.NewGuid(), FilePath = source,
            FileName = "source.mp3", ProcessingAction = ProcessingActionIds.Convert, DurationTicks = TimeSpan.TicksPerSecond };
        var item = new ConvertedTrackPreparationPlanItem(0, track, source, target, false);
        var resume = new ConvertedTrackResumeState(ConvertedTrackPreparationPlanStatus.RequiresPreparation, 1, 0, 1, 0);
        var plan = new ConvertedTrackPreparationPlan([item], [], [item], false, 0, 1, 0,
            ConvertedTrackPreparationPlanStatus.RequiresPreparation, resume);
        var trace = new PerformanceTraceService(true);
        trace.TryStartSession(new());
        var store = new WorkManifestService(trace);
        var manifest = new ExportWorkManifest();
        store.Save(path, manifest);
        var before = File.ReadAllBytes(path);
        var processor = new RecordingAudioExportProcessingService
        {
            Prepare = _ =>
            {
                Assert.Equal(before, File.ReadAllBytes(path));
                Assert.Equal(ProjectManifestTrackStatuses.Converting, Assert.Single(manifest.Tracks).Status);
                if (outcome == "Failed") throw new IOException("conversion failure");
                if (outcome == "Canceled") throw new OperationCanceledException();
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.WriteAllText(target, "complete audio");
                return Task.CompletedTask;
            }
        };
        var service = new ConvertedTrackPreparationWorkflowService(store, processor);
        var request = new ConvertedTrackPreparationWorkflowRequest(plan, manifest, path,
            ExportPreset.Parse("AAC Mono 64 kbps"), "unused", 1, TimeSpan.TicksPerSecond, new object());
        if (outcome == "Success")
            await service.RunAsync(request, new(), CancellationToken.None);
        else if (outcome == "Canceled")
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.RunAsync(request, new(), CancellationToken.None));
        else
            await Assert.ThrowsAsync<ExportTrackException>(() => service.RunAsync(request, new(), CancellationToken.None));
        var saved = Assert.Single(store.ReadOnly(path).Tracks);
        Assert.Equal(outcome == "Success" ? ProjectManifestTrackStatuses.Converted : outcome, saved.Status);
        Assert.Equal(outcome == "Success", WorkManifestService.IsReusableConvertedManifestEntry(saved, request.Preset.DisplayName));
        var saves = trace.CompleteSession(PerformanceOutcome.Success)!.Operations.Single(operation => operation.Name == "manifest.save");
        Assert.Equal(2, saves.Metrics.Single(metric => metric.Name == "Count").Value);
    }

    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "BookStitch.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Repeated_sources_prepare_independent_files_with_parallel_workers()
    {
        Directory.CreateDirectory(_root);
        var source = Path.Combine(_root, "source.m4a");
        await File.WriteAllBytesAsync(source, new byte[1024 * 1024]);
        var tracks = Enumerable.Range(1, 2).Select(index => new TrackInfo
        { Index = index, FilePath = source, FileName = index == 1 ? "source.m4a" : "repeated-source.m4a", ProcessingAction = ProcessingActionIds.Reuse, SampleRateHz = 44100,
          DurationTicks = TimeSpan.FromSeconds(1).Ticks }).ToArray();
        var manifest = new ExportWorkManifest();
        var manifestService = new WorkManifestService();
        var preset = ExportPreset.Parse("AAC Stereo 96 kbps");
        var plan = new ConvertedTrackPreparationPlanService(new ConvertedTrackReconciliationService(manifestService))
            .Build(manifest, ProjectManifestTypes.FolderProject, tracks, _root, Path.Combine(_root, "converted"), preset);
        var result = await new ConvertedTrackPreparationWorkflowService(manifestService, new AacExportProcessingService())
            .RunAsync(new ConvertedTrackPreparationWorkflowRequest(plan, manifest, Path.Combine(_root, "project.json"),
                preset, "unused", 4, TimeSpan.FromSeconds(2).Ticks, new object()),
                new ConvertedTrackPreparationWorkflowCallbacks(), CancellationToken.None);
        Assert.NotEqual(plan.Items[0].ConvertedPath, plan.Items[1].ConvertedPath);
        Assert.Equal(2, result.PreparedTrackCount);
        Assert.Equal(2, result.FinalProgress.CompletedCount);
        Assert.Equal(2, manifest.Tracks.Count);
        foreach (var item in plan.Items)
            Assert.Equal(await File.ReadAllBytesAsync(source), await File.ReadAllBytesAsync(item.ConvertedPath));
    }

    [Fact]
    public async Task RunAsync_PreparesPendingTrackAndPersistsCompletedManifestState()
    {
        Directory.CreateDirectory(_root);
        var sourcePath = Path.Combine(_root, "source.m4a");
        var convertedPath = Path.Combine(_root, "converted", "001_source.m4a");
        var manifestPath = Path.Combine(_root, "project.json");
        await File.WriteAllBytesAsync(sourcePath, [1, 2, 3, 4]);

        var track = new TrackInfo
        {
            Index = 1,
            FilePath = sourcePath,
            FileName = "source.m4a",
            Extension = ".m4a",
            SampleRateHz = 44100,
            ProcessingAction = "Übernehmen",
            DurationTicks = TimeSpan.FromSeconds(10).Ticks
        };
        var item = new ConvertedTrackPreparationPlanItem(
            0,
            track,
            sourcePath,
            convertedPath,
            CanReuse: false);
        var resumeState = new ConvertedTrackResumeState(
            ConvertedTrackPreparationPlanStatus.RequiresPreparation,
            TotalCount: 1,
            ReusableCount: 0,
            PendingCount: 1,
            ReusableDurationTicks: 0);
        var plan = new ConvertedTrackPreparationPlan(
            [item],
            [],
            [item],
            ManifestChanged: false,
            ReusableCount: 0,
            PendingCount: 1,
            ReusableDurationTicks: 0,
            Status: ConvertedTrackPreparationPlanStatus.RequiresPreparation,
            ResumeState: resumeState);
        var manifest = new ExportWorkManifest();
        var preset = new ExportPreset("AAC Stereo 96 kbps", 96, 2);
        var manifestService = new WorkManifestService();
        var service = new ConvertedTrackPreparationWorkflowService(
            manifestService,
            new AacExportProcessingService());
        var progressSnapshots = new List<ConvertedTrackPreparationProgressSnapshot>();

        var result = await service.RunAsync(
            new ConvertedTrackPreparationWorkflowRequest(
                plan,
                manifest,
                manifestPath,
                preset,
                FfmpegPath: "unused-for-copy",
                ParallelConversions: 2,
                TotalTicks: track.DurationTicks.Value,
                ManifestSyncRoot: new object()),
            new ConvertedTrackPreparationWorkflowCallbacks(progressSnapshots.Add),
            CancellationToken.None);

        Assert.Equal(1, result.PreparedTrackCount);
        Assert.True(File.Exists(convertedPath));
        Assert.Equal(await File.ReadAllBytesAsync(sourcePath), await File.ReadAllBytesAsync(convertedPath));
        Assert.Equal(1, result.FinalProgress.CompletedCount);
        Assert.Equal(track.DurationTicks.Value, result.FinalProgress.CurrentTicks);
        Assert.Contains(progressSnapshots, snapshot => snapshot.CompletedCount == 1);

        var entry = Assert.Single(manifest.Tracks);
        Assert.Equal(ProjectManifestTrackStatuses.Converted, entry.Status);
        Assert.Equal(Path.GetFullPath(convertedPath), entry.ConvertedPath);
        Assert.True(File.Exists(manifestPath));
    }

    [Fact]
    public async Task RunAsync_ForwardsPerformanceContextToAudioPreparation()
    {
        Directory.CreateDirectory(_root);
        var sourcePath = Path.Combine(_root, "source.mp3");
        await File.WriteAllTextAsync(sourcePath, "payload");
        var track = new TrackInfo
        {
            Index = 1,
            FilePath = sourcePath,
            FileName = "source.mp3",
            ProcessingAction = "Konvertieren",
            DurationTicks = TimeSpan.FromSeconds(2).Ticks
        };
        var item = new ConvertedTrackPreparationPlanItem(
            0,
            track,
            track.FilePath,
            Path.Combine(_root, "converted", "source.m4a"),
            CanReuse: false);
        var resumeState = new ConvertedTrackResumeState(
            ConvertedTrackPreparationPlanStatus.RequiresPreparation, 1, 0, 1, 0);
        var plan = new ConvertedTrackPreparationPlan(
            [item], [], [item], false, 0, 1, 0,
            ConvertedTrackPreparationPlanStatus.RequiresPreparation, resumeState);
        var processor = new RecordingAudioExportProcessingService();
        var service = new ConvertedTrackPreparationWorkflowService(new WorkManifestService(), processor);
        var context = new PerformanceExternalProcessContext(
            new PerformanceTraceService(enabled: true),
            Guid.NewGuid());

        var result = await service.RunAsync(
            new ConvertedTrackPreparationWorkflowRequest(
                plan,
                new ExportWorkManifest(),
                Path.Combine(_root, "project.json"),
                new ExportPreset("AAC Stereo 96 kbps", 96, 2),
                FfmpegPath: "ffmpeg",
                ParallelConversions: 1,
                TotalTicks: track.DurationTicks.Value,
                ManifestSyncRoot: new object(),
                PerformanceContext: context),
            new ConvertedTrackPreparationWorkflowCallbacks(),
            CancellationToken.None);

        Assert.Equal(1, result.PreparedTrackCount);
        Assert.Same(context, processor.LastPreparationContext);
    }

    [Fact]
    public async Task RunAsync_WithFullyReusablePlanReportsExistingProgressWithoutPreparingTracks()
    {
        var track = new TrackInfo
        {
            Index = 1,
            FileName = "ready.flac",
            DurationTicks = TimeSpan.FromSeconds(5).Ticks
        };
        var item = new ConvertedTrackPreparationPlanItem(
            0,
            track,
            "ready.flac",
            "ready.m4a",
            CanReuse: true);
        var resumeState = new ConvertedTrackResumeState(
            ConvertedTrackPreparationPlanStatus.FullyReusable,
            TotalCount: 1,
            ReusableCount: 1,
            PendingCount: 0,
            ReusableDurationTicks: track.DurationTicks.Value);
        var plan = new ConvertedTrackPreparationPlan(
            [item],
            [item],
            [],
            ManifestChanged: false,
            ReusableCount: 1,
            PendingCount: 0,
            ReusableDurationTicks: track.DurationTicks.Value,
            Status: ConvertedTrackPreparationPlanStatus.FullyReusable,
            ResumeState: resumeState);
        var snapshots = new List<ConvertedTrackPreparationProgressSnapshot>();
        var service = new ConvertedTrackPreparationWorkflowService(
            new WorkManifestService(),
            new AacExportProcessingService());

        var result = await service.RunAsync(
            new ConvertedTrackPreparationWorkflowRequest(
                plan,
                new ExportWorkManifest(),
                Path.Combine(_root, "project.json"),
                new ExportPreset("AAC Stereo 96 kbps", 96, 2),
                FfmpegPath: "unused",
                ParallelConversions: 2,
                TotalTicks: track.DurationTicks.Value,
                ManifestSyncRoot: new object()),
            new ConvertedTrackPreparationWorkflowCallbacks(snapshots.Add),
            CancellationToken.None);

        Assert.Equal(0, result.PreparedTrackCount);
        Assert.Equal(1, result.FinalProgress.CompletedCount);
        Assert.Equal(track.DurationTicks.Value, result.FinalProgress.CurrentTicks);
        Assert.Single(snapshots);
    }

    [Fact]
    public async Task RunBatchAsync_PreparesOnlyExplicitBatchItems()
    {
        Directory.CreateDirectory(_root);
        var sourcePath = Path.Combine(_root, "single.m4a");
        var convertedPath = Path.Combine(_root, "converted", "single.m4a");
        await File.WriteAllBytesAsync(sourcePath, [9, 8, 7]);

        var track = new TrackInfo
        {
            Index = 1,
            FilePath = sourcePath,
            FileName = "single.m4a",
            Extension = ".m4a",
            SampleRateHz = 44100,
            ProcessingAction = "Übernehmen",
            DurationTicks = TimeSpan.FromSeconds(2).Ticks
        };
        var item = new ConvertedTrackPreparationPlanItem(0, track, sourcePath, convertedPath, false);
        var resumeState = new ConvertedTrackResumeState(
            ConvertedTrackPreparationPlanStatus.RequiresPreparation, 1, 0, 1, 0);
        var plan = new ConvertedTrackPreparationPlan(
            [item], [], [item], false, 0, 1, 0,
            ConvertedTrackPreparationPlanStatus.RequiresPreparation, resumeState);
        var request = new ConvertedTrackPreparationWorkflowRequest(
            plan, new ExportWorkManifest(), Path.Combine(_root, "project.json"),
            new ExportPreset("AAC Stereo 96 kbps", 96, 2), "unused", 1,
            track.DurationTicks.Value, new object());
        var service = new ConvertedTrackPreparationWorkflowService(
            new WorkManifestService(), new AacExportProcessingService());

        var result = await service.RunBatchAsync(
            request,
            new ConvertedTrackPreparationBatch([item], resumeState),
            new ConvertedTrackPreparationWorkflowCallbacks(),
            CancellationToken.None);

        Assert.Equal(1, result.PreparedTrackCount);
        Assert.True(File.Exists(convertedPath));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // Test cleanup must not hide assertion failures.
        }
    }
}
