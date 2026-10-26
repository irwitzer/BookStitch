using BookStitch.Models;
using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class PresetBitrateWarningServiceTests
{
    [Theory]
    [InlineData(96, "AAC Stereo 160 kbps", true)]
    [InlineData(96, "AAC Stereo 256 kbps", true)]
    [InlineData(96, "AAC Stereo 96 kbps", false)]
    [InlineData(96, "AAC Mono 64 kbps", false)]
    [InlineData(1411, "AAC Stereo 256 kbps", false)] // Audio-CD PCM evidence, not an encoded 96-kbps source.
    [InlineData(null, "AAC Stereo 160 kbps", false)]
    [InlineData(0, "AAC Stereo 160 kbps", false)]
    [InlineData(-1, "AAC Stereo 160 kbps", false)]
    public void Loaded_source_bitrate_is_compared_to_the_selected_preset(int? sourceRate, string preset, bool expected)
    {
        Assert.Equal(expected, PresetBitrateWarningService.ShouldWarnAfterLoad(
            [new TrackInfo { BitrateKbps = sourceRate }], preset, selectedPresetFullyPrepared: false));
    }

    [Fact]
    public void Mixed_sources_keep_the_existing_highest_known_source_comparison()
    {
        TrackInfo[] tracks = [new() { BitrateKbps = 64 }, new() { BitrateKbps = null }, new() { BitrateKbps = 192 }];
        Assert.Equal(192, PresetBitrateWarningService.GetMaxSourceBitrateKbps(tracks));
        Assert.False(PresetBitrateWarningService.ShouldWarnAfterLoad(tracks, "AAC Stereo 160 kbps", false));
        Assert.True(PresetBitrateWarningService.ShouldWarnAfterLoad(tracks, "AAC Stereo 256 kbps", false));
        Assert.Null(PresetBitrateWarningService.GetMaxSourceBitrateKbps([]));
        Assert.False(PresetBitrateWarningService.ShouldWarnAfterLoad([], "AAC Stereo 256 kbps", false));
    }

    [Theory]
    [InlineData("AAC Stereo 256 kbps", true, true, false)]
    [InlineData("AAC Stereo 256 kbps", false, true, true)]
    [InlineData("AAC Stereo 192 kbps", true, true, true)]
    [InlineData("AAC Stereo 256 kbps", true, false, true)]
    public void Only_complete_preparation_for_the_exact_selected_preset_suppresses_the_load_warning(
        string preparedPreset, bool secondTrackPrepared, bool hasPath, bool expectedWarning)
    {
        const string selected = "AAC Stereo 256 kbps";
        TrackInfo[] tracks =
        [
            new() { Index = 2, FilePath = "source-a.mp3", BitrateKbps = 96, HasReusableConvertedFile = true,
                PreparedConvertedPreset = preparedPreset, PreparedConvertedPath = hasPath ? "converted-a.m4a" : "" },
            new() { Index = 1, FilePath = "source-b.mp3", BitrateKbps = 96, HasReusableConvertedFile = secondTrackPrepared,
                PreparedConvertedPreset = preparedPreset, PreparedConvertedPath = hasPath ? "converted-b.m4a" : "" }
        ];
        var progress = new TrackPreparedStateRefreshService().GetPreparationProgress(tracks, selected);
        var complete = progress.TotalCount > 0 && progress.PendingCount == 0;
        Assert.Equal(expectedWarning, PresetBitrateWarningService.ShouldWarnAfterLoad(tracks, selected, complete));
        Assert.Equal(96, PresetBitrateWarningService.GetMaxSourceBitrateKbps(tracks));
        Assert.Equal(new[] { 2, 1 }, tracks.Select(track => track.Index));
        Assert.All(tracks, track => Assert.Equal(preparedPreset, track.PreparedConvertedPreset));
    }
}
