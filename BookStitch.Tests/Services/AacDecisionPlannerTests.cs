using BookStitch.Models;
using BookStitch.Services;
using BookStitch.Tests.TestHelpers;
using System.IO;
using System.Text.Json;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class AacDecisionPlannerTests
{
    private static readonly ExportPreset User = ExportPreset.Parse("AAC Stereo 128 kbps");
    private static TrackInfo Track(int rate = 44100, decimal? bitrate = 128000m, long ticks = 100, string config = "asc") =>
        new() { SourceId = Guid.NewGuid(), AudioValidationPassed = true, AacOptimizationEligible = true,
            SampleRateHz = rate, AudioBitrateBitsPerSecond = bitrate, DurationTicks = ticks, Channels = 2,
            AacConfigurationId = config, Codec = "AAC", Extension = ".m4a" };
    private static Dictionary<string, string?> Calibration(params (int Rate, int Bitrate, string Config)[] values) =>
        values.ToDictionary(v => $"{v.Rate}/2/{v.Bitrate}", v => (string?)v.Config);

    [Theory]
    [InlineData(118000, 128)] [InlineData(138000, 128)] [InlineData(117999, null)]
    [InlineData(138001, null)] [InlineData(86000, 96)] [InlineData(106000, 96)]
    public void Classes_use_exact_stream_bits_and_inclusive_boundaries(int bits, int? expected) =>
        Assert.Equal(expected, AacDecisionPlanner.Classify(bits, 2));

    [Theory]
    [InlineData(12000, true)] [InlineData(11025, false)] [InlineData(8000, false)] [InlineData(44101, false)]
    [InlineData(96000, true)]
    public void Approved_rates_are_exact(int rate, bool expected) => Assert.Equal(expected, AacDecisionPlanner.IsAllowedRate(rate));

    [Fact]
    public void Joint_optimization_weights_duration_and_ignores_forced_and_excluded_tracks()
    {
        var longest = Track(22050, 96000, 10000);
        var forced = Track(96000, 256000, 999999); forced.AacOptimizationEligible = false;
        var excluded = Track(48000, 192000, 999999); excluded.IsExcluded = true;
        var tracks = new[] { longest, Track(48000), Track(48000), forced, excluded };
        var plan = AacDecisionPlanner.Build(tracks, User, 44100, Calibration((22050, 96, "asc"), (48000, 128, "asc")));
        Assert.Equal(22050, plan.SampleRateHz); Assert.Equal(96, plan.BitrateKbps);
        Assert.True(plan.Tracks[0].Reuse); Assert.Equal(4, plan.Tracks.Count);
        Assert.Equal(10000, plan.ReusedDurationTicks); Assert.Equal(1000199, plan.ConvertedDurationTicks);
        Assert.Equal(AacConversionReason.Technical, plan.Tracks[3].Reason);
        Assert.All(plan.Tracks.Skip(1).Take(2), t => Assert.Equal(AacConversionReason.SampleRate, t.Reason));
    }

    [Theory]
    [InlineData(2000, true)] [InlineData(2001, false)]
    public void Unusual_bitrate_is_all_copy_only_and_uses_unrounded_median(int delta, bool original)
    {
        var tracks = new[] { Track(bitrate: 73400m - delta), Track(bitrate: 73400m + delta) };
        var plan = AacDecisionPlanner.Build(tracks, User, 44100, Calibration());
        Assert.Equal(original, plan.IsOriginal);
        if (original) { Assert.Equal(73400m, plan.OriginalBitrateBitsPerSecond); Assert.Equal(0, plan.ConvertedCount); Assert.True(plan.Preset.IsPassThrough); }
        else Assert.Equal(2, plan.ConvertedCount);
    }

    [Theory]
    [InlineData("regular")] [InlineData("rate")] [InlineData("config")] [InlineData("technical")]
    public void Unusual_bitrate_rejects_mixed_or_unsafe_projects(string difference)
    {
        var a = Track(bitrate: 73400m); var b = Track(bitrate: 73400m);
        if (difference == "regular") b.AudioBitrateBitsPerSecond = 128000;
        if (difference == "rate") b.SampleRateHz = 48000;
        if (difference == "config") b.AacConfigurationId = "other";
        if (difference == "technical") b.AacOptimizationEligible = false;
        Assert.False(AacDecisionPlanner.Build([a, b], User, 44100, Calibration()).IsOriginal);
    }

    [Fact]
    public void Config_groups_need_encoder_evidence_for_mixed_plans_but_full_copy_does_not()
    {
        var a = Track(config: "a"); var b = Track(config: "b", ticks: 200);
        var unsafePlan = AacDecisionPlanner.Build([a, b], User, 48000, Calibration());
        Assert.Equal(48000, unsafePlan.SampleRateHz); Assert.Equal(2, unsafePlan.ConvertedCount);
        var safe = AacDecisionPlanner.Build([a, b], User, 48000, Calibration((44100, 128, "a")));
        Assert.True(safe.Tracks[0].Reuse); Assert.False(safe.Tracks[1].Reuse);
        Assert.Equal(AacConversionReason.Technical, safe.Tracks[1].Reason);
        Assert.Equal(0, AacDecisionPlanner.Build([a], User, 48000, Calibration()).ConvertedCount);
    }

    [Fact]
    public void Tie_prefers_complete_user_profile_then_rate_then_bitrate_then_config()
    {
        var tracks = new[] { Track(44100), Track(48000) };
        Assert.Equal(44100, AacDecisionPlanner.Build(tracks, User, 44100, Calibration((44100, 128, "asc"), (48000, 128, "asc"))).SampleRateHz);
        Assert.Equal(48000, AacDecisionPlanner.Build(tracks, User, 32000, Calibration((44100, 128, "asc"), (48000, 128, "asc"))).SampleRateHz);
        var rates = new[] { Track(48000, 96000), Track(48000, 128000) };
        Assert.Equal(128, AacDecisionPlanner.Build(rates, User, 32000, Calibration((48000, 96, "asc"), (48000, 128, "asc"))).BitrateKbps);
    }

    [Fact]
    public void Reasons_are_disjoint_and_user_alternative_is_recalculated()
    {
        var tracks = new[] { Track(22050, 96000, 1000), Track(44100), Track(22050, 128000), Track(11025, 128000) };
        var evidence = Calibration((22050, 96, "asc"), (22050, 128, "asc"), (44100, 128, "asc"));
        var plan = AacDecisionPlanner.Build(tracks, User, 44100, evidence);
        Assert.Equal(new AacConversionReason?[] { null, AacConversionReason.SampleRate, AacConversionReason.Bitrate, AacConversionReason.SampleRate }, plan.Tracks.Select(t => t.Reason));
        var requested = AacDecisionPlanner.Build(tracks, User, 44100, evidence, true);
        Assert.True(requested.Tracks[1].Reuse); Assert.Equal(300, requested.ReusedDurationTicks + requested.ConvertedDurationTicks - 1000);
        Assert.Equal(requested.Tracks.Count, requested.ConvertedCount + requested.ReusedCount);
    }

    [Fact]
    public void Unknown_duration_or_unvalidated_source_blocks_planning()
    {
        var t = Track(); t.DurationTicks = null;
        Assert.Throws<InvalidDataException>(() => AacDecisionPlanner.Build([t], User, 44100, Calibration()));
        t.DurationTicks = 100; t.AudioValidationPassed = null;
        Assert.Throws<InvalidDataException>(() => AacDecisionPlanner.Build([t], User, 44100, Calibration()));
    }

    [Fact]
    public void Apply_restores_source_evidence_after_resume_and_rejects_wrong_track_identity()
    {
        var track = Track();
        var plan = AacDecisionPlanner.Build([track], User, 44100, Calibration());
        var restored = new TrackInfo { SourceId = track.SourceId };
        AacDecisionPlanner.Apply(plan, [restored]);
        Assert.Equal(ProcessingActionIds.Reuse, AudioProcessingService.DetermineProcessingAction(restored, User));
        Assert.Equal("asc", restored.AacConfigurationId); Assert.Equal(128000m, restored.AudioBitrateBitsPerSecond);
        Assert.Throws<InvalidDataException>(() => AacDecisionPlanner.Apply(plan, [new TrackInfo { SourceId = Guid.NewGuid() }]));
    }

    [Fact]
    public void Persisted_original_mode_restores_identity_and_plan_without_parsing_display_text()
    {
        using var folder = new TemporaryFolder();
        var store = new FolderProjectManifestService(); store.Create(folder.Path, "source");
        var plan = AacDecisionPlanner.Build([Track(bitrate: 73400m)], User, 44100, Calibration());
        store.SavePlan(folder.Path, plan);
        var regular = AacDecisionPlanner.Build([Track()], User, 44100, Calibration());
        store.SavePlan(folder.Path, regular);
        var main = store.Read(folder.Path)!;
        Assert.False(main.AacPlan!.IsOriginal); Assert.True(main.OriginalAacPlan!.Preset.IsPassThrough);
        Assert.Equal(0, main.OriginalAacPlan.Preset.BitrateKbps);
        Assert.Equal(73400m, main.OriginalAacPlan.Preset.OriginalBitrateBitsPerSecond);
        var restored = JsonSerializer.Deserialize<AacDecisionPlan>(JsonSerializer.Serialize(plan))!;
        Assert.Equal(plan.Tracks, restored.Tracks);
        Assert.Equal(plan.PlanIntegrity, AacDecisionPlanner.Integrity(restored));
        restored.Tracks.Clear(); Assert.NotEqual(plan.PlanIntegrity, AacDecisionPlanner.Integrity(restored));
    }

    [Fact]
    public void Signature_ignores_metadata_but_tracks_source_settings_and_exclusions()
    {
        using var folder = new TemporaryFolder(); var file = Path.Combine(folder.Path, "source.m4a"); File.WriteAllText(file, "source");
        var t = Track(); t.FilePath = file;
        string Signature() => AacDecisionPlanner.Signature([t], folder.Path, User.DisplayName, 44100, "toolchain");
        var original = Signature(); t.ChapterTitle = "metadata"; Assert.Equal(original, Signature());
        t.IsExcluded = true; Assert.NotEqual(original, Signature()); t.IsExcluded = false;
        File.AppendAllText(file, "change"); Assert.NotEqual(original, Signature());
    }
}
