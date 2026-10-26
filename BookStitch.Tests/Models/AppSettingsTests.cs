using BookStitch.Models;
using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Models;

public class AppSettingsTests
{
    [Fact]
    public void Fresh_settings_show_desktop_output_and_saved_user_path_survives_loading()
    {
        Assert.Equal(OutputFolderFallbackService.GetDesktopFallbackFolder(),new AppSettings().OutputFolder);
        var path=System.IO.Path.GetTempPath();
        var loaded=System.Text.Json.JsonSerializer.Deserialize<AppSettings>(
            System.Text.Json.JsonSerializer.Serialize(new AppSettings {OutputFolder=path}))!;
        Assert.Equal(path,loaded.OutputFolder);
        Assert.Equal(OutputFolderFallbackService.GetDesktopFallbackFolder(),
            System.Text.Json.JsonSerializer.Deserialize<AppSettings>("{}")!.OutputFolder);
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("en-US")]
    [InlineData("fr-FR")]
    public void Warning_ids_are_language_independent_and_unknown_warnings_can_be_reset(string language)
    {
        var settings = new AppSettings { UiLanguage = language };
        WarningSuppressionService.SetSuppressed(settings, WarningSuppressionService.DiscCount, true);
        WarningSuppressionService.SetSuppressed(settings, WarningSuppressionService.DiscCount, true);
        WarningSuppressionService.SetSuppressed(settings, "future.warning", true);
        var reloaded = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(
            System.Text.Json.JsonSerializer.Serialize(settings))!;
        Assert.Equal(2, reloaded.SuppressedWarnings.Count);
        Assert.True(WarningSuppressionService.IsSuppressed(reloaded, WarningSuppressionService.DiscCount));
        WarningSuppressionService.SetSuppressed(reloaded, WarningSuppressionService.DiscCount, false);
        Assert.False(WarningSuppressionService.IsSuppressed(reloaded, WarningSuppressionService.DiscCount));
        Assert.True(WarningSuppressionService.IsSuppressed(reloaded, "future.warning"));
        WarningSuppressionService.ResetAll(reloaded);
        Assert.Empty(reloaded.SuppressedWarnings);
        Assert.Equal(language, reloaded.UiLanguage);
    }

    [Fact]
    public void Defaults_MatchVersionTwoProductDecisions()
    {
        var settings = new AppSettings();

        Assert.Equal(180, settings.ProjectRetentionDays);
        Assert.Equal(ProjectRetentionModes.Days, settings.ProjectRetentionMode);
        Assert.Equal(".m4a", settings.DefaultOutputExtension);
        Assert.True(settings.OverwriteFinalOutputWithoutAsking);
        Assert.True(settings.MergeAutomaticallyAfterConversion);
        Assert.True(settings.CompatibilityDescriptionEnabled);
        Assert.Equal("Flac", settings.AudioDiscWorkingFormat);
        Assert.Equal(OutputFolderLayoutService.DefaultLayout, settings.OutputFolderLayout);
        Assert.Equal(OutputFolderPrimaryActionService.DefaultAction, settings.OutputFolderPrimaryAction);
        Assert.Equal("{Autor} - {Titel}", settings.DefaultFileNameTemplate);
        Assert.Equal("Standard", settings.FocusProfile);
        Assert.Equal("Bass", settings.SoundLibrary);
        Assert.Equal(1000, settings.CoverWarningThresholdPixels);
        var chapters = new ChapterSettings();
        Assert.Equal("Auto", chapters.Mode);
        Assert.Equal("ZeroPadded", chapters.NumberingStyle);
    }

    [Fact]
    public void Missing_preferences_use_new_defaults_but_explicit_choices_are_preserved()
    {
        var settings = System.Text.Json.JsonSerializer.Deserialize<AppSettings>("{}")!;
        settings.ChapterSettings = ChapterSettingsService.Normalize(settings.ChapterSettings, legacyUseLeadingZeros: true);
        Assert.Equal(UiLanguageService.System, settings.UiLanguage);
        Assert.Equal("AAC Stereo 128 kbps", settings.SelectedExportPreset);
        Assert.True(settings.MergeAutomaticallyAfterConversion);
        Assert.True(MetadataFieldBindingPreferencesService.GetIsEnabled(settings.MetadataPreferences,
            ProjectMetadataFieldBindingCatalog.TitleToAlbum));
        Assert.Equal("Dash", settings.ChapterSettings!.Separator1);
        Assert.Equal("Dash", settings.ChapterSettings.Separator2);
        Assert.Equal("Dash", ChapterSettingsService.NormalizeSeparator("invalid"));
        Assert.Equal(128, ExportPreset.Parse(null).BitrateKbps);
        Assert.Equal(128, ExportPreset.Parse("invalid").BitrateKbps);
        Assert.Equal(192, ExportPreset.Parse("AAC Stereo 192 kbps").BitrateKbps);

        settings.MergeAutomaticallyAfterConversion = false;
        settings.SelectedExportPreset = "AAC Stereo 192 kbps";
        settings.ChapterSettings.Separator1 = "Space";
        settings.MetadataPreferences.MetadataFieldBindings[ProjectMetadataFieldBindingCatalog.TitleToAlbumId] = false;
        var restored = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(System.Text.Json.JsonSerializer.Serialize(settings))!;
        restored.ChapterSettings = ChapterSettingsService.Normalize(restored.ChapterSettings, legacyUseLeadingZeros: true);
        Assert.False(restored.MergeAutomaticallyAfterConversion);
        Assert.Equal("AAC Stereo 192 kbps", restored.SelectedExportPreset);
        Assert.Equal("Space", restored.ChapterSettings!.Separator1);
        Assert.False(MetadataFieldBindingPreferencesService.GetIsEnabled(restored.MetadataPreferences,
            ProjectMetadataFieldBindingCatalog.TitleToAlbum));
    }

    [Fact]
    public void AnimationTimings_UseProductDefaultsAndOptionalDebugOverrides()
    {
        Assert.Equal(550, AnimationTimingSettingsService.GetMetadataPanelMilliseconds(null));
        Assert.Equal(220, AnimationTimingSettingsService.GetProjectSetupPageMilliseconds(null));

        var debugSettings = new DebugSettings
        {
            MetadataPanelAnimationMillisecondsOverride = 875,
            ProjectSetupPageAnimationMillisecondsOverride = 140
        };

        Assert.Equal(875, AnimationTimingSettingsService.GetMetadataPanelMilliseconds(debugSettings));
        Assert.Equal(140, AnimationTimingSettingsService.GetProjectSetupPageMilliseconds(debugSettings));
        Assert.Null(AnimationTimingSettingsService.ToMetadataPanelOverride(550));
        Assert.Null(AnimationTimingSettingsService.ToProjectSetupPageOverride(220));
    }
}
