using BookStitch.Models;

namespace BookStitch.Services;

public readonly record struct FocusAttentionPlan(int FlashCount, bool BringToForeground, bool UseTemporaryTopmost)
{
    public static FocusAttentionPlan None => new(0, false, false);

    public bool IsEnabled => FlashCount > 0 || BringToForeground;
}

public static class FocusSettingsService
{
    public const FocusProfile DefaultProfile = FocusProfile.Standard;

    public static FocusProfile NormalizeProfile(string? value)
    {
        return Enum.TryParse<FocusProfile>(value, ignoreCase: true, out var profile)
            ? profile
            : DefaultProfile;
    }

    public static FocusAttentionPlan GetAttentionPlan(FocusProfile profile, NotificationEvent notificationEvent)
    {
        if (profile == FocusProfile.Off)
            return FocusAttentionPlan.None;

        var flashCount = notificationEvent switch
        {
            NotificationEvent.DiscChangeRequired => 10,
            NotificationEvent.UserActionRequired => 5,
            NotificationEvent.Warning => 5,
            NotificationEvent.Error => 5,
            _ => 0
        };

        if (flashCount == 0)
            return FocusAttentionPlan.None;

        var bringToForeground = profile == FocusProfile.Foreground
            || notificationEvent == NotificationEvent.Error;

        return new FocusAttentionPlan(
            flashCount,
            bringToForeground,
            UseTemporaryTopmost: bringToForeground);
    }
}
