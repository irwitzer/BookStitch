using System.Windows.Media;

namespace BookStitch.Services;

public enum BackgroundPreset
{
    Black = 0,
    DeepPetrol = 2,
    MidnightBlue = 3,
    Ivory = 4,
    PearlStone = 5
}

public sealed record BackgroundPresetDefinition(
    BackgroundPreset Preset,
    string ResourceKey,
    Color BackgroundColor,
    Color PanelBackgroundColor,
    Color InputBackgroundColor,
    Color InfoPopupBackgroundColor,
    AppearanceMode Mode)
{
    public string DisplayName => LocalizationText.Get(ResourceKey);
}

public static class BackgroundPresetCatalog
{
    public const string DefaultPreset = nameof(BackgroundPreset.Black);

    private const string LegacyBookStitchStandardPreset = "BookStitchStandard";

    private static readonly Dictionary<string, BackgroundPreset> LegacyAliases = new(StringComparer.Ordinal)
    {
        ["BlackNew"] = BackgroundPreset.Black,
        ["DeepPetrolNew"] = BackgroundPreset.DeepPetrol,
        ["MidnightBlueNew"] = BackgroundPreset.MidnightBlue,
        [LegacyBookStitchStandardPreset] = BackgroundPreset.Black
    };

    public static IReadOnlyList<BackgroundPresetDefinition> All { get; } =
    [
        new(BackgroundPreset.Black, "Appearance.Background.Black", Color.FromRgb(0x00, 0x00, 0x00), Color.FromRgb(0x18, 0x1E, 0x24), Color.FromRgb(0x11, 0x16, 0x1B), Color.FromRgb(0x00, 0x00, 0x00), AppearanceMode.Dark),
        new(BackgroundPreset.DeepPetrol, "Appearance.Background.DeepPetrol", Color.FromRgb(0x06, 0x17, 0x19), Color.FromRgb(0x11, 0x2D, 0x30), Color.FromRgb(0x0E, 0x27, 0x2A), Color.FromRgb(0x06, 0x17, 0x19), AppearanceMode.Dark),
        new(BackgroundPreset.MidnightBlue, "Appearance.Background.MidnightBlue", Color.FromRgb(0x08, 0x11, 0x1A), Color.FromRgb(0x15, 0x26, 0x38), Color.FromRgb(0x11, 0x20, 0x30), Color.FromRgb(0x08, 0x11, 0x1A), AppearanceMode.Dark),
        new(BackgroundPreset.Ivory, "Appearance.Background.Ivory", Color.FromRgb(0xF4, 0xF1, 0xEA), Color.FromRgb(0xFF, 0xFC, 0xF7), Color.FromRgb(0xE8, 0xE2, 0xD8), Color.FromRgb(0xFB, 0xF8, 0xF2), AppearanceMode.Light),
        new(BackgroundPreset.PearlStone, "Appearance.Background.PearlStone", Color.FromRgb(0xD1, 0xD6, 0xDA), Color.FromRgb(0xC9, 0xD3, 0xD8), Color.FromRgb(0xD5, 0xDD, 0xE5), Color.FromRgb(0xEE, 0xF2, 0xF4), AppearanceMode.Light)
    ];

    public static bool IsDeveloperOnly(BackgroundPreset preset) => preset == BackgroundPreset.PearlStone;

    public static BackgroundPreset NormalizeRegular(string? preset, bool developerEnabled)
    {
        var normalized = Normalize(preset);
        return !developerEnabled && IsDeveloperOnly(normalized) ? BackgroundPreset.Black : normalized;
    }

    public static BackgroundPreset Normalize(string? preset)
    {
        if (!string.IsNullOrWhiteSpace(preset) && LegacyAliases.TryGetValue(preset, out var alias))
            return alias;

        return Enum.TryParse<BackgroundPreset>(preset, ignoreCase: false, out var parsed) &&
            All.Any(item => item.Preset == parsed)
                ? parsed
                : BackgroundPreset.Black;
    }

    public static BackgroundPresetDefinition Get(string? preset)
    {
        var normalized = Normalize(preset);
        return All.Single(item => item.Preset == normalized);
    }
}
