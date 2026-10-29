using BookStitch.Models;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace BookStitch.Services;

public static class AppearanceSettingsService
{
    public const string MetadataBannerImageSourceKey = "MetadataBannerImageSource";
    private static readonly Dictionary<string, BitmapSource> ArtworkImages = new();

    public const string DefaultBackgroundPreset = BackgroundPresetCatalog.DefaultPreset;
    public const string DefaultTextColorPreset = TextColorPresetCatalog.DefaultPreset;
    public const string AppBackgroundBrushKey = AppearanceResourceKeys.AppBackgroundBrush;
    public const string PanelBackgroundBrushKey = AppearanceResourceKeys.PanelBackgroundBrush;
    public const string ProjectSetupCardBackgroundBrushKey = AppearanceResourceKeys.ProjectSetupCardBackgroundBrush;
    public const string InputBackgroundBrushKey = AppearanceResourceKeys.InputBackgroundBrush;
    public const string MainTextBrushKey = AppearanceResourceKeys.MainTextBrush;
    public const string MutedTextBrushKey = AppearanceResourceKeys.MutedTextBrush;
    public const string TertiaryTextBrushKey = AppearanceResourceKeys.TertiaryTextBrush;
    public const string OnAccentTextBrushKey = AppearanceResourceKeys.OnAccentTextBrush;
    public const string PrimaryButtonBackgroundBrushKey = AppearanceResourceKeys.PrimaryButtonBackgroundBrush;
    public const string PrimaryButtonHoverBackgroundBrushKey = AppearanceResourceKeys.PrimaryButtonHoverBackgroundBrush;
    public const string PrimaryButtonPressedBackgroundBrushKey = AppearanceResourceKeys.PrimaryButtonPressedBackgroundBrush;
    public const string PrimaryButtonForegroundBrushKey = AppearanceResourceKeys.PrimaryButtonForegroundBrush;
    public const string PrimaryButtonBorderBrushKey = AppearanceResourceKeys.PrimaryButtonBorderBrush;
    public const string PrimaryButtonHoverBorderBrushKey = AppearanceResourceKeys.PrimaryButtonHoverBorderBrush;
    public const string PrimaryButtonPressedBorderBrushKey = AppearanceResourceKeys.PrimaryButtonPressedBorderBrush;
    public const string PrimaryMutedButtonBackgroundBrushKey = AppearanceResourceKeys.PrimaryMutedButtonBackgroundBrush;
    public const string PrimaryMutedButtonHoverBackgroundBrushKey = AppearanceResourceKeys.PrimaryMutedButtonHoverBackgroundBrush;
    public const string PrimaryMutedButtonPressedBackgroundBrushKey = AppearanceResourceKeys.PrimaryMutedButtonPressedBackgroundBrush;
    public const string PrimaryMutedButtonBorderBrushKey = AppearanceResourceKeys.PrimaryMutedButtonBorderBrush;
    public const string PrimaryMutedButtonHoverBorderBrushKey = AppearanceResourceKeys.PrimaryMutedButtonHoverBorderBrush;
    public const string PrimaryMutedButtonPressedBorderBrushKey = AppearanceResourceKeys.PrimaryMutedButtonPressedBorderBrush;
    public const string SecondaryButtonBackgroundBrushKey = AppearanceResourceKeys.SecondaryButtonBackgroundBrush;
    public const string SecondaryButtonHoverBackgroundBrushKey = AppearanceResourceKeys.SecondaryButtonHoverBackgroundBrush;
    public const string SecondaryButtonPressedBackgroundBrushKey = AppearanceResourceKeys.SecondaryButtonPressedBackgroundBrush;
    public const string SecondaryButtonForegroundBrushKey = AppearanceResourceKeys.SecondaryButtonForegroundBrush;
    public const string SecondaryButtonBorderBrushKey = AppearanceResourceKeys.SecondaryButtonBorderBrush;
    public const string SecondaryButtonHoverBorderBrushKey = AppearanceResourceKeys.SecondaryButtonHoverBorderBrush;
    public const string AccentBrushKey = AppearanceResourceKeys.AccentBrush;
    public const string AccentHoverBrushKey = AppearanceResourceKeys.AccentHoverBrush;
    public const string AccentSoftBrushKey = AppearanceResourceKeys.AccentSoftBrush;
    public const string AccentForegroundBrushKey = AppearanceResourceKeys.AccentForegroundBrush;
    public const string SelectionBrushKey = AppearanceResourceKeys.SelectionBrush;
    public const string ChoiceItemHighlightBackgroundBrushKey = AppearanceResourceKeys.ChoiceItemHighlightBackgroundBrush;
    public const string ChoiceItemHighlightForegroundBrushKey = AppearanceResourceKeys.ChoiceItemHighlightForegroundBrush;
    public const string PanelBorderBrushKey = AppearanceResourceKeys.PanelBorderBrush;
    public const string InputBorderBrushKey = AppearanceResourceKeys.InputBorderBrush;
    public const string NeutralHoverBackgroundBrushKey = AppearanceResourceKeys.NeutralHoverBackgroundBrush;
    public const string NeutralPressedBackgroundBrushKey = AppearanceResourceKeys.NeutralPressedBackgroundBrush;
    public const string DisabledButtonBackgroundBrushKey = AppearanceResourceKeys.DisabledButtonBackgroundBrush;
    public const string DisabledButtonBorderBrushKey = AppearanceResourceKeys.DisabledButtonBorderBrush;
    public const string DisabledButtonTextBrushKey = AppearanceResourceKeys.DisabledButtonTextBrush;
    public const string ScrollBarTrackBrushKey = AppearanceResourceKeys.ScrollBarTrackBrush;
    public const string ScrollBarThumbBrushKey = AppearanceResourceKeys.ScrollBarThumbBrush;
    public const string ScrollBarThumbHoverBrushKey = AppearanceResourceKeys.ScrollBarThumbHoverBrush;
    public const string ScrollBarThumbDraggingBrushKey = AppearanceResourceKeys.ScrollBarThumbDraggingBrush;
    public const string GridLineBrushKey = AppearanceResourceKeys.GridLineBrush;
    public const string DangerTextBrushKey = AppearanceResourceKeys.DangerTextBrush;
    public const string DangerBorderBrushKey = AppearanceResourceKeys.DangerBorderBrush;
    public const string DangerBackgroundBrushKey = AppearanceResourceKeys.DangerBackgroundBrush;
    public const string DangerHoverBackgroundBrushKey = AppearanceResourceKeys.DangerHoverBackgroundBrush;
    public const string DangerHoverBorderBrushKey = AppearanceResourceKeys.DangerHoverBorderBrush;
    public const string DangerPressedBackgroundBrushKey = AppearanceResourceKeys.DangerPressedBackgroundBrush;
    public const string DangerPressedBorderBrushKey = AppearanceResourceKeys.DangerPressedBorderBrush;
    public const string ErrorTextBrushKey = AppearanceResourceKeys.ErrorTextBrush;
    public const string ErrorSurfaceBrushKey = AppearanceResourceKeys.ErrorSurfaceBrush;
    public const string DuplicateWarningBackgroundBrushKey = AppearanceResourceKeys.DuplicateWarningBackgroundBrush;
    public const string QuietDangerTextBrushKey = AppearanceResourceKeys.QuietDangerTextBrush;
    public const string QuietDangerBorderBrushKey = AppearanceResourceKeys.QuietDangerBorderBrush;
    public const string QuietDangerBackgroundBrushKey = AppearanceResourceKeys.QuietDangerBackgroundBrush;
    public const string QuietDangerHoverBackgroundBrushKey = AppearanceResourceKeys.QuietDangerHoverBackgroundBrush;
    public const string QuietDangerHoverBorderBrushKey = AppearanceResourceKeys.QuietDangerHoverBorderBrush;
    public const string QuietDangerHoverTextBrushKey = AppearanceResourceKeys.QuietDangerHoverTextBrush;
    public const string QuietDangerPressedBackgroundBrushKey = AppearanceResourceKeys.QuietDangerPressedBackgroundBrush;
    public const string BlockingWarningRowBackgroundBrushKey = AppearanceResourceKeys.BlockingWarningRowBackgroundBrush;
    public const string BlockingWarningRowForegroundBrushKey = AppearanceResourceKeys.BlockingWarningRowForegroundBrush;
    public const string BlockingWarningRowBorderBrushKey = AppearanceResourceKeys.BlockingWarningRowBorderBrush;
    public const string BlockingWarningRowHoverBackgroundBrushKey = AppearanceResourceKeys.BlockingWarningRowHoverBackgroundBrush;
    public const string BlockingWarningRowSelectedBackgroundBrushKey = AppearanceResourceKeys.BlockingWarningRowSelectedBackgroundBrush;
    public const string BlockingWarningRowSelectedForegroundBrushKey = AppearanceResourceKeys.BlockingWarningRowSelectedForegroundBrush;
    public const string BlockingWarningRowSelectedBorderBrushKey = AppearanceResourceKeys.BlockingWarningRowSelectedBorderBrush;
    public const string WarningProgressBackgroundBrushKey = AppearanceResourceKeys.WarningProgressBackgroundBrush;
    public const string WarningProgressForegroundBrushKey = AppearanceResourceKeys.WarningProgressForegroundBrush;
    public const string WarningProgressBorderBrushKey = AppearanceResourceKeys.WarningProgressBorderBrush;
    public const string WarningProgressTextBrushKey = AppearanceResourceKeys.WarningProgressTextBrush;
    public const string InfoIconBorderBrushKey = AppearanceResourceKeys.InfoIconBorderBrush;
    public const string InfoIconGlyphBrushKey = AppearanceResourceKeys.InfoIconGlyphBrush;
    public const string InfoIconHoverBorderBrushKey = AppearanceResourceKeys.InfoIconHoverBorderBrush;
    public const string InfoIconHoverGlyphBrushKey = AppearanceResourceKeys.InfoIconHoverGlyphBrush;
    public const string InfoOnPrimaryIconBorderBrushKey = AppearanceResourceKeys.InfoOnPrimaryIconBorderBrush;
    public const string InfoOnPrimaryIconGlyphBrushKey = AppearanceResourceKeys.InfoOnPrimaryIconGlyphBrush;
    public const string InfoOnPrimaryIconHoverBorderBrushKey = AppearanceResourceKeys.InfoOnPrimaryIconHoverBorderBrush;
    public const string InfoOnPrimaryIconHoverGlyphBrushKey = AppearanceResourceKeys.InfoOnPrimaryIconHoverGlyphBrush;
    public const string InfoPopupBackgroundBrushKey = AppearanceResourceKeys.InfoPopupBackgroundBrush;
    public const string InfoPopupBorderBrushKey = AppearanceResourceKeys.InfoPopupBorderBrush;
    public const string InfoPopupForegroundBrushKey = AppearanceResourceKeys.InfoPopupForegroundBrush;
    public const string CoverPlaceholderImageSourceKey = AppearanceResourceKeys.CoverPlaceholderImageSource;
    public const string BrandLogoImageSourceKey = AppearanceResourceKeys.BrandLogoImageSource;
    public const string CoverPickerHintVerticalAlignmentKey = AppearanceResourceKeys.CoverPickerHintVerticalAlignment;
    public const string CoverPickerHintFontWeightKey = AppearanceResourceKeys.CoverPickerHintFontWeight;

