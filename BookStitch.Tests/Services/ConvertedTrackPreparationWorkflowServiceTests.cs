using BookStitch.Models;
using BookStitch.Services;
using System.IO;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class ConvertedTrackPreparationWorkflowServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "BookStitch.Tests",
        Guid.NewGuid().ToString("N"));

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
