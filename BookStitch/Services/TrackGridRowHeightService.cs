namespace BookStitch.Services;

public sealed record TrackGridRowHeightOption(int Level, string ResourceKey, double RowHeight)
{
    public string DisplayName => LocalizationText.Get(ResourceKey);
}

public static class TrackGridRowHeightService
{
    public const int MinimumLevel = 0;
    public const int MaximumLevel = 5;

    private static readonly IReadOnlyList<TrackGridRowHeightOption> OptionsInternal =
    [
        new(0, "Appearance.RowHeight.VeryCompact", 22),
        new(1, "Appearance.RowHeight.Compact", 24),
        new(2, "Appearance.RowHeight.Normal", 26),
        new(3, "Appearance.RowHeight.Large", 28),
        new(4, "Appearance.RowHeight.VeryLarge", 30),
        new(5, "Appearance.RowHeight.Maximum", 32)
    ];

    public static IReadOnlyList<TrackGridRowHeightOption> Options => OptionsInternal;

    public static int NormalizeLevel(int level) =>
        Math.Clamp(level, MinimumLevel, MaximumLevel);

    public static double GetRowHeight(int level) =>
        OptionsInternal[NormalizeLevel(level)].RowHeight;
}
