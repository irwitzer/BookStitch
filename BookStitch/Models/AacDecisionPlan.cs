namespace BookStitch.Models;

public enum AacConversionReason { Technical = 1, SampleRate = 2, Bitrate = 3 }

public sealed record AacTrackDecision(Guid SourceId, int Occurrence, bool Reuse,
    AacConversionReason? Reason, long DurationTicks, decimal? AudioBitrateBitsPerSecond = null,
    int? SourceSampleRateHz = null, int? SourceChannels = null, string? SourceConfigurationId = null, bool Eligible = false);
public sealed record AacConversionGroup(AacConversionReason Reason, int TrackCount, long DurationTicks, IReadOnlyList<Guid> TrackIds);

public sealed class AacDecisionPlan
{
    public int Version { get; set; } = 1;
    public string Signature { get; set; } = "";
    public string SourceSignature { get; set; } = "";
    public string PlanIntegrity { get; set; } = "";
    public string ToolchainIdentity { get; set; } = "";
    public string Decision { get; set; } = "Optimized";
    public bool ForceEncodeAll { get; set; }
    public string UserPreset { get; set; } = "";
    public int UserSampleRateHz { get; set; }
    public string EffectivePreset { get; set; } = "";
    public int SampleRateHz { get; set; }
    public int Channels { get; set; }
    public int BitrateKbps { get; set; }
    public decimal? OriginalBitrateBitsPerSecond { get; set; }
    public string ConfigurationId { get; set; } = "";
    public List<AacTrackDecision> Tracks { get; set; } = [];
    public int ConvertedCount => Tracks.Count(t => !t.Reuse);
    public long ConvertedDurationTicks => Tracks.Where(t => !t.Reuse).Sum(t => t.DurationTicks);
    public int ReusedCount => Tracks.Count(t => t.Reuse);
    public long ReusedDurationTicks => Tracks.Where(t => t.Reuse).Sum(t => t.DurationTicks);
    public IReadOnlyList<AacConversionGroup> Groups => Enum.GetValues<AacConversionReason>().Select(reason =>
    {
        var items = Tracks.Where(t => t.Reason == reason).ToArray();
        return new AacConversionGroup(reason, items.Length, items.Sum(t => t.DurationTicks), items.Select(t => t.SourceId).ToArray());
    }).ToArray();
    public bool IsOriginal => OriginalBitrateBitsPerSecond.HasValue;
    [System.Text.Json.Serialization.JsonIgnore]
    public ExportPreset Preset => IsOriginal
        ? ExportPreset.RegisterOriginal(EffectivePreset, Channels, OriginalBitrateBitsPerSecond!.Value)
        : ExportPreset.Parse(EffectivePreset);
}
