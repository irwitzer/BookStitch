using BookStitch.Models;
using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class FocusSettingsServiceTests
{
    [Theory]
    [InlineData(null, FocusProfile.Standard)]
    [InlineData("", FocusProfile.Standard)]
    [InlineData("off", FocusProfile.Off)]
    [InlineData("Foreground", FocusProfile.Foreground)]
    [InlineData("unknown", FocusProfile.Standard)]
    public void NormalizeProfile_ReturnsExpectedProfile(string? value, FocusProfile expected)
    {
        Assert.Equal(expected, FocusSettingsService.NormalizeProfile(value));
    }

    [Fact]
    public void Off_DisablesAttentionForEveryEvent()
    {
        foreach (var notificationEvent in Enum.GetValues<NotificationEvent>())
            Assert.False(FocusSettingsService.GetAttentionPlan(FocusProfile.Off, notificationEvent).IsEnabled);
    }

    [Theory]
    [InlineData(NotificationEvent.DiscChangeRequired, 10, false)]
    [InlineData(NotificationEvent.UserActionRequired, 5, false)]
    [InlineData(NotificationEvent.Warning, 5, false)]
    [InlineData(NotificationEvent.Error, 5, true)]
    [InlineData(NotificationEvent.ProjectCompleted, 0, false)]
    [InlineData(NotificationEvent.Information, 0, false)]
    public void Standard_ReturnsExpectedAttentionPlan(
        NotificationEvent notificationEvent,
        int expectedFlashCount,
        bool expectedForeground)
    {
        var plan = FocusSettingsService.GetAttentionPlan(FocusProfile.Standard, notificationEvent);

        Assert.Equal(expectedFlashCount, plan.FlashCount);
        Assert.Equal(expectedForeground, plan.BringToForeground);
        Assert.Equal(expectedForeground, plan.UseTemporaryTopmost);
    }

    [Theory]
    [InlineData(NotificationEvent.DiscChangeRequired, 10)]
    [InlineData(NotificationEvent.UserActionRequired, 5)]
    [InlineData(NotificationEvent.Warning, 5)]
    [InlineData(NotificationEvent.Error, 5)]
    public void Foreground_BringsEveryActionEventToForeground(
        NotificationEvent notificationEvent,
        int expectedFlashCount)
    {
        var plan = FocusSettingsService.GetAttentionPlan(FocusProfile.Foreground, notificationEvent);

        Assert.Equal(expectedFlashCount, plan.FlashCount);
        Assert.True(plan.BringToForeground);
        Assert.True(plan.UseTemporaryTopmost);
    }

    [Theory]
    [InlineData(NotificationEvent.ProjectCompleted)]
    [InlineData(NotificationEvent.Information)]
    public void Foreground_IgnoresNonActionEvents(NotificationEvent notificationEvent)
    {
        Assert.False(FocusSettingsService.GetAttentionPlan(FocusProfile.Foreground, notificationEvent).IsEnabled);
    }
}
