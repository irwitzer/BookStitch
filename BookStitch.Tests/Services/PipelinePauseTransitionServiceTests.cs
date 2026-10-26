using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class PipelinePauseTransitionServiceTests
{
    private readonly PipelinePauseTransitionService _service = new();

    [Fact]
    public void CreateCompletedPause_ReturnsStablePausedExecutionState()
    {
        var result = _service.CreateCompletedPause();

        AssertPlan(
            result,
            isPipelinePaused: true,
            isPipelineFailed: false,
            pauseRequested: false,
            isBusy: false,
            isExporting: false,
            isDiscImporting: false);
    }

    [Fact]
    public void CreateContinuationStart_ClearsPausedExecutionState()
    {
        var result = _service.CreateContinuationStart();

        AssertPlan(
            result,
            isPipelinePaused: false,
            isPipelineFailed: false,
            pauseRequested: false,
            isBusy: false,
            isExporting: false,
            isDiscImporting: false);
    }

    [Fact]
    public void CreateFailure_ReturnsStableFailedExecutionState()
    {
        var result = _service.CreateFailure();

        AssertPlan(
            result,
            isPipelinePaused: false,
            isPipelineFailed: true,
            pauseRequested: false,
            isBusy: false,
            isExporting: false,
            isDiscImporting: false);
    }

    [Fact]
    public void CreateStableStateActivation_ClearsPauseAndPreservesOtherExecutionState()
    {
        var result = _service.CreateStableStateActivation(
            isPipelineFailed: true,
            isBusy: true,
            isExporting: false,
            isDiscImporting: true);

        AssertPlan(
            result,
            isPipelinePaused: false,
            isPipelineFailed: true,
            pauseRequested: false,
            isBusy: true,
            isExporting: false,
            isDiscImporting: true);
    }

    [Fact]
    public void CreatePauseRequest_SetsRequestAndPreservesExecutionState()
    {
        var result = _service.CreatePauseRequest(
            isPipelinePaused: false,
            isPipelineFailed: true,
            isBusy: true,
            isExporting: true,
            isDiscImporting: false);

        AssertPlan(
            result,
            isPipelinePaused: false,
            isPipelineFailed: true,
            pauseRequested: true,
            isBusy: true,
            isExporting: true,
            isDiscImporting: false);
    }

    [Fact]
    public void CreateFailureClear_ClearsOnlyFailureState()
    {
        var result = _service.CreateFailureClear(
            isPipelinePaused: true,
            pauseRequested: true,
            isBusy: true,
            isExporting: false,
            isDiscImporting: true);

        AssertPlan(
            result,
            isPipelinePaused: true,
            isPipelineFailed: false,
            pauseRequested: true,
            isBusy: true,
            isExporting: false,
            isDiscImporting: true);
    }

    [Fact]
    public void CreateFailureMarker_SetsFailureAndClearsPauseOnly()
    {
        var result = _service.CreateFailureMarker(
            pauseRequested: true,
            isBusy: true,
            isExporting: true,
            isDiscImporting: false);

        AssertPlan(
            result,
            isPipelinePaused: false,
            isPipelineFailed: true,
            pauseRequested: true,
            isBusy: true,
            isExporting: true,
            isDiscImporting: false);
    }

    private static void AssertPlan(
        PipelinePauseTransitionPlan result,
        bool isPipelinePaused,
        bool isPipelineFailed,
        bool pauseRequested,
        bool isBusy,
        bool isExporting,
        bool isDiscImporting)
    {
        Assert.Equal(isPipelinePaused, result.IsPipelinePaused);
        Assert.Equal(isPipelineFailed, result.IsPipelineFailed);
        Assert.Equal(pauseRequested, result.PauseRequested);
        Assert.Equal(isBusy, result.IsBusy);
        Assert.Equal(isExporting, result.IsExporting);
        Assert.Equal(isDiscImporting, result.IsDiscImporting);
    }
}