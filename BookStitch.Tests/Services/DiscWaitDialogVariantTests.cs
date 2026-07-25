using BookStitch.Models;
using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class DiscWaitDialogVariantTests
{
    [Fact]
    public void AppSettings_DefaultsToLegacyDialog()
    {
        var settings = new AppSettings();
        Assert.False(settings.UseBoxedDiscWaitDialog);
    }

    [Fact]
    public void BothDialogImplementationsRemainConstructible()
    {
        Assert.IsAssignableFrom<IDiscWaitDialogService>(new DiscWaitDialogService());
        Assert.IsAssignableFrom<IDiscWaitDialogService>(new BoxedDiscWaitDialogService());
    }

    [Theory]
    [InlineData(DiscPollingDisplayState.Unsupported)]
    [InlineData(DiscPollingDisplayState.Duplicate)]
    public void ActionableNotice_RemainsVisibleWhileDriveReturnsToWaiting(DiscPollingDisplayState displayedState)
    {
        Assert.True(DiscPollingDisplayStateRules.ShouldKeepNoticeVisible(
            displayedState,
            DiscPollingDisplayState.Waiting));
    }

    [Theory]
    [InlineData(DiscPollingDisplayState.Unsupported, DiscPollingDisplayState.Ready)]
    [InlineData(DiscPollingDisplayState.Unsupported, DiscPollingDisplayState.Duplicate)]
    [InlineData(DiscPollingDisplayState.Duplicate, DiscPollingDisplayState.Unsupported)]
    [InlineData(DiscPollingDisplayState.Waiting, DiscPollingDisplayState.Waiting)]
    public void Notice_IsReplacedForNewRelevantState(
        DiscPollingDisplayState displayedState,
        DiscPollingDisplayState nextState)
    {
        Assert.False(DiscPollingDisplayStateRules.ShouldKeepNoticeVisible(displayedState, nextState));
    }
}
