using BookStitch.Models;
using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class PausedPipelineContinuationServiceV1AutomationTests
{
    private readonly PausedPipelineContinuationService _service = new();

    [Fact]
    public void Resolve_AcquiringSourcesForAudioDisc_ContinuesAudioDiscRip()
    {
        var result = _service.Resolve(new PausedPipelineContinuationInput(
            ProjectPipelineState.AcquiringSources,
            IsAudioDiscProjectAwaitingRip: true,
            LoadedProjectIsAudioDisc: false,
            LoadedProjectNeedsDiscImport: false,
            LoadedProjectIsMp3Disc: false));

        Assert.Equal(PausedPipelineContinuationKind.AudioDiscRip, result);
    }

    [Fact]
    public void Resolve_AcquiringSourcesForMp3Disc_ContinuesMp3DiscImport()
    {
        var result = _service.Resolve(new PausedPipelineContinuationInput(
            ProjectPipelineState.AcquiringSources,
            IsAudioDiscProjectAwaitingRip: false,
            LoadedProjectIsAudioDisc: false,
            LoadedProjectNeedsDiscImport: true,
            LoadedProjectIsMp3Disc: false));

        Assert.Equal(PausedPipelineContinuationKind.Mp3DiscImport, result);
    }

    [Fact]
    public void Resolve_NonSourceAcquisitionState_ContinuesCurrentExportPlan()
    {
        var result = _service.Resolve(new PausedPipelineContinuationInput(
            ProjectPipelineState.ReviewBeforeMerge,
            IsAudioDiscProjectAwaitingRip: true,
            LoadedProjectIsAudioDisc: true,
            LoadedProjectNeedsDiscImport: true,
            LoadedProjectIsMp3Disc: true));

        Assert.Equal(PausedPipelineContinuationKind.CurrentExportPlan, result);
    }
}
