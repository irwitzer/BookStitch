using BookStitch.Models;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace BookStitch.Services;

/// <summary>Pure, deterministic planning. Calibration evidence is supplied by the preflight owner.</summary>
public static class AacDecisionPlanner
{
    public static bool IsAllowedRate(int? rate) => rate is 12000 or 16000 or 22050 or 24000 or
        32000 or 44100 or 48000 or 64000 or 88200 or 96000;

    public static int? Classify(decimal? bitsPerSecond, int channels) => bitsPerSecond is > 0
        ? (channels == 1 ? new[] { 64, 96 } : new[] { 96, 128, 160, 192, 256 })
            .Where(b => Math.Abs(bitsPerSecond.Value - b * 1000m) <= 10000m).Cast<int?>().FirstOrDefault()
        : null;

    private static bool Eligible(TrackInfo t, int channels) => t.AudioValidationPassed == true &&
        t.AacOptimizationEligible && t.Channels == channels && t.AudioBitrateBitsPerSecond is > 0 &&
        !string.IsNullOrWhiteSpace(t.AacConfigurationId);

    public static AacDecisionPlan Build(IReadOnlyList<TrackInfo> tracks, ExportPreset user, int standardRate,
        IReadOnlyDictionary<string, string?> calibratedConfigurations, bool useUserValues = false,
        int? fixedRate = null)
    {
        var primary = BuildForChannels(tracks, user, standardRate, calibratedConfigurations, user.Channels, useUserValues, fixedRate);
        if (useUserValues || fixedRate.HasValue) return primary;
        var alternative = BuildForChannels(tracks, user, standardRate, calibratedConfigurations,
            user.Channels == 1 ? 2 : 1, false, null);
        return Rank(new[] { primary, alternative }, user, standardRate).First();
    }

    private static AacDecisionPlan BuildForChannels(IReadOnlyList<TrackInfo> tracks, ExportPreset user, int standardRate,
        IReadOnlyDictionary<string, string?> calibratedConfigurations, int channels, bool useUserValues, int? fixedRate)
    {
        var included = tracks.Select((track, index) => (track, index)).Where(x => !x.track.IsExcluded).ToArray();
        if (included.Length == 0 || included.Any(x => x.track.AudioValidationPassed != true || x.track.DurationTicks is not > 0 || x.track.SampleRateHz is not > 0))
            throw new InvalidDataException(LocalizationText.Get("Aac.Plan.SourceError"));
        var candidates = included.Where(x => Eligible(x.track, channels) && IsAllowedRate(x.track.SampleRateHz)).ToArray();
        var plans = new List<AacDecisionPlan>();
        if (!useUserValues)
        {
            foreach (var group in candidates.Where(x => Classify(x.track.AudioBitrateBitsPerSecond, channels).HasValue)
                .GroupBy(x => (Rate: x.track.SampleRateHz!.Value, Bitrate: Classify(x.track.AudioBitrateBitsPerSecond, channels)!.Value,
                    Config: x.track.AacConfigurationId!)))
            {
                var preset = ExportPreset.Parse($"AAC {(channels == 1 ? "Mono" : "Stereo")} {group.Key.Bitrate} kbps");
                var allCopy = group.Count() == included.Length;
                if (allCopy || (calibratedConfigurations.TryGetValue(CalibrationKey(group.Key.Rate, preset), out var config) && config == group.Key.Config))
                    plans.Add(Create(tracks, user, standardRate, preset, group.Key.Rate, group.Key.Config, false));
            }
            if (candidates.Length == included.Length && candidates.All(x => Classify(x.track.AudioBitrateBitsPerSecond, channels) is null) &&
                candidates.Select(x => x.track.SampleRateHz).Distinct().Count() == 1 &&
                candidates.Select(x => x.track.AacConfigurationId).Distinct(StringComparer.Ordinal).Count() == 1)
            {
                var values = candidates.Select(x => x.track.AudioBitrateBitsPerSecond!.Value).Order().ToArray();
                var median = (values[(values.Length - 1) / 2] + values[values.Length / 2]) / 2m;
                if (values.All(v => Math.Abs(v - median) <= 2000m))
                {
                    var original = ExportPreset.RegisterOriginal(LocalizationText.Format("Aac.Plan.OriginalPreset",
                        channels == 1 ? "Mono" : "Stereo", (median / 1000m).ToString("F1", System.Globalization.CultureInfo.CurrentUICulture)), channels, median);
                    var plan = Create(tracks, user, standardRate, original, candidates[0].track.SampleRateHz!.Value,
                        candidates[0].track.AacConfigurationId!, false);
                    plan.OriginalBitrateBitsPerSecond = median;
                    plans.Add(plan);
                }
            }
        }
        else
        {
            var rate = fixedRate ?? standardRate;
            foreach (var group in candidates.Where(x => x.track.SampleRateHz == rate &&
                Classify(x.track.AudioBitrateBitsPerSecond, channels) == user.BitrateKbps).GroupBy(x => x.track.AacConfigurationId!))
                if (group.Count() == included.Length || (calibratedConfigurations.TryGetValue(CalibrationKey(rate, user), out var config) && config == group.Key))
                    plans.Add(Create(tracks, user, standardRate, user, rate, group.Key, false));
        }
        return Rank(plans, user, standardRate).FirstOrDefault()
            ?? Create(tracks, user, standardRate, user, fixedRate ?? standardRate, "", true);
    }

