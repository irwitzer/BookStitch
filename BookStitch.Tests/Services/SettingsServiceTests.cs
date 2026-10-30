using BookStitch.Models;
using BookStitch.Services;
using BookStitch.Tests.TestHelpers;
using System.IO;
using System.Text.Json;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class SettingsServiceTests
{
    [Theory]
    [InlineData("de-DE")]
    [InlineData("en-US")]
    [InlineData("es-ES")]
    [InlineData("fr-FR")]
    [InlineData("cs-CZ")]
    public void Rejected_instance_reads_persisted_language_without_writing_or_migrating_settings(string language)
    {
        using var folder = new TemporaryFolder();
        var service = new SettingsService(Path.Combine(folder.Path, "app"), Path.Combine(folder.Path, "default"));
        var actualRoot = Path.Combine(folder.Path, "moved-projects");
        service.Save(new AppSettings { WorkingFolder = actualRoot, UiLanguage = language });
        var paths = Directory.GetFiles(folder.Path, "*", SearchOption.AllDirectories);
        var before = paths.ToDictionary(path => path, File.ReadAllBytes);
        var fresh = new SettingsService(Path.Combine(folder.Path, "app"), Path.Combine(folder.Path, "default"));
        Assert.Equal(language, fresh.ReadUiLanguageAtStartup());
        var culture = UiLanguageService.Resolve(fresh.ReadUiLanguageAtStartup(), System.Globalization.CultureInfo.GetCultureInfo("de-DE"));
        var message = LocalizationText.Get("Startup.AlreadyRunning.Message", culture);
        Assert.NotEqual("Startup.AlreadyRunning.Message", message);
        if (language != "de-DE") Assert.NotEqual(LocalizationText.Get("Startup.AlreadyRunning.Message", System.Globalization.CultureInfo.GetCultureInfo("de-DE")), message);
        Assert.Equal(paths.OrderBy(x => x), Directory.GetFiles(folder.Path, "*", SearchOption.AllDirectories).OrderBy(x => x));
        Assert.All(before, pair => Assert.Equal(pair.Value, File.ReadAllBytes(pair.Key)));
    }

    [Fact]
    public void Rejected_instance_does_not_archive_corrupt_location_or_create_defaults()
    {
        using var folder = new TemporaryFolder();
        var service = new SettingsService(folder.Path, Path.Combine(folder.Path, "projects"));
        File.WriteAllText(service.LocationFilePath, "broken");
        Assert.Equal(UiLanguageService.System, service.ReadUiLanguageAtStartup());
        Assert.Equal("broken", File.ReadAllText(service.LocationFilePath));
        Assert.Single(Directory.GetFiles(folder.Path, "*", SearchOption.AllDirectories));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t\r\n ")]
    [InlineData("unavailable-output-folder")]
    [InlineData("  unavailable-output-folder  ")]
    public void Loading_settings_normalizes_only_blank_output_paths(string? outputFolder)
    {
        using var folder = new TemporaryFolder();
        var service = new SettingsService(Path.Combine(folder.Path,"app"),Path.Combine(folder.Path,"projects"));
        Directory.CreateDirectory(service.SettingsFolder);
        File.WriteAllText(service.SettingsFilePath,JsonSerializer.Serialize(new AppSettings {OutputFolder=outputFolder}));
        var loaded = service.Load();
        Assert.Equal(string.IsNullOrWhiteSpace(outputFolder)
            ? OutputFolderFallbackService.GetDesktopFallbackFolder() : outputFolder,loaded.OutputFolder);
    }

    [Fact]
    public void Loading_settings_preserves_an_existing_user_output_folder()
    {
        using var folder = new TemporaryFolder();
        var service = new SettingsService(Path.Combine(folder.Path,"app"),Path.Combine(folder.Path,"projects"));
        Directory.CreateDirectory(service.SettingsFolder);
        File.WriteAllText(service.SettingsFilePath,JsonSerializer.Serialize(new AppSettings {OutputFolder=folder.Path}));
        Assert.Equal(folder.Path,service.Load().OutputFolder);
    }

    [Theory]
    [InlineData(BookStitchEnvironment.Production, true)]
    [InlineData(BookStitchEnvironment.Production, false)]
    [InlineData(BookStitchEnvironment.Development, true)]
    [InlineData(BookStitchEnvironment.Development, false)]
    [InlineData(BookStitchEnvironment.XTester, true)]
    [InlineData(BookStitchEnvironment.XTester, false)]
    public void Developer_access_override_and_discovery_survive_full_settings_service_reload(BookStitchEnvironment environment, bool visible)
    {
        using var folder = new TemporaryFolder();
        var service = new SettingsService(Path.Combine(folder.Path, "app"), Path.Combine(folder.Path, "projects"));
        var settings = new AppSettings { DeveloperToolsVisible = visible, DeveloperToolsSelectorUnlocked = true };
        service.Save(settings);
        var state = new DeveloperToolsAccessState();
        state.Initialize(service.Load(), environment, () => true);
        Assert.Equal(visible, state.IsVisible);
        Assert.True(state.IsSelectorAvailable);
    }

    [Fact]
    public void Warning_suppression_survives_restart_and_reset_preserves_unrelated_preferences()
    {
        using var folder = new TemporaryFolder();
        var service = new SettingsService(Path.Combine(folder.Path, "AppData"), Path.Combine(folder.Path, "Projects"));
        var settings = new AppSettings { UiLanguage = UiLanguageIds.English, SelectedExportPreset = "AAC Stereo 96 kbps" };
        settings.MetadataPreferences.WarnBeforeDiscardingConceptDrafts = false;
        WarningSuppressionService.SetSuppressed(settings, WarningSuppressionService.DiscCount, true);
        WarningSuppressionService.SetSuppressed(settings, "future.warning", true);
        WarningSuppressionService.SetSuppressed(settings, WarningSuppressionService.ProjectFolder, true);
        WarningSuppressionService.SetSuppressed(settings, WarningSuppressionService.DiscCount, true);
        service.Save(settings);
        var loaded = service.Load();
        Assert.Equal(3, loaded.SuppressedWarnings.Count);
        Assert.True(WarningSuppressionService.IsSuppressed(loaded, WarningSuppressionService.ProjectFolder));
        Assert.True(WarningSuppressionService.IsSuppressed(loaded, WarningSuppressionService.DiscCount));
        Assert.True(WarningSuppressionService.IsSuppressed(loaded, "future.warning"));
        WarningSuppressionService.ResetAll(loaded);
        service.Save(loaded);
        var reset = service.Load();
        Assert.Empty(reset.SuppressedWarnings);
        Assert.False(WarningSuppressionService.IsSuppressed(reset, WarningSuppressionService.ProjectFolder));
        Assert.True(reset.MetadataPreferences.WarnBeforeDiscardingConceptDrafts);
        Assert.Equal(UiLanguageIds.English, reset.UiLanguage);
        Assert.Equal("AAC Stereo 96 kbps", reset.SelectedExportPreset);
    }

    [Theory]
    [InlineData("Custom - {Titel} - {Autor}", "Custom - {Titel} - {Autor}")]
    [InlineData("My Custom - {Titel}", "My Custom - {Titel}")]
    [InlineData("{Titel} - custom", "{Titel} - custom")]
    public void Loading_current_settings_preserves_user_templates(string template, string expected)
    {
        using var folder = new TemporaryFolder();
        var service = new SettingsService(Path.Combine(folder.Path, "AppData"), Path.Combine(folder.Path, "Projects"));
        Directory.CreateDirectory(service.SettingsFolder);
        var settings = new AppSettings { DefaultFileNameTemplate = template, UiLanguage = UiLanguageIds.French, DefaultGenre = "Custom" };
        File.WriteAllText(service.SettingsFilePath, JsonSerializer.Serialize(settings));
        Assert.Equal(expected, service.Load().DefaultFileNameTemplate);
        var stored = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(service.SettingsFilePath))!;
        Assert.Equal(expected, stored.DefaultFileNameTemplate);
        Assert.Equal(UiLanguageIds.French, stored.UiLanguage);
        Assert.Equal("Custom", stored.DefaultGenre);
    }

    [Fact]
    public void SaveAndLoad_LockedLanguageAndWorkTypeSurviveRestartAndInitializeFreshProjects()
    {
        using var folder = new TemporaryFolder();
        var appData = Path.Combine(folder.Path, "AppData");
        var projects = Path.Combine(folder.Path, "Projects");
        var settings = new AppSettings();
        settings.MetadataPreferences.PreferredLanguages = ["de", "en"];
        settings.MetadataPreferences.LanguagePreferenceLocked = true;
        settings.MetadataPreferences.PreferredAudioWorkType = AudioWorkType.AudioDrama;
        settings.MetadataPreferences.AudioWorkTypePreferenceLocked = true;
        new SettingsService(appData, projects).Save(settings);

        var preferences = new SettingsService(appData, projects).Load().MetadataPreferences;
        Assert.True(preferences.LanguagePreferenceLocked);
        Assert.True(preferences.AudioWorkTypePreferenceLocked);
        Assert.Equal(AudioWorkType.AudioDrama, preferences.PreferredAudioWorkType);
        Assert.Equal(new[] { "de", "en" }, preferences.PreferredLanguages);
        foreach (var project in new[] { new ProjectMetadata(), new ProjectMetadata() })
        {
            new ProjectMetadataInitializationService().Apply(project, [], ProjectMetadataInitializationState.InitialSetOpen,
                new(PreferredLanguages: preferences.PreferredLanguages, IsLanguagePreferenceFixed: preferences.LanguagePreferenceLocked,
                    PreferredAudioWorkType: preferences.PreferredAudioWorkType, IsAudioWorkTypePreferenceFixed: preferences.AudioWorkTypePreferenceLocked));
            Assert.Equal(new[] { "de", "en" }, project.GetValue<IReadOnlyList<string>>(ProjectMetadataField.Languages).Value);
            Assert.Equal(AudioWorkType.AudioDrama, project.GetValue<AudioWorkType?>(ProjectMetadataField.AudioWorkType).Value);
        }
    }

    [Fact]
    public void SaveAndLoad_PreservesExplicitUiLanguageAndNormalizesUnknownValue()
    {
        using var folder = new TemporaryFolder();
        var service = new SettingsService(
            Path.Combine(folder.Path, "AppData"), Path.Combine(folder.Path, "Projects"));
        var settings = new AppSettings { UiLanguage = UiLanguageService.German };

        service.Save(settings);
        Assert.Equal(UiLanguageService.German, service.Load().UiLanguage);

        settings.UiLanguage = "unknown";
        service.Save(settings);
        Assert.Equal(UiLanguageService.System, service.Load().UiLanguage);

        var json = File.ReadAllText(service.SettingsFilePath);
        File.WriteAllText(service.SettingsFilePath,
            json.Replace("\"UiLanguage\": \"System\"", "\"UiLanguage\": 42", StringComparison.Ordinal));
        var recovered = service.Load();
        Assert.Equal(UiLanguageService.System, recovered.UiLanguage);
        Assert.Equal(settings.DefaultGenre, recovered.DefaultGenre);
    }

    [Fact]
    public void Save_WritesGlobalSettingsAndLocationAtomically()
    {
        using var folder = new TemporaryFolder();
        var appData = Path.Combine(folder.Path, "AppData", "BookStitch");
        var projectRoot = Path.Combine(folder.Path, "Music", "BookStitchProjects");
        var service = new SettingsService(appData, projectRoot);
        var settings = new AppSettings
        {
            WorkingFolder = projectRoot,
            DefaultGenre = "Testgenre"
        };

        service.Save(settings);

        Assert.True(File.Exists(Path.Combine(projectRoot, "software-settings", "global-settings.json")));
        Assert.True(File.Exists(Path.Combine(appData, "settings-location.json")));
        Assert.Empty(Directory.EnumerateFiles(projectRoot, "*.tmp", SearchOption.AllDirectories));
        Assert.Empty(Directory.EnumerateFiles(appData, "*.tmp", SearchOption.AllDirectories));
        using (var document = JsonDocument.Parse(File.ReadAllText(service.SettingsFilePath)))
        {
            Assert.Equal(
                AppSettingsVersions.CurrentSchemaVersion,
                document.RootElement.GetProperty("schemaVersion").GetInt32());
        }

        var loaded = service.Load();
        Assert.Equal("Testgenre", loaded.DefaultGenre);
        Assert.Equal(Path.GetFullPath(projectRoot), loaded.WorkingFolder);
    }

    [Fact]
    public void SaveAndLoad_PreservesExplicitNeverRetentionModeWithoutChangingNumericDays()
    {
        using var folder = new TemporaryFolder();
        var service = new SettingsService(Path.Combine(folder.Path, "AppData", "BookStitch"), Path.Combine(folder.Path, "Projects"));
        var settings = new AppSettings
        {
            WorkingFolder = Path.Combine(folder.Path, "Projects"),
            ProjectRetentionDays = 180,
            ProjectRetentionMode = ProjectRetentionModes.Never
        };

        service.Save(settings);
        var loaded = service.Load();

        Assert.Equal(ProjectRetentionModes.Never, loaded.ProjectRetentionMode);
        Assert.Equal(180, loaded.ProjectRetentionDays);
    }

    [Fact]
    public void SaveAndLoad_PreservesRepresentativeGlobalSettingsDocument()
    {
        using var folder = new TemporaryFolder();
        var appData = Path.Combine(folder.Path, "AppData", "BookStitch");
        var projectRoot = Path.Combine(folder.Path, "Projects");
        var service = new SettingsService(appData, projectRoot);

        service.Save(CreateRepresentativeSettings(projectRoot));

        var loaded = service.Load();

        AssertRepresentativeSettings(loaded, projectRoot);
    }

    [Fact]
    public void Load_MissingGlobalSettingsReturnsNormalizedDefaultsWithoutCreatingSettingsDocument()
    {
        using var folder = new TemporaryFolder();
        var appData = Path.Combine(folder.Path, "AppData", "BookStitch");
        var projectRoot = Path.Combine(folder.Path, "Projects");
        var service = new SettingsService(appData, projectRoot);

        var loaded = service.Load();

        Assert.Equal(Path.GetFullPath(projectRoot), loaded.WorkingFolder);
        Assert.Equal("Audiobook", loaded.DefaultGenre);
        Assert.Equal("Auto", Assert.IsType<ChapterSettings>(loaded.ChapterSettings).Mode);
        Assert.NotNull(loaded.MetadataPreferences);
        Assert.False(File.Exists(service.SettingsFilePath));
        Assert.True(File.Exists(service.LocationFilePath));
    }

    [Fact]
    public void Load_SettingsWithoutCompatibilityDescriptionSettingUseEnabledDefault()
    {
        using var folder = new TemporaryFolder();
        var appData = Path.Combine(folder.Path, "AppData", "BookStitch");
        var projectRoot = Path.Combine(folder.Path, "Projects");
        var service = new SettingsService(appData, projectRoot);
        Directory.CreateDirectory(service.SettingsFolder);
        File.WriteAllText(
            service.SettingsFilePath,
            $$"""
            {
              "WorkingFolder": "{{JsonEncodedText(projectRoot)}}",
              "DefaultGenre": "Legacy"
            }
            """);

        var loaded = service.Load();

        Assert.True(loaded.CompatibilityDescriptionEnabled);
    }

    [Fact]
    public void Load_IgnoresLegacySubtleScrollbarSettingProperty()
    {
        using var folder = new TemporaryFolder();
        var appData = Path.Combine(folder.Path, "AppData", "BookStitch");
        var projectRoot = Path.Combine(folder.Path, "Projects");
        var service = new SettingsService(appData, projectRoot);
        Directory.CreateDirectory(service.SettingsFolder);
        File.WriteAllText(
            service.SettingsFilePath,
            JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["WorkingFolder"] = projectRoot,
                ["BackgroundPreset"] = BackgroundPreset.Black.ToString(),
                ["TextColorPreset"] = TextColorPreset.Frost.ToString(),
                ["UseSubtleScrollBars"] = true,
                ["LastSettingsTab"] = "Appearance"
            }));

        var loaded = service.Load();

        Assert.Equal(BackgroundPreset.Black.ToString(), loaded.BackgroundPreset);
        Assert.Equal(TextColorPreset.Frost.ToString(), loaded.TextColorPreset);
        Assert.Equal("Appearance", loaded.LastSettingsTab);
    }

    [Fact]
    public void Load_MigratesLegacyCoolClearEmbeddedChapterTextStyleToWarmSand()
    {
        using var folder = new TemporaryFolder();
        var appData = Path.Combine(folder.Path, "AppData", "BookStitch");
        var projectRoot = Path.Combine(folder.Path, "Projects");
        var service = new SettingsService(appData, projectRoot);
        Directory.CreateDirectory(service.SettingsFolder);
        File.WriteAllText(
            service.SettingsFilePath,
            $$"""
            {
              "WorkingFolder": "{{JsonEncodedText(projectRoot)}}",
              "EmbeddedChapterTextStyle": "CoolClear"
            }
            """);

        var loaded = service.Load();

        Assert.Equal(TextColorPreset.WarmSand.ToString(), loaded.TextColorPreset);
    }

    [Fact]
    public void Load_MigratesLegacyEmbeddedChapterTextStyleToTextColorPreset()
    {
        using var folder = new TemporaryFolder();
        var appData = Path.Combine(folder.Path, "AppData", "BookStitch");
        var projectRoot = Path.Combine(folder.Path, "Projects");
        var service = new SettingsService(appData, projectRoot);
        Directory.CreateDirectory(service.SettingsFolder);
        File.WriteAllText(
            service.SettingsFilePath,
            $$"""
            {
              "WorkingFolder": "{{JsonEncodedText(projectRoot)}}",
              "EmbeddedChapterTextStyle": "WarmSand"
            }
            """);

        var loaded = service.Load();

        Assert.Equal(TextColorPreset.WarmSand.ToString(), loaded.TextColorPreset);
    }

    [Theory]
    [InlineData("BlackNew", "Black")]
    [InlineData("GraphiteNew", "DeepPetrol")]
    [InlineData("DeepPetrolNew", "DeepPetrol")]
    [InlineData("MidnightBlueNew", "MidnightBlue")]
    public void Load_MigratesLegacyNewBackgroundPresetNames(string legacyPreset, string expectedPreset)
    {
        using var folder = new TemporaryFolder();
        var appData = Path.Combine(folder.Path, "AppData", "BookStitch");
        var projectRoot = Path.Combine(folder.Path, "Projects");
        var service = new SettingsService(appData, projectRoot);
        Directory.CreateDirectory(service.SettingsFolder);
        File.WriteAllText(
            service.SettingsFilePath,
            $$"""
            {
              "WorkingFolder": "{{JsonEncodedText(projectRoot)}}",
              "BackgroundPreset": "{{legacyPreset}}"
            }
            """);

        var loaded = service.Load();

        Assert.Equal(expectedPreset, loaded.BackgroundPreset);
    }

    [Theory]
    [InlineData("FrostNew", "Frost")]
    [InlineData("SageMistNew", "SageMist")]
    [InlineData("WarmLinenNew", "WarmLinen")]
    [InlineData("LavenderSmokeNew", "LavenderSmoke")]
    [InlineData("SoftNeutral", "WarmSand")]
    [InlineData("HighContrast", "WarmSand")]
    public void Load_MigratesLegacyTextColorPresetNames(string legacyPreset, string expectedPreset)
    {
        using var folder = new TemporaryFolder();
        var appData = Path.Combine(folder.Path, "AppData", "BookStitch");
        var projectRoot = Path.Combine(folder.Path, "Projects");
        var service = new SettingsService(appData, projectRoot);
        Directory.CreateDirectory(service.SettingsFolder);
        File.WriteAllText(
            service.SettingsFilePath,
            $$"""
            {
              "WorkingFolder": "{{JsonEncodedText(projectRoot)}}",
              "TextColorPreset": "{{legacyPreset}}"
            }
            """);

        var loaded = service.Load();

        Assert.Equal(expectedPreset, loaded.TextColorPreset);
    }

    [Fact]
    public void Load_UsesLightTextFallbackForIvory()
    {
        using var folder = new TemporaryFolder();
        var appData = Path.Combine(folder.Path, "AppData", "BookStitch");
        var projectRoot = Path.Combine(folder.Path, "Projects");
        var service = new SettingsService(appData, projectRoot);
        Directory.CreateDirectory(service.SettingsFolder);
        File.WriteAllText(
            service.SettingsFilePath,
            $$"""
            {
              "WorkingFolder": "{{JsonEncodedText(projectRoot)}}",
              "BackgroundPreset": "Ivory",
              "TextColorPreset": "WarmSand"
            }
            """);

        var loaded = service.Load();

        Assert.Equal(BackgroundPreset.Ivory.ToString(), loaded.BackgroundPreset);
        Assert.Equal(TextColorPreset.Ink.ToString(), loaded.TextColorPreset);
    }

    [Fact]
    public void Load_SettingsWithoutAppearancePropertiesUseAppearanceDefaults()
    {
        using var folder = new TemporaryFolder();
        var appData = Path.Combine(folder.Path, "AppData", "BookStitch");
        var projectRoot = Path.Combine(folder.Path, "Projects");
        var service = new SettingsService(appData, projectRoot);
        Directory.CreateDirectory(service.SettingsFolder);
        File.WriteAllText(
            service.SettingsFilePath,
            $$"""
            {
              "WorkingFolder": "{{JsonEncodedText(projectRoot)}}",
              "DefaultGenre": "Legacy"
            }
            """);

        var loaded = service.Load();

        Assert.Equal(AppearanceSettingsService.DefaultBackgroundPreset, loaded.BackgroundPreset);
        Assert.Equal(AppearanceSettingsService.DefaultTextColorPreset, loaded.TextColorPreset);
        Assert.Equal("General", loaded.LastSettingsTab);
    }

    [Fact]
    public void Load_MigratesLegacyAppDataSettingsAndKeepsBackup()
    {
        using var folder = new TemporaryFolder();
        var appData = Path.Combine(folder.Path, "AppData", "BookStitch");
        var projectRoot = Path.Combine(folder.Path, "CustomProjects");
        Directory.CreateDirectory(appData);

        var legacySettings = new AppSettings
        {
            WorkingFolder = projectRoot,
            DefaultGenre = "Legacy-Genre"
        };
        File.WriteAllText(
            Path.Combine(appData, "settings.json"),
            JsonSerializer.Serialize(legacySettings));

        var service = new SettingsService(appData, Path.Combine(folder.Path, "DefaultProjects"));
        var loaded = service.Load();

        Assert.Equal("Legacy-Genre", loaded.DefaultGenre);
        Assert.Equal(Path.GetFullPath(projectRoot), loaded.WorkingFolder);
        Assert.True(File.Exists(Path.Combine(projectRoot, "software-settings", "global-settings.json")));
        Assert.True(File.Exists(Path.Combine(appData, "settings-location.json")));
        Assert.False(File.Exists(Path.Combine(appData, "settings.json")));
        Assert.True(File.Exists(Path.Combine(appData, "settings.migrated-backup.json")));
    }

    [Fact]
    public void Load_UnversionedGlobalSettingsPreservesRepresentativePersistentStructures()
    {
        using var folder = new TemporaryFolder();
        var appData = Path.Combine(folder.Path, "AppData", "BookStitch");
        var projectRoot = Path.Combine(folder.Path, "Projects");
        var service = new SettingsService(appData, projectRoot);
        Directory.CreateDirectory(service.SettingsFolder);
        File.WriteAllText(
            service.SettingsFilePath,
            $$"""
            {
              "DefaultGenre": "Krimi",
              "DefaultFileNameTemplate": "{Autor} - {Titel} - {Jahr}",
              "CompatibilityDescriptionEnabled": false,
              "MetadataPreferences": {
                "OutputPolicies": { "Genres": 1 },
                "MetadataFieldBindings": {
                  "narrators-to-composers": false,
                  "title-to-album": true
                },
                "PreferredGenres": [ "Krimi", "Fantasy" ],
                "GenrePreferenceLocked": true,
                "PreferredLanguages": [ "de-DE", "en-US" ],
                "LanguagePreferenceLocked": true,
                "RememberLastConceptGroup": true,
                "LastConceptGroupKey": "people",
                "ShowTechnicalMetadataOrigins": true,
                "WarnBeforeDiscardingConceptDrafts": false
              },
              "UseLeadingZerosInChapterSuggestions": true,
              "ChapterSettings": {
                "Mode": "Custom",
                "NumberingStyle": "ZeroPadded",
                "PreviewDataSource": "CurrentProject",
                "Slot1": "BookTitle",
                "Separator1": "Dash",
                "Slot2": "Number",
                "Separator2": "Pipe",
                "Slot3": "OriginalTagTitle"
              },
              "LastSelectedFolder": "{{JsonEncodedText(Path.Combine(projectRoot, "last-selected"))}}",
              "LastSelectedOpticalDrive": "D:\\",
              "FfmpegPath": "{{JsonEncodedText(Path.Combine(projectRoot, "tools", "ffmpeg.exe"))}}",
              "WorkingFolder": "{{JsonEncodedText(projectRoot)}}",
              "OutputFolder": "{{JsonEncodedText(Path.Combine(projectRoot, "output"))}}",
              "OutputFolderPrimaryAction": "Change",
              "BackgroundPreset": "DeepPetrol",
              "TextColorPreset": "Sage",
              "LastSettingsTab": "Drive",
              "OutputFolderLayout": "SeriesAuthorTitleNested",
              "TrackGridAutoFitEnabled": false,
              "TrackGridColumns": [
                { "Key": "Title", "DisplayIndex": 1, "IsVisible": false, "Width": 412.5 },
                { "Key": "Duration", "DisplayIndex": 0, "IsVisible": true, "Width": null }
              ],
              "ExperimentalDriveRoundEnabled": true,
              "UsePrivateGenreList": true,
              "DiscDriveOrder": [
                {
                  "StableId": "serial:ABC-123",
                  "DriveRoot": "{{JsonEncodedText(Path.Combine(projectRoot, "disc-drive"))}}",
                  "DisplayName": "Test drive",
                  "UserName": "Studio-Laufwerk",
                  "DevicePath": "device-1",
                  "StorageSerialNumber": "ABC-123",
                  "PnpDeviceInstanceId": "USBSTOR\\CDROM\\ABC-123",
                  "Vendor": "ACME",
                  "Product": "DVD",
                  "Revision": "2.0",
                  "HardwareFingerprint": "FINGERPRINT",
                  "IsEnabled": false,
                  "Order": 2,
                  "LastSeenUtc": "2026-08-20T14:30:00Z"
                }
              ]
            }
            """);

        var loaded = service.Load();

        AssertRepresentativeSettings(loaded, projectRoot);
        using var migratedDocument = JsonDocument.Parse(File.ReadAllText(service.SettingsFilePath));
        Assert.Equal(
            AppSettingsVersions.CurrentSchemaVersion,
            migratedDocument.RootElement.GetProperty("schemaVersion").GetInt32());
    }

    [Fact]
    public void Load_VersionOneMigratesLegacyValuesToSchemaTwoAndRemovesHistoricalProperties()
    {
        using var folder = new TemporaryFolder();
        var appData = Path.Combine(folder.Path, "AppData", "BookStitch");
        var projectRoot = Path.Combine(folder.Path, "Projects");
        var service = new SettingsService(appData, projectRoot);
        Directory.CreateDirectory(service.SettingsFolder);
        File.WriteAllText(
            service.SettingsFilePath,
            $$"""
            {
              "schemaVersion": 1,
              "WorkingFolder": "{{JsonEncodedText(projectRoot)}}",
              "DefaultGenre": "Schema-One",
              "UseLeadingZerosInChapterSuggestions": true,
              "ChapterSettings": {
                "Mode": "Custom",
                "NumberingStyle": "SpacePadded"
              },
              "SoundProfile": "Off",
              "EmbeddedChapterTextStyle": "CoolClear",
              "DeleteProjectsOlderThanDays": 45,
              "MetadataPanelAnimationMilliseconds": 775,
              "ProjectSetupPageAnimationMilliseconds": 315,
              "ForceShowFfmpegSetupButton": true,
              "AacBitrateKbps": 96,
              "AutoEjectDisc": true,
              "ShowCompletedProjects": false,
              "ShowIncompleteProjects": true
            }
            """);

        var loaded = service.Load();

        Assert.Equal(AppSettingsVersions.CurrentSchemaVersion, loaded.SchemaVersion);
        Assert.Equal("Schema-One", loaded.DefaultGenre);
        Assert.Equal(45, loaded.ProjectRetentionDays);
        Assert.Equal(ChapterNumberingStyle.SpacePadded.ToString(), loaded.ChapterSettings!.NumberingStyle);
        Assert.Equal(EventProfile.Important.ToString(), loaded.EventProfile);
        Assert.Equal(SoundLibrary.None.ToString(), loaded.SoundLibrary);

        var debug = new DebugSettingsService(service).Load();
        Assert.Equal(775, debug.MetadataPanelAnimationMillisecondsOverride);
        Assert.Equal(315, debug.ProjectSetupPageAnimationMillisecondsOverride);
        Assert.True(debug.ForceShowFfmpegSetupButton);

        using var migratedDocument = JsonDocument.Parse(File.ReadAllText(service.SettingsFilePath));
        var root = migratedDocument.RootElement;
        Assert.Equal(AppSettingsVersions.CurrentSchemaVersion, root.GetProperty("schemaVersion").GetInt32());
        foreach (var removedProperty in new[]
                 {
                     "UseLeadingZerosInChapterSuggestions",
                     "SoundProfile",
                     "EmbeddedChapterTextStyle",
                     "DeleteProjectsOlderThanDays",
                     "MetadataPanelAnimationMilliseconds",
                     "ProjectSetupPageAnimationMilliseconds",
                     "AacBitrateKbps",
                     "AutoEjectDisc",
                     "ShowCompletedProjects",
                     "ShowIncompleteProjects"
                 })
        {
            Assert.False(root.TryGetProperty(removedProperty, out _), removedProperty);
        }
    }

    [Fact]
    public void MetadataFieldBindingPreference_DefaultsEnabledAndRoundTripsExplicitFalseAndTrue()
    {
        using var folder = new TemporaryFolder();
        var appData = Path.Combine(folder.Path, "AppData", "BookStitch");
        var projectRoot = Path.Combine(folder.Path, "Projects");
        var service = new SettingsService(appData, projectRoot);
        var settings = service.Load();
        var definition = ProjectMetadataFieldBindingCatalog.NarratorsToComposers;

        Assert.True(MetadataFieldBindingPreferencesService.GetIsEnabled(settings.MetadataPreferences, definition));

        Assert.True(MetadataFieldBindingPreferencesService.SetIsEnabled(settings.MetadataPreferences, definition, false));
        service.Save(settings);
        var disabledReload = new SettingsService(appData, projectRoot).Load();
        Assert.False(MetadataFieldBindingPreferencesService.GetIsEnabled(disabledReload.MetadataPreferences, definition));

        var legacyEnabledProject = new ProjectMetadata();
        legacyEnabledProject.SetUseNarratorsAsComposers(true);
        var legacyDisabledProject = new ProjectMetadata();
        legacyDisabledProject.SetUseNarratorsAsComposers(false);
        Assert.True(legacyEnabledProject.UseNarratorsAsComposers);
        Assert.False(legacyDisabledProject.UseNarratorsAsComposers);
        Assert.False(MetadataFieldBindingPreferencesService.GetIsEnabled(disabledReload.MetadataPreferences, definition));

        Assert.True(MetadataFieldBindingPreferencesService.SetIsEnabled(disabledReload.MetadataPreferences, definition, true));
        new SettingsService(appData, projectRoot).Save(disabledReload);
        var enabledReload = new SettingsService(appData, projectRoot).Load();
        Assert.True(MetadataFieldBindingPreferencesService.GetIsEnabled(enabledReload.MetadataPreferences, definition));

        var titleAlbum = ProjectMetadataFieldBindingCatalog.TitleToAlbum;
        Assert.True(MetadataFieldBindingPreferencesService.GetIsEnabled(enabledReload.MetadataPreferences, titleAlbum));
        Assert.True(MetadataFieldBindingPreferencesService.SetIsEnabled(enabledReload.MetadataPreferences, titleAlbum, false));
        new SettingsService(appData, projectRoot).Save(enabledReload);
        enabledReload = new SettingsService(appData, projectRoot).Load();
        Assert.False(MetadataFieldBindingPreferencesService.GetIsEnabled(enabledReload.MetadataPreferences, titleAlbum));
        Assert.True(MetadataFieldBindingPreferencesService.SetIsEnabled(enabledReload.MetadataPreferences, titleAlbum, true));
        new SettingsService(appData, projectRoot).Save(enabledReload);
        var titleAlbumEnabledReload = new SettingsService(appData, projectRoot).Load();
        Assert.True(MetadataFieldBindingPreferencesService.GetIsEnabled(titleAlbumEnabledReload.MetadataPreferences, titleAlbum));
        Assert.True(MetadataFieldBindingPreferencesService.SetIsEnabled(titleAlbumEnabledReload.MetadataPreferences, titleAlbum, false));
        new SettingsService(appData, projectRoot).Save(titleAlbumEnabledReload);
        var titleAlbumDisabledReload = new SettingsService(appData, projectRoot).Load();
        Assert.False(MetadataFieldBindingPreferencesService.GetIsEnabled(titleAlbumDisabledReload.MetadataPreferences, titleAlbum));
    }

    [Fact]
    public void Load_UsesLocationFileToFindMovedProjectRoot()
    {
        using var folder = new TemporaryFolder();
        var appData = Path.Combine(folder.Path, "AppData", "BookStitch");
        var firstRoot = Path.Combine(folder.Path, "FirstRoot");
        var movedRoot = Path.Combine(folder.Path, "MovedRoot");
        var service = new SettingsService(appData, firstRoot);

        service.Save(new AppSettings
        {
            WorkingFolder = movedRoot,
            DefaultGenre = "Verschoben"
        });

        var reloadedService = new SettingsService(appData, firstRoot);
        var loaded = reloadedService.Load();

        Assert.Equal(Path.GetFullPath(movedRoot), reloadedService.ProjectRootFolder);
        Assert.Equal("Verschoben", loaded.DefaultGenre);
    }

    [Fact]
    public void Load_ProductionEnvironmentKeepsExistingLocationFileCompatibility()
    {
        using var folder = new TemporaryFolder();
        var context = CreateContext(
            BookStitchEnvironment.Production,
            Path.Combine(folder.Path, "AppData"),
            Path.Combine(folder.Path, "Music"),
            Path.Combine(folder.Path, "DefaultProjects"));
        var movedRoot = Path.Combine(folder.Path, "MovedProductionProjects");
        var service = new SettingsService(context);
        service.Save(new AppSettings
        {
            WorkingFolder = movedRoot,
            DefaultGenre = "Production"
        });

        var reloaded = new SettingsService(context);
        var settings = reloaded.Load();

        Assert.Equal(Path.GetFullPath(movedRoot), reloaded.ProjectRootFolder);
        Assert.Equal("Production", settings.DefaultGenre);
        Assert.True(File.Exists(Path.Combine(context.AppDataRoot, SettingsService.SettingsLocationFileName)));
    }

    [Fact]
    public void Load_DevelopmentUsesOwnLocationFile()
    {
        using var folder = new TemporaryFolder();
        var context = CreateContext(
            BookStitchEnvironment.Development,
            Path.Combine(folder.Path, "AppData"),
            Path.Combine(folder.Path, "Music"),
            Path.Combine(folder.Path, "DefaultDevelopmentProjects"));
        var movedRoot = Path.Combine(folder.Path, "MovedDevelopmentProjects");

        new SettingsService(context).Save(new AppSettings
        {
            WorkingFolder = movedRoot,
            DefaultGenre = "Development"
        });

        var reloaded = new SettingsService(context);
        var settings = reloaded.Load();

        Assert.Equal(Path.Combine(folder.Path, "AppData", "BookStitch-Development"), context.AppDataRoot);
        Assert.Equal(Path.GetFullPath(movedRoot), reloaded.ProjectRootFolder);
        Assert.Equal("Development", settings.DefaultGenre);
    }

    [Fact]
    public void Load_XTesterUsesOwnLocationFile()
    {
        using var folder = new TemporaryFolder();
        var context = CreateContext(
            BookStitchEnvironment.XTester,
            Path.Combine(folder.Path, "AppData"),
            Path.Combine(folder.Path, "Music"),
            Path.Combine(folder.Path, "DefaultXTesterProjects"));
        var movedRoot = Path.Combine(folder.Path, "MovedXTesterProjects");

        new SettingsService(context).Save(new AppSettings
        {
            WorkingFolder = movedRoot,
            DefaultGenre = "XTester"
        });

        var reloaded = new SettingsService(context);
        var settings = reloaded.Load();

        Assert.Equal(Path.Combine(folder.Path, "AppData", "BookStitch-XTester"), context.AppDataRoot);
        Assert.Equal(Path.GetFullPath(movedRoot), reloaded.ProjectRootFolder);
        Assert.Equal("XTester", settings.DefaultGenre);
    }

    [Theory]
    [InlineData(BookStitchEnvironment.Development)]
    [InlineData(BookStitchEnvironment.XTester)]
    public void Load_NonProductionDoesNotMigrateProductionLegacySettings(BookStitchEnvironment environment)
    {
        using var folder = new TemporaryFolder();
        var appDataBase = Path.Combine(folder.Path, "AppData");
        var music = Path.Combine(folder.Path, "Music");
        var productionAppData = Path.Combine(appDataBase, "BookStitch");
        Directory.CreateDirectory(productionAppData);
        File.WriteAllText(
            Path.Combine(productionAppData, SettingsService.LegacySettingsFileName),
            JsonSerializer.Serialize(new AppSettings
            {
                WorkingFolder = Path.Combine(folder.Path, "ProductionLegacyProjects"),
                DefaultGenre = "Production-Legacy"
            }));
        var context = CreateContext(
            environment,
            appDataBase,
            music,
            Path.Combine(folder.Path, "UnusedDefault"));

        var service = new SettingsService(context);
        var loaded = service.Load();

        Assert.Equal(Path.GetFullPath(context.DefaultProjectRoot), loaded.WorkingFolder);
        Assert.Equal("Audiobook", loaded.DefaultGenre);
        Assert.True(File.Exists(Path.Combine(productionAppData, SettingsService.LegacySettingsFileName)));
        Assert.False(File.Exists(Path.Combine(context.AppDataRoot, SettingsService.LegacySettingsFileName)));
    }

    [Theory]
    [InlineData(BookStitchEnvironment.Development)]
    [InlineData(BookStitchEnvironment.XTester)]
    public void Load_NonProductionIgnoresLegacySettingsInOwnAppData(BookStitchEnvironment environment)
    {
        using var folder = new TemporaryFolder();
        var context = CreateContext(
            environment,
            Path.Combine(folder.Path, "AppData"),
            Path.Combine(folder.Path, "Music"),
            Path.Combine(folder.Path, "UnusedDefault"));
        Directory.CreateDirectory(context.AppDataRoot);
        File.WriteAllText(
            Path.Combine(context.AppDataRoot, SettingsService.LegacySettingsFileName),
            JsonSerializer.Serialize(new AppSettings
            {
                WorkingFolder = Path.Combine(folder.Path, "LegacyProjects"),
                DefaultGenre = "Legacy"
            }));

        var loaded = new SettingsService(context).Load();

        Assert.Equal(Path.GetFullPath(context.DefaultProjectRoot), loaded.WorkingFolder);
        Assert.Equal("Audiobook", loaded.DefaultGenre);
        Assert.True(File.Exists(Path.Combine(context.AppDataRoot, SettingsService.LegacySettingsFileName)));
    }

    [Fact]
    public void Load_CorruptGlobalSettingsArchivesFileAndReturnsDefaults()
    {
        using var folder = new TemporaryFolder();
        var appData = Path.Combine(folder.Path, "AppData", "BookStitch");
        var projectRoot = Path.Combine(folder.Path, "Projects");
        var service = new SettingsService(appData, projectRoot);
        Directory.CreateDirectory(service.SettingsFolder);
        File.WriteAllText(service.SettingsFilePath, "{ not valid json");

        var loaded = service.Load();

        Assert.Equal("Audiobook", loaded.DefaultGenre);
        Assert.Equal(Path.GetFullPath(projectRoot), loaded.WorkingFolder);
        Assert.False(File.Exists(service.SettingsFilePath));
        Assert.Single(Directory.EnumerateFiles(service.SettingsFolder, "global-settings.corrupt-*.json"));

        File.WriteAllText(service.SettingsFilePath, "{ still not valid json");
        new SettingsService(appData, projectRoot).Load();

        Assert.Equal(2, Directory.EnumerateFiles(service.SettingsFolder, "global-settings.corrupt-*.json").Count());
    }

    [Fact]
    public void Load_FutureGlobalSettingsSchemaIsRejectedWithoutChangingFile()
    {
        using var folder = new TemporaryFolder();
        var appData = Path.Combine(folder.Path, "AppData", "BookStitch");
        var projectRoot = Path.Combine(folder.Path, "Projects");
        var service = new SettingsService(appData, projectRoot);
        Directory.CreateDirectory(service.SettingsFolder);
        var futureJson = $$"""
            {
              "schemaVersion": {{AppSettingsVersions.CurrentSchemaVersion + 1}},
              "DefaultGenre": "Future"
            }
            """;
        File.WriteAllText(service.SettingsFilePath, futureJson);

        Assert.Throws<PersistentJsonSchemaException>(() => service.Load());

        Assert.Equal(futureJson, File.ReadAllText(service.SettingsFilePath));
        Assert.Empty(Directory.EnumerateFiles(service.SettingsFolder, "global-settings.corrupt-*.json"));
    }

    [Fact]
    public void Load_UnreadableGlobalSettingsDoesNotArchiveOrReplaceFile()
    {
        using var folder = new TemporaryFolder();
        var appData = Path.Combine(folder.Path, "AppData", "BookStitch");
        var projectRoot = Path.Combine(folder.Path, "Projects");
        var service = new SettingsService(appData, projectRoot);
        service.Save(new AppSettings { WorkingFolder = projectRoot, DefaultGenre = "Locked" });
        var originalJson = File.ReadAllText(service.SettingsFilePath);

        using (new FileStream(service.SettingsFilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Assert.Throws<IOException>(() => new SettingsService(appData, projectRoot).Load());
            Assert.Empty(Directory.EnumerateFiles(service.SettingsFolder, "global-settings.corrupt-*.json"));
        }

        Assert.Equal(originalJson, File.ReadAllText(service.SettingsFilePath));
    }

    [Fact]
    public void Load_WhenCorruptGlobalSettingsCannotBeArchivedLeavesOriginalInPlace()
    {
        using var folder = new TemporaryFolder();
        var appData = Path.Combine(folder.Path, "AppData", "BookStitch");
        var projectRoot = Path.Combine(folder.Path, "Projects");
        var service = new SettingsService(appData, projectRoot);
        Directory.CreateDirectory(service.SettingsFolder);
        const string corruptJson = "{ invalid json";
        File.WriteAllText(service.SettingsFilePath, corruptJson);

        using (new FileStream(service.SettingsFilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Assert.Throws<IOException>(() => service.Load());
        }

        Assert.Equal(corruptJson, File.ReadAllText(service.SettingsFilePath));
        Assert.Empty(Directory.EnumerateFiles(service.SettingsFolder, "global-settings.corrupt-*.json"));
    }

    [Fact]
    public void Load_CorruptLocationArchivesItAndBootstrapsReplacement()
    {
        using var folder = new TemporaryFolder();
        var appData = Path.Combine(folder.Path, "AppData", "BookStitch");
        var projectRoot = Path.Combine(folder.Path, "Projects");
        Directory.CreateDirectory(appData);
        var locationPath = Path.Combine(appData, SettingsService.SettingsLocationFileName);
        File.WriteAllText(locationPath, "{ invalid json");

        var service = new SettingsService(appData, projectRoot);
        var loaded = service.Load();

        Assert.Equal(Path.GetFullPath(projectRoot), loaded.WorkingFolder);
        Assert.True(File.Exists(locationPath));
        Assert.Single(Directory.EnumerateFiles(appData, "settings-location.corrupt-*.json"));
        Assert.False(File.Exists(service.SettingsFilePath));
    }

    [Fact]
    public void Load_UnreadableLocationDoesNotArchiveOrReplaceFile()
    {
        using var folder = new TemporaryFolder();
        var appData = Path.Combine(folder.Path, "AppData", "BookStitch");
        var projectRoot = Path.Combine(folder.Path, "Projects");
        var service = new SettingsService(appData, projectRoot);
        service.Save(new AppSettings { WorkingFolder = projectRoot });
        var originalJson = File.ReadAllText(service.LocationFilePath);

        using (new FileStream(service.LocationFilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Assert.Throws<IOException>(() => new SettingsService(appData, projectRoot).Load());
            Assert.Empty(Directory.EnumerateFiles(appData, "settings-location.corrupt-*.json"));
        }

        Assert.Equal(originalJson, File.ReadAllText(service.LocationFilePath));
    }

    [Fact]
    public void Save_UnchangedProjectRootDoesNotRewriteLocationFile()
    {
        using var folder = new TemporaryFolder();
        var appData = Path.Combine(folder.Path, "AppData", "BookStitch");
        var projectRoot = Path.Combine(folder.Path, "Projects");
        var service = new SettingsService(appData, projectRoot);
        var settings = new AppSettings { WorkingFolder = projectRoot, DefaultGenre = "First" };
        service.Save(settings);
        File.SetLastWriteTimeUtc(service.LocationFilePath, new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc));
        var locationWriteTime = File.GetLastWriteTimeUtc(service.LocationFilePath);

        settings.DefaultGenre = "Second";
        service.Save(settings);

        Assert.Equal(locationWriteTime, File.GetLastWriteTimeUtc(service.LocationFilePath));
    }

    private static AppSettings CreateRepresentativeSettings(string projectRoot) =>
        new()
        {
            DefaultGenre = "Krimi",
            DefaultFileNameTemplate = "{Autor} - {Titel} - {Jahr}",
            CompatibilityDescriptionEnabled = false,
            MetadataPreferences = new MetadataPreferences
            {
                OutputPolicies = new Dictionary<string, OutputFieldPolicy>(StringComparer.Ordinal)
                {
                    [ProjectMetadataField.Genres.ToString()] = OutputFieldPolicy.Disabled
                },
                MetadataFieldBindings = new Dictionary<string, bool>(StringComparer.Ordinal)
                {
                    [ProjectMetadataFieldBindingCatalog.NarratorsToComposersId] = false,
                    [ProjectMetadataFieldBindingCatalog.TitleToAlbumId] = true
                },
                PreferredGenres = ["Krimi", "Fantasy"],
                GenrePreferenceLocked = true,
                PreferredLanguages = ["de-DE", "en-US"],
                LanguagePreferenceLocked = true,
                RememberLastConceptGroup = true,
                LastConceptGroupKey = ConceptMetadataGroupIds.People,
                ShowTechnicalMetadataOrigins = true,
                WarnBeforeDiscardingConceptDrafts = false
            },
            ChapterSettings = new ChapterSettings
            {
                Mode = ChapterSettingsService.ModeCustom,
                NumberingStyle = ChapterNumberingStyle.ZeroPadded.ToString(),
                PreviewDataSource = ChapterSettingsService.PreviewDataSourceCurrentProject,
                Slot1 = "BookTitle",
                Separator1 = "Dash",
                Slot2 = "Number",
                Separator2 = "Pipe",
                Slot3 = "OriginalTagTitle"
            },
            LastSelectedFolder = Path.Combine(projectRoot, "last-selected"),
            LastSelectedOpticalDrive = @"D:\",
            FfmpegPath = Path.Combine(projectRoot, "tools", "ffmpeg.exe"),
            WorkingFolder = projectRoot,
            OutputFolder = Path.Combine(projectRoot, "output"),
            OutputFolderPrimaryAction = nameof(OutputFolderPrimaryAction.Change),
            BackgroundPreset = BackgroundPreset.DeepPetrol.ToString(),
            TextColorPreset = TextColorPreset.Sage.ToString(),
            LastSettingsTab = "Drive",
            OutputFolderLayout = OutputFolderLayoutService.LayoutSeriesAuthorTitleNested,
            TrackGridAutoFitEnabled = false,
            TrackGridColumns =
            [
                new TrackGridColumnLayoutItem
                {
                    Key = "Title",
                    DisplayIndex = 1,
                    IsVisible = false,
                    Width = 412.5
                },
                new TrackGridColumnLayoutItem
                {
                    Key = "Duration",
                    DisplayIndex = 0,
                    IsVisible = true,
                    Width = null
                }
            ],
            ExperimentalDriveRoundEnabled = true,
            UsePrivateGenreList = true,
            DiscDriveOrder =
            [
                new ConfiguredDiscDrive
                {
                    StableId = "serial:ABC-123",
                    DriveRoot = Path.Combine(projectRoot, "disc-drive"),
                    DisplayName = "Test drive",
                    UserName = "Studio-Laufwerk",
                    DevicePath = "device-1",
                    StorageSerialNumber = "ABC-123",
                    PnpDeviceInstanceId = "USBSTOR\\CDROM\\ABC-123",
                    Vendor = "ACME",
                    Product = "DVD",
                    Revision = "2.0",
                    HardwareFingerprint = "FINGERPRINT",
                    IsEnabled = false,
                    Order = 2,
                    LastSeenUtc = new DateTime(2026, 8, 20, 14, 30, 0, DateTimeKind.Utc)
                }
            ]
        };

    private static void AssertRepresentativeSettings(AppSettings loaded, string projectRoot)
    {
        Assert.Equal("Krimi", loaded.DefaultGenre);
        Assert.Equal("{Autor} - {Titel} - {Jahr}", loaded.DefaultFileNameTemplate);
        Assert.False(loaded.CompatibilityDescriptionEnabled);
        Assert.Equal(nameof(OutputFolderPrimaryAction.Change), loaded.OutputFolderPrimaryAction);
        Assert.Equal(OutputFolderLayoutService.LayoutSeriesAuthorTitleNested, loaded.OutputFolderLayout);
        Assert.Equal(BackgroundPreset.DeepPetrol.ToString(), loaded.BackgroundPreset);
        Assert.Equal(TextColorPreset.Sage.ToString(), loaded.TextColorPreset);
        Assert.Equal("Drive", loaded.LastSettingsTab);

        Assert.Equal(Path.GetFullPath(projectRoot), loaded.WorkingFolder);
        Assert.Equal(Path.Combine(projectRoot, "last-selected"), loaded.LastSelectedFolder);
        Assert.Equal(@"D:\", loaded.LastSelectedOpticalDrive);
        Assert.Equal(Path.Combine(projectRoot, "tools", "ffmpeg.exe"), loaded.FfmpegPath);
        Assert.Equal(Path.Combine(projectRoot, "output"), loaded.OutputFolder);

        var metadata = loaded.MetadataPreferences;
        Assert.Equal(["Krimi", "Fantasy"], metadata.PreferredGenres);
        Assert.Equal(["de-DE", "en-US"], metadata.PreferredLanguages);
        Assert.True(metadata.GenrePreferenceLocked);
        Assert.True(metadata.LanguagePreferenceLocked);
        Assert.Equal(OutputFieldPolicy.Disabled, MetadataPreferencesService.GetPolicy(metadata, ProjectMetadataField.Genres));
        Assert.False(MetadataFieldBindingPreferencesService.GetIsEnabled(metadata, ProjectMetadataFieldBindingCatalog.NarratorsToComposers));
        Assert.True(MetadataFieldBindingPreferencesService.GetIsEnabled(metadata, ProjectMetadataFieldBindingCatalog.TitleToAlbum));
        Assert.True(metadata.RememberLastConceptGroup);
        Assert.Equal(ConceptMetadataGroupIds.People, metadata.LastConceptGroupKey);
        Assert.True(metadata.ShowTechnicalMetadataOrigins);
        Assert.False(metadata.WarnBeforeDiscardingConceptDrafts);

        var chapter = Assert.IsType<ChapterSettings>(loaded.ChapterSettings);
        Assert.Equal(
            ["Custom", "ZeroPadded", "CurrentProject", "BookTitle", "Dash", "Number", "Pipe", "OriginalTagTitle"],
            [chapter.Mode, chapter.NumberingStyle, chapter.PreviewDataSource, chapter.Slot1, chapter.Separator1, chapter.Slot2, chapter.Separator2, chapter.Slot3]);
        Assert.False(loaded.TrackGridAutoFitEnabled);
        Assert.Collection(
            loaded.TrackGridColumns,
            item =>
            {
                Assert.Equal("Title", item.Key);
                Assert.Equal(1, item.DisplayIndex);
                Assert.False(item.IsVisible);
                Assert.Equal(412.5, item.Width);
            },
            item =>
            {
                Assert.Equal("Duration", item.Key);
                Assert.Equal(0, item.DisplayIndex);
                Assert.True(item.IsVisible);
                Assert.Null(item.Width);
            });

        Assert.True(loaded.ExperimentalDriveRoundEnabled);
        Assert.True(loaded.UsePrivateGenreList);
        var drive = Assert.Single(loaded.DiscDriveOrder);
        Assert.Equal("serial:ABC-123", drive.StableId);
        Assert.Equal(Path.Combine(projectRoot, "disc-drive"), drive.DriveRoot);
        Assert.Equal("Test drive", drive.DisplayName);
        Assert.Equal("Studio-Laufwerk", drive.UserName);
        Assert.Equal("device-1", drive.DevicePath);
        Assert.Equal("ABC-123", drive.StorageSerialNumber);
        Assert.Equal("USBSTOR\\CDROM\\ABC-123", drive.PnpDeviceInstanceId);
        Assert.Equal("ACME", drive.Vendor);
        Assert.Equal("DVD", drive.Product);
        Assert.Equal("2.0", drive.Revision);
        Assert.Equal("FINGERPRINT", drive.HardwareFingerprint);
        Assert.False(drive.IsEnabled);
        Assert.Equal(2, drive.Order);
        Assert.Equal(new DateTime(2026, 8, 20, 14, 30, 0, DateTimeKind.Utc), drive.LastSeenUtc);
    }

    [Fact]
    public void Load_LegacyRootOnlyDiscDriveConfigurationRemainsAvailableForIdentityMigration()
    {
        using var folder = new TemporaryFolder();
        var appData = Path.Combine(folder.Path, "AppData", "BookStitch");
        var projectRoot = Path.Combine(folder.Path, "Projects");
        var service = new SettingsService(appData, projectRoot);
        Directory.CreateDirectory(service.SettingsFolder);
        File.WriteAllText(service.SettingsFilePath, $$"""
            {
              "WorkingFolder": "{{JsonEncodedText(projectRoot)}}",
              "DiscDriveOrder": [
                { "DriveRoot": "G:\\", "DisplayName": "Legacy", "IsEnabled": false, "Order": 3 }
              ]
            }
            """);

        var loaded = service.Load();

        var drive = Assert.Single(loaded.DiscDriveOrder);
        Assert.Equal(@"G:\", drive.DriveRoot);
        Assert.Equal(string.Empty, drive.StableId);
        Assert.Equal(string.Empty, drive.UserName);
        Assert.False(drive.IsEnabled);
        Assert.Equal(3, drive.Order);
    }

    private static BookStitchEnvironmentContext CreateContext(
        BookStitchEnvironment environment,
        string appDataBase,
        string musicFolder,
        string fallbackProjectRoot)
    {
        return new BookStitchEnvironmentContext(
            environment,
            RepositoryRoot: null,
            BookStitchEnvironmentResolver.GetAppDataRoot(environment, appDataBase),
            BookStitchEnvironmentResolver.GetDefaultProjectRoot(environment, musicFolder, fallbackProjectRoot));
    }

    private static string JsonEncodedText(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
}
