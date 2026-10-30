using BookStitch.Models;
using BookStitch.Services;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class AppearanceSettingsServiceTests
{
    [Theory]
    [InlineData(BackgroundPreset.Black, "Appearance.Background.Black", "#000000", "#181E24", "#11161B", AppearanceMode.Dark)]
    [InlineData(BackgroundPreset.DeepPetrol, "Appearance.Background.DeepPetrol", "#061719", "#112D30", "#0E272A", AppearanceMode.Dark)]
    [InlineData(BackgroundPreset.MidnightBlue, "Appearance.Background.MidnightBlue", "#08111A", "#152638", "#112030", AppearanceMode.Dark)]
    [InlineData(BackgroundPreset.Ivory, "Appearance.Background.Ivory", "#F4F1EA", "#FFFCF7", "#E8E2D8", AppearanceMode.Light)]
    [InlineData(BackgroundPreset.PearlStone, "Appearance.Background.PearlStone", "#D1D6DA", "#C9D3D8", "#D5DDE5", AppearanceMode.Light)]
    public void BackgroundPresets_ExposeFinalSurfaceColorsAndMode(
        BackgroundPreset preset,
        string expectedResourceKey,
        string expectedBackgroundColor,
        string expectedPanelColor,
        string expectedInputColor,
        AppearanceMode expectedMode)
    {
        var definition = AppearanceSettingsService.GetBackgroundPreset(preset.ToString());

        Assert.Equal(expectedResourceKey, definition.ResourceKey);
        Assert.Equal(LocalizationText.Get(expectedResourceKey), definition.DisplayName);
        Assert.Equal(expectedBackgroundColor, ToHex(definition.BackgroundColor));
        Assert.Equal(expectedPanelColor, ToHex(definition.PanelBackgroundColor));
        Assert.Equal(expectedInputColor, ToHex(definition.InputBackgroundColor));
        Assert.Equal(expectedMode, definition.Mode);
    }

    [Fact]
    public void BackgroundPresets_ExposeOnlyFinalPresets()
    {
        Assert.Equal("DeepPetrol", AppearanceSettingsService.DefaultBackgroundPreset);
        Assert.Equal(AppearanceSettingsService.DefaultBackgroundPreset, new AppSettings().BackgroundPreset);
        Assert.Equal(
            [
                BackgroundPreset.Black,
                BackgroundPreset.DeepPetrol,
                BackgroundPreset.MidnightBlue,
                BackgroundPreset.Ivory,
                BackgroundPreset.PearlStone
            ],
            AppearanceSettingsService.BackgroundPresets.Select(item => item.Preset).ToArray());
    }

    [Theory]
    [InlineData("BlackNew", BackgroundPreset.Black)]
    [InlineData("GraphiteNew", BackgroundPreset.DeepPetrol)]
    [InlineData("DeepPetrolNew", BackgroundPreset.DeepPetrol)]
    [InlineData("MidnightBlueNew", BackgroundPreset.MidnightBlue)]
    [InlineData("BookStitchStandard", BackgroundPreset.Black)]
    [InlineData("PearlStone", BackgroundPreset.PearlStone)]
    [InlineData("Graphite", BackgroundPreset.DeepPetrol)]
    [InlineData("Unknown", BackgroundPreset.DeepPetrol)]
    [InlineData(null, BackgroundPreset.DeepPetrol)]
    [InlineData("", BackgroundPreset.DeepPetrol)]
    [InlineData("Black", BackgroundPreset.Black)]
    public void BackgroundPresets_NormalizeLegacyAliases(string? value, BackgroundPreset expected)
    {
        Assert.Equal(expected, AppearanceSettingsService.NormalizeBackgroundPreset(value));
    }

    [Theory]
    [InlineData(TextColorPreset.Frost, "Appearance.TextColor.Frost", "#F7FBFD", "#B5C2CB", "#93A6B2", "#F7FBFD", AppearanceMode.Dark)]
    [InlineData(TextColorPreset.SageMist, "Appearance.TextColor.SageMist", "#F4FAF6", "#B1C8BE", "#8FB0A2", "#F4FAF6", AppearanceMode.Dark)]
    [InlineData(TextColorPreset.WarmLinen, "Appearance.TextColor.WarmLinen", "#FFF8EC", "#CDBB9C", "#AE9674", "#FFF8EC", AppearanceMode.Dark)]
    [InlineData(TextColorPreset.LavenderSmoke, "Appearance.TextColor.LavenderSmoke", "#FAF7FD", "#C4BDD1", "#A399B5", "#FAF7FD", AppearanceMode.Dark)]
    [InlineData(TextColorPreset.WarmSand, "Appearance.TextColor.WarmSand", "#FFF7E8", "#E9DABD", "#D6C4A4", "#FFF7E8", AppearanceMode.Dark)]
    [InlineData(TextColorPreset.Sage, "Appearance.TextColor.Sage", "#F3F8F1", "#C8DCCF", "#A7D6C4", "#F3F8F1", AppearanceMode.Dark)]
    [InlineData(TextColorPreset.Ink, "Appearance.TextColor.Ink", "#1C2528", "#4D5A5E", "#5A666A", "#F7FBFD", AppearanceMode.Light)]
    public void TextColorPresets_ExposeFinalColorsAndCompatibility(
        TextColorPreset preset,
        string expectedResourceKey,
        string expectedPrimaryTextColor,
        string expectedSecondaryTextColor,
        string expectedTertiaryTextColor,
        string expectedOnAccentTextColor,
        AppearanceMode expectedMode)
    {
        var definition = TextColorPresetCatalog.All.Single(item => item.Preset == preset);

        Assert.Equal(expectedResourceKey, definition.ResourceKey);
        Assert.Equal(LocalizationText.Get(expectedResourceKey), definition.DisplayName);
        Assert.Equal(expectedPrimaryTextColor, ToHex(definition.PrimaryTextColor));
        Assert.Equal(expectedSecondaryTextColor, ToHex(definition.SecondaryTextColor));
        Assert.Equal(expectedTertiaryTextColor, ToHex(definition.TertiaryTextColor));
        Assert.Equal(expectedOnAccentTextColor, ToHex(definition.OnAccentTextColor));
        Assert.True(definition.IsCompatibleWith(expectedMode));
    }

    [Fact]
    public void TextColorPresets_ExposeOnlyFinalPresets()
    {
        Assert.Equal(AppearanceSettingsService.DefaultTextColorPreset, TextColorPreset.WarmSand.ToString());
        Assert.Equal(
            [
                TextColorPreset.Frost,
                TextColorPreset.SageMist,
                TextColorPreset.WarmLinen,
                TextColorPreset.LavenderSmoke,
                TextColorPreset.WarmSand,
                TextColorPreset.Sage,
                TextColorPreset.Ink
            ],
            AppearanceSettingsService.TextColorPresets.Select(item => item.Preset).ToArray());
    }

    [Theory]
    [InlineData(AppearanceMode.Dark, "#126F73", "#168A8F", "#63D4D8", "#63D4D8", "#126F73")]
    [InlineData(AppearanceMode.Light, "#326B68", "#39736F", "#5B8A86", "#326B68", "#326B68")]
    public void BrandAccentPalette_ExposesOnlyAccentSemanticColors(
        AppearanceMode mode,
        string accent,
        string accentHover,
        string accentSoft,
        string accentForeground,
        string selection)
    {
        var palette = BrandAccentPalette.Get(mode);

        Assert.Equal(mode, palette.Mode);
        Assert.Equal(accent, ToHex(palette.Accent));
        Assert.Equal(accentHover, ToHex(palette.AccentHover));
        Assert.Equal(accentSoft, ToHex(palette.AccentSoft));
        Assert.Equal(accentForeground, ToHex(palette.AccentForeground));
        Assert.Equal(selection, ToHex(palette.Selection));
    }

    [Theory]
    [InlineData(AppearanceMode.Dark, "#126F73", "#168A8F", "#0F5F63", "#FFF7E8", "#1B242A", "#22303A", "#18262D", "#FFF7E8", "#35414D", "#63D4D8")]
    [InlineData(AppearanceMode.Light, "#326B68", "#39736F", "#2C5C59", "#FFF7E8", "#DED8CF", "#E1DBD1", "#D6CEC2", "#1C2528", "#8A8074", "#326B68")]
    public void ButtonPalette_SeparatesPrimaryAndSecondaryModeContracts(
        AppearanceMode mode,
        string primary, string primaryHover, string primaryPressed, string primaryForeground,
        string secondary, string secondaryHover, string secondaryPressed, string secondaryForeground,
        string secondaryBorder, string secondaryHoverBorder)
    {
        var palette = ButtonPalette.Get(mode);

        Assert.Equal(primary, ToHex(palette.PrimaryBackgroundColor));
        Assert.Equal(primaryHover, ToHex(palette.PrimaryHoverBackgroundColor));
        Assert.Equal(primaryPressed, ToHex(palette.PrimaryPressedBackgroundColor));
        Assert.Equal(primaryForeground, ToHex(palette.PrimaryForegroundColor));
        Assert.Equal(secondary, ToHex(palette.SecondaryBackgroundColor));
        Assert.Equal(secondaryHover, ToHex(palette.SecondaryHoverBackgroundColor));
        Assert.Equal(secondaryPressed, ToHex(palette.SecondaryPressedBackgroundColor));
        Assert.Equal(secondaryForeground, ToHex(palette.SecondaryForegroundColor));
        Assert.Equal(secondaryBorder, ToHex(palette.SecondaryBorderColor));
        Assert.Equal(secondaryHoverBorder, ToHex(palette.SecondaryHoverBorderColor));
    }

    [Theory]
    [InlineData(AppearanceAssetRole.CoverPlaceholder)]
    [InlineData(AppearanceAssetRole.BrandLogo)]
    public void AppearanceAssetCatalog_ResolvesSemanticAssetsForDarkAndLight(AppearanceAssetRole role)
    {
        var dark = AppearanceAssetCatalog.Get(role, AppearanceMode.Dark);
        var light = AppearanceAssetCatalog.Get(role, AppearanceMode.Light);

        Assert.Equal(role, dark.Role);
        Assert.Equal(role, light.Role);
        Assert.Equal(AppearanceMode.Dark, dark.Mode);
        Assert.Equal(AppearanceMode.Light, light.Mode);
        Assert.False(string.IsNullOrWhiteSpace(dark.RelativePath));
        Assert.False(string.IsNullOrWhiteSpace(light.RelativePath));
        Assert.False(string.IsNullOrWhiteSpace(dark.PackUriString));
        Assert.False(string.IsNullOrWhiteSpace(light.PackUriString));
    }

    [Fact]
    public void AppearanceAssetCatalog_RecognizesCoverPlaceholderPathsWithoutMatchingUserCovers()
    {
        var dark = AppearanceAssetCatalog.Get(AppearanceAssetRole.CoverPlaceholder, AppearanceMode.Dark);
        var light = AppearanceAssetCatalog.Get(AppearanceAssetRole.CoverPlaceholder, AppearanceMode.Light);

        Assert.True(AppearanceAssetCatalog.IsCoverPlaceholderPath(dark.RelativePath));
        Assert.True(AppearanceAssetCatalog.IsCoverPlaceholderPath(dark.PackUriString));
        Assert.True(AppearanceAssetCatalog.IsCoverPlaceholderPath(light.RelativePath));
        Assert.True(AppearanceAssetCatalog.IsCoverPlaceholderPath(light.PackUriString));
        Assert.False(AppearanceAssetCatalog.IsCoverPlaceholderPath(@"C:\Books\Cover.jpg"));
    }

    [Theory]
    [InlineData("FrostNew", AppearanceMode.Dark, TextColorPreset.Frost)]
    [InlineData("SageMistNew", AppearanceMode.Dark, TextColorPreset.SageMist)]
    [InlineData("WarmLinenNew", AppearanceMode.Dark, TextColorPreset.WarmLinen)]
    [InlineData("LavenderSmokeNew", AppearanceMode.Dark, TextColorPreset.LavenderSmoke)]
    [InlineData("SoftNeutral", AppearanceMode.Dark, TextColorPreset.WarmSand)]
    [InlineData("HighContrast", AppearanceMode.Dark, TextColorPreset.WarmSand)]
    [InlineData("CoolClear", AppearanceMode.Dark, TextColorPreset.WarmSand)]
    [InlineData("Unknown", AppearanceMode.Dark, TextColorPreset.WarmSand)]
    [InlineData("Unknown", AppearanceMode.Light, TextColorPreset.Ink)]
    public void TextColorPresets_NormalizeLegacyAliasesAndUnknownValues(
        string value,
        AppearanceMode mode,
        TextColorPreset expected)
    {
        Assert.Equal(expected, TextColorPresetCatalog.Normalize(value, mode));
    }

    [Theory]
    [InlineData(BackgroundPreset.Black, TextColorPreset.Frost)]
    [InlineData(BackgroundPreset.Black, TextColorPreset.Sage)]
    [InlineData(BackgroundPreset.Ivory, TextColorPreset.Ink)]
    [InlineData(BackgroundPreset.PearlStone, TextColorPreset.Ink)]
    public void ApplyToResources_ScrollBarsUseOnlyModePalette(
        BackgroundPreset backgroundPreset,
        TextColorPreset textPreset)
    {
        var resources = new ResourceDictionary();
        var settings = new AppSettings
        {
            BackgroundPreset = backgroundPreset.ToString(),
            TextColorPreset = textPreset.ToString()
        };

        AppearanceSettingsService.ApplyToResources(resources, settings);

        var mode = AppearanceSettingsService.GetBackgroundPreset(backgroundPreset.ToString()).Mode;
        var palette = AppearanceModePalette.Get(mode);
        Assert.Equal(palette.ScrollBarTrackColor, GetBrush(resources, AppearanceSettingsService.ScrollBarTrackBrushKey).Color);
        Assert.Equal(palette.ScrollBarThumbColor, GetBrush(resources, AppearanceSettingsService.ScrollBarThumbBrushKey).Color);
        Assert.Equal(palette.ScrollBarThumbHoverColor, GetBrush(resources, AppearanceSettingsService.ScrollBarThumbHoverBrushKey).Color);
        Assert.Equal(palette.ScrollBarThumbDraggingColor, GetBrush(resources, AppearanceSettingsService.ScrollBarThumbDraggingBrushKey).Color);
    }

    [Theory]
    [InlineData(BackgroundPreset.Black, BackgroundPreset.MidnightBlue, TextColorPreset.Frost)]
    [InlineData(BackgroundPreset.Ivory, BackgroundPreset.PearlStone, TextColorPreset.Ink)]
    public void Mode_owned_surfaces_do_not_change_between_background_presets_of_the_same_mode(
        BackgroundPreset firstPreset,
        BackgroundPreset secondPreset,
        TextColorPreset textPreset)
    {
        var first = new ResourceDictionary();
        var second = new ResourceDictionary();
        AppearanceSettingsService.ApplyToResources(first, new AppSettings
        {
            BackgroundPreset = firstPreset.ToString(),
            TextColorPreset = textPreset.ToString()
        });
        AppearanceSettingsService.ApplyToResources(second, new AppSettings
        {
            BackgroundPreset = secondPreset.ToString(),
            TextColorPreset = textPreset.ToString()
        });

        foreach (var key in new[]
                 {
                     AppearanceSettingsService.ProjectSetupCardBackgroundBrushKey,
                     AppearanceSettingsService.ScrollBarTrackBrushKey,
                     AppearanceSettingsService.ScrollBarThumbBrushKey,
                     AppearanceSettingsService.ScrollBarThumbHoverBrushKey,
                     AppearanceSettingsService.ScrollBarThumbDraggingBrushKey,
                     AppearanceSettingsService.ChoiceItemHighlightBackgroundBrushKey,
                     AppearanceSettingsService.ChoiceItemHighlightForegroundBrushKey
                 })
        {
            Assert.Equal(GetBrush(first, key).Color, GetBrush(second, key).Color);
        }
    }

    [Fact]
    public void Dark_scrollbar_surface_is_independent_of_compatible_text_preset()
    {
        var warm = new ResourceDictionary();
        var frost = new ResourceDictionary();
        AppearanceSettingsService.ApplyToResources(warm, new AppSettings { BackgroundPreset = BackgroundPreset.Black.ToString(), TextColorPreset = TextColorPreset.WarmSand.ToString() });
        AppearanceSettingsService.ApplyToResources(frost, new AppSettings { BackgroundPreset = BackgroundPreset.Black.ToString(), TextColorPreset = TextColorPreset.Frost.ToString() });

        foreach (var key in new[] { AppearanceSettingsService.ScrollBarTrackBrushKey, AppearanceSettingsService.ScrollBarThumbBrushKey, AppearanceSettingsService.ScrollBarThumbHoverBrushKey, AppearanceSettingsService.ScrollBarThumbDraggingBrushKey })
            Assert.Equal(GetBrush(warm, key).Color, GetBrush(frost, key).Color);
    }

    [Theory]
    [InlineData(BackgroundPreset.Black, TextColorPreset.WarmSand, TextColorPreset.WarmSand)]
    [InlineData(BackgroundPreset.Black, TextColorPreset.Frost, TextColorPreset.Frost)]
    [InlineData(BackgroundPreset.DeepPetrol, TextColorPreset.Sage, TextColorPreset.Sage)]
    [InlineData(BackgroundPreset.MidnightBlue, TextColorPreset.LavenderSmoke, TextColorPreset.LavenderSmoke)]
    [InlineData(BackgroundPreset.Ivory, TextColorPreset.Ink, TextColorPreset.Ink)]
    [InlineData(BackgroundPreset.PearlStone, TextColorPreset.Ink, TextColorPreset.Ink)]
    [InlineData(BackgroundPreset.Ivory, TextColorPreset.WarmSand, TextColorPreset.Ink)]
    [InlineData(BackgroundPreset.Ivory, TextColorPreset.Frost, TextColorPreset.Ink)]
    [InlineData(BackgroundPreset.Black, TextColorPreset.Ink, TextColorPreset.WarmSand)]
    [InlineData(BackgroundPreset.DeepPetrol, TextColorPreset.Ink, TextColorPreset.WarmSand)]
    public void Normalize_EnforcesTextCompatibility(
        BackgroundPreset backgroundPreset,
        TextColorPreset textPreset,
        TextColorPreset expectedTextPreset)
    {
        var settings = new AppSettings
        {
            BackgroundPreset = backgroundPreset.ToString(),
            TextColorPreset = textPreset.ToString()
        };

        AppearanceSettingsService.Normalize(settings);

        Assert.Equal(backgroundPreset.ToString(), settings.BackgroundPreset);
        Assert.Equal(expectedTextPreset.ToString(), settings.TextColorPreset);
    }

    [Fact]
    public void SettingsUiCompatibleTextListsFollowBackgroundMode()
    {
        Assert.Equal(
            [
                TextColorPreset.Frost,
                TextColorPreset.SageMist,
                TextColorPreset.WarmLinen,
                TextColorPreset.LavenderSmoke,
                TextColorPreset.WarmSand,
                TextColorPreset.Sage
            ],
            AppearanceSettingsService.GetCompatibleTextColorPresets(BackgroundPreset.Black.ToString()).Select(item => item.Preset).ToArray());

        Assert.Equal(
            [TextColorPreset.Ink],
            AppearanceSettingsService.GetCompatibleTextColorPresets(BackgroundPreset.Ivory.ToString()).Select(item => item.Preset).ToArray());
    }

    [Theory]
    [InlineData(BackgroundPreset.Black, TextColorPreset.WarmSand, "#000000", "#181E24", "#11161B", "#FFF7E8", "#E9DABD", "#D6C4A4", "#FFF7E8", "#63D4D8", "#2C3640", "#35414D", "#22303A", "#18262D", "#1B242A", "#2F3C44", "#6F8088", "#27313A", "#FFFFFF", "#B95C67", "#7A2630", "#96313D", "#E07A85", "#652029", "#C95F6B", "#FF7070", "#2D1414", "#6B3F46", "#D58B91", "#7B3D45", "#211719", "#2C1A1D", "#A3545D", "#E3A0A6", "#351C20", "#241416", "#D35B62", "#A5444A", "#F1C8CB")]
    [InlineData(BackgroundPreset.Ivory, TextColorPreset.Ink, "#F4F1EA", "#FFFCF7", "#E8E2D8", "#1C2528", "#4D5A5E", "#5A666A", "#F7FBFD", "#326B68", "#C9C1B5", "#8A8074", "#E1DBD1", "#D6CEC2", "#DED8CF", "#C6BEB2", "#746E67", "#D5CEC3", "#1A1A1A", "#B65F5A", "#D98C87", "#D17B76", "#A94E4A", "#C86C67", "#96413D", "#B6323C", "#F8E3E6", "#F4D7DB", "#7A2630", "#C8868E", "#FAEAEC", "#F4D7DB", "#A94855", "#5F1E26", "#EDC1C7", "#F8E3E6", "#B6323C", "#D68B93", "#7A2630")]
    public void ApplyToResources_UpdatesCompleteRuntimePalette(
        BackgroundPreset backgroundPreset,
        TextColorPreset textPreset,
        string appBackground,
        string panelBackground,
        string inputBackground,
        string mainText,
        string mutedText,
        string tertiaryText,
        string onAccentText,
        string accentForeground,
        string panelBorder,
        string inputBorder,
        string neutralHover,
        string neutralPressed,
        string disabledBackground,
        string disabledBorder,
        string disabledText,
        string gridLine,
        string dangerText,
        string dangerBorder,
        string dangerBackground,
        string dangerHoverBackground,
        string dangerHoverBorder,
        string dangerPressedBackground,
        string dangerPressedBorder,
        string errorText,
        string errorSurface,
        string duplicateWarningBackground,
        string quietDangerText,
        string quietDangerBorder,
        string quietDangerBackground,
        string quietDangerHoverBackground,
        string quietDangerHoverBorder,
        string quietDangerHoverText,
        string quietDangerPressedBackground,
        string warningProgressBackground,
        string warningProgressForeground,
        string warningProgressBorder,
        string warningProgressText)
    {
        var resources = new ResourceDictionary();
        var settings = new AppSettings
        {
            BackgroundPreset = backgroundPreset.ToString(),
            TextColorPreset = textPreset.ToString()
        };

        AppearanceSettingsService.ApplyToResources(resources, settings);

        AssertBrush(resources, AppearanceSettingsService.AppBackgroundBrushKey, appBackground);
        AssertBrush(resources, AppearanceSettingsService.PanelBackgroundBrushKey, panelBackground);
        AssertBrush(resources, AppearanceSettingsService.InputBackgroundBrushKey, inputBackground);
        AssertBrush(resources, AppearanceSettingsService.MainTextBrushKey, mainText);
        AssertBrush(resources, AppearanceSettingsService.MutedTextBrushKey, mutedText);
        AssertBrush(resources, AppearanceSettingsService.TertiaryTextBrushKey, tertiaryText);
        AssertBrush(resources, AppearanceSettingsService.OnAccentTextBrushKey, onAccentText);
        AssertBrush(resources, AppearanceSettingsService.AccentForegroundBrushKey, accentForeground);
        AssertBrush(resources, AppearanceSettingsService.PanelBorderBrushKey, panelBorder);
        AssertBrush(resources, AppearanceSettingsService.InputBorderBrushKey, inputBorder);
        AssertBrush(resources, AppearanceSettingsService.NeutralHoverBackgroundBrushKey, neutralHover);
        AssertBrush(resources, AppearanceSettingsService.NeutralPressedBackgroundBrushKey, neutralPressed);
        AssertBrush(resources, AppearanceSettingsService.DisabledButtonBackgroundBrushKey, disabledBackground);
        AssertBrush(resources, AppearanceSettingsService.DisabledButtonBorderBrushKey, disabledBorder);
        AssertBrush(resources, AppearanceSettingsService.DisabledButtonTextBrushKey, disabledText);
        var resolvedBackground = AppearanceSettingsService.GetBackgroundPreset(backgroundPreset.ToString());
        var resolvedText = AppearanceSettingsService.GetTextColorPreset(textPreset.ToString(), resolvedBackground.Mode);
        var brand = BrandAccentPalette.Get(resolvedBackground.Mode);
        var buttons = ButtonPalette.Get(resolvedBackground.Mode);
        Assert.Equal(buttons.PrimaryBackgroundColor, GetBrush(resources, AppearanceSettingsService.PrimaryButtonBackgroundBrushKey).Color);
        Assert.Equal(buttons.PrimaryHoverBackgroundColor, GetBrush(resources, AppearanceSettingsService.PrimaryButtonHoverBackgroundBrushKey).Color);
        Assert.Equal(buttons.PrimaryPressedBackgroundColor, GetBrush(resources, AppearanceSettingsService.PrimaryButtonPressedBackgroundBrushKey).Color);
        Assert.Equal(buttons.PrimaryForegroundColor, GetBrush(resources, AppearanceSettingsService.PrimaryButtonForegroundBrushKey).Color);
        Assert.Equal(buttons.SecondaryBackgroundColor, GetBrush(resources, AppearanceSettingsService.SecondaryButtonBackgroundBrushKey).Color);
        Assert.Equal(buttons.SecondaryHoverBackgroundColor, GetBrush(resources, AppearanceSettingsService.SecondaryButtonHoverBackgroundBrushKey).Color);
        Assert.Equal(buttons.SecondaryPressedBackgroundColor, GetBrush(resources, AppearanceSettingsService.SecondaryButtonPressedBackgroundBrushKey).Color);
        Assert.Equal(buttons.SecondaryForegroundColor, GetBrush(resources, AppearanceSettingsService.SecondaryButtonForegroundBrushKey).Color);
        Assert.Equal(brand.Accent, GetBrush(resources, AppearanceSettingsService.AccentBrushKey).Color);
        Assert.Equal(brand.AccentHover, GetBrush(resources, AppearanceSettingsService.AccentHoverBrushKey).Color);
        Assert.Equal(brand.AccentSoft, GetBrush(resources, AppearanceSettingsService.AccentSoftBrushKey).Color);
        Assert.Equal(brand.AccentForeground, GetBrush(resources, AppearanceSettingsService.AccentForegroundBrushKey).Color);
        Assert.Equal(brand.Selection, GetBrush(resources, AppearanceSettingsService.SelectionBrushKey).Color);
        var modePalette = AppearanceModePalette.Get(resolvedBackground.Mode);
        Assert.Equal(modePalette.ChoiceItemHighlightBackgroundColor, GetBrush(resources, AppearanceSettingsService.ChoiceItemHighlightBackgroundBrushKey).Color);
        Assert.Equal(modePalette.ChoiceItemHighlightForegroundColor, GetBrush(resources, AppearanceSettingsService.ChoiceItemHighlightForegroundBrushKey).Color);
        Assert.Equal(modePalette.ProjectSetupCardBackgroundColor, GetBrush(resources, AppearanceSettingsService.ProjectSetupCardBackgroundBrushKey).Color);
        var infoPalette = InfoPaletteCatalog.Resolve(resolvedBackground, resolvedText);
        Assert.Equal(resolvedBackground.InfoPopupBackgroundColor, GetBrush(resources, AppearanceSettingsService.InfoPopupBackgroundBrushKey).Color);
        Assert.Equal(infoPalette.PopupBorderColor, GetBrush(resources, AppearanceSettingsService.InfoPopupBorderBrushKey).Color);
        Assert.Equal(infoPalette.PopupForegroundColor, GetBrush(resources, AppearanceSettingsService.InfoPopupForegroundBrushKey).Color);
        Assert.Equal(infoPalette.OnPrimaryIconBorderColor, GetBrush(resources, AppearanceSettingsService.InfoOnPrimaryIconBorderBrushKey).Color);
        Assert.Equal(infoPalette.OnPrimaryIconGlyphColor, GetBrush(resources, AppearanceSettingsService.InfoOnPrimaryIconGlyphBrushKey).Color);
        Assert.Equal(infoPalette.OnPrimaryIconHoverBorderColor, GetBrush(resources, AppearanceSettingsService.InfoOnPrimaryIconHoverBorderBrushKey).Color);
        Assert.Equal(infoPalette.OnPrimaryIconHoverGlyphColor, GetBrush(resources, AppearanceSettingsService.InfoOnPrimaryIconHoverGlyphBrushKey).Color);
        Assert.Equal(
            VerticalAlignment.Bottom,
            resources[AppearanceSettingsService.CoverPickerHintVerticalAlignmentKey]);
        Assert.Equal(
            FontWeights.Normal,
            resources[AppearanceSettingsService.CoverPickerHintFontWeightKey]);
        Assert.Equal(modePalette.ScrollBarTrackColor, GetBrush(resources, AppearanceSettingsService.ScrollBarTrackBrushKey).Color);
        Assert.Equal(modePalette.ScrollBarThumbColor, GetBrush(resources, AppearanceSettingsService.ScrollBarThumbBrushKey).Color);
        Assert.Equal(modePalette.ScrollBarThumbHoverColor, GetBrush(resources, AppearanceSettingsService.ScrollBarThumbHoverBrushKey).Color);
        Assert.Equal(modePalette.ScrollBarThumbDraggingColor, GetBrush(resources, AppearanceSettingsService.ScrollBarThumbDraggingBrushKey).Color);
        AssertBrush(resources, AppearanceSettingsService.GridLineBrushKey, gridLine);
        AssertBrush(resources, AppearanceSettingsService.DangerTextBrushKey, dangerText);
        AssertBrush(resources, AppearanceSettingsService.DangerBorderBrushKey, dangerBorder);
        AssertBrush(resources, AppearanceSettingsService.DangerBackgroundBrushKey, dangerBackground);
        AssertBrush(resources, AppearanceSettingsService.DangerHoverBackgroundBrushKey, dangerHoverBackground);
        AssertBrush(resources, AppearanceSettingsService.DangerHoverBorderBrushKey, dangerHoverBorder);
        AssertBrush(resources, AppearanceSettingsService.DangerPressedBackgroundBrushKey, dangerPressedBackground);
        AssertBrush(resources, AppearanceSettingsService.DangerPressedBorderBrushKey, dangerPressedBorder);
        AssertBrush(resources, AppearanceSettingsService.ErrorTextBrushKey, errorText);
        AssertBrush(resources, AppearanceSettingsService.ErrorSurfaceBrushKey, errorSurface);
        AssertBrush(resources, AppearanceSettingsService.DuplicateWarningBackgroundBrushKey, duplicateWarningBackground);
        AssertBrush(resources, AppearanceSettingsService.QuietDangerTextBrushKey, quietDangerText);
        AssertBrush(resources, AppearanceSettingsService.QuietDangerBorderBrushKey, quietDangerBorder);
        AssertBrush(resources, AppearanceSettingsService.QuietDangerBackgroundBrushKey, quietDangerBackground);
        AssertBrush(resources, AppearanceSettingsService.QuietDangerHoverBackgroundBrushKey, quietDangerHoverBackground);
        AssertBrush(resources, AppearanceSettingsService.QuietDangerHoverBorderBrushKey, quietDangerHoverBorder);
        AssertBrush(resources, AppearanceSettingsService.QuietDangerHoverTextBrushKey, quietDangerHoverText);
        AssertBrush(resources, AppearanceSettingsService.QuietDangerPressedBackgroundBrushKey, quietDangerPressedBackground);
        AssertBrush(resources, AppearanceSettingsService.WarningProgressBackgroundBrushKey, warningProgressBackground);
        AssertBrush(resources, AppearanceSettingsService.WarningProgressForegroundBrushKey, warningProgressForeground);
        AssertBrush(resources, AppearanceSettingsService.WarningProgressBorderBrushKey, warningProgressBorder);
        AssertBrush(resources, AppearanceSettingsService.WarningProgressTextBrushKey, warningProgressText);
        Assert.IsType<BitmapImage>(resources[AppearanceSettingsService.CoverPlaceholderImageSourceKey]);
        Assert.IsType<BitmapImage>(resources[AppearanceSettingsService.BrandLogoImageSourceKey]);
        Assert.Equal(brand.Selection, GetBrush(resources, SystemColors.HighlightBrushKey).Color);
        Assert.Equal(brand.Selection, GetBrush(resources, SystemColors.InactiveSelectionHighlightBrushKey).Color);
        Assert.Equal(onAccentText, ToHex(GetBrush(resources, SystemColors.HighlightTextBrushKey).Color));
        Assert.Equal(onAccentText, ToHex(GetBrush(resources, SystemColors.InactiveSelectionHighlightTextBrushKey).Color));
    }

    [Theory]
    [InlineData(BackgroundPreset.Black, "#000000")]
    [InlineData(BackgroundPreset.DeepPetrol, "#061719")]
    [InlineData(BackgroundPreset.MidnightBlue, "#08111A")]
    [InlineData(BackgroundPreset.Ivory, "#FBF8F2")]
    [InlineData(BackgroundPreset.PearlStone, "#EEF2F4")]
    public void InfoPopupBackground_IsOwnedByBackgroundPreset(BackgroundPreset preset, string expected)
    {
        var background = BackgroundPresetCatalog.Get(preset.ToString());
        Assert.Equal(expected, ToHex(background.InfoPopupBackgroundColor));
    }

    [Theory]
    [InlineData(AppearanceMode.Dark, "#557B80", "#6A969B", "#3F5F63")]
    [InlineData(AppearanceMode.Light, "#8E9996", "#788481", "#626D6A")]
    public void ScrollbarPalette_UsesExactModeValuesAndTransparentTrack(
        AppearanceMode mode, string normal, string hover, string dragging)
    {
        var palette = AppearanceModePalette.Get(mode);
        Assert.Equal(Colors.Transparent, palette.ScrollBarTrackColor);
        Assert.Equal(normal, ToHex(palette.ScrollBarThumbColor));
        Assert.Equal(hover, ToHex(palette.ScrollBarThumbHoverColor));
        Assert.Equal(dragging, ToHex(palette.ScrollBarThumbDraggingColor));
    }

    [Fact]
    public void CoverPickerHintPresentation_KeepsSeparateLightAndDarkPaletteDefinitions()
    {
        Assert.Equal(AppearanceMode.Dark, CoverPickerHintPresentation.Dark.Mode);
        Assert.Equal(AppearanceMode.Light, CoverPickerHintPresentation.Light.Mode);
        Assert.NotSame(CoverPickerHintPresentation.Dark, CoverPickerHintPresentation.Light);
    }

    [Theory]
    [InlineData(100, 6, 10, 2)]
    [InlineData(250, 10, 11.25, 3.75)]
    [InlineData(500, 16, 14, 6)]
    public void CoverPickerHintPresentation_ClampsMetricsFromActualWidth(
        double width, double padding, double fontSize, double bottomPadding)
    {
        var metrics = CoverPickerHintPresentation.Calculate(width);
        Assert.Equal(padding, metrics.HorizontalPadding);
        Assert.Equal(fontSize, metrics.FontSize);
        Assert.Equal(bottomPadding, metrics.VerticalPadding);
        Assert.Equal(fontSize + (2 * bottomPadding), metrics.BannerHeight);
    }

    [Fact]
    public void ApplyToResources_DarkLightDarkRoundtripRestoresBrandPaletteAndKeepsOnAccentTextIndependent()
    {
        var resources = new ResourceDictionary();
        var settings = new AppSettings
        {
            BackgroundPreset = BackgroundPreset.Black.ToString(),
            TextColorPreset = TextColorPreset.WarmSand.ToString()
        };

        AppearanceSettingsService.ApplyToResources(resources, settings);
        Assert.Equal(BrandAccentPalette.Dark.Accent, GetBrush(resources, AppearanceSettingsService.AccentBrushKey).Color);
        Assert.Equal(TextColorPresetCatalog.Get(TextColorPreset.WarmSand.ToString(), AppearanceMode.Dark).OnAccentTextColor, GetBrush(resources, AppearanceSettingsService.OnAccentTextBrushKey).Color);

        settings.BackgroundPreset = BackgroundPreset.Ivory.ToString();
        settings.TextColorPreset = TextColorPreset.Ink.ToString();
        AppearanceSettingsService.ApplyToResources(resources, settings);
        Assert.Equal(ButtonPalette.Light.PrimaryBackgroundColor, GetBrush(resources, AppearanceSettingsService.PrimaryButtonBackgroundBrushKey).Color);
        Assert.Equal(BrandAccentPalette.Light.Selection, GetBrush(resources, SystemColors.HighlightBrushKey).Color);
        Assert.Equal(TextColorPresetCatalog.Get(TextColorPreset.Ink.ToString(), AppearanceMode.Light).OnAccentTextColor, GetBrush(resources, AppearanceSettingsService.OnAccentTextBrushKey).Color);

        settings.BackgroundPreset = BackgroundPreset.Black.ToString();
        settings.TextColorPreset = TextColorPreset.WarmSand.ToString();
        AppearanceSettingsService.ApplyToResources(resources, settings);
        Assert.Equal(BrandAccentPalette.Dark.Accent, GetBrush(resources, AppearanceSettingsService.AccentBrushKey).Color);
        Assert.Equal(ButtonPalette.Dark.PrimaryForegroundColor, GetBrush(resources, AppearanceSettingsService.PrimaryButtonForegroundBrushKey).Color);
    }

    [Fact]
    public void ApplyToResources_ReplacesFrozenBrushInsteadOfMutatingIt()
    {
        var frozenBrush = new SolidColorBrush(Colors.Red);
        frozenBrush.Freeze();
        var resources = new ResourceDictionary
        {
            [AppearanceSettingsService.AccentBrushKey] = frozenBrush
        };
        var settings = new AppSettings
        {
            BackgroundPreset = BackgroundPreset.Black.ToString()
        };

        AppearanceSettingsService.ApplyToResources(resources, settings);

        var updatedBrush = GetBrush(resources, AppearanceSettingsService.AccentBrushKey);
        Assert.NotSame(frozenBrush, updatedBrush);
        Assert.False(updatedBrush.IsFrozen);
        Assert.Equal("#126F73", ToHex(updatedBrush.Color));
    }

    [Fact]
    public void Cover_hint_presentation_resources_follow_every_background_preset()
    {
        foreach (var background in AppearanceSettingsService.BackgroundPresets)
        {
            var resources = new ResourceDictionary();
            var compatibleText = AppearanceSettingsService
                .GetCompatibleTextColorPresets(background.Preset.ToString())[0];

            AppearanceSettingsService.ApplyToResources(
                resources,
                new AppSettings
                {
                    BackgroundPreset = background.Preset.ToString(),
                    TextColorPreset = compatibleText.Preset.ToString()
                });

            Assert.Equal(
                VerticalAlignment.Bottom,
                resources[AppearanceSettingsService.CoverPickerHintVerticalAlignmentKey]);
            Assert.Equal(
                FontWeights.Normal,
                resources[AppearanceSettingsService.CoverPickerHintFontWeightKey]);
        }
    }

    [Theory]
    [InlineData(BackgroundPreset.Ivory)]
    [InlineData(BackgroundPreset.PearlStone)]
    public void Light_danger_buttons_use_the_brighter_dark_text_palette(BackgroundPreset backgroundPreset)
    {
        var resources = new ResourceDictionary();
        AppearanceSettingsService.ApplyToResources(
            resources,
            new AppSettings
            {
                BackgroundPreset = backgroundPreset.ToString(),
                TextColorPreset = TextColorPreset.Ink.ToString()
            });

        AssertBrush(resources, AppearanceSettingsService.DangerTextBrushKey, "#1A1A1A");
        AssertBrush(resources, AppearanceSettingsService.DangerBackgroundBrushKey, "#D98C87");
        AssertBrush(resources, AppearanceSettingsService.DangerBorderBrushKey, "#B65F5A");
        AssertBrush(resources, AppearanceSettingsService.DangerHoverBackgroundBrushKey, "#D17B76");
        AssertBrush(resources, AppearanceSettingsService.DangerHoverBorderBrushKey, "#A94E4A");
        AssertBrush(resources, AppearanceSettingsService.DangerPressedBackgroundBrushKey, "#C86C67");
        AssertBrush(resources, AppearanceSettingsService.DangerPressedBorderBrushKey, "#96413D");
    }

    [Theory]
    [InlineData(BackgroundPreset.Black, TextColorPreset.WarmSand, "#211719", "#D58B91", "#7B3D45", "#2C1A1D", "#351C20", "#E3A0A6", "#A3545D")]
    [InlineData(BackgroundPreset.Ivory, TextColorPreset.Ink, "#C75B57", "#000000", "#9E4542", "#D06B66", "#A74643", "#FFFFFF", "#873633")]
    [InlineData(BackgroundPreset.PearlStone, TextColorPreset.Ink, "#C75B57", "#000000", "#9E4542", "#D06B66", "#A74643", "#FFFFFF", "#873633")]
    public void Blocking_warning_rows_use_their_own_mode_aware_surface_family(
        BackgroundPreset backgroundPreset,
        TextColorPreset textPreset,
        string background,
        string foreground,
        string border,
        string hoverBackground,
        string selectedBackground,
        string selectedForeground,
        string selectedBorder)
    {
        var resources = new ResourceDictionary();
        AppearanceSettingsService.ApplyToResources(
            resources,
            new AppSettings
            {
                BackgroundPreset = backgroundPreset.ToString(),
                TextColorPreset = textPreset.ToString()
            });

        AssertBrush(resources, AppearanceSettingsService.BlockingWarningRowBackgroundBrushKey, background);
        AssertBrush(resources, AppearanceSettingsService.BlockingWarningRowForegroundBrushKey, foreground);
        AssertBrush(resources, AppearanceSettingsService.BlockingWarningRowBorderBrushKey, border);
        AssertBrush(resources, AppearanceSettingsService.BlockingWarningRowHoverBackgroundBrushKey, hoverBackground);
        AssertBrush(resources, AppearanceSettingsService.BlockingWarningRowSelectedBackgroundBrushKey, selectedBackground);
        AssertBrush(resources, AppearanceSettingsService.BlockingWarningRowSelectedForegroundBrushKey, selectedForeground);
        AssertBrush(resources, AppearanceSettingsService.BlockingWarningRowSelectedBorderBrushKey, selectedBorder);
    }

    [Fact]
    public void CopyAppearanceResources_PropagatesCompleteRuntimePaletteToDialogResources()
    {
        var ownerResources = new ResourceDictionary();
        var settings = new AppSettings
        {
            BackgroundPreset = BackgroundPreset.Ivory.ToString(),
            TextColorPreset = TextColorPreset.Ink.ToString()
        };
        AppearanceSettingsService.ApplyToResources(ownerResources, settings);
        var dialogResources = new ResourceDictionary();

        AppearanceSettingsService.CopyAppearanceResources(ownerResources, dialogResources);

        foreach (var key in AppearanceResourceKeys.RuntimeBrushKeys)
        {
            Assert.Equal(
                ToHex(GetBrush(ownerResources, key).Color),
                ToHex(GetBrush(dialogResources, key).Color));
        }

        foreach (var key in AppearanceResourceKeys.RuntimePresentationKeys)
            Assert.Equal(ownerResources[key], dialogResources[key]);
    }

    [Fact]
    public void CompatibleTextPresetsMeetRoleBasedContrastTargets()
    {
        foreach (var background in AppearanceSettingsService.BackgroundPresets)
        {
            foreach (var text in TextColorPresetCatalog.GetCompatiblePresets(background.Mode))
            {
                var surfaces = new[]
                {
                    background.BackgroundColor,
                    background.PanelBackgroundColor,
                    background.InputBackgroundColor
                };

                foreach (var surface in surfaces)
                {
                    AssertContrastAtLeast(text.PrimaryTextColor, surface, 4.5, text.DisplayName, background.DisplayName, "primary");
                    AssertContrastAtLeast(text.SecondaryTextColor, surface, 4.5, text.DisplayName, background.DisplayName, "secondary");
                    AssertContrastAtLeast(text.TertiaryTextColor, surface, 3.0, text.DisplayName, background.DisplayName, "tertiary");
                }

                AssertContrastAtLeast(
                    text.OnAccentTextColor,
                    BrandAccentPalette.Get(background.Mode).Accent,
                    4.5,
                    text.DisplayName,
                    background.DisplayName,
                    "on-accent");
            }
        }
    }

    [Fact]
    public void BrandAccentForegroundMeetsSupportingContrastTargetForModeSurfaces()
    {
        foreach (var background in AppearanceSettingsService.BackgroundPresets)
        {
            var accentForeground = BrandAccentPalette.Get(background.Mode).AccentForeground;
            foreach (var surface in new[]
                     {
                         background.BackgroundColor,
                         background.PanelBackgroundColor,
                         background.InputBackgroundColor
                     })
            {
                AssertContrastAtLeast(
                    accentForeground,
                    surface,
                    3.0,
                    "AccentForeground",
                    background.DisplayName,
                    "accent/supporting");
            }
        }
    }

    private static void AssertContrastAtLeast(
        Color foreground,
        Color background,
        double minimum,
        string foregroundRole,
        string backgroundPreset,
        string role)
    {
        var actual = ContrastRatio(foreground, background);
        Assert.True(
            actual >= minimum,
            $"{foregroundRole} {role} {ToHex(foreground)} on {backgroundPreset} {ToHex(background)}: {actual:F2} < {minimum:F1}");
    }

    private static void AssertBrush(ResourceDictionary resources, object key, string expected) =>
        Assert.Equal(expected, ToHex(GetBrush(resources, key).Color));

    private static SolidColorBrush GetBrush(ResourceDictionary resources, object key) =>
        Assert.IsType<SolidColorBrush>(resources[key]);

    private static string ToHex(Color color) =>
        $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    private static double ContrastRatio(Color foreground, Color background)
    {
        var foregroundLuminance = RelativeLuminance(foreground);
        var backgroundLuminance = RelativeLuminance(background);
        var lighter = Math.Max(foregroundLuminance, backgroundLuminance);
        var darker = Math.Min(foregroundLuminance, backgroundLuminance);
        return (lighter + 0.05) / (darker + 0.05);
    }

    private static double RelativeLuminance(Color color) =>
        0.2126 * Linear(color.R) + 0.7152 * Linear(color.G) + 0.0722 * Linear(color.B);

    private static double Linear(byte channel)
    {
        var value = channel / 255.0;
        return value <= 0.03928
            ? value / 12.92
            : Math.Pow((value + 0.055) / 1.055, 2.4);
    }
}
