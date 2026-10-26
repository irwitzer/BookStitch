using BookStitch.Models;

namespace BookStitch.Services;

/// <summary>One target rate per folder project. Source rates remain source evidence.</summary>
public static class FolderSampleRateService
{
    public const int DefaultRate = 44100;
    // Rates accepted by FFmpeg's native AAC encoder.
    public static bool IsSupported(int? rate) => rate is
        7350 or 8000 or 11025 or 12000 or 16000 or 22050 or 24000 or 32000 or 44100 or 48000 or 64000 or 88200 or 96000;

    public static bool CanVote(int? rate) => rate is
        16000 or 22050 or 24000 or 32000 or 44100 or 48000 or 64000 or 88200 or 96000;

    public static int SelectTarget(IEnumerable<TrackInfo> tracks, int standardRate = 44100)
    {
        var rates = tracks.Where(track => !track.IsExcluded && CanVote(track.SampleRateHz))
            .GroupBy(track => track.SampleRateHz!.Value)
            .OrderByDescending(group => group.Count()).ThenByDescending(group => group.Key);
        return rates.FirstOrDefault()?.Key ?? SampleRateSettingsService.Normalize(standardRate);
    }

    public static AacSampleRateDecision Decide(IEnumerable<TrackInfo> tracks, ExportPreset preset, int standardRate)
    {
        var included = tracks.Where(track => !track.IsExcluded).ToArray();
        var standard = SampleRateSettingsService.Normalize(standardRate);
        var qualified = included.Length > 0 && included.All(track =>
            track.AacOptimizationEligible && track.SampleRateHz is > 0 &&
            AudioProcessingService.AacMatchesPreset(track.BitrateKbps, track.Channels, preset));
        qualified = qualified && included.GroupBy(track => track.SampleRateHz).All(group =>
            group.Select(track => track.AacConfigurationId).Distinct(StringComparer.Ordinal).Count() == 1);
        var target = qualified ? SelectTarget(included, standard) : standard;
        var full = qualified && included.All(track => CanVote(track.SampleRateHz) && track.SampleRateHz == target);
        return new(target, standard, qualified, full);
    }

    public static string DecisionKey(IEnumerable<TrackInfo> tracks, ExportPreset preset, int standardRate) =>
        string.Join("|", new[] { preset.DisplayName, standardRate.ToString(System.Globalization.CultureInfo.InvariantCulture) }
            .Concat(tracks.Select(track => $"{track.SourceId}:{track.FilePath}:{track.IsExcluded}:{track.SampleRateHz}:{track.BitrateKbps}:{track.Channels}:{track.AacOptimizationEligible}:{track.AacConfigurationId}")));

    public static int ReadTarget(string? projectFolder)
    {
        return ProjectSampleRateService.ReadTarget(projectFolder);
    }

    public static int RequireSupported(int rate) => IsSupported(rate) ? rate :
        throw new System.IO.InvalidDataException("Invalid folder project target sample rate.");

    public static void Apply(IEnumerable<TrackInfo> tracks, int targetRate)
    {
        RequireSupported(targetRate);
        foreach (var track in tracks) track.TargetSampleRateHz = targetRate;
    }
}

/// <summary>Only a transition into full pass-through announces it; leaving re-arms the notice.</summary>
public sealed class FolderPassThroughNoticeState
{
    private string? _active;
    public bool Update(IReadOnlyCollection<TrackInfo> tracks, ExportPreset preset, bool eligible)
    {
        var included = tracks.Where(track => !track.IsExcluded).ToArray();
        var key = eligible && included.Length > 0 && included.All(track =>
            track.SampleRateHz == track.TargetSampleRateHz &&
            AudioProcessingService.DetermineProcessingAction(track, preset) == ProcessingActionIds.Reuse)
            ? $"{preset.DisplayName}|{included[0].TargetSampleRateHz}" : null;
        var announce = key is not null && key != _active;
        _active = key;
        return announce;
    }
}

public sealed record AacSampleRateDecision(int TargetRate, int StandardRate, bool Qualified, bool FullPassThrough)
{
    public bool NeedsHighRateDialog => Qualified && !FullPassThrough && TargetRate > 48000;
    public int EffectiveRate(bool preferStandard) => preferStandard ? StandardRate : TargetRate;
}

public sealed class AacSampleRatePreviewState
{
    private string? _key;
    private string? _presented;
    private bool _accepted;
    private bool _preferStandard;
    public AacSampleRateDecision Decision { get; private set; } = new(44100, 44100, false, false);
    public int EffectiveRate => Decision.EffectiveRate(_preferStandard);
    public bool NeedsAcceptance => (Decision.FullPassThrough || Decision.NeedsHighRateDialog) && !_accepted;
    public bool ShouldPresent => NeedsAcceptance && _presented != _key;
    public void Refresh(string key, AacSampleRateDecision decision)
    {
        if (_key != key) { _accepted = false; _preferStandard = false; _presented = null; }
        _key = key; Decision = decision;
    }
    public void Presented() => _presented = _key;
    public void Accept(bool preferStandard) { _accepted = true; _preferStandard = preferStandard; }
}
