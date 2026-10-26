using BookStitch.Models;

namespace BookStitch.Services;

public static class WarningSuppressionService
{
    public const string DiscCount = "disc.count";
    public const string ProjectFolder = "project.folder";

    public static bool IsSuppressed(AppSettings settings, string warningId) =>
        settings.SuppressedWarnings?.Contains(warningId, StringComparer.Ordinal) == true;

    public static void SetSuppressed(AppSettings settings, string warningId, bool suppressed)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(warningId);
        settings.SuppressedWarnings ??= [];
        settings.SuppressedWarnings.RemoveAll(id => string.Equals(id, warningId, StringComparison.Ordinal));
        if (suppressed)
            settings.SuppressedWarnings.Add(warningId);
    }

    public static void ResetAll(AppSettings settings)
    {
        settings.SuppressedWarnings = [];
        // Keep the existing metadata preference compatible with the global reset.
        settings.MetadataPreferences ??= new();
        settings.MetadataPreferences.WarnBeforeDiscardingConceptDrafts = true;
        if (settings.CoverWarningThresholdPixels == CoverWarningSettingsService.Disabled)
            settings.CoverWarningThresholdPixels = CoverWarningSettingsService.Threshold2000Pixels;
    }
}