    public static IOrderedEnumerable<AacDecisionPlan> Rank(IEnumerable<AacDecisionPlan> plans, ExportPreset user, int standardRate) =>
        plans.OrderByDescending(p => p.ReusedDurationTicks)
            .ThenByDescending(p => p.Channels == user.Channels && p.SampleRateHz == standardRate && p.BitrateKbps == user.BitrateKbps && !p.IsOriginal)
            .ThenByDescending(p => p.Channels == user.Channels)
            .ThenByDescending(p => p.SampleRateHz).ThenByDescending(p => p.BitrateKbps)
            .ThenBy(p => p.ConfigurationId, StringComparer.Ordinal);

    // Upper bounds before encoder compatibility is known. Ranking uses the same rules as Build.
    public static IReadOnlyList<AacDecisionPlan> PossibleReusePlans(IReadOnlyList<TrackInfo> tracks, ExportPreset user, int standardRate)
    {
        var plans = tracks.Where(track => !track.IsExcluded && track.Channels is 1 or 2 && Eligible(track, track.Channels.Value) && IsAllowedRate(track.SampleRateHz) &&
                Classify(track.AudioBitrateBitsPerSecond, track.Channels!.Value).HasValue)
            .GroupBy(track => (Channels: track.Channels!.Value, Rate: track.SampleRateHz!.Value, Bitrate: Classify(track.AudioBitrateBitsPerSecond, track.Channels!.Value)!.Value,
                Config: track.AacConfigurationId!))
            .Select(group => Create(tracks, user, standardRate,
                ExportPreset.Parse($"AAC {(group.Key.Channels == 1 ? "Mono" : "Stereo")} {group.Key.Bitrate} kbps"),
                group.Key.Rate, group.Key.Config, false));
        return Rank(plans, user, standardRate).ToArray();
    }

    private static AacDecisionPlan Create(IReadOnlyList<TrackInfo> tracks, ExportPreset user, int standard,
        ExportPreset effective, int rate, string config, bool forceAll)
    {
        var plan = new AacDecisionPlan { UserPreset = user.DisplayName, UserSampleRateHz = standard,
            EffectivePreset = effective.DisplayName, SampleRateHz = rate, Channels = effective.Channels,
            BitrateKbps = effective.BitrateKbps, ConfigurationId = config };
        for (var i = 0; i < tracks.Count; i++)
        {
            var t = tracks[i];
            if (t.IsExcluded) continue;
            AacConversionReason? reason = forceAll || !Eligible(t, effective.Channels) ? AacConversionReason.Technical
                : t.SampleRateHz != rate || !IsAllowedRate(t.SampleRateHz) ? AacConversionReason.SampleRate
                : !effective.IsPassThrough && Classify(t.AudioBitrateBitsPerSecond, effective.Channels) != effective.BitrateKbps ? AacConversionReason.Bitrate
                : t.AacConfigurationId != config ? AacConversionReason.Technical : null;
            plan.Tracks.Add(new(t.SourceId, i, reason is null, reason, t.DurationTicks!.Value,
                t.AudioBitrateBitsPerSecond, t.SampleRateHz, t.Channels, t.AacConfigurationId, t.AacOptimizationEligible));
        }
        return plan;
    }

