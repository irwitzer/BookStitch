using BookStitch.Models;
using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class PipelinePauseRequestServiceTests
{
    private readonly PipelinePauseRequestService _service = new();

    [Theory]
    [InlineData(ProjectPipelineState.AcquiringSources)]
    [InlineData(ProjectPipelineState.Converting)]
    [InlineData(ProjectPipelineState.Merging)]
    public void Create_AllowsPauseForActivePipelineStates(
        ProjectPipelineState pipelineState)
    {
        var result = _service.Create(
            new PipelinePauseRequestInput(
                pipelineState,
                IsDiscImporting: true,
                IsExporting: false,
                CanCancelDiscImport: true,
                CanCancelExport: false));

        Assert.True(result.ShouldPause);
        Assert.Equal("Pause wird vorbereitet …", result.StatusText);
        Assert.Equal(
            "Laufende Vorgänge werden kontrolliert beendet …",
            result.ProgressText);
    }

    [Theory]
    [InlineData(ProjectPipelineState.Preparing)]
    [InlineData(ProjectPipelineState.ReviewBeforeMerge)]
    [InlineData(ProjectPipelineState.Completed)]
    public void Create_IgnoresStatesThatCannotBePaused(
        ProjectPipelineState pipelineState)
    {
        var result = _service.Create(
            new PipelinePauseRequestInput(
                pipelineState,
                IsDiscImporting: true,
                IsExporting: true,
                CanCancelDiscImport: true,
                CanCancelExport: true));

        Assert.Equal(PipelinePauseRequestPlan.NotApplicable, result);
    }

    [Fact]
    public void Create_CancelsOnlyActiveCancelableOperations()
    {
        var result = _service.Create(
            new PipelinePauseRequestInput(
                ProjectPipelineState.Converting,
                IsDiscImporting: true,
                IsExporting: true,
                CanCancelDiscImport: false,
                CanCancelExport: true));

        Assert.False(result.CancelDiscImport);
        Assert.True(result.CancelExport);
    }

    [Theory]
    [InlineData(ProjectPipelineState.AcquiringSources)]
    [InlineData(ProjectPipelineState.Converting)]
    public void Create_WithoutCancelableWorkCannotEnterAnUnfinishablePause(ProjectPipelineState phase)
    {
        Assert.Equal(PipelinePauseRequestPlan.NotApplicable, _service.Create(new(phase, false, false, false, false)));
        Assert.Equal(PipelinePauseRequestPlan.NotApplicable, _service.Create(new(phase, true, true, false, false)));
    }

    [Fact]
    public void Create_DoesNotCancelInactiveOperations()
    {
        var result = _service.Create(
            new PipelinePauseRequestInput(
                ProjectPipelineState.AcquiringSources,
                IsDiscImporting: false,
                IsExporting: false,
                CanCancelDiscImport: true,
                CanCancelExport: true));

        Assert.Equal(PipelinePauseRequestPlan.NotApplicable, result);
        Assert.False(result.CancelDiscImport);
        Assert.False(result.CancelExport);
    }
}