using System.Windows.Media;

namespace BookStitch.Services;

public sealed record ButtonPaletteDefinition(
    AppearanceMode Mode,
    Color PrimaryBackgroundColor,
    Color PrimaryHoverBackgroundColor,
    Color PrimaryPressedBackgroundColor,
    Color PrimaryForegroundColor,
    Color PrimaryBorderColor,
    Color PrimaryHoverBorderColor,
    Color PrimaryPressedBorderColor,
    Color PrimaryMutedBackgroundColor,
    Color PrimaryMutedHoverBackgroundColor,
    Color PrimaryMutedPressedBackgroundColor,
    Color PrimaryMutedBorderColor,
    Color PrimaryMutedHoverBorderColor,
    Color PrimaryMutedPressedBorderColor,
    Color SecondaryBackgroundColor,
    Color SecondaryHoverBackgroundColor,
    Color SecondaryPressedBackgroundColor,
    Color SecondaryForegroundColor,
    Color SecondaryBorderColor,
    Color SecondaryHoverBorderColor);

public static class ButtonPalette
{
    public static ButtonPaletteDefinition Get(AppearanceMode mode) => mode switch
    {
        AppearanceMode.Light => Light,
        _ => Dark
    };

    // Values intentionally mirror the previously effective button appearance.
    // Keeping them here makes button states independent from text/input/neutral palettes.
    public static ButtonPaletteDefinition Dark { get; } = new(
        AppearanceMode.Dark,
        Color.FromRgb(0x12, 0x6F, 0x73),
        Color.FromRgb(0x16, 0x8A, 0x8F),
        Color.FromRgb(0x0F, 0x5F, 0x63),
        Color.FromRgb(0xFF, 0xF7, 0xE8),
        Color.FromRgb(0x12, 0x6F, 0x73),
        Color.FromRgb(0x16, 0x8A, 0x8F),
        Color.FromRgb(0x0F, 0x5F, 0x63),
        Color.FromRgb(0x21, 0x49, 0x4C),
        Color.FromRgb(0x28, 0x5A, 0x5E),
        Color.FromRgb(0x1B, 0x3E, 0x41),
        Color.FromRgb(0x37, 0x67, 0x6A),
        Color.FromRgb(0x4F, 0x8A, 0x8D),
        Color.FromRgb(0x31, 0x5B, 0x5E),
        Color.FromRgb(0x1B, 0x24, 0x2A),
        Color.FromRgb(0x22, 0x30, 0x3A),
        Color.FromRgb(0x18, 0x26, 0x2D),
        Color.FromRgb(0xFF, 0xF7, 0xE8),
        Color.FromRgb(0x35, 0x41, 0x4D),
        Color.FromRgb(0x63, 0xD4, 0xD8));

    public static ButtonPaletteDefinition Light { get; } = new(
        AppearanceMode.Light,
        Color.FromRgb(0x32, 0x6B, 0x68),
        Color.FromRgb(0x39, 0x73, 0x6F),
        Color.FromRgb(0x2C, 0x5C, 0x59),
        Color.FromRgb(0xFF, 0xF7, 0xE8),
        Color.FromRgb(0x32, 0x6B, 0x68),
        Color.FromRgb(0x39, 0x73, 0x6F),
        Color.FromRgb(0x2C, 0x5C, 0x59),
        Color.FromRgb(0x5E, 0x7E, 0x7A),
        Color.FromRgb(0x67, 0x89, 0x84),
        Color.FromRgb(0x52, 0x6F, 0x6B),
        Color.FromRgb(0x52, 0x73, 0x6F),
        Color.FromRgb(0x66, 0x8D, 0x88),
        Color.FromRgb(0x49, 0x66, 0x62),
        Color.FromRgb(0xDE, 0xD8, 0xCF),
        Color.FromRgb(0xE1, 0xDB, 0xD1),
        Color.FromRgb(0xD6, 0xCE, 0xC2),
        Color.FromRgb(0x1C, 0x25, 0x28),
        Color.FromRgb(0x8A, 0x80, 0x74),
        Color.FromRgb(0x32, 0x6B, 0x68));
}
