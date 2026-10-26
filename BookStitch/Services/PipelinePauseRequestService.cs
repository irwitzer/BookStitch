using BookStitch.Models;

namespace BookStitch.Services;

public sealed record PipelinePauseRequestInput(
    ProjectPipelineState PipelineState,
    bool IsDiscImporting,
    bool IsExporting,
    bool CanCancelDiscImport,
    bool CanCancelExport);

public sealed record PipelinePauseRequestPlan(
    bool ShouldPause,
    bool CancelDiscImport,
    bool CancelExport,
    string StatusText,
    string ProgressText)
{
    public static PipelinePauseRequestPlan NotApplicable { get; } =
        new(
            ShouldPause: false,
            CancelDiscImport: false,
            CancelExport: false,
            StatusText: string.Empty,
            ProgressText: string.Empty);
}

public sealed class PipelinePauseRequestService
{
    public PipelinePauseRequestPlan Create(PipelinePauseRequestInput input)
    {
        if (input.PipelineState is not (
            ProjectPipelineState.AcquiringSources or
            ProjectPipelineState.Converting or ProjectPipelineState.Merging) ||
            !(input.IsDiscImporting && input.CanCancelDiscImport || input.IsExporting && input.CanCancelExport))
        {
            return PipelinePauseRequestPlan.NotApplicable;
        }

        return new PipelinePauseRequestPlan(
            ShouldPause: true,
            CancelDiscImport:
                input.IsDiscImporting &&
                input.CanCancelDiscImport,
            CancelExport:
                input.IsExporting &&
                input.CanCancelExport,
            StatusText: LocalizationText.Get("Pipeline.Pause.Preparing"),
            ProgressText: LocalizationText.Get("Pipeline.Pause.Progress"));
    }
}
