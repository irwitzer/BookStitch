using BookStitch.Models;

namespace BookStitch.Services;

public enum PausedPipelineContinuationKind
{
    CurrentExportPlan,
    Mp3DiscImport,
    AudioDiscRip
}

public sealed record PausedPipelineContinuationInput(
    ProjectPipelineState PipelineState,
    bool IsAudioDiscProjectAwaitingRip,
    bool LoadedProjectIsAudioDisc,
    bool LoadedProjectNeedsDiscImport,
    bool LoadedProjectIsMp3Disc);

public sealed class PausedPipelineContinuationService
{
    public PausedPipelineContinuationKind Resolve(PausedPipelineContinuationInput input)
    {
        if (input.PipelineState != ProjectPipelineState.AcquiringSources)
            return PausedPipelineContinuationKind.CurrentExportPlan;

        if (input.IsAudioDiscProjectAwaitingRip || input.LoadedProjectIsAudioDisc)
            return PausedPipelineContinuationKind.AudioDiscRip;

        if (input.LoadedProjectNeedsDiscImport || input.LoadedProjectIsMp3Disc)
            return PausedPipelineContinuationKind.Mp3DiscImport;

        return PausedPipelineContinuationKind.CurrentExportPlan;
    }
}
