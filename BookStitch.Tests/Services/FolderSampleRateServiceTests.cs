using BookStitch.Models;
using BookStitch.Services;
using BookStitch.Tests.TestHelpers;
using System.IO;
using System.Text.Json;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class FolderSampleRateServiceTests
{
    [Theory]
    [InlineData(16000)] [InlineData(22050)] [InlineData(24000)] [InlineData(32000)]
    [InlineData(44100)] [InlineData(48000)] [InlineData(64000)] [InlineData(88200)] [InlineData(96000)]
    public void Every_voting_rate_can_win_and_pass_through(int rate)
    {
        var decision = FolderSampleRateService.Decide([Qualified(rate)], ExportPreset.Parse("AAC Stereo 128 kbps"), 48000);
        Assert.Equal(rate, decision.TargetRate); Assert.True(decision.FullPassThrough);
        Assert.Equal(48000, decision.EffectiveRate(true));
    }

    [Theory]
    [InlineData(7350)] [InlineData(8000)] [InlineData(11025)] [InlineData(12000)]
    [InlineData(16430)] [InlineData(32200)] [InlineData(88300)] [InlineData(192000)]
    public void Nonvoting_rates_do_not_outvote_a_valid_candidate(int rate)
    {
        var tracks = Enumerable.Range(0, 51).Select(_ => Qualified(rate)).Append(Qualified(24000)).ToArray();
        var decision = FolderSampleRateService.Decide(tracks, ExportPreset.Parse("AAC Stereo 128 kbps"), 48000);
        Assert.True(decision.Qualified); Assert.False(decision.FullPassThrough); Assert.Equal(24000, decision.TargetRate);
        Assert.Equal(48000, FolderSampleRateService.SelectTarget([Qualified(rate)], 48000));
    }

    [Theory]
    [InlineData(22050, 100, 22050, 0, 22050)]
    [InlineData(32000, 99, 32200, 1, 32000)]
    [InlineData(24000, 49, 16430, 51, 24000)]
    [InlineData(88200, 99, 88300, 1, 88200)]
    [InlineData(44100, 50, 48000, 50, 48000)]
    [InlineData(16000, 99, 96000, 1, 16000)]
    [InlineData(8000, 100, 8000, 0, 44100)]
    [InlineData(24000, 99, 8000, 1, 24000)]
    [InlineData(16430, 100, 16430, 0, 44100)]
    [InlineData(96000, 100, 96000, 0, 96000)]
    public void Required_example_matrix(int first, int firstCount, int second, int secondCount, int target)
    {
        var tracks = Enumerable.Repeat(first, firstCount).Concat(Enumerable.Repeat(second, secondCount)).Select(Qualified).ToArray();
        Assert.Equal(target, FolderSampleRateService.Decide(tracks, ExportPreset.Parse("AAC Stereo 128 kbps"), 44100).TargetRate);
    }

    [Theory]
    [InlineData("missing-rate")] [InlineData("technical-failure")] [InlineData("other-format")] [InlineData("preset")]
    public void Unsafe_or_unqualified_input_forces_whole_project_to_standard(string reason)
    {
        var track = Qualified(96000);
        if (reason == "missing-rate") track.SampleRateHz = null;
        if (reason is "technical-failure" or "other-format") track.AacOptimizationEligible = false;
        if (reason == "preset") track.BitrateKbps = 64;
        var decision = FolderSampleRateService.Decide([Qualified(96000), track], ExportPreset.Parse("AAC Stereo 128 kbps"), 48000);
        Assert.False(decision.Qualified); Assert.False(decision.FullPassThrough); Assert.False(decision.NeedsHighRateDialog);
        Assert.Equal(48000, decision.TargetRate);
    }

    private static TrackInfo Qualified(int rate) => new() { SampleRateHz = rate, Codec = "AAC", Extension = ".m4a",
        BitrateKbps = 128, Channels = 2, AudioValidationPassed = true, AacOptimizationEligible = true };

    public static TheoryData<int?[], int> Decisions => new()
    {
        { [], 44100 },
        { [48000, 48000], 48000 },
        { [44100, 44100], 44100 },
        { [22050, 22050], 22050 },
        { [32000, 32000], 32000 },
        { [96000, 96000], 96000 },
        { [44100, 48000], 48000 },
        { [44100, 44100, 48000], 44100 },
        { [48000, 48000, 32000, 32000, 44100], 48000 },
        { [32000, 32000, 48000], 32000 },
        { [22050, 22050, 48000], 22050 },
        { [22050, null], 22050 },
        { [12345, 12345], 44100 },
        { [null, 0, -1], 44100 },
        { [48000, 48000, 12345], 48000 },
    };

    [Theory, MemberData(nameof(Decisions))]
    public void Selects_only_voting_rates_by_track_count_and_higher_ties(int?[] rates, int expected)
    {
        var tracks = rates.Select((rate, index) => new TrackInfo
        { SampleRateHz = rate, Index = index + 1, DurationTicks = index == 0 ? TimeSpan.TicksPerHour : 1 }).ToArray();
        Assert.Equal(expected, FolderSampleRateService.SelectTarget(tracks));
        Assert.Equal(rates, tracks.Select(track => track.SampleRateHz));
        Assert.Equal(Enumerable.Range(1, rates.Length), tracks.Select(track => track.Index));
    }

    [Fact]
    public void Excluded_tracks_do_not_vote_and_repeated_logical_tracks_each_vote()
    {
        var tracks = new[] {
            new TrackInfo { FilePath = "same.m4a", SampleRateHz = 48000 },
            new TrackInfo { FilePath = "same.m4a", SampleRateHz = 48000 },
            new TrackInfo { SampleRateHz = 44100 },
            new TrackInfo { SampleRateHz = 22050, IsExcluded = true }
        };
        Assert.Equal(48000, FolderSampleRateService.SelectTarget(tracks));
    }

    [Theory]
    [InlineData(22050)]
    [InlineData(32000)]
    [InlineData(48000)]
    public void Pass_through_notice_rearms_after_switching_away_and_back(int rate)
    {
        var track = new TrackInfo { Codec = "AAC", Extension = ".m4a", BitrateKbps = 128,
            Channels = 2, SampleRateHz = rate, TargetSampleRateHz = rate };
        var notice = new FolderPassThroughNoticeState();
        var matching = ExportPreset.Parse("AAC Stereo 128 kbps");
        var other = ExportPreset.Parse("AAC Mono 64 kbps");
        Assert.True(notice.Update([track], matching, true));
        Assert.False(notice.Update([track], matching, true));
        Assert.False(notice.Update([track], other, true));
        Assert.True(notice.Update([track], matching, true));
        Assert.False(notice.Update([track], matching, false));
        track.SampleRateHz = null;
        Assert.False(notice.Update([track], matching, true));
    }

    [Fact]
    public void Fixed_project_rate_survives_lifecycle_and_add_sources_and_legacy_defaults()
    {
        using var folder = new TemporaryFolder();
        var store = new FolderProjectManifestService();
        store.Create(folder.Path, "source", 48000);
        var work = new WorkManifestService();
        var manifestPath = ProjectFolderLayout.GetExportManifestPath(folder.Path);
        var manifest = work.LoadOrCreate(manifestPath, ProjectManifestTypes.FolderProject, folder.Path, "source", "AAC Stereo 128 kbps");
        work.Save(manifestPath, manifest);
        Assert.Equal(48000, FolderSampleRateService.ReadTarget(folder.Path));
        var added = new TrackInfo { SampleRateHz = 22050, Codec = "AAC", Extension = ".m4a", Channels = 2, BitrateKbps = 128 };
        FolderSampleRateService.Apply([added], FolderSampleRateService.ReadTarget(folder.Path));
        Assert.Equal(48000, added.TargetSampleRateHz);
        Assert.Equal(ProcessingActionIds.Convert, AudioProcessingService.DetermineProcessingAction(added, ExportPreset.Parse("AAC Stereo 128 kbps")));
        var legacy = JsonSerializer.Deserialize<FolderProjectManifest>("{\"ProjectId\":\"legacy\"}");
        Assert.Equal(44100, legacy!.TargetSampleRateHz);
    }

    [Fact]
    public void Prepared_cache_evidence_is_bound_to_target_rate()
    {
        using var folder = new TemporaryFolder();
        var source = Path.Combine(folder.Path, "source.m4a");
        var converted = Path.Combine(folder.Path, "prepared.m4a");
        File.WriteAllText(source, "source"); File.WriteAllText(converted, "prepared");
        var track = new TrackInfo { SourceId = Guid.NewGuid(), ProcessingAction = ProcessingActionIds.Reuse,
            TargetSampleRateHz = 48000 };
        var preset = ExportPreset.Parse("AAC Stereo 128 kbps");
        var manifest = new ExportWorkManifest(); var work = new WorkManifestService();
        work.UpdateTrack(manifest, 0, track, source, converted, preset);
        Assert.True(work.CanReuseConvertedTrack(manifest, 0, track, source, converted, preset));
        track.TargetSampleRateHz = 44100;
        Assert.False(work.CanReuseConvertedTrack(manifest, 0, track, source, converted, preset));
    }
    [Fact]
    public void Preview_rearms_after_cancel_preset_and_global_changes_but_keeps_accepted_choice()
    {
        var state = new AacSampleRatePreviewState();
        var full = new AacSampleRateDecision(96000, 44100, true, true);
        state.Refresh("matching", full);
        Assert.True(state.ShouldPresent); state.Presented();
        Assert.False(state.ShouldPresent); Assert.True(state.NeedsAcceptance);
        state.Accept(true); Assert.Equal(44100, state.EffectiveRate);
        state.Refresh("matching", full); Assert.False(state.NeedsAcceptance);
        state.Refresh("other-preset", new(44100, 44100, false, false));
        Assert.False(state.ShouldPresent);
        state.Refresh("matching", full); Assert.True(state.ShouldPresent);
        Assert.Equal(96000, state.EffectiveRate);
        state.Accept(true);
        state.Refresh("new-standard", new(96000, 48000, true, true));
        Assert.True(state.ShouldPresent); Assert.Equal(96000, state.EffectiveRate);
    }

    [Theory]
    [InlineData(48000, false, false)]
    [InlineData(64000, false, true)]
    [InlineData(96000, true, false)]
    public void Only_high_mixed_decisions_request_dialog_b(int target, bool full, bool expected)
    {
        Assert.Equal(expected, new AacSampleRateDecision(target, 44100, true, full).NeedsHighRateDialog);
        Assert.False(new AacSampleRateDecision(target, 44100, false, full).NeedsHighRateDialog);
    }

}
