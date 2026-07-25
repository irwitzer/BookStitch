using BookStitch.Models;
using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class ConvertedTrackPreparationProgressServiceTests
{
    [Fact]
    public void GetSnapshot_StartsWithReusableResumeProgress()
    {
        var resumeState = new ConvertedTrackResumeState(
            ConvertedTrackPreparationPlanStatus.PartiallyReusable,
            TotalCount: 4,
            ReusableCount: 2,
            PendingCount: 2,
            ReusableDurationTicks: 300);
        var service = new ConvertedTrackPreparationProgressService(resumeState, totalTicks: 1000);

        var snapshot = service.GetSnapshot();

        Assert.Equal(2, snapshot.CompletedCount);
        Assert.Equal(4, snapshot.TotalCount);
        Assert.Equal(300, snapshot.CurrentTicks);
        Assert.Equal(1000, snapshot.TotalTicks);
        Assert.Empty(snapshot.ActiveTrackIndexes);
    }

    [Fact]
    public void UpdateActiveTrack_CombinesActiveAndCompletedProgress()
    {
        var resumeState = new ConvertedTrackResumeState(
            ConvertedTrackPreparationPlanStatus.PartiallyReusable,
            TotalCount: 3,
            ReusableCount: 1,
            PendingCount: 2,
            ReusableDurationTicks: 200);
        var service = new ConvertedTrackPreparationProgressService(resumeState, totalTicks: 900);

        service.UpdateActiveTrack(trackIndex: 2, progressTicks: 120, durationTicks: 300);
        var snapshot = service.UpdateActiveTrack(trackIndex: 1, progressTicks: 80, durationTicks: 300);

        Assert.Equal(1, snapshot.CompletedCount);
        Assert.Equal(400, snapshot.CurrentTicks);
        Assert.Equal(new[] { 1, 2 }, snapshot.ActiveTrackIndexes);
    }

    [Fact]
    public void CompleteTrack_RemovesActiveProgressAndAdvancesCompletedProgress()
    {
        var resumeState = new ConvertedTrackResumeState(
            ConvertedTrackPreparationPlanStatus.RequiresPreparation,
            TotalCount: 2,
            ReusableCount: 0,
            PendingCount: 2,
            ReusableDurationTicks: 0);
        var service = new ConvertedTrackPreparationProgressService(resumeState, totalTicks: 600);

        service.UpdateActiveTrack(trackIndex: 0, progressTicks: 150, durationTicks: 300);
        var snapshot = service.CompleteTrack(trackIndex: 0, durationTicks: 300);

        Assert.Equal(1, snapshot.CompletedCount);
        Assert.Equal(300, snapshot.CurrentTicks);
        Assert.Empty(snapshot.ActiveTrackIndexes);
    }

    [Fact]
    public void Progress_IsClampedToKnownTotals()
    {
        var resumeState = new ConvertedTrackResumeState(
            ConvertedTrackPreparationPlanStatus.FullyReusable,
            TotalCount: 1,
            ReusableCount: 3,
            PendingCount: 0,
            ReusableDurationTicks: 900);
        var service = new ConvertedTrackPreparationProgressService(resumeState, totalTicks: 500);

        var snapshot = service.GetSnapshot();

        Assert.Equal(1, snapshot.CompletedCount);
        Assert.Equal(500, snapshot.CurrentTicks);
    }
    [Fact]
    public void StartTrack_ReportsAssignedJobBeforeFfmpegProgressArrives()
    {
        var resumeState = new ConvertedTrackResumeState(
            ConvertedTrackPreparationPlanStatus.RequiresPreparation,
            TotalCount: 3,
            ReusableCount: 0,
            PendingCount: 3,
            ReusableDurationTicks: 0);
        var service = new ConvertedTrackPreparationProgressService(resumeState, totalTicks: 900);

        var snapshot = service.StartTrack(trackIndex: 1);

        Assert.Equal(0, snapshot.CompletedCount);
        Assert.Equal(new[] { 1 }, snapshot.ActiveTrackIndexes);
        Assert.Equal(0, snapshot.CurrentTicks);
    }

}
