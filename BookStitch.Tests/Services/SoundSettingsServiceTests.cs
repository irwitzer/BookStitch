using BookStitch.Models;
using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class SoundSettingsServiceTests
{
    [Theory]
    [InlineData(null, SoundProfile.Important)]
    [InlineData("", SoundProfile.Important)]
    [InlineData("DiscOnly", SoundProfile.DiscOnly)]
    [InlineData("all", SoundProfile.All)]
    [InlineData("unknown", SoundProfile.Important)]
    public void NormalizeProfile_ReturnsExpectedProfile(string? value, SoundProfile expected)
    {
        Assert.Equal(expected, SoundSettingsService.NormalizeProfile(value));
    }

    [Theory]
    [InlineData(null, SoundLibrary.Gentle)]
    [InlineData("", SoundLibrary.Gentle)]
    [InlineData("Bass", SoundLibrary.Bass)]
    [InlineData("hammondorgan", SoundLibrary.HammondOrgan)]
    [InlineData("Warm", SoundLibrary.Warm)]
    [InlineData("retro", SoundLibrary.Retro)]
    [InlineData("Digital", SoundLibrary.Gentle)]
    [InlineData("unknown", SoundLibrary.Gentle)]
    public void NormalizeLibrary_ReturnsExpectedLibrary(string? value, SoundLibrary expected)
    {
        Assert.Equal(expected, SoundSettingsService.NormalizeLibrary(value));
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, 0)]
    [InlineData(65, 65)]
    [InlineData(100, 100)]
    [InlineData(101, 100)]
    public void NormalizeVolumePercent_ClampsValue(int value, int expected)
    {
        Assert.Equal(expected, SoundSettingsService.NormalizeVolumePercent(value));
    }

    [Fact]
    public void Off_DisablesEveryEvent()
    {
        foreach (var notificationEvent in Enum.GetValues<NotificationEvent>())
            Assert.False(SoundSettingsService.IsEnabled(SoundProfile.Off, notificationEvent));
    }

    [Fact]
    public void DiscOnly_EnablesDiscChangeAndCompletion()
    {
        Assert.True(SoundSettingsService.IsEnabled(SoundProfile.DiscOnly, NotificationEvent.DiscChangeRequired));
        Assert.True(SoundSettingsService.IsEnabled(SoundProfile.DiscOnly, NotificationEvent.ProjectCompleted));
        Assert.False(SoundSettingsService.IsEnabled(SoundProfile.DiscOnly, NotificationEvent.UserActionRequired));
        Assert.False(SoundSettingsService.IsEnabled(SoundProfile.DiscOnly, NotificationEvent.Warning));
        Assert.False(SoundSettingsService.IsEnabled(SoundProfile.DiscOnly, NotificationEvent.Error));
        Assert.False(SoundSettingsService.IsEnabled(SoundProfile.DiscOnly, NotificationEvent.Information));
    }

    [Fact]
    public void Important_EnablesDiscCompletionUserActionsWarningsAndErrors()
    {
        Assert.True(SoundSettingsService.IsEnabled(SoundProfile.Important, NotificationEvent.DiscChangeRequired));
        Assert.True(SoundSettingsService.IsEnabled(SoundProfile.Important, NotificationEvent.ProjectCompleted));
        Assert.True(SoundSettingsService.IsEnabled(SoundProfile.Important, NotificationEvent.UserActionRequired));
        Assert.True(SoundSettingsService.IsEnabled(SoundProfile.Important, NotificationEvent.Warning));
        Assert.True(SoundSettingsService.IsEnabled(SoundProfile.Important, NotificationEvent.Error));
        Assert.False(SoundSettingsService.IsEnabled(SoundProfile.Important, NotificationEvent.Information));
    }

    [Fact]
    public void All_EnablesEveryEvent()
    {
        foreach (var notificationEvent in Enum.GetValues<NotificationEvent>())
            Assert.True(SoundSettingsService.IsEnabled(SoundProfile.All, notificationEvent));
    }
}
