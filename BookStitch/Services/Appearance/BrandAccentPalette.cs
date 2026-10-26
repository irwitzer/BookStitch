using System.Windows.Media;

namespace BookStitch.Services;

public sealed record BrandAccentPaletteDefinition(
    AppearanceMode Mode,
    Color Accent,
    Color AccentHover,
    Color AccentSoft,
    Color AccentForeground,
    Color Selection);

public static class BrandAccentPalette
{
    public static BrandAccentPaletteDefinition Get(AppearanceMode mode) => mode switch
    {
        AppearanceMode.Light => Light,
        _ => Dark
    };

    public static BrandAccentPaletteDefinition Dark { get; } = new(
        AppearanceMode.Dark,
        Color.FromRgb(0x12, 0x6F, 0x73),
        Color.FromRgb(0x16, 0x8A, 0x8F),
        Color.FromRgb(0x63, 0xD4, 0xD8),
        Color.FromRgb(0x63, 0xD4, 0xD8),
        Color.FromRgb(0x12, 0x6F, 0x73));

    public static BrandAccentPaletteDefinition Light { get; } = new(
        AppearanceMode.Light,
        Color.FromRgb(0x32, 0x6B, 0x68),
        Color.FromRgb(0x39, 0x73, 0x6F),
        Color.FromRgb(0x5B, 0x8A, 0x86),
        Color.FromRgb(0x32, 0x6B, 0x68),
        Color.FromRgb(0x32, 0x6B, 0x68));
}
