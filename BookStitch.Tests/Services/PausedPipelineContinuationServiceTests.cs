using BookStitch.Models;
using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public class PausedPipelineContinuationServiceTests
{
    private readonly PausedPipelineContinuationService _service = new();

    [Theory]
    [InlineData(true, false, false, false, PausedPipelineContinuationKind.AudioDiscRip, ProjectManifestTypes.FolderProject)]
    [InlineData(false, false, false, true, PausedPipelineContinuationKind.AudioDiscRip, ProjectManifestTypes.AudioCdProject)]
    [InlineData(false, true, false, false, PausedPipelineContinuationKind.Mp3DiscImport, ProjectManifestTypes.FolderProject)]
    [InlineData(false, false, true, false, PausedPipelineContinuationKind.Mp3DiscImport, ProjectManifestTypes.Mp3DiscProject)]
    public void CreatePlan_AcquiringSources_uses_each_continuation_trigger_independently(
        bool isAudioDiscProjectAwaitingRip,
        bool needsDiscImport,
        bool isMp3Disc,
        bool isAudioDisc,
        PausedPipelineContinuationKind expectedKind,
        string expectedManifestType)
    {
        var result = _service.CreatePlan(
            CreateInput(
                ProjectPipelineState.AcquiringSources,
                isAudioDiscProjectAwaitingRip,
                needsDiscImport,
                isMp3Disc,
                isAudioDisc));

        Assert.Equal(expectedKind, result.Kind);
        Assert.Equal(expectedManifestType, result.ProjectManifestType);
    }

    [Fact]
    public void CreatePlan_Converting_ContinuesCurrentExportPlan()
    {
        var result = _service.CreatePlan(
            CreateInput(
                ProjectPipelineState.Converting,
                needsDiscImport: true,
                isMp3Disc: true));

        Assert.Equal(PausedPipelineContinuationKind.CurrentExportPlan, result.Kind);
        Assert.Equal(ProjectManifestTypes.Mp3DiscProject, result.ProjectManifestType);
    }

    [Fact]
    public void CreatePlan_LocalProject_UsesFolderManifestType()
    {
        var result = _service.CreatePlan(
            CreateInput(
                ProjectPipelineState.ReviewBeforeMerge,
                isLocal: true));

        Assert.Equal(PausedPipelineContinuationKind.CurrentExportPlan, result.Kind);
        Assert.Equal(ProjectManifestTypes.FolderProject, result.ProjectManifestType);
    }

    private static PausedPipelineContinuationInput CreateInput(
        ProjectPipelineState pipelineState,
        bool isAudioDiscProjectAwaitingRip = false,
        bool needsDiscImport = false,
        bool isMp3Disc = false,
        bool isAudioDisc = false,
        bool isLocal = false) =>
        new(
            pipelineState,
            isAudioDiscProjectAwaitingRip,
            new PipelineProjectState(
                needsDiscImport,
                isMp3Disc,
                isAudioDisc,
                isLocal));
}
