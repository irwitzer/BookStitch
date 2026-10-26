using BookStitch.Models;

namespace BookStitch.Services;

public static class EventProfileSettingsService
{
    public const EventProfile DefaultProfile = EventProfile.Important;

    public static EventProfile NormalizeProfile(string? value)
    {
        return Enum.TryParse<EventProfile>(value, ignoreCase: true, out var profile)
            ? profile
            : DefaultProfile;
    }

    public static bool Includes(EventProfile profile, NotificationEvent notificationEvent)
    {
        return profile switch
        {
            EventProfile.DiscOnly => notificationEvent is NotificationEvent.DiscChangeRequired,
            EventProfile.Important => notificationEvent is
                NotificationEvent.DiscChangeRequired or
                NotificationEvent.UserActionRequired or
                NotificationEvent.ProjectCompleted or
                NotificationEvent.Warning or
                NotificationEvent.Error,
            EventProfile.All => true,
            _ => false
        };
    }

    public static EventProfile MigrateLegacySoundProfile(string? legacySoundProfile, out bool soundDisabled)
    {
        soundDisabled = false;

        if (string.IsNullOrWhiteSpace(legacySoundProfile))
            return DefaultProfile;

        if (string.Equals(legacySoundProfile, "Off", StringComparison.OrdinalIgnoreCase))
        {
            soundDisabled = true;
            return DefaultProfile;
        }

        return NormalizeProfile(legacySoundProfile);
    }
}
