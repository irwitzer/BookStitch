using BookStitch.Models;
using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class LiveConversionWorkflowServiceTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "BookStitch.Tests", Guid.NewGuid().ToString("N"));

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
            new TrackInfo { FilePath = sourcePath, FileName = "source.mp3", ProcessingAction = "Übernehmen" },
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
            new TrackInfo { FilePath = sourcePath, FileName = "missing.mp3", ProcessingAction = "Übernehmen" },
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
            new TrackInfo { FilePath = sourcePath, FileName = "source.mp3", ProcessingAction = "Übernehmen" },
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
