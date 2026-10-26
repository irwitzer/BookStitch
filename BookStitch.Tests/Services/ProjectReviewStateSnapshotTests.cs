using BookStitch.Models;
using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class ProjectReviewStateSnapshotTests
{
    [Theory]
    [InlineData(ProjectPipelineState.Preparing, false, false)]
    [InlineData(ProjectPipelineState.ReviewBeforeMerge, true, false)]
    [InlineData(ProjectPipelineState.Completed, false, true)]
    public void Constructor_PreservesConsistentReviewState(
        ProjectPipelineState phase,
        bool isWaitingForManualMergeReview,
        bool isCompleted)
    {
        var snapshot = new ProjectReviewStateSnapshot(
            phase,
            isWaitingForManualMergeReview,
            isCompleted);

        Assert.Equal(phase, snapshot.Phase);
        Assert.Equal(
            isWaitingForManualMergeReview,
            snapshot.IsWaitingForManualMergeReview);
        Assert.Equal(isCompleted, snapshot.IsCompleted);
        Assert.True(snapshot.IsConsistent);
    }

    [Fact]
    public void Constructor_NormalizesManualReviewToReviewPhase()
    {
        var snapshot = new ProjectReviewStateSnapshot(
            ProjectPipelineState.Preparing,
            IsWaitingForManualMergeReview: true,
            IsCompleted: false);

        Assert.Equal(
            ProjectPipelineState.ReviewBeforeMerge,
            snapshot.Phase);
        Assert.True(snapshot.IsWaitingForManualMergeReview);
        Assert.False(snapshot.IsCompleted);
        Assert.True(snapshot.IsConsistent);
    }

    [Fact]
    public void Constructor_NormalizesCompletionToCompletedPhase()
    {
        var snapshot = new ProjectReviewStateSnapshot(
            ProjectPipelineState.ReviewBeforeMerge,
            IsWaitingForManualMergeReview: true,
            IsCompleted: true);

        Assert.Equal(ProjectPipelineState.Completed, snapshot.Phase);
        Assert.False(snapshot.IsWaitingForManualMergeReview);
        Assert.True(snapshot.IsCompleted);
        Assert.True(snapshot.IsConsistent);
    }

    [Fact]
    public void CompletedPhase_NormalizesCompletionFlag()
    {
        var snapshot = new ProjectReviewStateSnapshot(
            ProjectPipelineState.Completed,
            IsWaitingForManualMergeReview: false,
            IsCompleted: false);

        Assert.Equal(ProjectPipelineState.Completed, snapshot.Phase);
        Assert.False(snapshot.IsWaitingForManualMergeReview);
        Assert.True(snapshot.IsCompleted);
        Assert.True(snapshot.IsConsistent);
    }

    [Fact]
    public void IsReviewBeforeMerge_ReflectsNormalizedPipelinePhase()
    {
        var snapshot = new ProjectReviewStateSnapshot(
            ProjectPipelineState.Preparing,
            IsWaitingForManualMergeReview: true,
            IsCompleted: false);

        Assert.True(snapshot.IsReviewBeforeMerge);
    }

    [Fact]
    public void WithPhase_PreservesOnlyCompatibleReviewState()
    {
        var original = new ProjectReviewStateSnapshot(
            ProjectPipelineState.ReviewBeforeMerge,
            IsWaitingForManualMergeReview: true,
            IsCompleted: false);

        var preparing = original.WithPhase(ProjectPipelineState.Preparing);
        var completed = original.WithPhase(ProjectPipelineState.Completed);

        Assert.Equal(ProjectPipelineState.Preparing, preparing.Phase);
        Assert.False(preparing.IsWaitingForManualMergeReview);
        Assert.False(preparing.IsCompleted);
        Assert.True(preparing.IsConsistent);

        Assert.Equal(ProjectPipelineState.Completed, completed.Phase);
        Assert.False(completed.IsWaitingForManualMergeReview);
        Assert.True(completed.IsCompleted);
        Assert.True(completed.IsConsistent);
    }

    [Fact]
    public void WithManualMergeReview_TransitionsToConsistentReviewState()
    {
        var original = new ProjectReviewStateSnapshot(
            ProjectPipelineState.Preparing,
            IsWaitingForManualMergeReview: false,
            IsCompleted: false);

        var waiting = original.WithManualMergeReview(true);
        var noLongerWaiting = waiting.WithManualMergeReview(false);

        Assert.Equal(
            ProjectPipelineState.ReviewBeforeMerge,
            waiting.Phase);
        Assert.True(waiting.IsWaitingForManualMergeReview);
        Assert.False(waiting.IsCompleted);
        Assert.True(waiting.IsConsistent);

        Assert.Equal(
            ProjectPipelineState.ReviewBeforeMerge,
            noLongerWaiting.Phase);
        Assert.False(noLongerWaiting.IsWaitingForManualMergeReview);
        Assert.False(noLongerWaiting.IsCompleted);
        Assert.True(noLongerWaiting.IsConsistent);
    }

    [Fact]
    public void WithCompletion_TransitionsToAndFromConsistentCompletionState()
    {
        var original = new ProjectReviewStateSnapshot(
            ProjectPipelineState.ReviewBeforeMerge,
            IsWaitingForManualMergeReview: true,
            IsCompleted: false);

        var completed = original.WithCompletion(true);
        var reopened = completed.WithCompletion(false);

        Assert.Equal(ProjectPipelineState.Completed, completed.Phase);
        Assert.False(completed.IsWaitingForManualMergeReview);
        Assert.True(completed.IsCompleted);
        Assert.True(completed.IsConsistent);

        Assert.Equal(ProjectPipelineState.Preparing, reopened.Phase);
        Assert.False(reopened.IsWaitingForManualMergeReview);
        Assert.False(reopened.IsCompleted);
        Assert.True(reopened.IsConsistent);
    }
}