namespace BookStitch.Services;

public sealed record PipelinePauseTransitionPlan(
    bool IsPipelinePaused,
    bool IsPipelineFailed,
    bool PauseRequested,
    bool IsBusy,
    bool IsExporting,
    bool IsDiscImporting);

public sealed class PipelinePauseTransitionService
{
    public PipelinePauseTransitionPlan CreateCompletedPause() =>
        CreatePlan(
            isPipelinePaused: true,
            isPipelineFailed: false,
            pauseRequested: false,
            isBusy: false,
            isExporting: false,
            isDiscImporting: false);

    public PipelinePauseTransitionPlan CreateContinuationStart() =>
        CreatePlan(
            isPipelinePaused: false,
            isPipelineFailed: false,
            pauseRequested: false,
            isBusy: false,
            isExporting: false,
            isDiscImporting: false);

    public PipelinePauseTransitionPlan CreateFailure() =>
        CreatePlan(
            isPipelinePaused: false,
            isPipelineFailed: true,
            pauseRequested: false,
            isBusy: false,
            isExporting: false,
            isDiscImporting: false);

    public PipelinePauseTransitionPlan CreateStableStateActivation(
        bool isPipelineFailed,
        bool isBusy,
        bool isExporting,
        bool isDiscImporting) =>
        CreatePlan(
            isPipelinePaused: false,
            isPipelineFailed: isPipelineFailed,
            pauseRequested: false,
            isBusy: isBusy,
            isExporting: isExporting,
            isDiscImporting: isDiscImporting);

    public PipelinePauseTransitionPlan CreatePauseRequest(
        bool isPipelinePaused,
        bool isPipelineFailed,
        bool isBusy,
        bool isExporting,
        bool isDiscImporting) =>
        CreatePlan(
            isPipelinePaused: isPipelinePaused,
            isPipelineFailed: isPipelineFailed,
            pauseRequested: true,
            isBusy: isBusy,
            isExporting: isExporting,
            isDiscImporting: isDiscImporting);

    public PipelinePauseTransitionPlan CreateFailureClear(
        bool isPipelinePaused,
        bool pauseRequested,
        bool isBusy,
        bool isExporting,
        bool isDiscImporting) =>
        CreatePlan(
            isPipelinePaused: isPipelinePaused,
            isPipelineFailed: false,
            pauseRequested: pauseRequested,
            isBusy: isBusy,
            isExporting: isExporting,
            isDiscImporting: isDiscImporting);

    public PipelinePauseTransitionPlan CreateFailureMarker(
        bool pauseRequested,
        bool isBusy,
        bool isExporting,
        bool isDiscImporting) =>
        CreatePlan(
            isPipelinePaused: false,
            isPipelineFailed: true,
            pauseRequested: pauseRequested,
            isBusy: isBusy,
            isExporting: isExporting,
            isDiscImporting: isDiscImporting);

    private static PipelinePauseTransitionPlan CreatePlan(
        bool isPipelinePaused,
        bool isPipelineFailed,
        bool pauseRequested,
        bool isBusy,
        bool isExporting,
        bool isDiscImporting) =>
        new(
            IsPipelinePaused: isPipelinePaused,
            IsPipelineFailed: isPipelineFailed,
            PauseRequested: pauseRequested,
            IsBusy: isBusy,
            IsExporting: isExporting,
            IsDiscImporting: isDiscImporting);
}