    public static AacDecisionPlan EncodeAll(IReadOnlyList<TrackInfo> tracks, ExportPreset user, int rate, bool explicitChoice)
    {
        if (user.IsPassThrough) throw new InvalidDataException(LocalizationText.Get("Aac.Plan.InvalidOriginal"));
        var plan = Create(tracks, user, rate, user, rate, "", true);
        plan.ForceEncodeAll = explicitChoice;
        plan.Decision = explicitChoice ? "EncodeAll" : "AutomaticConvertAll";
        return plan;
    }

    public static string CalibrationKey(int rate, ExportPreset preset) => $"{rate}/{preset.Channels}/{preset.BitrateKbps}";

    public static string Integrity(AacDecisionPlan plan)
    {
        // Preserve validation of already confirmed v1 plans that predate the optional policy flag.
        var payload = JsonSerializer.Serialize(new { plan.Version, plan.Signature, plan.SourceSignature, plan.ToolchainIdentity, plan.Decision,
            plan.UserPreset, plan.UserSampleRateHz, plan.EffectivePreset, plan.SampleRateHz, plan.Channels, plan.BitrateKbps,
            plan.OriginalBitrateBitsPerSecond, plan.ConfigurationId, plan.Tracks });
        if (plan.ForceEncodeAll) payload += "|ForceEncodeAll";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
    }

    public static string Signature(IReadOnlyList<TrackInfo> tracks, string root, string preset, int rate, string toolchain, bool sourceOnly = false)
    {
        var values = tracks.Select(t =>
        {
            var file = new FileInfo(TrackPathService.GetTrackPath(root, t));
            return new { t.SourceId, Path = file.FullName.ToUpperInvariant(), Exists = file.Exists,
                Length = file.Exists ? file.Length : -1, Modified = file.Exists ? file.LastWriteTimeUtc.Ticks : -1,
                Excluded = !sourceOnly && t.IsExcluded };
        }).ToArray();
        if (sourceOnly) values = values.OrderBy(v => v.SourceId).ThenBy(v => v.Path, StringComparer.Ordinal).ToArray();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { values, preset, rate, toolchain }))));
    }

    public static void Apply(AacDecisionPlan plan, IReadOnlyList<TrackInfo> tracks)
    {
        if (plan.ForceEncodeAll && (plan.IsOriginal || plan.ReusedCount != 0))
            throw new InvalidDataException(LocalizationText.Get("Aac.Plan.SourceError"));
        foreach (var t in tracks) { t.TargetSampleRateHz = plan.SampleRateHz; t.AacPlanAction = null; t.AacForceEncodeAll = plan.ForceEncodeAll; }
        foreach (var item in plan.Tracks)
        {
            if (item.Occurrence < 0 || item.Occurrence >= tracks.Count || tracks[item.Occurrence].SourceId != item.SourceId)
                throw new InvalidDataException(LocalizationText.Get("Aac.Plan.SourceError"));
            var track = tracks[item.Occurrence];
            track.AudioBitrateBitsPerSecond = item.AudioBitrateBitsPerSecond;
            track.SampleRateHz = item.SourceSampleRateHz ?? track.SampleRateHz;
            track.Channels = item.SourceChannels ?? track.Channels;
            track.AacOptimizationEligible = item.Eligible;
            track.AacConfigurationId = item.SourceConfigurationId;
            track.AudioValidationPassed = true;
            track.DurationTicks = item.DurationTicks;
            track.AllowAacReuse = item.Reuse;
            track.AacPlanAction = item.Reuse ? ProcessingActionIds.Reuse : ProcessingActionIds.Convert;
            track.AacPlanPreset = plan.EffectivePreset;
            if (item.Reuse) track.AacConfigurationId = plan.ConfigurationId;
            track.ProcessingAction = track.AacPlanAction;
        }
    }
}
