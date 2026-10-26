using BookStitch.Models;

namespace BookStitch.Services;

public static class CoverWarningSettingsService
{
    public const int Disabled = 0;
    public const int DefaultThresholdPixels = 1000;
    public const int Threshold1000Pixels = 1000;
    public const int Threshold2000Pixels = 2000;

    public static int ResolveWarningChoice(int currentThresholdPixels, bool alwaysWarn)
    {
        if (!alwaysWarn)
            return Disabled;
        var threshold = NormalizeThresholdPixels(currentThresholdPixels);
        return threshold == Disabled ? Threshold2000Pixels : threshold;
    }

    public static int NormalizeThresholdPixels(int value) =>
        value is Disabled or 500 or Threshold1000Pixels or Threshold2000Pixels
            ? value
            : DefaultThresholdPixels;

    public static void Normalize(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.CoverWarningThresholdPixels = NormalizeThresholdPixels(settings.CoverWarningThresholdPixels);
    }
}
