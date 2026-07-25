using System.IO;

using BookStitch.Models;
using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class AudioDiscRipWorkflowServiceTests
{
    [Fact]
    public async Task RunAsync_CompletesDiscAndWaitsForLiveConversions()
    {
        var manifest = CreateManifest();
        var saveCount = 0;
        var waitedForLiveConversions = false;
        var service = CreateService();

        var result = await service.RunAsync(CreateRequest(
            manifest,
            ripDiscAsync: (disc, _, _) =>
            {
                disc.Tracks[0].Status = AudioDiscTrackStatus.Ripped;
                return Task.FromResult(AudioDiscRipResult.Success(1));
            },
            saveSnapshot: () => saveCount++,
            waitForLiveConversionsAsync: () =>
            {
                waitedForLiveConversions = true;
                return Task.CompletedTask;
            }));

        Assert.Equal(AudioDiscRipWorkflowOutcome.Completed, result.Outcome);
        Assert.Equal(AudioDiscProjectStatus.RippingCompleted, manifest.Status);
        Assert.Equal(AudioDiscStatus.Completed, manifest.Discs[0].Status);
        Assert.True(waitedForLiveConversions);
        Assert.True(saveCount >= 2);
    }

    [Fact]
    public async Task RunAsync_EjectsFinalDiscWithoutRequestingDiscChange()
    {
        var manifest = CreateManifest();
        var ejectedDiscNumbers = new List<int>();
        var discChangeRequested = false;
        var service = CreateService();

        var result = await service.RunAsync(CreateRequest(
            manifest,
            tryEjectDisc: disc =>
            {
                ejectedDiscNumbers.Add(disc.DiscNumber);
                return true;
            },
            discChangeRequired: (_, _) => discChangeRequested = true));

        Assert.Equal(AudioDiscRipWorkflowOutcome.Completed, result.Outcome);
        Assert.Equal([1], ejectedDiscNumbers);
        Assert.False(discChangeRequested);
    }

    [Fact]
    public async Task RunAsync_EjectsEveryDiscButRequestsDiscChangeOnlyBetweenDiscs()
    {
        var manifest = CreateManifest(totalDiscs: 2);
        var ejectedDiscNumbers = new List<int>();
        var discChangeDiscNumbers = new List<int>();
        var service = CreateService();

        var result = await service.RunAsync(CreateRequest(
            manifest,
            tryEjectDisc: disc =>
            {
                ejectedDiscNumbers.Add(disc.DiscNumber);
                return true;
            },
            discChangeRequired: (disc, _) => discChangeDiscNumbers.Add(disc.DiscNumber)));

        Assert.Equal(AudioDiscRipWorkflowOutcome.Completed, result.Outcome);
        Assert.Equal([1, 2], ejectedDiscNumbers);
        Assert.Equal([1], discChangeDiscNumbers);
    }

    [Fact]
    public async Task RunAsync_CanceledRipPreservesResumeState()
    {
        var manifest = CreateManifest();
        string? canceledReason = null;
        var service = CreateService();

        var result = await service.RunAsync(CreateRequest(
            manifest,
            ripDiscAsync: (_, _, _) => Task.FromResult(AudioDiscRipResult.Canceled(2)),
            markCanceled: reason => canceledReason = reason));

        Assert.Equal(AudioDiscRipWorkflowOutcome.Canceled, result.Outcome);
        Assert.Equal(2, result.CompletedTracks);
        Assert.Equal(AudioDiscProjectStatus.Canceled, manifest.Status);
        Assert.Equal(AudioDiscStatus.Pending, manifest.Discs[0].Status);
        Assert.NotNull(canceledReason);
        Assert.Contains("abgebrochen", canceledReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RunAsync_DeferredDiscWaitPreservesProjectAndLiveConversionState()
    {
        var manifest = CreateManifest();
        var liveConversionCanceled = false;
        var saveCount = 0;
        var service = CreateService();

        var result = await service.RunAsync(CreateRequest(
            manifest,
            waitForRequiredDiscAsync: (_, _) => Task.FromResult<AudioDiscPollingResult?>(null),
            saveSnapshot: () => saveCount++,
            markCanceled: _ => liveConversionCanceled = true));

        Assert.Equal(AudioDiscRipWorkflowOutcome.WaitingForDisc, result.Outcome);
        Assert.NotEqual(AudioDiscProjectStatus.Canceled, manifest.Status);
        Assert.Equal(AudioDiscStatus.Pending, manifest.Discs[0].Status);
        Assert.False(liveConversionCanceled);
        Assert.Equal(0, saveCount);
    }

    [Fact]
    public async Task RunAsync_RemovedDiscWaitsAndRetriesSameDisc()
    {
        var manifest = CreateManifest();
        var ripAttempts = 0;
        var requiredDiscRemoved = 0;
        var waitCalls = 0;
        var service = CreateService();

        var result = await service.RunAsync(CreateRequest(
            manifest,
            waitForRequiredDiscAsync: (_, _) =>
            {
                waitCalls++;
                return Task.FromResult<AudioDiscPollingResult?>(CreatePollingResult());
            },
            confirmAvailableAsync: (_, _) => Task.FromResult(false),
            ripDiscAsync: (disc, _, _) =>
            {
                ripAttempts++;
                if (ripAttempts == 1)
                    return Task.FromResult(AudioDiscRipResult.Failed("read failed", 0));

                disc.Tracks[0].Status = AudioDiscTrackStatus.Ripped;
                return Task.FromResult(AudioDiscRipResult.Success(1));
            },
            requiredDiscRemoved: _ => requiredDiscRemoved++));

        Assert.Equal(AudioDiscRipWorkflowOutcome.Completed, result.Outcome);
        Assert.Equal(2, ripAttempts);
        Assert.Equal(2, waitCalls);
        Assert.Equal(1, requiredDiscRemoved);
    }

    [Fact]
    public async Task RunAsync_PersistentReadFailureMarksDiscAndLiveConversionFailed()
    {
        var manifest = CreateManifest();
        string? liveFailure = null;
        var service = CreateService();

        var result = await service.RunAsync(CreateRequest(
            manifest,
            confirmAvailableAsync: (_, _) => Task.FromResult(true),
            ripDiscAsync: (_, _, _) => Task.FromResult(AudioDiscRipResult.Failed("Disc 1, Track 1 failed", 0)),
            markFailed: reason => liveFailure = reason));

        Assert.Equal(AudioDiscRipWorkflowOutcome.Failed, result.Outcome);
        Assert.Equal(AudioDiscProjectStatus.Failed, manifest.Status);
        Assert.Equal(AudioDiscStatus.Failed, manifest.Discs[0].Status);
        Assert.Equal("Disc 1, Track 1 failed", manifest.ErrorMessage);
        Assert.Equal(manifest.ErrorMessage, liveFailure);
    }

    private static AudioDiscRipWorkflowService CreateService() =>
        new(new AudioDiscProjectService(), new AudioDiscPipelineTimingService());

    private static AudioDiscRipWorkflowRequest CreateRequest(
        AudioDiscProjectManifest manifest,
        Func<AudioDiscProjectManifestDisc, IProgress<AudioDiscRipProgress>, CancellationToken, Task<AudioDiscRipResult>>? ripDiscAsync = null,
        Func<AudioDiscProjectManifestDisc, CancellationToken, Task<AudioDiscPollingResult?>>? waitForRequiredDiscAsync = null,
        Func<AudioDiscProjectManifestDisc, CancellationToken, Task<bool>>? confirmAvailableAsync = null,
        Func<AudioDiscProjectManifestDisc, bool>? tryEjectDisc = null,
        Func<Task>? waitForLiveConversionsAsync = null,
        Action? saveSnapshot = null,
        Action<string>? markCanceled = null,
        Action<string>? markFailed = null,
        Action<AudioDiscProjectManifestDisc>? requiredDiscRemoved = null,
        Action<AudioDiscProjectManifestDisc, bool>? discChangeRequired = null)
    {
        return new AudioDiscRipWorkflowRequest(
            manifest,
            CancellationToken.None,
            (_, _) => Task.FromResult<AudioDiscProjectManifestDisc?>(null),
            waitForRequiredDiscAsync ?? ((_, _) => Task.FromResult<AudioDiscPollingResult?>(CreatePollingResult())),
            confirmAvailableAsync ?? ((_, _) => Task.FromResult(true)),
            ripDiscAsync ?? ((disc, _, _) =>
            {
                disc.Tracks[0].Status = AudioDiscTrackStatus.Ripped;
                return Task.FromResult(AudioDiscRipResult.Success(1));
            }),
            tryEjectDisc ?? (_ => true),
            () => Task.CompletedTask,
            waitForLiveConversionsAsync ?? (() => Task.CompletedTask),
            saveSnapshot ?? (() => { }),
            markCanceled ?? (_ => { }),
            markFailed ?? (_ => { }),
            (_, _) => { },
            (_, _) => { },
            requiredDiscRemoved ?? (_ => { }),
            discChangeRequired ?? ((_, _) => { }));
    }

    private static AudioDiscProjectManifest CreateManifest(int totalDiscs = 1)
    {
        return new AudioDiscProjectManifest
        {
            ProjectFolder = Path.GetTempPath(),
            TotalDiscs = totalDiscs,
            Discs = Enumerable.Range(1, totalDiscs)
                .Select(discNumber => new AudioDiscProjectManifestDisc
                {
                    DiscNumber = discNumber,
                    DiscIdentity = $"disc-{discNumber}",
                    SourceDriveRoot = "Y:\\",
                    Tracks =
                    [
                        new AudioDiscProjectManifestTrack
                        {
                            GlobalIndex = discNumber,
                            DiscNumber = discNumber,
                            TrackNumber = 1,
                            Status = AudioDiscTrackStatus.Pending
                        }
                    ]
                })
                .ToList()
        };
    }

    private static AudioDiscPollingResult CreatePollingResult()
    {
        var disc = new AudioDiscInfo(
            "Y:\\",
            "Y:",
            [],
            TimeSpan.Zero,
            "disc-1");
        return new AudioDiscPollingResult(
            new DiscPollingResult(true, "", "", ""),
            disc,
            null);
    }
}
