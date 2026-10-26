using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class InfoPaletteCatalogTests
{
    [Theory]
    [InlineData(BackgroundPreset.Black, TextColorPreset.Frost, InfoPaletteVariant.Cool)]
    [InlineData(BackgroundPreset.Black, TextColorPreset.SageMist, InfoPaletteVariant.Cool)]
    [InlineData(BackgroundPreset.Black, TextColorPreset.Sage, InfoPaletteVariant.Cool)]
    [InlineData(BackgroundPreset.Black, TextColorPreset.WarmLinen, InfoPaletteVariant.Soft)]
    [InlineData(BackgroundPreset.Black, TextColorPreset.LavenderSmoke, InfoPaletteVariant.Soft)]
    [InlineData(BackgroundPreset.Black, TextColorPreset.WarmSand, InfoPaletteVariant.Soft)]
    [InlineData(BackgroundPreset.Ivory, TextColorPreset.Ink, InfoPaletteVariant.Light)]
    [InlineData(BackgroundPreset.PearlStone, TextColorPreset.Ink, InfoPaletteVariant.Light)]
    public void Resolve_MapsCurrentAppearancePresetsToSemanticInfoPalette(
        BackgroundPreset backgroundPreset,
        TextColorPreset textPreset,
        InfoPaletteVariant expected)
    {
        var background = BackgroundPresetCatalog.Get(backgroundPreset.ToString());
        var text = TextColorPresetCatalog.Get(textPreset.ToString(), background.Mode);

        var palette = InfoPaletteCatalog.Resolve(background, text);
        Assert.Equal(expected, palette.Variant);
    }

    [Theory]
    [InlineData(BackgroundPreset.Black, BackgroundPreset.DeepPetrol, TextColorPreset.Frost)]
    [InlineData(BackgroundPreset.Ivory, BackgroundPreset.PearlStone, TextColorPreset.Ink)]
    public void Resolve_InfoChromeIsOwnedByInfoPaletteWithinEachMode(
        BackgroundPreset firstBackgroundPreset,
        BackgroundPreset secondBackgroundPreset,
        TextColorPreset textPreset)
    {
        var firstBackground = BackgroundPresetCatalog.Get(firstBackgroundPreset.ToString());
        var secondBackground = BackgroundPresetCatalog.Get(secondBackgroundPreset.ToString());
        var first = InfoPaletteCatalog.Resolve(firstBackground, TextColorPresetCatalog.Get(textPreset.ToString(), firstBackground.Mode));
        var second = InfoPaletteCatalog.Resolve(secondBackground, TextColorPresetCatalog.Get(textPreset.ToString(), secondBackground.Mode));

        Assert.Equal(first.PopupBorderColor, second.PopupBorderColor);
        Assert.Equal(first.PopupForegroundColor, second.PopupForegroundColor);
        Assert.NotEqual(default, first.IconBorderColor);
        Assert.NotEqual(default, first.IconGlyphColor);
    }

    [Fact]
    public void DarkTextVariant_CanChangeIconsWithoutChangingPopupChrome()
    {
        var background = BackgroundPresetCatalog.Get(BackgroundPreset.Black.ToString());
        var frost = InfoPaletteCatalog.Resolve(background, TextColorPresetCatalog.Get(TextColorPreset.Frost.ToString(), AppearanceMode.Dark));
        var warm = InfoPaletteCatalog.Resolve(background, TextColorPresetCatalog.Get(TextColorPreset.WarmSand.ToString(), AppearanceMode.Dark));

        Assert.NotEqual(frost.Variant, warm.Variant);
        Assert.Equal(frost.PopupBorderColor, warm.PopupBorderColor);
        Assert.Equal(frost.PopupForegroundColor, warm.PopupForegroundColor);
    }

    [Theory]
    [InlineData(BackgroundPreset.Ivory, 0x1A1A1Au, 0x000000u)]
    [InlineData(BackgroundPreset.Black, 0xF5F7FAu, 0xFFFFFFu)]
    public void Resolve_OnPrimaryIconChromeUsesModeContrast(
        BackgroundPreset backgroundPreset,
        uint expectedNormalRgb,
        uint expectedHoverRgb)
    {
        var background = BackgroundPresetCatalog.Get(backgroundPreset.ToString());
        var text = TextColorPresetCatalog.Get(
            background.Mode == AppearanceMode.Light ? TextColorPreset.Ink.ToString() : TextColorPreset.Frost.ToString(),
            background.Mode);

        var palette = InfoPaletteCatalog.Resolve(background, text);

        Assert.Equal(System.Windows.Media.Color.FromRgb((byte)(expectedNormalRgb >> 16), (byte)(expectedNormalRgb >> 8), (byte)expectedNormalRgb), palette.OnPrimaryIconBorderColor);
        Assert.Equal(palette.OnPrimaryIconBorderColor, palette.OnPrimaryIconGlyphColor);
        Assert.Equal(System.Windows.Media.Color.FromRgb((byte)(expectedHoverRgb >> 16), (byte)(expectedHoverRgb >> 8), (byte)expectedHoverRgb), palette.OnPrimaryIconHoverBorderColor);
        Assert.Equal(palette.OnPrimaryIconHoverBorderColor, palette.OnPrimaryIconHoverGlyphColor);
    }

}
