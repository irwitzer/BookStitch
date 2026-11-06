using BookStitch.Models;
using BookStitch.Services;
using BookStitch.Tests.TestHelpers;
using System.IO;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class AacDecisionSelectionServiceTests
{
    private static readonly ExportPreset User = ExportPreset.Parse("AAC Stereo 128 kbps");
    private static TrackInfo Track(int rate = 44100, decimal bits = 128000, bool eligible = true) => new()
    {
        SourceId = Guid.NewGuid(), AudioValidationPassed = true, DurationTicks = 37 * TimeSpan.TicksPerSecond,
        SampleRateHz = rate, Channels = 2, AudioBitrateBitsPerSecond = bits, AacConfigurationId = "asc",
        AacOptimizationEligible = eligible, Codec = eligible ? "AAC" : "MP3", Extension = eligible ? ".m4a" : ".mp3"
    };

    private static AacDecisionOptions Options(TrackInfo[] tracks, int rate = 44100)
    {
        var calibration = new Dictionary<string, string?> { ["44100/2/128"] = "asc", ["48000/2/128"] = "asc" };
        return new(AacDecisionPlanner.Build(tracks, User, rate, calibration),
            AacDecisionPlanner.Build(tracks, User, rate, calibration, true), AacDecisionPlanner.EncodeAll(tracks, User, rate, true));
    }

    [Theory]
    [InlineData(AacDecisionChoice.PassThrough)]
    [InlineData(AacDecisionChoice.EncodeAll)]
    [InlineData(AacDecisionChoice.Back)]
    public void Every_new_folder_requires_its_own_choice_even_after_a_matching_profile_was_accepted(AacDecisionChoice previous)
    {
        var first = Options([Track()]);
        Assert.True(AacDecisionSelectionService.Select(first, requireReuseConfirmation: true).NeedsDialog);
        AacDecisionSelectionService.Confirm(first, previous);
        var second = Options([Track()]);
        Assert.True(AacDecisionSelectionService.Select(second, requireReuseConfirmation: true).NeedsDialog);
        Assert.False(AacDecisionSelectionService.Select(Options([Track(eligible: false)]), requireReuseConfirmation: true).NeedsDialog);
    }

    [Theory]
    [InlineData("mp3", false, 0)]
    [InlineData("ko", false, 0)]
    [InlineData("matching", false, 2)]
    [InlineData("partial", true, 1)]
    [InlineData("different-rate", true, 2)]
    [InlineData("original", true, 2)]
    public void Prompt_is_derived_from_complete_plan_and_actual_profile_match(string scenario, bool prompt, int reused)
    {
        var tracks = scenario switch
        {
            "mp3" or "ko" => new[] { Track(eligible: false), Track(eligible: false) },
            "partial" => [Track(), Track(eligible: false)],
            "different-rate" => [Track(48000), Track(48000)],
            "original" => [Track(bits: 73400), Track(bits: 73400)],
            _ => [Track(bits: 118000), Track(bits: 138000)]
        };
        if (scenario == "ko") foreach (var track in tracks) { track.Codec = "AAC"; track.Extension = ".m4a"; }
        var selection = AacDecisionSelectionService.Select(Options(tracks));
        Assert.Equal(prompt, selection.NeedsDialog); Assert.Equal(reused, selection.Plan.ReusedCount);
        if (reused == 0)
        {
            Assert.Equal(User.DisplayName, selection.Plan.EffectivePreset); Assert.Equal(44100, selection.Plan.SampleRateHz);
            Assert.False(selection.Plan.ForceEncodeAll); Assert.Equal("AutomaticConvertAll", selection.Plan.Decision);
        }
        if (scenario == "matching") Assert.Equal("AutomaticReuse", selection.Plan.Decision);
    }

    [Fact]
    public void Confirm_all_encodes_even_matching_sources_and_back_does_not_confirm_a_plan()
    {
        var tracks = new[] { Track(), Track(eligible: false) }; var options = Options(tracks);
        Assert.Null(AacDecisionSelectionService.Confirm(options, AacDecisionChoice.Back));
        var accepted = AacDecisionSelectionService.Confirm(options, AacDecisionChoice.EncodeAll)!;
        Assert.True(accepted.ForceEncodeAll); Assert.Equal(0, accepted.ReusedCount); Assert.Equal(2, accepted.ConvertedCount);
        AacDecisionPlanner.Apply(accepted, tracks);
        Assert.All(tracks, t => Assert.Equal(ProcessingActionIds.Convert, AudioProcessingService.DetermineProcessingAction(t, User)));
        Assert.Equal(options.Optimized, AacDecisionSelectionService.Confirm(options, AacDecisionChoice.PassThrough));
    }

    [Fact]
    public void Explicit_all_survives_save_resume_review_channel_changes_and_add_sources()
    {
        using var folder = new TemporaryFolder(); var store = new FolderProjectManifestService(); store.Create(folder.Path, "source");
        var tracks = new[] { Track() }; var accepted = AacDecisionSelectionService.Confirm(Options(tracks), AacDecisionChoice.EncodeAll)!;
        store.SavePlan(folder.Path, accepted);
        var restored = store.Read(folder.Path)!.AacPlan!;
        Assert.True(restored.ForceEncodeAll); Assert.Equal(restored.PlanIntegrity, AacDecisionPlanner.Integrity(restored));
        AacDecisionPlanner.Apply(restored, tracks);
        var mono = ExportPreset.Parse("AAC Mono 64 kbps");
        Assert.Equal(ProcessingActionIds.Convert, AudioProcessingService.DetermineProcessingAction(tracks[0], mono));
        var review = AacDecisionSelectionService.Select(Options(tracks), review: true, forceEncodeAll: restored.ForceEncodeAll);
        Assert.False(review.NeedsDialog); Assert.True(review.Plan.ForceEncodeAll); Assert.Equal(0, review.Plan.ReusedCount);
        var extension = AacDecisionSelectionService.Select(Options([tracks[0], Track()]), forceEncodeAll: restored.ForceEncodeAll);
        Assert.False(extension.NeedsDialog); Assert.Equal(2, extension.Plan.ConvertedCount); Assert.Equal(0, extension.Plan.ReusedCount);
    }

    [Fact]
    public void Review_preset_choice_has_no_additional_optimization_prompt()
    {
        var selection = AacDecisionSelectionService.Select(Options([Track(48000), Track(eligible: false)]), review: true);
        Assert.False(selection.NeedsDialog); Assert.Equal(User.DisplayName, selection.Plan.EffectivePreset);
    }

    [Fact]
    public void Encode_all_cannot_claim_a_source_copy_and_policy_is_integrity_bound()
    {
        var track = Track(); var plan = Options([track]).Optimized;
        var integrity = AacDecisionPlanner.Integrity(plan); plan.ForceEncodeAll = true;
        Assert.NotEqual(integrity, AacDecisionPlanner.Integrity(plan));
        Assert.Throws<InvalidDataException>(() => AacDecisionPlanner.Apply(plan, [track]));
    }

    [Fact]
    public void Prepared_copy_is_not_displayed_as_ready_for_an_explicit_convert_plan()
    {
        using var folder = new TemporaryFolder();
        var source = Path.Combine(folder.Path, "source.m4a"); var prepared = Path.Combine(folder.Path, "prepared.m4a");
        File.WriteAllText(source, "source"); File.WriteAllText(prepared, "copy");
        var track = Track(); track.FilePath = source; track.ProcessingAction = ProcessingActionIds.Reuse;
        var store = new WorkManifestService(); var path = ProjectFolderLayout.GetExportManifestPath(folder.Path);
        var manifest = store.LoadOrCreate(path, ProjectManifestTypes.FolderProject, folder.Path, folder.Path, User.DisplayName);
        store.UpdateTrack(manifest, 0, track, source, prepared, User); store.Save(path, manifest);
        var refresh = new TrackPreparedStateRefreshService();
        refresh.RefreshForCurrentPreset([track], folder.Path, User.DisplayName); Assert.True(track.HasReusableConvertedFile);
        AacDecisionPlanner.Apply(AacDecisionPlanner.EncodeAll([track], User, 44100, true), [track]);
        refresh.RefreshForCurrentPreset([track], folder.Path, User.DisplayName); Assert.False(track.HasReusableConvertedFile);
        Assert.False(store.CanReuseConvertedTrack(manifest, 0, track, source, prepared, User));
    }
}
