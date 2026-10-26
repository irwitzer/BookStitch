using BookStitch.Services;

namespace BookStitch.Presentation;

public sealed record CoverWarningOptionDefinition(int ThresholdPixels, string ResourceKey);

public static class CoverWarningSettingsPresentation
{
    public const string HeadingResourceKey = "CoverWarningThresholdHeading";

    public static IReadOnlyList<CoverWarningOptionDefinition> Options { get; } =
    [
        new(CoverWarningSettingsService.Disabled, "CoverWarningThresholdNone"),
        new(500, "CoverWarningThreshold500"),
        new(1000, "CoverWarningThreshold1000"),
        new(2000, "CoverWarningThreshold2000")
    ];
}
