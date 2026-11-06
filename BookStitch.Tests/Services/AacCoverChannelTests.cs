using BookStitch.Models;
using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class AacCoverChannelTests
{
    [Theory]
    [InlineData("[png @ 0000019efb965240] chunk too big", false, false)]
    [InlineData("[aac @ 0000019efb965240] invalid data", false, true)]
    [InlineData("[png @ 0000019efb965240] chunk too big\n[aac @ 0000019efb965240] invalid data", false, true)]
    [InlineData("unknown container error", false, true)]
    [InlineData("[png @ 0000019efb965240] chunk too big", true, true)]
    public void Only_unambiguously_attached_picture_errors_are_ignored(string stderr, bool realVideo, bool fatal)
    {
        var probe = new AudioProbeInfo { Success = true, HasNonCoverVideo = realVideo, AttachedPictureCodecs = ["png"] };
        Assert.Equal(fatal, AudioProbeDiagnosticService.HasAudioErrors(probe, stderr));
        probe.AttachedPictureCodecs.Clear();
        Assert.True(AudioProbeDiagnosticService.HasAudioErrors(probe, stderr));
    }

    [Theory]
    [InlineData(1, 64000, false)]
    [InlineData(2, 128000, false)]
    [InlineData(1, 81000, true)]
    [InlineData(2, 81000, true)]
    public void Opposite_channel_profile_is_explicitly_offered_and_encode_all_retains_user_values(int channels, int bits, bool original)
    {
        var user = ExportPreset.Parse(channels == 1 ? "AAC Stereo 128 kbps" : "AAC Mono 64 kbps");
        var track = new TrackInfo { SourceId = Guid.NewGuid(), AudioValidationPassed = true, AacOptimizationEligible = true,
            Channels = channels, SampleRateHz = 44100, AudioBitrateBitsPerSecond = bits, DurationTicks = TimeSpan.TicksPerMinute,
            AacConfigurationId = "config" };
        var calibrations = new Dictionary<string, string?>();
        var options = new AacDecisionOptions(AacDecisionPlanner.Build([track], user, 44100, calibrations),
            AacDecisionPlanner.Build([track], user, 44100, calibrations, true), AacDecisionPlanner.EncodeAll([track], user, 44100, true));
        Assert.Equal(channels, options.Optimized.Channels); Assert.Equal(original, options.Optimized.IsOriginal);
        Assert.Equal(1, options.Optimized.ReusedCount); Assert.Equal(user.DisplayName, options.Optimized.UserPreset);
        Assert.True(AacDecisionSelectionService.Select(options).NeedsDialog);
        Assert.Null(AacDecisionSelectionService.Confirm(options, AacDecisionChoice.Back));
        var converted = AacDecisionSelectionService.Confirm(options, AacDecisionChoice.EncodeAll)!;
        Assert.Equal(user.Channels, converted.Channels); Assert.Equal(user.BitrateKbps, converted.BitrateKbps);
        Assert.True(converted.ForceEncodeAll); Assert.Equal(0, converted.ReusedCount);
        Assert.False(AacDecisionSelectionService.Select(options, forceEncodeAll: true).NeedsDialog);
        var review = AacDecisionSelectionService.Select(options, review: true);
        Assert.False(review.NeedsDialog); Assert.Equal(user.Channels, review.Plan.Channels);
        Assert.Equal(channels, AacDecisionSelectionService.Confirm(options, AacDecisionChoice.PassThrough)!.Channels);
    }
}
