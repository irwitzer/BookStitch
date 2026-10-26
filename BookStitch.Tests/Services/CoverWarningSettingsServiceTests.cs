using BookStitch.Models;
using BookStitch.Presentation;
using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class CoverWarningSettingsServiceTests
{
    [Theory]
    [InlineData(0, false, 0)]
    [InlineData(500, false, 0)]
    [InlineData(1000, false, 0)]
    [InlineData(2000, false, 0)]
    [InlineData(0, true, 2000)]
    [InlineData(500, true, 500)]
    [InlineData(1000, true, 1000)]
    [InlineData(2000, true, 2000)]
    public void Warning_toggle_uses_the_existing_threshold(int threshold, bool alwaysWarn, int expected)
    {
        Assert.Equal(expected, CoverWarningSettingsService.ResolveWarningChoice(threshold, alwaysWarn));
    }

    [Theory]
    [InlineData(0, 2000)]
    [InlineData(500, 500)]
    [InlineData(1000, 1000)]
    [InlineData(2000, 2000)]
    public void Reset_all_warnings_restores_disabled_cover_warning_without_changing_active_thresholds(int threshold, int expected)
    {
        var settings = new AppSettings { CoverWarningThresholdPixels = threshold };
        WarningSuppressionService.ResetAll(settings);
        Assert.Equal(expected, settings.CoverWarningThresholdPixels);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(500)]
    [InlineData(1000)]
    [InlineData(2000)]
    public void NormalizeThresholdPixels_PreservesSupportedValues(int value)
    {
        Assert.Equal(value, CoverWarningSettingsService.NormalizeThresholdPixels(value));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    [InlineData(499)]
    [InlineData(501)]
    [InlineData(1500)]
    [InlineData(9999)]
    public void NormalizeThresholdPixels_InvalidValueFallsBackToCurrentDefault(int value)
    {
        Assert.Equal(
            CoverWarningSettingsService.DefaultThresholdPixels,
            CoverWarningSettingsService.NormalizeThresholdPixels(value));
    }

    [Fact]
    public void Shared_presentation_exposes_the_supported_thresholds_once_in_ui_order()
    {
        Assert.Equal(
            [0, 500, 1000, 2000],
            CoverWarningSettingsPresentation.Options.Select(option => option.ThresholdPixels));
        Assert.Equal("CoverWarningThresholdHeading", CoverWarningSettingsPresentation.HeadingResourceKey);
    }

    [Fact]
    public void Normalize_RepairsInvalidPersistedValue()
    {
        var settings = new AppSettings
        {
            CoverWarningThresholdPixels = 777
        };

        CoverWarningSettingsService.Normalize(settings);

        Assert.Equal(
            CoverWarningSettingsService.DefaultThresholdPixels,
            settings.CoverWarningThresholdPixels);
    }
}
