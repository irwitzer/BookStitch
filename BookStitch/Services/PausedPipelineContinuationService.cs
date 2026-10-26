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
    PipelineProjectState ProjectState);

public sealed record PausedPipelineContinuationPlan(
    PausedPipelineContinuationKind Kind,
    string ProjectManifestType);

public sealed class PausedPipelineContinuationService
{
    public PausedPipelineContinuationPlan CreatePlan(
        PausedPipelineContinuationInput input)
    {
        var kind = ResolveKind(input);
        var projectManifestType = input.ProjectState.IsMp3Disc
            ? ProjectManifestTypes.Mp3DiscProject
            : input.ProjectState.IsAudioDisc
                ? ProjectManifestTypes.AudioCdProject
                : ProjectManifestTypes.FolderProject;

        return new PausedPipelineContinuationPlan(
            kind,
            projectManifestType);
    }

    private static PausedPipelineContinuationKind ResolveKind(
        PausedPipelineContinuationInput input)
    {
        if (input.PipelineState != ProjectPipelineState.AcquiringSources)
            return PausedPipelineContinuationKind.CurrentExportPlan;

        if (input.IsAudioDiscProjectAwaitingRip ||
            input.ProjectState.IsAudioDisc)
        {
            return PausedPipelineContinuationKind.AudioDiscRip;
        }

        if (input.ProjectState.NeedsDiscImport ||
            input.ProjectState.IsMp3Disc)
        {
            return PausedPipelineContinuationKind.Mp3DiscImport;
        }

        return PausedPipelineContinuationKind.CurrentExportPlan;
    }
}
