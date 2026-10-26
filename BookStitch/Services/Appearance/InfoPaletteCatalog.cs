using System.Windows.Media;

namespace BookStitch.Services;

public enum InfoPaletteVariant
{
    Cool,
    Soft,
    Light
}

public sealed record InfoPaletteDefinition(
    InfoPaletteVariant Variant,
    Color IconBorderColor,
    Color IconGlyphColor,
    Color IconHoverBorderColor,
    Color IconHoverGlyphColor,
    Color OnPrimaryIconBorderColor,
    Color OnPrimaryIconGlyphColor,
    Color OnPrimaryIconHoverBorderColor,
    Color OnPrimaryIconHoverGlyphColor,
    Color PopupBorderColor,
    Color PopupForegroundColor);

public static class InfoPaletteCatalog
{
    private static readonly InfoPaletteDefinition Cool = new(
        InfoPaletteVariant.Cool,
        Color.FromRgb(0x5F, 0x88, 0xA8),
        Color.FromRgb(0xB8, 0xD3, 0xE6),
        Color.FromRgb(0x7B, 0xA8, 0xC8),
        Color.FromRgb(0xD9, 0xEC, 0xF7),
        Color.FromRgb(0xF5, 0xF7, 0xFA),
        Color.FromRgb(0xF5, 0xF7, 0xFA),
        Color.FromRgb(0xFF, 0xFF, 0xFF),
        Color.FromRgb(0xFF, 0xFF, 0xFF),
        Color.FromRgb(0x35, 0x41, 0x4D),
        Color.FromRgb(0xFF, 0xF7, 0xE8));

    private static readonly InfoPaletteDefinition Soft = new(
        InfoPaletteVariant.Soft,
        Color.FromRgb(0x72, 0x7D, 0xA8),
        Color.FromRgb(0xC5, 0xC9, 0xE2),
        Color.FromRgb(0x8D, 0x99, 0xC6),
        Color.FromRgb(0xE2, 0xE5, 0xF4),
        Color.FromRgb(0xF5, 0xF7, 0xFA),
        Color.FromRgb(0xF5, 0xF7, 0xFA),
        Color.FromRgb(0xFF, 0xFF, 0xFF),
        Color.FromRgb(0xFF, 0xFF, 0xFF),
        Color.FromRgb(0x35, 0x41, 0x4D),
        Color.FromRgb(0xFF, 0xF7, 0xE8));

    private static readonly InfoPaletteDefinition Light = new(
        InfoPaletteVariant.Light,
        Color.FromRgb(0x52, 0x6F, 0x86),
        Color.FromRgb(0x2F, 0x4F, 0x68),
        Color.FromRgb(0x36, 0x5F, 0x7A),
        Color.FromRgb(0x19, 0x3D, 0x56),
        Color.FromRgb(0x1A, 0x1A, 0x1A),
        Color.FromRgb(0x1A, 0x1A, 0x1A),
        Color.FromRgb(0x00, 0x00, 0x00),
        Color.FromRgb(0x00, 0x00, 0x00),
        Color.FromRgb(0x8A, 0x80, 0x74),
        Color.FromRgb(0x1C, 0x25, 0x28));

    public static InfoPaletteDefinition Resolve(
        BackgroundPresetDefinition background,
        TextColorPresetDefinition text)
    {
        ArgumentNullException.ThrowIfNull(background);
        ArgumentNullException.ThrowIfNull(text);

        return background.Mode == AppearanceMode.Light
            ? Light
            : text.Preset switch
            {
                TextColorPreset.WarmLinen or
                TextColorPreset.LavenderSmoke or
                TextColorPreset.WarmSand => Soft,
                _ => Cool
            };
    }
}
