using BookStitch.Models;
using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public class PausedPipelineContinuationServiceTests
{
    private readonly PausedPipelineContinuationService _service = new();

    [Fact]
    public void Resolve_FreshMp3DiscAcquisition_ContinuesImportDirectly()
    {
        var result = _service.Resolve(new PausedPipelineContinuationInput(
            ProjectPipelineState.AcquiringSources,
            IsAudioDiscProjectAwaitingRip: false,
            LoadedProjectIsAudioDisc: false,
            LoadedProjectNeedsDiscImport: true,
            LoadedProjectIsMp3Disc: true));

        Assert.Equal(PausedPipelineContinuationKind.Mp3DiscImport, result);
    }

    [Fact]
    public void Resolve_LoadedMp3DiscAcquisition_ContinuesImportDirectly()
    {
        var result = _service.Resolve(new PausedPipelineContinuationInput(
            ProjectPipelineState.AcquiringSources,
            IsAudioDiscProjectAwaitingRip: false,
            LoadedProjectIsAudioDisc: false,
            LoadedProjectNeedsDiscImport: false,
            LoadedProjectIsMp3Disc: true));

        Assert.Equal(PausedPipelineContinuationKind.Mp3DiscImport, result);
    }

    [Fact]
    public void Resolve_AudioDiscAcquisition_ContinuesRipDirectly()
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
    public void Resolve_Converting_ContinuesCurrentExportPlan()
    {
        var result = _service.Resolve(new PausedPipelineContinuationInput(
            ProjectPipelineState.Converting,
            IsAudioDiscProjectAwaitingRip: false,
            LoadedProjectIsAudioDisc: false,
            LoadedProjectNeedsDiscImport: true,
            LoadedProjectIsMp3Disc: true));

        Assert.Equal(PausedPipelineContinuationKind.CurrentExportPlan, result);
    }
}
