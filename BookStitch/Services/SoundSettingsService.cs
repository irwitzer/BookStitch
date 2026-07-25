using BookStitch.Models;

namespace BookStitch.Services;

public static class SoundSettingsService
{
    public const SoundProfile DefaultProfile = SoundProfile.Important;
    public const int DefaultVolumePercent = 65;
    public const SoundLibrary DefaultLibrary = SoundLibrary.Gentle;

    public static SoundProfile NormalizeProfile(string? value)
    {
        return Enum.TryParse<SoundProfile>(value, ignoreCase: true, out var profile)
            ? profile
            : DefaultProfile;
    }

    public static SoundLibrary NormalizeLibrary(string? value)
    {
        return Enum.TryParse<SoundLibrary>(value, ignoreCase: true, out var library)
            ? library
            : DefaultLibrary;
    }

    public static int NormalizeVolumePercent(int value)
    {
        return Math.Clamp(value, 0, 100);
    }

    public static bool IsEnabled(SoundProfile profile, NotificationEvent notificationEvent)
    {
        return profile switch
        {
            SoundProfile.Off => false,
            SoundProfile.DiscOnly => notificationEvent is NotificationEvent.DiscChangeRequired
                or NotificationEvent.ProjectCompleted,
            SoundProfile.Important => notificationEvent is NotificationEvent.DiscChangeRequired
                or NotificationEvent.ProjectCompleted
                or NotificationEvent.UserActionRequired
                or NotificationEvent.Warning
                or NotificationEvent.Error,
            SoundProfile.All => true,
            _ => false
        };
    }
}