    public static IReadOnlyList<BackgroundPresetDefinition> BackgroundPresets => BackgroundPresetCatalog.All;
    public static IReadOnlyList<TextColorPresetDefinition> TextColorPresets => TextColorPresetCatalog.All;

    public static void Normalize(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        settings.BackgroundPreset = NormalizeBackgroundPreset(settings.BackgroundPreset).ToString();
        var backgroundPreset = GetBackgroundPreset(settings.BackgroundPreset);
        settings.TextColorPreset = TextColorPresetCatalog
            .Normalize(settings.TextColorPreset, backgroundPreset.Mode)
            .ToString();
    }

    public static BackgroundPreset NormalizeBackgroundPreset(string? preset) => BackgroundPresetCatalog.Normalize(preset);

    public static BackgroundPresetDefinition GetBackgroundPreset(string? preset) => BackgroundPresetCatalog.Get(preset);

    public static TextColorPresetDefinition GetTextColorPreset(string? preset, AppearanceMode mode) =>
        TextColorPresetCatalog.Get(preset, mode);

    public static IReadOnlyList<TextColorPresetDefinition> GetCompatibleTextColorPresets(string? backgroundPreset) =>
        TextColorPresetCatalog.GetCompatiblePresets(GetBackgroundPreset(backgroundPreset).Mode);

