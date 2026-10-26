using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class ExportSettingsNormalizationServiceTests
{
    [Theory]
    [InlineData(null, ".m4a")]
    [InlineData("", ".m4a")]
    [InlineData(".mp3", ".m4a")]
    [InlineData(".m4a", ".m4a")]
    [InlineData(".m4b", ".m4b")]
    public void NormalizeOutputExtension_UsesSupportedExtensionOrDefault(
        string? value,
        string expected)
    {
        Assert.Equal(
            expected,
            ExportSettingsNormalizationService.NormalizeOutputExtension(value));
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("  {title}  ", "{title}")]
    public void NormalizeFileNameTemplate_TrimsAndHandlesNull(
        string? value,
        string expected)
    {
        Assert.Equal(
            expected,
            ExportSettingsNormalizationService.NormalizeFileNameTemplate(value));
    }

    [Fact]
    public void NormalizePreset_PreservesAvailablePreset()
    {
        var presets = new[] { "AAC Stereo 192 kbps", "AAC Mono 96 kbps" };

        var result = ExportSettingsNormalizationService.NormalizePreset(
            "AAC Mono 96 kbps",
            presets,
            "AAC Stereo 192 kbps");

        Assert.Equal("AAC Mono 96 kbps", result);
    }

    [Fact]
    public void NormalizePreset_FallsBackForUnavailablePreset()
    {
        var presets = new[] { "AAC Stereo 192 kbps", "AAC Mono 96 kbps" };

        var result = ExportSettingsNormalizationService.NormalizePreset(
            "Unbekannt",
            presets,
            "AAC Stereo 192 kbps");

        Assert.Equal("AAC Stereo 192 kbps", result);
    }

    [Theory]
    [InlineData(".m4a", null, ".m4a", false)]
    [InlineData(".m4a", ".mp3", ".m4a", false)]
    [InlineData(".m4a", ".m4b", ".m4b", true)]
    public void CreateOutputExtensionChange_NormalizesAndDetectsChanges(
        string currentValue,
        string? incomingValue,
        string expectedValue,
        bool expectedHasChanged)
    {
        var result = ExportSettingsNormalizationService.CreateOutputExtensionChange(
            currentValue,
            incomingValue);

        Assert.Equal(expectedValue, result.Value);
        Assert.Equal(expectedHasChanged, result.HasChanged);
    }

    [Theory]
    [InlineData("", null, "", false)]
    [InlineData("{title}", "  {title}  ", "{title}", false)]
    [InlineData("{title}", "{author} - {title}", "{author} - {title}", true)]
    public void CreateFileNameTemplateChange_NormalizesAndDetectsChanges(
        string currentValue,
        string? incomingValue,
        string expectedValue,
        bool expectedHasChanged)
    {
        var result = ExportSettingsNormalizationService.CreateFileNameTemplateChange(
            currentValue,
            incomingValue);

        Assert.Equal(expectedValue, result.Value);
        Assert.Equal(expectedHasChanged, result.HasChanged);
    }

    [Fact]
    public void CreatePresetChange_DetectsUnchangedAvailablePreset()
    {
        var presets = new[] { "AAC Stereo 192 kbps", "AAC Mono 96 kbps" };

        var result = ExportSettingsNormalizationService.CreatePresetChange(
            "AAC Mono 96 kbps",
            "AAC Mono 96 kbps",
            presets,
            "AAC Stereo 192 kbps");

        Assert.Equal("AAC Mono 96 kbps", result.Value);
        Assert.False(result.HasChanged);
    }

    [Fact]
    public void CreatePresetChange_DetectsChangedAvailablePreset()
    {
        var presets = new[] { "AAC Stereo 192 kbps", "AAC Mono 96 kbps" };

        var result = ExportSettingsNormalizationService.CreatePresetChange(
            "AAC Stereo 192 kbps",
            "AAC Mono 96 kbps",
            presets,
            "AAC Stereo 192 kbps");

        Assert.Equal("AAC Mono 96 kbps", result.Value);
        Assert.True(result.HasChanged);
    }

    [Fact]
    public void CreatePresetChange_UsesDefaultBeforeComparing()
    {
        var presets = new[] { "AAC Stereo 192 kbps", "AAC Mono 96 kbps" };

        var result = ExportSettingsNormalizationService.CreatePresetChange(
            "AAC Stereo 192 kbps",
            "Unbekannt",
            presets,
            "AAC Stereo 192 kbps");

        Assert.Equal("AAC Stereo 192 kbps", result.Value);
        Assert.False(result.HasChanged);
    }
}