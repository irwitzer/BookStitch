using BookStitch.Models;
using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class NotificationServiceTests
{
    [Fact]
    public void EventProfileFiltersBothSoundAndAttention()
    {
        var played = new List<NotificationEvent>();
        var attention = new List<NotificationEvent>();
        var service = CreateService(
            new AppSettings
            {
                EventProfile = EventProfile.DiscOnly.ToString(),
                SoundLibrary = SoundLibrary.Retro.ToString()
            },
            played,
            attention);

        service.Notify(NotificationEvent.Error);

        Assert.Empty(played);
        Assert.Empty(attention);
    }

    [Fact]
    public void SoundLibraryNoneDisablesOnlySound()
    {
        var played = new List<NotificationEvent>();
        var attention = new List<NotificationEvent>();
        var service = CreateService(
            new AppSettings
            {
                EventProfile = EventProfile.All.ToString(),
                SoundLibrary = SoundLibrary.None.ToString()
            },
            played,
            attention);

        service.Notify(NotificationEvent.Error);

        Assert.Empty(played);
        Assert.Equal([NotificationEvent.Error], attention);
    }

    [Fact]
    public void EnabledEventWithSoundLibraryTriggersSoundAndAttention()
    {
        var played = new List<NotificationEvent>();
        var attention = new List<NotificationEvent>();
        var service = CreateService(
            new AppSettings
            {
                EventProfile = EventProfile.Important.ToString(),
                SoundLibrary = SoundLibrary.Gentle.ToString()
            },
            played,
            attention);

        service.Notify(NotificationEvent.DiscChangeRequired);

        Assert.Equal([NotificationEvent.DiscChangeRequired], played);
        Assert.Equal([NotificationEvent.DiscChangeRequired], attention);
    }

    [Fact]
    public void DecisionServiceCanSuppressBeforeProfileAndReactions()
    {
        var played = new List<NotificationEvent>();
        var attention = new List<NotificationEvent>();
        var service = CreateService(
            new AppSettings
            {
                EventProfile = EventProfile.All.ToString(),
                SoundLibrary = SoundLibrary.Gentle.ToString()
            },
            played,
            attention);

        service.Notify(new NotificationEventContext(
            NotificationEvent.DiscChangeRequired,
            RequiresUserInteraction: false,
            CircleDriveSearchActive: true,
            CircleDriveFoundNextDisc: true));

        Assert.Empty(played);
        Assert.Empty(attention);
    }

    private static NotificationService CreateService(
        AppSettings settings,
        List<NotificationEvent> played,
        List<NotificationEvent> attention)
    {
        return new NotificationService(
            () => settings,
            played.Add,
            attention.Add);
    }
}
