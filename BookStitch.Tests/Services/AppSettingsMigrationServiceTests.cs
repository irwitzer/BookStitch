using BookStitch.Models;
using BookStitch.Services;
using System.Text.Json;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class AppSettingsMigrationServiceTests
{
    [Fact]
    public void Current_schema_preserves_user_template_language_and_preferences()
    {
        var settings = new AppSettings { DefaultFileNameTemplate = "Custom - {Titel} - {Autor}", UiLanguage = UiLanguageIds.French, DefaultGenre = "User genre" };
        Assert.False(AppSettingsMigrationService.MigrateToCurrent(settings, AppSettingsVersions.CurrentSchemaVersion, ReadLegacy("{}")));
        Assert.Equal("Custom - {Titel} - {Autor}", settings.DefaultFileNameTemplate);
        Assert.Equal(UiLanguageIds.French, settings.UiLanguage);
        Assert.Equal("User genre", settings.DefaultGenre);
        Assert.False(AppSettingsMigrationService.MigrateToCurrent(settings, AppSettingsVersions.CurrentSchemaVersion, ReadLegacy("{}")));
    }

    [Theory]
    [InlineData(true, "ZeroPadded")]
    [InlineData(false, "Plain")]
    public void MigrateToCurrent_LegacyChapterFlagSuppliesOnlyMissingChapterSettings(
        bool useLeadingZeros,
        string expectedNumberingStyle)
    {
        var settings = new AppSettings { ChapterSettings = null };
        var legacyData = ReadLegacy($$"""{ "UseLeadingZerosInChapterSuggestions": {{useLeadingZeros.ToString().ToLowerInvariant()}} }""");

        var migrated = AppSettingsMigrationService.MigrateToCurrent(
            settings,
            PersistentJsonSchema.LegacyVersion,
            legacyData);

        Assert.True(migrated);
        Assert.Equal(AppSettingsVersions.CurrentSchemaVersion, settings.SchemaVersion);
        Assert.Equal(expectedNumberingStyle, Assert.IsType<ChapterSettings>(settings.ChapterSettings).NumberingStyle);

        settings.ChapterSettings = new ChapterSettings { NumberingStyle = ChapterNumberingStyle.SpacePadded.ToString() };
        AppSettingsMigrationService.MigrateToCurrent(settings, 1, legacyData);
        Assert.Equal(ChapterNumberingStyle.SpacePadded.ToString(), settings.ChapterSettings.NumberingStyle);
    }

    [Fact]
    public void MigrateToCurrent_VersionOneConsumesHistoricalFieldsWithoutKeepingMirrors()
    {
        var settings = new AppSettings
        {
            EventProfile = "All",
            SoundLibrary = "Retro",
            TextColorPreset = "WarmSand"
        };
        var legacyData = ReadLegacy(
            """
            {
              "SoundProfile": "Off",
              "EmbeddedChapterTextStyle": "CoolClear",
              "DeleteProjectsOlderThanDays": 45
            }
            """);

        AppSettingsMigrationService.MigrateToCurrent(settings, 1, legacyData);

        Assert.Equal("Important", settings.EventProfile);
        Assert.Equal("None", settings.SoundLibrary);
        Assert.Equal("CoolClear", settings.TextColorPreset);
        Assert.Equal(45, settings.ProjectRetentionDays);
        Assert.Equal(ProjectRetentionModes.Days, settings.ProjectRetentionMode);
    }

    [Fact]
    public void MigrateToCurrent_CurrentSettingsAreNotReinterpretedAsLegacy()
    {
        var settings = new AppSettings
        {
            ChapterSettings = null,
            EventProfile = "All",
            SoundLibrary = "Gentle",
            ProjectRetentionDays = 90
        };
        var legacyData = ReadLegacy(
            """
            {
              "UseLeadingZerosInChapterSuggestions": true,
              "SoundProfile": "Off",
              "DeleteProjectsOlderThanDays": 10
            }
            """);

        var migrated = AppSettingsMigrationService.MigrateToCurrent(
            settings,
            AppSettingsVersions.CurrentSchemaVersion,
            legacyData);

        Assert.False(migrated);
        Assert.Null(settings.ChapterSettings);
        Assert.Equal("All", settings.EventProfile);
        Assert.Equal("Gentle", settings.SoundLibrary);
        Assert.Equal(90, settings.ProjectRetentionDays);
    }

    private static AppSettingsLegacyData ReadLegacy(string json)
    {
        using var document = JsonDocument.Parse(json);
        return AppSettingsLegacyData.Read(document.RootElement);
    }
}
