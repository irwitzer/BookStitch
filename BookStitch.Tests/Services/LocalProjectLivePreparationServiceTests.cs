using BookStitch.Services;
using System.Collections.Concurrent;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class LocalProjectLivePreparationServiceTests
{
    [Fact]
    public async Task RunAsync_StartsPreparationWhileImportIsStillRunning()
    {
        var importService = new ControlledImportService();
        var service = new LocalProjectLivePreparationService(importService);
        var firstPreparationStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowFirstPreparationToFinish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var runTask = service.RunAsync(
            new LocalProjectLivePreparationRequest(
                "source",
                ["source\\01.mp3", "source\\02.mp3"],
                "project",
                ParallelJobs: 2),
            async (copiedFile, token) =>
            {
                if (copiedFile.CompletedFiles == 1)
                {
                    firstPreparationStarted.TrySetResult();
                    await allowFirstPreparationToFinish.Task.WaitAsync(token);
                }
            },
            progress: null,
            CancellationToken.None);

        await firstPreparationStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.False(importService.ImportCompleted);

        allowFirstPreparationToFinish.TrySetResult();
        var result = await runTask;

        Assert.Equal(2, result.PreparedFiles);
        Assert.True(importService.ImportCompleted);
    }

    [Fact]
    public async Task RunAsync_RespectsParallelJobLimit()
    {
        var importService = new ImmediateImportService(fileCount: 5);
        var service = new LocalProjectLivePreparationService(importService);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var active = 0;
        var maximumActive = 0;
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var runTask = service.RunAsync(
            new LocalProjectLivePreparationRequest(
                "source",
                Enumerable.Range(1, 5).Select(index => $"source\\{index:00}.mp3").ToArray(),
                "project",
                ParallelJobs: 2),
            async (_, token) =>
            {
                var current = Interlocked.Increment(ref active);
                UpdateMaximum(ref maximumActive, current);
                if (maximumActive >= 2)
                    started.TrySetResult();

                await release.Task.WaitAsync(token);
                Interlocked.Decrement(ref active);
            },
            progress: null,
            CancellationToken.None);

        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(2, Volatile.Read(ref maximumActive));

        release.TrySetResult();
        var result = await runTask;

        Assert.Equal(5, result.PreparedFiles);
        Assert.Equal(2, maximumActive);
    }

    [Fact]
    public async Task RunAsync_ReportsCombinedCopyAndPreparationProgress()
    {
        var importService = new ImmediateImportService(fileCount: 2);
        var service = new LocalProjectLivePreparationService(importService);
        var snapshots = new ConcurrentQueue<LocalProjectLivePreparationProgress>();

        var result = await service.RunAsync(
            new LocalProjectLivePreparationRequest(
                "source",
                ["source\\01.mp3", "source\\02.mp3"],
                "project",
                ParallelJobs: 1),
            (_, _) => Task.CompletedTask,
            new CollectingProgress<LocalProjectLivePreparationProgress>(snapshots.Enqueue),
            CancellationToken.None);

        Assert.Equal(2, result.PreparedFiles);
        Assert.Contains(snapshots, snapshot => snapshot.CopiedFiles == 1 && snapshot.PreparedFiles == 0);
        Assert.Contains(snapshots, snapshot => snapshot.CopiedFiles == 2 && snapshot.PreparedFiles == 2);
    }


    [Fact]
    public async Task RunAsync_WhenCanceledWhileWaitingForWorker_CompletesWithoutUnhandledException()
    {
        var importService = new ImmediateImportService(fileCount: 2);
        var service = new LocalProjectLivePreparationService(importService);
        using var cancellation = new CancellationTokenSource();
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var runTask = service.RunAsync(
            new LocalProjectLivePreparationRequest(
                "source",
                ["source\\01.mp3", "source\\02.mp3"],
                "project",
                ParallelJobs: 1),
            async (copiedFile, token) =>
            {
                if (copiedFile.CompletedFiles == 1)
                {
                    firstStarted.TrySetResult();
                    await Task.Delay(Timeout.InfiniteTimeSpan, token);
                }
            },
            progress: null,
            cancellation.Token);

        await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        cancellation.Cancel();

        var result = await runTask.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(0, result.PreparedFiles);
        Assert.True(result.WasCanceled);
    }

    [Fact]
    public async Task RunAsync_ReportsActivePreparationFiles()
    {
        var importService = new ImmediateImportService(fileCount: 2);
        var service = new LocalProjectLivePreparationService(importService);
        var snapshots = new ConcurrentQueue<LocalProjectLivePreparationProgress>();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var bothStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var active = 0;

        var runTask = service.RunAsync(
            new LocalProjectLivePreparationRequest(
                "source",
                ["source\\01.mp3", "source\\02.mp3"],
                "project",
                ParallelJobs: 2),
            async (_, token) =>
            {
                if (Interlocked.Increment(ref active) == 2)
                    bothStarted.TrySetResult();

                await release.Task.WaitAsync(token);
            },
            new CollectingProgress<LocalProjectLivePreparationProgress>(snapshots.Enqueue),
            CancellationToken.None);

        await bothStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Contains(snapshots, snapshot =>
            snapshot.ActiveFileNames.Contains("01.mp3") &&
            snapshot.ActiveFileNames.Contains("02.mp3"));

        release.TrySetResult();
        await runTask;
    }

    private static void UpdateMaximum(ref int target, int value)
    {
        int current;
        do
        {
            current = Volatile.Read(ref target);
            if (value <= current)
                return;
        }
        while (Interlocked.CompareExchange(ref target, value, current) != current);
    }

    private sealed class ControlledImportService : ILocalProjectImportService
    {
        public bool ImportCompleted { get; private set; }

        public async Task<LocalProjectImportResult> CopySourcesAsync(
            string sourceFolder,
            IReadOnlyCollection<string> sourceFiles,
            string projectFolder,
            IProgress<LocalProjectCopyProgress>? progress,
            IProgress<LocalProjectCopiedFile>? copiedFileProgress,
            CancellationToken cancellationToken)
        {
            var files = sourceFiles.ToArray();
            copiedFileProgress?.Report(new LocalProjectCopiedFile(1, files.Length, files[0], "project\\originals\\01.mp3", false));
            await Task.Delay(100, cancellationToken);
            copiedFileProgress?.Report(new LocalProjectCopiedFile(2, files.Length, files[1], "project\\originals\\02.mp3", false));
            ImportCompleted = true;

            return new LocalProjectImportResult(sourceFolder, projectFolder, "project\\originals", files.Length, files.Length);
        }
    }

    private sealed class ImmediateImportService(int fileCount) : ILocalProjectImportService
    {
        public Task<LocalProjectImportResult> CopySourcesAsync(
            string sourceFolder,
            IReadOnlyCollection<string> sourceFiles,
            string projectFolder,
            IProgress<LocalProjectCopyProgress>? progress,
            IProgress<LocalProjectCopiedFile>? copiedFileProgress,
            CancellationToken cancellationToken)
        {
            var files = sourceFiles.Take(fileCount).ToArray();
            for (var index = 0; index < files.Length; index++)
            {
                copiedFileProgress?.Report(new LocalProjectCopiedFile(
                    index + 1,
                    files.Length,
                    files[index],
                    $"project\\originals\\{index + 1:00}.mp3",
                    false));
            }

            return Task.FromResult(new LocalProjectImportResult(
                sourceFolder,
                projectFolder,
                "project\\originals",
                files.Length,
                files.Length));
        }
    }

    private sealed class CollectingProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
    }
}
