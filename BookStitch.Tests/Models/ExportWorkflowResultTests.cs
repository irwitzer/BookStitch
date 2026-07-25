using BookStitch.Models;
using Xunit;

namespace BookStitch.Tests.Models;

public sealed class ExportWorkflowResultTests
{
    [Fact]
    public void Constructor_PreservesConversionResumeState()
    {
        var resumeState = new ConvertedTrackResumeState(
            ConvertedTrackPreparationPlanStatus.PartiallyReusable,
            TotalCount: 3,
            ReusableCount: 2,
            PendingCount: 1,
            ReusableDurationTicks: TimeSpan.FromMinutes(10).Ticks);

        var result = new ExportWorkflowResult(
            ExportWorkflowResultStatus.Completed,
            "output.m4b",
            "project",
            "converted",
            ConversionResumeState: resumeState);

        Assert.Same(resumeState, result.ConversionResumeState);
    }

    [Fact]
    public void Constructor_WithoutConversionResumeState_RemainsSupported()
    {
        var result = new ExportWorkflowResult(
            ExportWorkflowResultStatus.Failed,
            "output.m4b",
            "project",
            "converted",
            new InvalidOperationException("Test"));

        Assert.Null(result.ConversionResumeState);
    }
}