    public static AppSettings GetEffectiveSettings(AppSettings settings) => DeveloperToolsCommands.AppearancePreview.Resolve(settings);

    public static AppearanceMode GetEffectiveMode(AppSettings settings) => GetBackgroundPreset(GetEffectiveSettings(settings).BackgroundPreset).Mode;

    public static void NormalizeRegular(AppSettings settings, bool developerEnabled)
    {
        settings.BackgroundPreset = BackgroundPresetCatalog.NormalizeRegular(settings.BackgroundPreset, developerEnabled).ToString();
        Normalize(settings);
    }

    public static void ApplyEffectiveToResources(ResourceDictionary resources, AppSettings settings) =>
        ApplyToResources(resources, GetEffectiveSettings(settings));

    public static void ApplyToResources(ResourceDictionary resources, AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(settings);

        var backgroundPreset = GetBackgroundPreset(settings.BackgroundPreset);
        var mode = backgroundPreset.Mode;
        var textPreset = GetTextColorPreset(settings.TextColorPreset, mode);
        var modePalette = AppearanceModePalette.Get(mode);
        var brandPalette = BrandAccentPalette.Get(mode);
        resources["TrackOrderVectorMode"] = DeveloperToolsCommands.AppearancePreview.TrackOrderVectors;
        resources["TrackOrderIconTheme"] = mode.ToString();
        var buttonPalette = ButtonPalette.Get(mode);

        ApplyBrush(resources, AppBackgroundBrushKey, backgroundPreset.BackgroundColor);
        ApplyBrush(resources, PanelBackgroundBrushKey, backgroundPreset.PanelBackgroundColor);
        ApplyBrush(resources, ProjectSetupCardBackgroundBrushKey, modePalette.ProjectSetupCardBackgroundColor);
        ApplyBrush(resources, InputBackgroundBrushKey, backgroundPreset.InputBackgroundColor);
        ApplyBrush(resources, MainTextBrushKey, textPreset.PrimaryTextColor);
        ApplyBrush(resources, MutedTextBrushKey, textPreset.SecondaryTextColor);
        ApplyBrush(resources, TertiaryTextBrushKey, textPreset.TertiaryTextColor);
        ApplyBrush(resources, OnAccentTextBrushKey, textPreset.OnAccentTextColor);
        ApplyBrush(resources, PrimaryButtonBackgroundBrushKey, buttonPalette.PrimaryBackgroundColor);
        ApplyBrush(resources, PrimaryButtonHoverBackgroundBrushKey, buttonPalette.PrimaryHoverBackgroundColor);
        ApplyBrush(resources, PrimaryButtonPressedBackgroundBrushKey, buttonPalette.PrimaryPressedBackgroundColor);
        ApplyBrush(resources, PrimaryButtonForegroundBrushKey, buttonPalette.PrimaryForegroundColor);
        ApplyBrush(resources, PrimaryButtonBorderBrushKey, buttonPalette.PrimaryBorderColor);
        ApplyBrush(resources, PrimaryButtonHoverBorderBrushKey, buttonPalette.PrimaryHoverBorderColor);
        ApplyBrush(resources, PrimaryButtonPressedBorderBrushKey, buttonPalette.PrimaryPressedBorderColor);
        ApplyBrush(resources, PrimaryMutedButtonBackgroundBrushKey, buttonPalette.PrimaryMutedBackgroundColor);
        ApplyBrush(resources, PrimaryMutedButtonHoverBackgroundBrushKey, buttonPalette.PrimaryMutedHoverBackgroundColor);
        ApplyBrush(resources, PrimaryMutedButtonPressedBackgroundBrushKey, buttonPalette.PrimaryMutedPressedBackgroundColor);
        ApplyBrush(resources, PrimaryMutedButtonBorderBrushKey, buttonPalette.PrimaryMutedBorderColor);
        ApplyBrush(resources, PrimaryMutedButtonHoverBorderBrushKey, buttonPalette.PrimaryMutedHoverBorderColor);
        ApplyBrush(resources, PrimaryMutedButtonPressedBorderBrushKey, buttonPalette.PrimaryMutedPressedBorderColor);
        ApplyBrush(resources, SecondaryButtonBackgroundBrushKey, buttonPalette.SecondaryBackgroundColor);
        ApplyBrush(resources, SecondaryButtonHoverBackgroundBrushKey, buttonPalette.SecondaryHoverBackgroundColor);
        ApplyBrush(resources, SecondaryButtonPressedBackgroundBrushKey, buttonPalette.SecondaryPressedBackgroundColor);
        ApplyBrush(resources, SecondaryButtonForegroundBrushKey, buttonPalette.SecondaryForegroundColor);
        ApplyBrush(resources, SecondaryButtonBorderBrushKey, buttonPalette.SecondaryBorderColor);
        ApplyBrush(resources, SecondaryButtonHoverBorderBrushKey, buttonPalette.SecondaryHoverBorderColor);
        ApplyBrush(resources, AccentBrushKey, brandPalette.Accent);
        ApplyBrush(resources, AccentHoverBrushKey, brandPalette.AccentHover);
        ApplyBrush(resources, AccentSoftBrushKey, brandPalette.AccentSoft);
        ApplyBrush(resources, AccentForegroundBrushKey, brandPalette.AccentForeground);
        ApplyBrush(resources, SelectionBrushKey, brandPalette.Selection);
        ApplyBrush(resources, ChoiceItemHighlightBackgroundBrushKey, modePalette.ChoiceItemHighlightBackgroundColor);
        ApplyBrush(resources, ChoiceItemHighlightForegroundBrushKey, modePalette.ChoiceItemHighlightForegroundColor);
        ApplyBrush(resources, SystemColors.HighlightBrushKey, brandPalette.Selection);
        ApplyBrush(resources, SystemColors.InactiveSelectionHighlightBrushKey, brandPalette.Selection);
        ApplyBrush(resources, SystemColors.HighlightTextBrushKey, textPreset.OnAccentTextColor);
        ApplyBrush(resources, SystemColors.InactiveSelectionHighlightTextBrushKey, textPreset.OnAccentTextColor);

        ApplyBrush(resources, PanelBorderBrushKey, modePalette.PanelBorderColor);
        ApplyBrush(resources, InputBorderBrushKey, modePalette.InputBorderColor);
        ApplyBrush(resources, NeutralHoverBackgroundBrushKey, modePalette.NeutralHoverBackgroundColor);
        ApplyBrush(resources, NeutralPressedBackgroundBrushKey, modePalette.NeutralPressedBackgroundColor);
        ApplyBrush(resources, DisabledButtonBackgroundBrushKey, modePalette.DisabledBackgroundColor);
        ApplyBrush(resources, DisabledButtonBorderBrushKey, modePalette.DisabledBorderColor);
        ApplyBrush(resources, DisabledButtonTextBrushKey, modePalette.DisabledTextColor);
        ApplyBrush(resources, ScrollBarTrackBrushKey, modePalette.ScrollBarTrackColor);
        ApplyBrush(resources, ScrollBarThumbBrushKey, modePalette.ScrollBarThumbColor);
        ApplyBrush(resources, ScrollBarThumbHoverBrushKey, modePalette.ScrollBarThumbHoverColor);
        ApplyBrush(resources, ScrollBarThumbDraggingBrushKey, modePalette.ScrollBarThumbDraggingColor);
        ApplyBrush(resources, GridLineBrushKey, modePalette.GridLineColor);
        ApplyBrush(resources, DangerTextBrushKey, modePalette.DangerTextColor);
        ApplyBrush(resources, DangerBorderBrushKey, modePalette.DangerBorderColor);
        ApplyBrush(resources, DangerBackgroundBrushKey, modePalette.DangerBackgroundColor);
        ApplyBrush(resources, DangerHoverBackgroundBrushKey, modePalette.DangerHoverBackgroundColor);
        ApplyBrush(resources, DangerHoverBorderBrushKey, modePalette.DangerHoverBorderColor);
        ApplyBrush(resources, DangerPressedBackgroundBrushKey, modePalette.DangerPressedBackgroundColor);
        ApplyBrush(resources, DangerPressedBorderBrushKey, modePalette.DangerPressedBorderColor);
        ApplyBrush(resources, ErrorTextBrushKey, modePalette.ErrorTextColor);
        ApplyBrush(resources, ErrorSurfaceBrushKey, modePalette.ErrorSurfaceColor);
        ApplyBrush(resources, DuplicateWarningBackgroundBrushKey, modePalette.DuplicateWarningBackgroundColor);
        ApplyBrush(resources, QuietDangerTextBrushKey, modePalette.QuietDangerTextColor);
        ApplyBrush(resources, QuietDangerBorderBrushKey, modePalette.QuietDangerBorderColor);
        ApplyBrush(resources, QuietDangerBackgroundBrushKey, modePalette.QuietDangerBackgroundColor);
        ApplyBrush(resources, QuietDangerHoverBackgroundBrushKey, modePalette.QuietDangerHoverBackgroundColor);
        ApplyBrush(resources, QuietDangerHoverBorderBrushKey, modePalette.QuietDangerHoverBorderColor);
        ApplyBrush(resources, QuietDangerHoverTextBrushKey, modePalette.QuietDangerHoverTextColor);
        ApplyBrush(resources, QuietDangerPressedBackgroundBrushKey, modePalette.QuietDangerPressedBackgroundColor);
        ApplyBrush(resources, BlockingWarningRowBackgroundBrushKey, modePalette.BlockingWarningRowBackgroundColor);
        ApplyBrush(resources, BlockingWarningRowForegroundBrushKey, modePalette.BlockingWarningRowForegroundColor);
        ApplyBrush(resources, BlockingWarningRowBorderBrushKey, modePalette.BlockingWarningRowBorderColor);
        ApplyBrush(resources, BlockingWarningRowHoverBackgroundBrushKey, modePalette.BlockingWarningRowHoverBackgroundColor);
        ApplyBrush(resources, BlockingWarningRowSelectedBackgroundBrushKey, modePalette.BlockingWarningRowSelectedBackgroundColor);
        ApplyBrush(resources, BlockingWarningRowSelectedForegroundBrushKey, modePalette.BlockingWarningRowSelectedForegroundColor);
        ApplyBrush(resources, BlockingWarningRowSelectedBorderBrushKey, modePalette.BlockingWarningRowSelectedBorderColor);
        ApplyBrush(resources, WarningProgressBackgroundBrushKey, modePalette.WarningProgressBackgroundColor);
        ApplyBrush(resources, WarningProgressForegroundBrushKey, modePalette.WarningProgressForegroundColor);
        ApplyBrush(resources, WarningProgressBorderBrushKey, modePalette.WarningProgressBorderColor);
        ApplyBrush(resources, WarningProgressTextBrushKey, modePalette.WarningProgressTextColor);

        var infoPalette = InfoPaletteCatalog.Resolve(backgroundPreset, textPreset);
        ApplyBrush(resources, InfoIconBorderBrushKey, infoPalette.IconBorderColor);
        ApplyBrush(resources, InfoIconGlyphBrushKey, infoPalette.IconGlyphColor);
        ApplyBrush(resources, InfoIconHoverBorderBrushKey, infoPalette.IconHoverBorderColor);
        ApplyBrush(resources, InfoIconHoverGlyphBrushKey, infoPalette.IconHoverGlyphColor);
        ApplyBrush(resources, InfoOnPrimaryIconBorderBrushKey, infoPalette.OnPrimaryIconBorderColor);
        ApplyBrush(resources, InfoOnPrimaryIconGlyphBrushKey, infoPalette.OnPrimaryIconGlyphColor);
        ApplyBrush(resources, InfoOnPrimaryIconHoverBorderBrushKey, infoPalette.OnPrimaryIconHoverBorderColor);
        ApplyBrush(resources, InfoOnPrimaryIconHoverGlyphBrushKey, infoPalette.OnPrimaryIconHoverGlyphColor);
        ApplyBrush(resources, InfoPopupBackgroundBrushKey, backgroundPreset.InfoPopupBackgroundColor);
        ApplyBrush(resources, InfoPopupBorderBrushKey, infoPalette.PopupBorderColor);
        ApplyBrush(resources, InfoPopupForegroundBrushKey, infoPalette.PopupForegroundColor);
        var coverHintPalette = CoverPickerHintPresentation.GetPalette(mode);
        resources[CoverPickerHintVerticalAlignmentKey] = coverHintPalette.VerticalAlignment;
        resources[CoverPickerHintFontWeightKey] = coverHintPalette.FontWeight;

        ApplyCachedArtwork(resources, CoverPlaceholderImageSourceKey, AppearanceAssetRole.CoverPlaceholder, mode);
        ApplyImageSource(resources, BrandLogoImageSourceKey, AppearanceAssetRole.BrandLogo, mode);
        ApplyCachedArtwork(resources, MetadataBannerImageSourceKey, AppearanceAssetRole.MetadataBanner, mode);
        ApplyCachedArtwork(resources, AppearanceResourceKeys.HeaderBrandImageSource, AppearanceAssetRole.HeaderBrand, mode);
        var useHeaderText = DeveloperToolsCommands.AppearancePreview.GetImage(AppearanceAssetRole.HeaderBrand)?.IsText == true;
        resources[AppearanceResourceKeys.HeaderBrandTextVisibility] = useHeaderText ? Visibility.Visible : Visibility.Collapsed;
        resources[AppearanceResourceKeys.HeaderBrandImageVisibility] = useHeaderText ? Visibility.Collapsed : Visibility.Visible;
        var infoOverride = DeveloperToolsCommands.AppearancePreview.GetImage(AppearanceAssetRole.InfoSquare);
        resources["InfoSquareImageSource"] = LoadArtwork(AppearanceAssetRole.InfoSquare, infoOverride?.RelativePath) ??
            LoadArtwork(AppearanceAssetRole.InfoSquare, "/BookStitch;component/Assets/Faq/BookMachin Press.png");
        ApplyBrush(resources, AppearanceResourceKeys.HeaderBrandSeparatorBrush,
            mode == AppearanceMode.Dark ? Color.FromRgb(0x99, 0x84, 0x57) : Color.FromRgb(0x8A, 0x79, 0x56));
    }

