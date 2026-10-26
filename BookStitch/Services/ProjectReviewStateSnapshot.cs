using BookStitch.Models;

namespace BookStitch.Services;

public sealed record ProjectReviewStateSnapshot
{
    public ProjectReviewStateSnapshot(
        ProjectPipelineState Phase,
        bool IsWaitingForManualMergeReview,
        bool IsCompleted)
    {
        if (IsCompleted || Phase == ProjectPipelineState.Completed)
        {
            this.Phase = ProjectPipelineState.Completed;
            this.IsWaitingForManualMergeReview = false;
            this.IsCompleted = true;
            return;
        }

        if (IsWaitingForManualMergeReview)
        {
            this.Phase = ProjectPipelineState.ReviewBeforeMerge;
            this.IsWaitingForManualMergeReview = true;
            this.IsCompleted = false;
            return;
        }

        this.Phase = Phase;
        this.IsWaitingForManualMergeReview = false;
        this.IsCompleted = false;
    }

    public ProjectPipelineState Phase { get; }

    public bool IsWaitingForManualMergeReview { get; }

    public bool IsCompleted { get; }

    public bool IsReviewBeforeMerge =>
        Phase == ProjectPipelineState.ReviewBeforeMerge;

    public bool IsConsistent => true;

    public ProjectReviewStateSnapshot WithPhase(ProjectPipelineState phase) =>
        new(
            phase,
            IsWaitingForManualMergeReview &&
            phase == ProjectPipelineState.ReviewBeforeMerge,
            phase == ProjectPipelineState.Completed);

    public ProjectReviewStateSnapshot WithManualMergeReview(bool isWaiting) =>
        isWaiting
            ? new(
                ProjectPipelineState.ReviewBeforeMerge,
                IsWaitingForManualMergeReview: true,
                IsCompleted: false)
            : new(
                Phase,
                IsWaitingForManualMergeReview: false,
                IsCompleted);

    public ProjectReviewStateSnapshot WithCompletion(bool isCompleted) =>
        isCompleted
            ? new(
                ProjectPipelineState.Completed,
                IsWaitingForManualMergeReview: false,
                IsCompleted: true)
            : new(
                Phase == ProjectPipelineState.Completed
                    ? ProjectPipelineState.Preparing
                    : Phase,
                IsWaitingForManualMergeReview,
                IsCompleted: false);
}