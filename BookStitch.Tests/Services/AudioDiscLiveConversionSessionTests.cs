using System.IO;

using BookStitch.Models;
using BookStitch.Services;
using BookStitch.Tests.TestHelpers;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class AudioDiscLiveConversionSessionTests
{
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
        using var processorStarted = new ManualResetEventSlim();
        using var releaseProcessor = new ManualResetEventSlim();
        using var session = new AudioDiscLiveConversionSession((_, _) =>
        {
            processorStarted.Set();
            releaseProcessor.Wait();
            return Task.FromResult(AudioDiscLiveConversionOutcome.Completed);
        });

        var queueTask = session.QueueAsync(CreateTrack(1, "001.flac"), CancellationToken.None);

        await queueTask.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.True(processorStarted.Wait(TimeSpan.FromSeconds(1)));
        Assert.Equal(1, session.GetSnapshot().AcceptedCount);

        releaseProcessor.Set();
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
                        CreateManifestTrack(1, "001.flac", AudioDiscTrackStatus.Ripped),
                        CreateManifestTrack(2, "002.flac", AudioDiscTrackStatus.Ripped),
                        CreateManifestTrack(3, "003.flac", AudioDiscTrackStatus.Pending)
                    ]
                }
            ]
        };

        var processed = new List<int>();
        using var session = new AudioDiscLiveConversionSession((track, _) =>
        {
            processed.Add(track.GlobalIndex);
            return Task.FromResult(AudioDiscLiveConversionOutcome.Completed);
        });

        var accepted = await session.QueueExistingRippedTracksAsync(manifest, CancellationToken.None);
        await session.WaitForCompletionAsync();

        Assert.Equal(1, accepted);
        Assert.Collection(processed, value => Assert.Equal(1, value));
        Assert.Equal(1, session.GetSnapshot().CompletedCount);
    }

    private static AudioDiscRippedTrack CreateTrack(int globalIndex, string fileName)
    {
        return new AudioDiscRippedTrack(
            DiscNumber: 1,
            GlobalIndex: globalIndex,
            TrackNumber: globalIndex,
            FilePath: Path.Combine("C:\\audio", fileName),
            Duration: TimeSpan.FromMinutes(1));
    }

    private static AudioDiscProjectManifestTrack CreateManifestTrack(
        int globalIndex,
        string fileName,
        string status)
    {
        return new AudioDiscProjectManifestTrack
        {
            GlobalIndex = globalIndex,
            DiscNumber = 1,
            TrackNumber = globalIndex,
            RelativePath = Path.Combine(ProjectFolderLayout.OriginalsFolderName, "CD 01", fileName),
            Duration = TimeSpan.FromMinutes(1),
            Status = status
        };
    }
}