    public static void CopyAppearanceResources(ResourceDictionary source, ResourceDictionary target)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);

        foreach (var key in AppearanceResourceKeys.RuntimeBrushKeys)
            CopyBrush(source, target, key);

        foreach (var key in AppearanceResourceKeys.RuntimePresentationKeys)
        {
            if (TryFindResource(source, key, out var value))
                target[key] = value;
        }
    }


    private static void ApplyBrush(ResourceDictionary resources, object key, Color color)
    {
        var brush = FindBrush(resources, key);
        if (brush is not null)
        {
            if (!brush.IsFrozen)
            {
                brush.Color = color;
                return;
            }

            resources[key] = new SolidColorBrush(color);
            return;
        }

        resources[key] = new SolidColorBrush(color);
    }

    private static void CopyBrush(ResourceDictionary source, ResourceDictionary target, object key)
    {
        var brush = FindBrush(source, key);
        if (brush is null)
            return;

        ApplyBrush(target, key, brush.Color);
    }

    private static SolidColorBrush? FindBrush(ResourceDictionary resources, object key)
    {
        if (resources.Contains(key) && resources[key] is SolidColorBrush brush)
            return brush;

        foreach (var dictionary in resources.MergedDictionaries)
        {
            var nested = FindBrush(dictionary, key);
            if (nested is not null)
                return nested;
        }

        return null;
    }

    private static bool TryFindResource(ResourceDictionary resources, object key, out object? value)
    {
        if (resources.Contains(key))
        {
            value = resources[key];
            return true;
        }

        foreach (var dictionary in resources.MergedDictionaries)
        {
            if (TryFindResource(dictionary, key, out value))
                return true;
        }

        value = null;
        return false;
    }

    private static void ApplyCachedArtwork(ResourceDictionary resources, string key, AppearanceAssetRole role, AppearanceMode mode)
    {
        var standard = AppearanceAssetCatalog.GetOptional(role, mode);
        var asset = DeveloperToolsCommands.AppearancePreview.GetImage(role);
        var image = LoadArtwork(role, asset?.RelativePath ?? standard?.RelativePath);
        if (image is null && asset is not null) image = LoadArtwork(role, standard?.RelativePath);
        resources[key] = image;
    }

    public static string? GetCoverPlaceholderOverride()
    {
        var asset = DeveloperToolsCommands.AppearancePreview.GetImage(AppearanceAssetRole.CoverPlaceholder);
        return asset is not null && LoadArtwork(AppearanceAssetRole.CoverPlaceholder, asset.RelativePath) is not null ?
            asset.RelativePath : null;
    }

    public static BitmapSource? LoadArtwork(AppearanceAssetRole role, string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath)) return null;
        var cacheKey = $"{role}:{relativePath}";
        if (ArtworkImages.TryGetValue(cacheKey, out var cached)) return cached;
        try
        {
            using var stream = Application.GetResourceStream(new Uri(relativePath, UriKind.Relative))?.Stream;
            if (stream is null) return null;
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();
            BitmapSource image = role == AppearanceAssetRole.HeaderBrand ? TrimTransparentHeaderMargins(bitmap) : bitmap;
            ArtworkImages[cacheKey] = image;
            return image;
        }
        catch (Exception error) when (error is System.IO.IOException or NotSupportedException or ArgumentException or InvalidOperationException or FormatException)
        {
            System.Diagnostics.Trace.TraceWarning("Appearance artwork unavailable: {0}", error.Message);
            return null;
        }
    }

    // Keep the original PNG intact. Only the displayed source rectangle excludes transparent padding.
    private static BitmapSource TrimTransparentHeaderMargins(BitmapSource source)
    {
        var pixels = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var stride = checked(source.PixelWidth * 4);
        var buffer = new byte[checked(stride * source.PixelHeight)];
        pixels.CopyPixels(buffer, stride, 0);
        var left = source.PixelWidth;
        var top = source.PixelHeight;
        var right = -1;
        var bottom = -1;
        for (var y = 0; y < source.PixelHeight; y++)
        for (var x = 0; x < source.PixelWidth; x++)
        {
            if (buffer[y * stride + x * 4 + 3] == 0) continue;
            left = Math.Min(left, x);
            top = Math.Min(top, y);
            right = Math.Max(right, x);
            bottom = Math.Max(bottom, y);
        }
        if (right < left || bottom < top) return source;
        const int padding = 4;
        left = Math.Max(0, left - padding);
        top = Math.Max(0, top - padding);
        right = Math.Min(source.PixelWidth - 1, right + padding);
        bottom = Math.Min(source.PixelHeight - 1, bottom + padding);
        var cropped = new CroppedBitmap(source, new Int32Rect(left, top, right - left + 1, bottom - top + 1));
        cropped.Freeze();
        return cropped;
    }

    private static void ApplyImageSource(ResourceDictionary resources, string key, AppearanceAssetRole role, AppearanceMode mode)
    {
        var asset = AppearanceAssetCatalog.Get(role, mode);
        var resourceUri = new Uri(asset.RelativePath, UriKind.Relative);
        var resourceInfo = Application.GetResourceStream(resourceUri)
            ?? throw new InvalidOperationException($"Appearance asset resource was not found: {asset.RelativePath}");
        using var stream = resourceInfo.Stream;
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        resources[key] = image;
    }
}
