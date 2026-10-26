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

    public static FocusAttentionPlan GetAttentionPlan(FocusProfile profile)
    {
        if (profile == FocusProfile.Off)
            return FocusAttentionPlan.None;

        var bringToForeground = profile == FocusProfile.Foreground;

        return new FocusAttentionPlan(
            FlashCount: bringToForeground ? 10 : 5,
            bringToForeground,
            UseTemporaryTopmost: bringToForeground);
    }
}
