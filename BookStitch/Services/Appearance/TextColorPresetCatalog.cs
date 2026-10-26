using System.Windows.Media;

namespace BookStitch.Services;

public enum TextColorPreset
{
    Frost,
    SageMist,
    WarmLinen,
    LavenderSmoke,
    WarmSand,
    Sage,
    Ink
}

public sealed record TextColorPresetDefinition(
    TextColorPreset Preset,
    string ResourceKey,
    Color PrimaryTextColor,
    Color SecondaryTextColor,
    Color TertiaryTextColor,
    Color OnAccentTextColor,
    IReadOnlySet<AppearanceMode> CompatibleModes)
{
    public string DisplayName => LocalizationText.Get(ResourceKey);

    public bool IsCompatibleWith(AppearanceMode mode) => CompatibleModes.Contains(mode);
}

public static class TextColorPresetCatalog
{
    public const string DefaultPreset = nameof(TextColorPreset.WarmSand);

    private static readonly IReadOnlySet<AppearanceMode> DarkModes = new HashSet<AppearanceMode> { AppearanceMode.Dark };
    private static readonly IReadOnlySet<AppearanceMode> LightModes = new HashSet<AppearanceMode> { AppearanceMode.Light };

    private static readonly Dictionary<string, TextColorPreset> LegacyAliases = new(StringComparer.Ordinal)
    {
        ["FrostNew"] = TextColorPreset.Frost,
        ["SageMistNew"] = TextColorPreset.SageMist,
        ["WarmLinenNew"] = TextColorPreset.WarmLinen,
        ["LavenderSmokeNew"] = TextColorPreset.LavenderSmoke,
        ["SoftNeutral"] = TextColorPreset.WarmSand,
        ["HighContrast"] = TextColorPreset.WarmSand,
        ["CoolClear"] = TextColorPreset.WarmSand
    };

    public static IReadOnlyList<TextColorPresetDefinition> All { get; } =
    [
        Dark(TextColorPreset.Frost, "Appearance.TextColor.Frost", Color.FromRgb(0xF7, 0xFB, 0xFD), Color.FromRgb(0xB5, 0xC2, 0xCB), Color.FromRgb(0x93, 0xA6, 0xB2)),
        Dark(TextColorPreset.SageMist, "Appearance.TextColor.SageMist", Color.FromRgb(0xF4, 0xFA, 0xF6), Color.FromRgb(0xB1, 0xC8, 0xBE), Color.FromRgb(0x8F, 0xB0, 0xA2)),
        Dark(TextColorPreset.WarmLinen, "Appearance.TextColor.WarmLinen", Color.FromRgb(0xFF, 0xF8, 0xEC), Color.FromRgb(0xCD, 0xBB, 0x9C), Color.FromRgb(0xAE, 0x96, 0x74)),
        Dark(TextColorPreset.LavenderSmoke, "Appearance.TextColor.LavenderSmoke", Color.FromRgb(0xFA, 0xF7, 0xFD), Color.FromRgb(0xC4, 0xBD, 0xD1), Color.FromRgb(0xA3, 0x99, 0xB5)),
        Dark(TextColorPreset.WarmSand, "Appearance.TextColor.WarmSand", Color.FromRgb(0xFF, 0xF7, 0xE8), Color.FromRgb(0xE9, 0xDA, 0xBD), Color.FromRgb(0xD6, 0xC4, 0xA4)),
        Dark(TextColorPreset.Sage, "Appearance.TextColor.Sage", Color.FromRgb(0xF3, 0xF8, 0xF1), Color.FromRgb(0xC8, 0xDC, 0xCF), Color.FromRgb(0xA7, 0xD6, 0xC4)),
        new(
            TextColorPreset.Ink,
            "Appearance.TextColor.Ink",
            Color.FromRgb(0x1C, 0x25, 0x28),
            Color.FromRgb(0x4D, 0x5A, 0x5E),
            Color.FromRgb(0x5A, 0x66, 0x6A),
            Color.FromRgb(0xF7, 0xFB, 0xFD),
            LightModes)
    ];

    public static TextColorPreset Normalize(string? preset, AppearanceMode mode)
    {
        var normalized = NormalizeKnownValue(preset);
        return IsCompatible(normalized, mode) ? normalized : GetDefaultForMode(mode);
    }

    public static TextColorPreset NormalizeKnownValue(string? preset)
    {
        if (!string.IsNullOrWhiteSpace(preset) && LegacyAliases.TryGetValue(preset, out var alias))
            return alias;

        return Enum.TryParse<TextColorPreset>(preset, ignoreCase: false, out var parsed) &&
            All.Any(item => item.Preset == parsed)
                ? parsed
                : TextColorPreset.WarmSand;
    }

    public static TextColorPreset GetDefaultForMode(AppearanceMode mode) => mode switch
    {
        AppearanceMode.Light => TextColorPreset.Ink,
        _ => TextColorPreset.WarmSand
    };

    public static IReadOnlyList<TextColorPresetDefinition> GetCompatiblePresets(AppearanceMode mode) =>
        All.Where(item => item.IsCompatibleWith(mode)).ToArray();

    public static TextColorPresetDefinition Get(string? preset, AppearanceMode mode)
    {
        var normalized = Normalize(preset, mode);
        return All.Single(item => item.Preset == normalized);
    }

    private static bool IsCompatible(TextColorPreset preset, AppearanceMode mode) =>
        All.Single(item => item.Preset == preset).IsCompatibleWith(mode);

    private static TextColorPresetDefinition Dark(
        TextColorPreset preset,
        string displayName,
        Color primaryTextColor,
        Color secondaryTextColor,
        Color tertiaryTextColor) =>
        new(preset, displayName, primaryTextColor, secondaryTextColor, tertiaryTextColor, primaryTextColor, DarkModes);
}
