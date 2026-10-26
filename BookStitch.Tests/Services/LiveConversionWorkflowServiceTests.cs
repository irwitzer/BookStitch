using BookStitch.Models;
using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class LiveConversionWorkflowServiceTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "BookStitch.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Critical_prepared_checkpoint_failure_propagates_instead_of_marking_audio_conversion_failed()
    {
        Directory.CreateDirectory(_tempRoot);
        var source = Path.Combine(_tempRoot, "source.mp3");
        var output = Path.Combine(_tempRoot, "prepared.m4a");
        await File.WriteAllTextAsync(source, "prepared audio");
        using var semaphore = new SemaphoreSlim(1);
        var track = new TrackInfo { FilePath = source, FileName = "source.mp3", SampleRateHz = 44100, ProcessingAction = ProcessingActionIds.Reuse };
        var preset = ExportPreset.Parse("AAC Stereo 192 kbps");
        var failure = new PreparedTrackCheckpointException(new IOException("checkpoint failed; saved state preserved"));
        var error = await Assert.ThrowsAsync<PreparedTrackCheckpointException>(() =>
            new LiveConversionWorkflowService(new AacExportProcessingService(), () => "").RunAsync(
                track, new(source, output, preset.DisplayName, 1, 1), preset, new(), semaphore,
                () => { }, () => Assert.Fail("A failed hard checkpoint cannot complete live preparation"),
                () => Assert.Fail("A hard checkpoint failure is not an AAC conversion failure"),
                CancellationToken.None, persistPreparedTrack: () => throw failure));
        Assert.Same(failure, error);
        Assert.True(File.Exists(output));
        Assert.Equal("prepared audio", await File.ReadAllTextAsync(source));
        Assert.Equal(1, semaphore.CurrentCount);
    }

    [Fact]
    public async Task Live_completion_registers_verified_cache_before_export_handoff()
    {
        Directory.CreateDirectory(_tempRoot);
        var source = Path.Combine(_tempRoot, "live.mp3");
        var output = Path.Combine(_tempRoot, "live.m4a");
        await File.WriteAllTextAsync(source, "prepared audio");
        var track = new TrackInfo { SourceId = Guid.NewGuid(), FilePath = source,
            FileName = "live.mp3", SampleRateHz = 44100, ProcessingAction = ProcessingActionIds.Reuse };
        var preset = ExportPreset.Parse("AAC Stereo 192 kbps");
        var manifests = new WorkManifestService();
        var manifest = new ExportWorkManifest();
        using var semaphore = new SemaphoreSlim(1);
        var finished = false;
        await new LiveConversionWorkflowService(new AacExportProcessingService(), () => "").RunAsync(
            track, new(source, output, preset.DisplayName, 1, 1), preset, new(), semaphore,
            () => { }, () => { Assert.True(manifests.CanReuseConvertedTrack(manifest, 0, track, source, output, preset)); finished = true; },
            () => Assert.Fail("Live preparation failed"), CancellationToken.None,
            persistPreparedTrack: () => manifests.UpdateTrack(manifest, 0, track, source, output, preset));
        Assert.True(finished);
        Assert.False(manifests.CanReuseConvertedTrack(manifest, 0, track, source, output, ExportPreset.Parse("AAC Mono 64 kbps")));
        // Prepared reuse trusts file stamps, not content hashes. Change the size
        // deterministically instead of relying on two fast writes having different timestamps.
        await File.WriteAllTextAsync(output, "changed audio with a different size");
        Assert.False(manifests.CanReuseConvertedTrack(manifest, 0, track, source, output, preset));
    }

    [Fact]
    public async Task RunAsync_CopiesTakeOverTrackAndMarksCompleted()
    {
        var sourcePath = Path.Combine(_tempRoot, "source.mp3");
        var convertedPath = Path.Combine(_tempRoot, "converted", "source.m4a");
        Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
        await File.WriteAllTextAsync(sourcePath, "test audio payload");

        var service = new LiveConversionWorkflowService(new AacExportProcessingService(), () => "");
        var queue = new LiveConversionQueueService();
        var item = new LiveConversionQueueItem(sourcePath, convertedPath, "AAC Stereo 128 kbps", 1, 1);
        using var semaphore = new SemaphoreSlim(1);

        var completed = 0;
        var failed = 0;

        await service.RunAsync(
            new TrackInfo { FilePath = sourcePath, FileName = "source.mp3", SampleRateHz = 44100, ProcessingAction = "Übernehmen" },
            item,
            ExportPreset.Parse("AAC Stereo 128 kbps"),
            queue,
            semaphore,
            () => { },
            () => completed++,
            () => failed++,
            CancellationToken.None);

        Assert.True(File.Exists(convertedPath));
        Assert.Equal("test audio payload", await File.ReadAllTextAsync(convertedPath));
        Assert.Equal(1, completed);
        Assert.Equal(0, failed);
        Assert.Equal(1, queue.CreateSnapshot().CompletedCount);
        Assert.Equal(1, semaphore.CurrentCount);
    }

    [Fact]
    public async Task RunAsync_ResolvesPerformanceContextAfterSemaphoreAndStartedCallback()
    {
        var sourcePath = Path.Combine(_tempRoot, "ordered-source.mp3");
        var convertedPath = Path.Combine(_tempRoot, "converted", "ordered-source.m4a");
        Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
        await File.WriteAllTextAsync(sourcePath, "payload");

        var service = new LiveConversionWorkflowService(new AacExportProcessingService(), () => "");
        var queue = new LiveConversionQueueService();
        var item = new LiveConversionQueueItem(sourcePath, convertedPath, "AAC Stereo 128 kbps", 1, 1);
        using var semaphore = new SemaphoreSlim(1);
        var order = new List<string>();

        await service.RunAsync(
            new TrackInfo { FilePath = sourcePath, FileName = "ordered-source.mp3", SampleRateHz = 44100, ProcessingAction = "Übernehmen" },
            item,
            ExportPreset.Parse("AAC Stereo 128 kbps"),
            queue,
            semaphore,
            () => order.Add("started"),
            () => { },
            () => { },
            CancellationToken.None,
            () =>
            {
                order.Add("performance-context");
                return null;
            });

        Assert.Equal(new[] { "started", "performance-context" }, order);
        Assert.Equal(1, semaphore.CurrentCount);
    }

    [Fact]
    public async Task RunAsync_ReportsFailureAndReleasesSemaphoreWhenPreparationFails()
    {
        var sourcePath = Path.Combine(_tempRoot, "missing.mp3");
        var convertedPath = Path.Combine(_tempRoot, "converted", "missing.m4a");
        var service = new LiveConversionWorkflowService(new AacExportProcessingService(), () => "");
        var queue = new LiveConversionQueueService();
        var item = new LiveConversionQueueItem(sourcePath, convertedPath, "AAC Stereo 128 kbps", 1, 1);
        using var semaphore = new SemaphoreSlim(1);

        var completed = 0;
        var failed = 0;

        await service.RunAsync(
            new TrackInfo { FilePath = sourcePath, FileName = "missing.mp3", SampleRateHz = 44100, ProcessingAction = "Übernehmen" },
            item,
            ExportPreset.Parse("AAC Stereo 128 kbps"),
            queue,
            semaphore,
            () => { },
            () => completed++,
            () => failed++,
            CancellationToken.None);

        Assert.False(File.Exists(convertedPath));
        Assert.Equal(0, completed);
        Assert.Equal(1, failed);
        Assert.Equal(0, queue.CreateSnapshot().CompletedCount);
        Assert.Equal(1, semaphore.CurrentCount);
    }

    [Fact]
    public async Task RunAsync_WhenCanceledWhileWaitingForSemaphore_DoesNotReportFailureOrReleaseUnownedSlot()
    {
        var sourcePath = Path.Combine(_tempRoot, "source.mp3");
        var convertedPath = Path.Combine(_tempRoot, "converted", "source.m4a");
        Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
        await File.WriteAllTextAsync(sourcePath, "payload");

        var service = new LiveConversionWorkflowService(new AacExportProcessingService(), () => "");
        var queue = new LiveConversionQueueService();
        var item = new LiveConversionQueueItem(sourcePath, convertedPath, "AAC Stereo 128 kbps", 1, 1);
        using var semaphore = new SemaphoreSlim(0, 1);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var failed = 0;

        await service.RunAsync(
            new TrackInfo { FilePath = sourcePath, FileName = "source.mp3", SampleRateHz = 44100, ProcessingAction = "Übernehmen" },
            item,
            ExportPreset.Parse("AAC Stereo 128 kbps"),
            queue,
            semaphore,
            () => { },
            () => { },
            () => failed++,
            cancellation.Token);

        Assert.Equal(0, failed);
        Assert.Equal(0, semaphore.CurrentCount);
        Assert.False(File.Exists(convertedPath));
    }

    [Fact]
    public async Task WaitForTasksAsync_ReturnsImmediatelyForEmptyTaskList()
    {
        var tasks = new List<Task>();
        var syncRoot = new object();
        var snapshots = 0;

        await LiveConversionWorkflowService.WaitForTasksAsync(
            tasks,
            syncRoot,
            () => snapshots++,
            TimeSpan.FromMilliseconds(1));

        Assert.Equal(0, snapshots);
    }

    [Fact]
    public async Task WaitForTasksAsync_WaitsForTasksAddedWhileEarlierTasksAreStillRunning()
    {
        var syncRoot = new object();
        var first = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var second = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = new List<Task> { first.Task };

        var waitTask = LiveConversionWorkflowService.WaitForTasksAsync(tasks, syncRoot);

        lock (syncRoot)
        {
            tasks.Add(second.Task);
        }

        first.SetResult(true);
        await Task.Delay(10);
        Assert.False(waitTask.IsCompleted);

        second.SetResult(true);
        await waitTask;
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempRoot))
                Directory.Delete(_tempRoot, recursive: true);
        }
        catch
        {
            // Test cleanup must not hide the actual assertion failure.
        }
    }
}
