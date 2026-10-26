using BookStitch.Models;
using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class EventProfileSettingsServiceTests
{
    [Theory]
    [InlineData(null, EventProfile.Important)]
    [InlineData("", EventProfile.Important)]
    [InlineData("DiscOnly", EventProfile.DiscOnly)]
    [InlineData("all", EventProfile.All)]
    [InlineData("unknown", EventProfile.Important)]
    public void NormalizeProfile_ReturnsExpectedProfile(string? value, EventProfile expected)
    {
        Assert.Equal(expected, EventProfileSettingsService.NormalizeProfile(value));
    }

    [Fact]
    public void DiscOnly_AllowsOnlyDiscChange()
    {
        Assert.True(EventProfileSettingsService.Includes(EventProfile.DiscOnly, NotificationEvent.DiscChangeRequired));
        Assert.False(EventProfileSettingsService.Includes(EventProfile.DiscOnly, NotificationEvent.Error));
        Assert.False(EventProfileSettingsService.Includes(EventProfile.DiscOnly, NotificationEvent.UserActionRequired));
        Assert.False(EventProfileSettingsService.Includes(EventProfile.DiscOnly, NotificationEvent.ProjectCompleted));
        Assert.False(EventProfileSettingsService.Includes(EventProfile.DiscOnly, NotificationEvent.Information));
    }

    [Fact]
    public void Important_AllowsImportantWorkflowEvents()
    {
        Assert.True(EventProfileSettingsService.Includes(EventProfile.Important, NotificationEvent.DiscChangeRequired));
        Assert.True(EventProfileSettingsService.Includes(EventProfile.Important, NotificationEvent.ProjectCompleted));
        Assert.True(EventProfileSettingsService.Includes(EventProfile.Important, NotificationEvent.UserActionRequired));
        Assert.True(EventProfileSettingsService.Includes(EventProfile.Important, NotificationEvent.Warning));
        Assert.True(EventProfileSettingsService.Includes(EventProfile.Important, NotificationEvent.Error));
        Assert.False(EventProfileSettingsService.Includes(EventProfile.Important, NotificationEvent.Information));
    }

    [Fact]
    public void All_AllowsEveryNotificationEvent()
    {
        foreach (var notificationEvent in Enum.GetValues<NotificationEvent>())
            Assert.True(EventProfileSettingsService.Includes(EventProfile.All, notificationEvent));
    }

    [Fact]
    public void LegacyOff_MigratesToImportantEventProfileAndDisablesSound()
    {
        var eventProfile = EventProfileSettingsService.MigrateLegacySoundProfile("Off", out var soundDisabled);

        Assert.Equal(EventProfile.Important, eventProfile);
        Assert.True(soundDisabled);
    }
}
