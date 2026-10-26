using BookStitch.Models;

namespace BookStitch.Services;

public static class ProjectRetentionPolicyService
{
    public static string NormalizeMode(string? mode) =>
        string.Equals(mode?.Trim(), ProjectRetentionModes.Never, StringComparison.OrdinalIgnoreCase)
            ? ProjectRetentionModes.Never
            : ProjectRetentionModes.Days;

    public static bool IsNever(string? mode) =>
        string.Equals(NormalizeMode(mode), ProjectRetentionModes.Never, StringComparison.Ordinal);

    public static string FormatExpiration(DateTime expiresUtc, string? mode, DateTime? todayLocal = null)
    {
        if (IsNever(mode))
            return LocalizationText.Get("Settings.Projects.Expiration.Never");
        if (expiresUtc == default)
            return LocalizationText.Get("Settings.Projects.Expiration.Unknown");

        var localExpiration = expiresUtc.Kind == DateTimeKind.Local ? expiresUtc : expiresUtc.ToLocalTime();
        var today = (todayLocal ?? DateTime.Now).Date;
        var remainingDays = (localExpiration.Date - today).Days;
        return remainingDays switch
        {
            < 0 => LocalizationText.Get("Settings.Projects.Expiration.Expired"),
            0 => LocalizationText.Get("Settings.Projects.Expiration.Today"),
            1 => LocalizationText.Get("Settings.Projects.Expiration.Tomorrow"),
            _ => LocalizationText.Format("Settings.Projects.Expiration.InDays", remainingDays)
        };
    }
}
