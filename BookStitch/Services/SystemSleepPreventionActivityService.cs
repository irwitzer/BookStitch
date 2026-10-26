using BookStitch.Models;

namespace BookStitch.Services;

public sealed record SystemSleepPreventionActivityInput(
    bool KeepComputerAwakeDuringLongOperations,
    ProjectPipelineState ProjectState,
    bool IsBusy,
    bool IsExporting,
    bool IsDiscImporting);

public sealed class SystemSleepPreventionActivityService
{
    public bool ShouldKeepAwake(SystemSleepPreventionActivityInput input)
    {
        if (!input.KeepComputerAwakeDuringLongOperations)
            return false;

        if (input.IsDiscImporting)
            return true;

        if (!input.IsBusy)
            return false;

        return input.ProjectState switch
        {
            ProjectPipelineState.Preparing => true,
            ProjectPipelineState.AcquiringSources => true,
            ProjectPipelineState.Converting => true,
            ProjectPipelineState.Merging => true,
            ProjectPipelineState.ReviewBeforeMerge => false,
            ProjectPipelineState.Completed => false,
            _ => false
        };
    }
}
