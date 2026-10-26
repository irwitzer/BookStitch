using System.IO;

using BookStitch.Models;
using BookStitch.Services;
using BookStitch.Tests.TestHelpers;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class AudioDiscLiveConversionSessionTests
{
    [Theory]
    [InlineData(true, PerformanceOutcome.Paused)]
    [InlineData(false, PerformanceOutcome.Cancelled)]
    public async Task Live_conversion_diagnostics_distinguish_pause_from_cancel_without_changing_cancellation(bool pause, PerformanceOutcome expected)
    {
        using var folder = new TemporaryFolder();
        using var cancellation = new CancellationTokenSource();
        var trace = new PerformanceTraceService(true);
        trace.TryStartSession(new());
        var audio = new RecordingAudioExportProcessingService { Prepare = token => Task.FromCanceled(token) };
        var manifest = new AudioDiscProjectManifest { ProjectFolder = folder.Path, TotalDiscs = 1 };
        using var session = new AudioDiscLiveConversionSession(new AudioDiscLiveConversionService(),
            new LiveConversionWorkflowService(audio, () => "ffmpeg"), new WorkManifestService(),
            manifest, ExportPreset.Parse("AAC Mono 64 kbps"), 4,
            startPerformanceOperation: _ =>
            {
                var operation = trace.StartOperation("disc.audio.convert");
                cancellation.Cancel();
                return operation;
            }, isPauseRequested: () => pause);
        var track = CreateTrack(1, "001.flac") with { FilePath = Path.Combine(folder.Path, "001.flac") };
        File.WriteAllText(track.FilePath, "source");
        await session.QueueAsync(track, cancellation.Token);
        await session.WaitForCompletionAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, session.GetSnapshot().CanceledCount);
        Assert.Equal(0, session.GetSnapshot().FailedCount);
        var report = trace.CompleteSession(PerformanceOutcome.Success)!;
        Assert.Equal(expected, Assert.Single(report.Operations).Outcome);
        Assert.Equal(0, trace.ActiveScopeCount);
    }

    [Fact]
    public async Task Next_disc_can_queue_while_previous_timing_runs_and_pause_waits_for_child_exit()
    {
        using var folder = new TemporaryFolder();
        using var cancellation = new CancellationTokenSource();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var exit = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var audio = new RecordingAudioExportProcessingService
        {
            AnalyzeTiming = async (_, _) =>
            {
                started.TrySetResult();
                await exit.Task; // Model a child finishing its controlled cancellation cleanup.
                throw new OperationCanceledException(cancellation.Token);
            }
        };
        var manifest = new AudioDiscProjectManifest { ProjectFolder = folder.Path, TotalDiscs = 2 };
        using var session = new AudioDiscLiveConversionSession(new AudioDiscLiveConversionService(),
            new LiveConversionWorkflowService(audio, () => "ffmpeg"), new WorkManifestService(),
            manifest, ExportPreset.Parse("AAC Mono 64 kbps"), 4, ffprobePath: "probe");
        var first = CreateTrack(1, "001.flac") with { FilePath = Path.Combine(folder.Path, "001.flac") };
        var second = CreateTrack(2, "002.flac") with { DiscNumber = 2, FilePath = Path.Combine(folder.Path, "002.flac") };
        File.WriteAllText(first.FilePath, "source one");
        File.WriteAllText(second.FilePath, "source two");
        try
        {
            await session.QueueAsync(first, cancellation.Token);
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await session.QueueAsync(second, cancellation.Token).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(2, session.GetSnapshot().AcceptedCount);
            cancellation.Cancel();
            var drain = session.WaitForCompletionAsync();
            Assert.False(drain.IsCompleted);
            exit.TrySetResult();
            await drain.WaitAsync(TimeSpan.FromSeconds(5));
            var stored = new WorkManifestService().LoadOrCreate(ProjectFolderLayout.GetExportManifestPath(folder.Path),
                ProjectManifestTypes.AudioCdProject, folder.Path, folder.Path, "AAC Mono 64 kbps");
            Assert.Contains(stored.Tracks, entry => entry.Status == ProjectManifestTrackStatuses.Converted &&
                entry.PreparationId != Guid.Empty && entry.AacTiming is null);
        }
        finally { cancellation.Cancel(); exit.TrySetResult(); await session.WaitForCompletionAsync(); }
    }

    [Fact]
    public async Task QueueAsync_TracksCompletionFailureCancellationAndDuplicates()
    {
        using var session = new AudioDiscLiveConversionSession((track, _) =>
            Task.FromResult(track.GlobalIndex switch
            {
                1 => AudioDiscLiveConversionOutcome.Completed,
                2 => AudioDiscLiveConversionOutcome.Failed,
                3 => AudioDiscLiveConversionOutcome.Canceled,
                _ => AudioDiscLiveConversionOutcome.Reused
            }));

        await session.QueueAsync(CreateTrack(1, "001.flac"), CancellationToken.None);
        await session.QueueAsync(CreateTrack(2, "002.flac"), CancellationToken.None);
        await session.QueueAsync(CreateTrack(3, "003.flac"), CancellationToken.None);
        await session.QueueAsync(CreateTrack(4, "004.flac"), CancellationToken.None);
        await session.QueueAsync(CreateTrack(1, "001.flac"), CancellationToken.None);

        await session.WaitForCompletionAsync();

        var snapshot = session.GetSnapshot();
        Assert.Equal(4, snapshot.AcceptedCount);
        Assert.Equal(1, snapshot.CompletedCount);
        Assert.Equal(1, snapshot.ReusedCount);
        Assert.Equal(1, snapshot.FailedCount);
        Assert.Equal(1, snapshot.CanceledCount);
        Assert.Equal(1, snapshot.DuplicateCount);
        Assert.Equal(4, snapshot.FinishedCount);
        Assert.Equal(0, snapshot.PendingCount);
    }

    [Fact]
    public async Task ConvertedCount_StartsWithExistingManifestProgressAndAddsNewCompletions()
    {
        using var session = new AudioDiscLiveConversionSession(
            (_, _) => Task.FromResult(AudioDiscLiveConversionOutcome.Completed),
            existingConvertedCount: 16);

        Assert.Equal(16, session.GetSnapshot().ConvertedCount);

        await session.QueueAsync(CreateTrack(17, "017.flac"), CancellationToken.None);
        await session.WaitForCompletionAsync();

        Assert.Equal(17, session.GetSnapshot().ConvertedCount);
    }

    [Fact]
    public async Task ConvertedCount_DoesNotDoubleCountManifestBackedReuse()
    {
        using var session = new AudioDiscLiveConversionSession(
            (_, _) => Task.FromResult(AudioDiscLiveConversionOutcome.Reused),
            existingConvertedCount: 16);

        await session.QueueAsync(CreateTrack(1, "001.flac"), CancellationToken.None);
        await session.WaitForCompletionAsync();

        var snapshot = session.GetSnapshot();
        Assert.Equal(1, snapshot.ReusedCount);
        Assert.Equal(16, snapshot.ConvertedCount);
    }

    [Fact]
    public async Task QueueAsync_TreatsSameGlobalTrackAsDuplicateEvenWhenPathRepresentationDiffers()
    {
        using var session = new AudioDiscLiveConversionSession((_, _) =>
            Task.FromResult(AudioDiscLiveConversionOutcome.Completed));

        await session.QueueAsync(CreateTrack(1, "001.flac"), CancellationToken.None);
        await session.QueueAsync(
            CreateTrack(1, Path.Combine("different", "001.flac")),
            CancellationToken.None);
        await session.WaitForCompletionAsync();

        var snapshot = session.GetSnapshot();
        Assert.Equal(1, snapshot.AcceptedCount);
        Assert.Equal(1, snapshot.CompletedCount);
        Assert.Equal(1, snapshot.DuplicateCount);
    }

    [Fact]
    public async Task WaitForCompletionAsync_WaitsForQueuedWork()
    {
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var session = new AudioDiscLiveConversionSession(async (_, token) =>
        {
            await release.Task.WaitAsync(token);
            return AudioDiscLiveConversionOutcome.Completed;
        });

        await session.QueueAsync(CreateTrack(1, "001.flac"), CancellationToken.None);
        var waitTask = session.WaitForCompletionAsync();

        Assert.False(waitTask.IsCompleted);
        Assert.Equal(1, session.GetSnapshot().PendingCount);

        release.SetResult(true);
        await waitTask;

        Assert.Equal(1, session.GetSnapshot().CompletedCount);
        Assert.Equal(0, session.GetSnapshot().PendingCount);
    }

    [Fact]
    public async Task QueueAsync_ConvertsThrownCancellationIntoCanceledOutcome()
    {
        using var session = new AudioDiscLiveConversionSession((_, _) =>
            throw new OperationCanceledException());

        await session.QueueAsync(CreateTrack(1, "001.flac"), CancellationToken.None);
        await session.WaitForCompletionAsync();

        var snapshot = session.GetSnapshot();
        Assert.Equal(1, snapshot.CanceledCount);
        Assert.Equal(0, snapshot.FailedCount);
    }


    [Fact]
    public async Task QueueAsync_ReturnsBeforeProcessorCompletesSynchronousStartup()
    {
        var processorStarted = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var releaseProcessor = new ManualResetEventSlim();
        using var session = new AudioDiscLiveConversionSession((_, _) =>
        {
            processorStarted.TrySetResult(true);
            releaseProcessor.Wait();
            return Task.FromResult(AudioDiscLiveConversionOutcome.Completed);
        });

        var queueCall = Task.Run(() =>
            session.QueueAsync(CreateTrack(1, "001.flac"), CancellationToken.None));

        try
        {
            await processorStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await queueCall.WaitAsync(TimeSpan.FromSeconds(1));
            Assert.Equal(1, session.GetSnapshot().AcceptedCount);
        }
        finally
        {
            releaseProcessor.Set();
        }

        await queueCall;
        await session.WaitForCompletionAsync();

        Assert.Equal(1, session.GetSnapshot().CompletedCount);
    }

    [Fact]
    public async Task StatusCallback_CanReadSnapshotWithoutBlockingSession()
    {
        AudioDiscLiveConversionSession? session = null;
        var callbackCount = 0;
        session = new AudioDiscLiveConversionSession(
            (_, _) => Task.FromResult(AudioDiscLiveConversionOutcome.Completed),
            _ =>
            {
                _ = session!.GetSnapshot();
                Interlocked.Increment(ref callbackCount);
            });

        using (session)
        {
            await session.QueueAsync(CreateTrack(1, "001.flac"), CancellationToken.None);
            await session.WaitForCompletionAsync();
        }

        Assert.True(callbackCount >= 2);
    }

    [Fact]
    public async Task QueueExistingRippedTracksAsync_QueuesOnlyCompleteRippedFiles()
    {
        using var folder = new TemporaryFolder();
        var rippedFolder = ProjectFolderLayout.GetDiscOriginalsFolder(folder.Path, 1);
        Directory.CreateDirectory(rippedFolder);
        var sourceId = Guid.NewGuid();

        var firstPath = Path.Combine(rippedFolder, "001.flac");
        var pendingPath = Path.Combine(rippedFolder, "003.flac");
        File.WriteAllBytes(firstPath, [1, 2, 3]);
        File.WriteAllBytes(pendingPath, [4, 5, 6]);

        var manifest = new AudioDiscProjectManifest
        {
            ProjectFolder = folder.Path,
            Discs =
            [
                new AudioDiscProjectManifestDisc
                {
                    DiscNumber = 1,
                    Tracks =
                    [
                        CreateManifestTrack(1, "001.flac", AudioDiscTrackStatus.Ripped, sourceId),
                        CreateManifestTrack(2, "002.flac", AudioDiscTrackStatus.Ripped),
                        CreateManifestTrack(3, "003.flac", AudioDiscTrackStatus.Pending)
                    ]
                }
            ]
        };

        var processed = new List<int>();
        var sourceIds = new List<Guid>();
        using var session = new AudioDiscLiveConversionSession((track, _) =>
        {
            processed.Add(track.GlobalIndex);
            sourceIds.Add(track.SourceId);
            return Task.FromResult(AudioDiscLiveConversionOutcome.Completed);
        });

        var accepted = await session.QueueExistingRippedTracksAsync(manifest, CancellationToken.None);
        await session.WaitForCompletionAsync();

        Assert.Equal(1, accepted);
        Assert.Collection(processed, value => Assert.Equal(1, value));
        Assert.Collection(sourceIds, value => Assert.Equal(sourceId, value));
        Assert.Equal(1, session.GetSnapshot().CompletedCount);
    }

    private static AudioDiscRippedTrack CreateTrack(int globalIndex, string fileName)
    {
        return new AudioDiscRippedTrack(
            DiscNumber: 1,
            GlobalIndex: globalIndex,
            TrackNumber: globalIndex,
            SourceId: Guid.NewGuid(),
            FilePath: Path.Combine("C:\\audio", fileName),
            Duration: TimeSpan.FromMinutes(1));
    }

    private static AudioDiscProjectManifestTrack CreateManifestTrack(
        int globalIndex,
        string fileName,
        string status,
        Guid sourceId = default)
    {
        return new AudioDiscProjectManifestTrack
        {
            GlobalIndex = globalIndex,
            SourceId = sourceId,
            DiscNumber = 1,
            TrackNumber = globalIndex,
            RelativePath = Path.Combine(ProjectFolderLayout.OriginalsFolderName, "CD 01", fileName),
            Duration = TimeSpan.FromMinutes(1),
            Status = status
        };
    }
}
