using BookStitch.Models;
using System.Globalization;

namespace BookStitch.Services;

public static class AacDecisionPresentationService
{
    public static string Profile(AacDecisionPlan plan)
    {
        var culture = CultureInfo.CurrentUICulture;
        var bitrate = plan.IsOriginal ? (plan.OriginalBitrateBitsPerSecond!.Value / 1000m).ToString("0.0", culture)
            : plan.BitrateKbps.ToString(culture);
        return LocalizationText.Format(plan.IsOriginal ? "Aac.Compact.OriginalProfile" : "Aac.Compact.Profile",
            plan.Channels == 1 ? "Mono" : "Stereo", bitrate, plan.SampleRateHz.ToString("N0", culture));
    }

    public static string? ReuseSummary(AacDecisionPlan plan) => Summary(plan.ReusedCount, plan.ReusedDurationTicks, "Reuse");
    public static string? ConvertSummary(AacDecisionPlan plan) => Summary(plan.ConvertedCount, plan.ConvertedDurationTicks, "Convert");

    private static string? Summary(int count, long ticks, string action)
    {
        if (count == 0) return null;
        var duration = ticks < TimeSpan.TicksPerMinute ? LocalizationText.Get("Aac.Compact.LessMinute")
            : LocalizationText.Format("Aac.Compact.Minutes", (ticks / (decimal)TimeSpan.TicksPerMinute)
                .ToString("#,0.#", CultureInfo.CurrentUICulture));
        return count == 1 ? LocalizationText.Format($"Aac.Compact.{action}One", duration)
            : LocalizationText.Format($"Aac.Compact.{action}Many", count, duration);
    }
}
