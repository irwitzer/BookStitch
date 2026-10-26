using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class PauseBeforeMergeDecisionServiceTests
{
    [Theory]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    public void ShouldPauseBeforeMerge_UsesLiveSwitchOrExplicitManualCommand(
        bool manualMergeRequested, bool autoMerge, bool expectedPause)
    {
        var service = new PauseBeforeMergeDecisionService();
        Assert.Equal(expectedPause, service.ShouldPauseBeforeMerge(
            new PauseBeforeMergeDecisionInput(manualMergeRequested, autoMerge)));
    }
}
