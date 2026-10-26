using BookStitch.Models;
using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class SystemSleepPreventionActivityServiceTests
{
    private readonly SystemSleepPreventionActivityService _service = new();

    [Theory]
    [InlineData(ProjectPipelineState.Preparing)]
    [InlineData(ProjectPipelineState.AcquiringSources)]
    [InlineData(ProjectPipelineState.Converting)]
    [InlineData(ProjectPipelineState.Merging)]
    public void ShouldKeepAwake_ForActiveProcessing_ReturnsTrue(ProjectPipelineState state)
    {
        var input = CreateInput(state, isBusy: true, isExporting: true);

        Assert.True(_service.ShouldKeepAwake(input));
    }

    [Theory]
    [InlineData(ProjectPipelineState.Preparing)]
    [InlineData(ProjectPipelineState.AcquiringSources)]
    [InlineData(ProjectPipelineState.Converting)]
    [InlineData(ProjectPipelineState.ReviewBeforeMerge)]
    [InlineData(ProjectPipelineState.Completed)]
    public void ShouldKeepAwake_ForDiscPollingOrImport_ReturnsTrue(ProjectPipelineState state)
    {
        var input = CreateInput(state, isBusy: false, isExporting: false, isDiscImporting: true);

        Assert.True(_service.ShouldKeepAwake(input));
    }

    [Fact]
    public void ShouldKeepAwake_ForReviewBeforeMergeUserDecision_ReturnsFalse()
    {
        var input = CreateInput(ProjectPipelineState.ReviewBeforeMerge, isBusy: true, isExporting: true);

        Assert.False(_service.ShouldKeepAwake(input));
    }

    [Fact]
    public void ShouldKeepAwake_ForCompletedExportDialog_ReturnsFalse()
    {
        var input = CreateInput(ProjectPipelineState.Completed, isBusy: true, isExporting: true);

        Assert.False(_service.ShouldKeepAwake(input));
    }

    [Fact]
    public void ShouldKeepAwake_WhenSettingDisabled_ReturnsFalse()
    {
        var input = CreateInput(
            ProjectPipelineState.Converting,
            keepComputerAwakeDuringLongOperations: false,
            isBusy: true,
            isExporting: true,
            isDiscImporting: true);

        Assert.False(_service.ShouldKeepAwake(input));
    }

    [Fact]
    public void ShouldKeepAwake_AfterWorkflowEnd_ReturnsFalse()
    {
        var input = CreateInput(ProjectPipelineState.Completed, isBusy: false, isExporting: false, isDiscImporting: false);

        Assert.False(_service.ShouldKeepAwake(input));
    }

    private static SystemSleepPreventionActivityInput CreateInput(
        ProjectPipelineState state,
        bool keepComputerAwakeDuringLongOperations = true,
        bool isBusy = false,
        bool isExporting = false,
        bool isDiscImporting = false) =>
        new(
            keepComputerAwakeDuringLongOperations,
            state,
            isBusy,
            isExporting,
            isDiscImporting);
}
