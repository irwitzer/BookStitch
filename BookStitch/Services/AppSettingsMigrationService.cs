using BookStitch.Models;
using System.Text.Json;

namespace BookStitch.Services;

internal sealed record AppSettingsLegacyData(
    bool? UseLeadingZerosInChapterSuggestions,
    string? SoundProfile,
    string? EmbeddedChapterTextStyle,
    int? ProjectRetentionDays,
    int? DeleteProjectsOlderThanDays,
    int? MetadataPanelAnimationMilliseconds,
    int? ProjectSetupPageAnimationMilliseconds,
    bool? ForceShowFfmpegSetupButton,
    bool? PerformanceDiagnosticsEnabled,
    bool? UseBoxedDiscWaitDialog)
{
    public static AppSettingsLegacyData Read(JsonElement root) => new(
        ReadBoolean(root, "UseLeadingZerosInChapterSuggestions"),
        ReadString(root, "SoundProfile"),
        ReadString(root, "EmbeddedChapterTextStyle"),
        ReadInteger(root, "ProjectRetentionDays"),
        ReadInteger(root, "DeleteProjectsOlderThanDays"),
        ReadInteger(root, "MetadataPanelAnimationMilliseconds"),
        ReadInteger(root, "ProjectSetupPageAnimationMilliseconds"),
        ReadBoolean(root, "ForceShowFfmpegSetupButton"),
        ReadBoolean(root, "PerformanceDiagnosticsEnabled"),
        ReadBoolean(root, "UseBoxedDiscWaitDialog"));

    private static bool? ReadBoolean(JsonElement root, string propertyName) =>
        root.TryGetProperty(propertyName, out var property) &&
        property.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? property.GetBoolean()
            : null;

    private static int? ReadInteger(JsonElement root, string propertyName) =>
        root.TryGetProperty(propertyName, out var property) &&
        property.ValueKind == JsonValueKind.Number &&
        property.TryGetInt32(out var value)
            ? value
            : null;

    private static string? ReadString(JsonElement root, string propertyName) =>
        root.TryGetProperty(propertyName, out var property) &&
        property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
}

internal static class AppSettingsMigrationService
{
    private const string LegacyDefaultTextColorPreset = "WarmSand";

    public static bool MigrateToCurrent(
        AppSettings settings,
        int sourceVersion,
        AppSettingsLegacyData legacyData)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(legacyData);

        if (sourceVersion < PersistentJsonSchema.LegacyVersion ||
            sourceVersion > AppSettingsVersions.CurrentSchemaVersion)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceVersion));
        }

        var migrated = sourceVersion < AppSettingsVersions.CurrentSchemaVersion;
        if (sourceVersion == PersistentJsonSchema.LegacyVersion)
        {
            MigrateHistoricalFields(settings, legacyData);
            sourceVersion = 1;
        }

        if (sourceVersion == 1)
            MigrateVersionOneToVersionTwo(settings, legacyData);

        if (sourceVersion < 3)
            settings.UiLanguage = UiLanguageService.System;

        settings.SchemaVersion = AppSettingsVersions.CurrentSchemaVersion;
        return migrated;
    }

    private static void MigrateVersionOneToVersionTwo(
        AppSettings settings,
        AppSettingsLegacyData legacyData)
    {
        MigrateHistoricalFields(settings, legacyData);

        if (legacyData.ProjectRetentionDays.HasValue)
        {
            settings.ProjectRetentionDays = legacyData.ProjectRetentionDays.Value;
        }
        else if (legacyData.DeleteProjectsOlderThanDays.HasValue)
        {
            settings.ProjectRetentionDays = legacyData.DeleteProjectsOlderThanDays.Value;
        }
    }

    private static void MigrateHistoricalFields(
        AppSettings settings,
        AppSettingsLegacyData legacyData)
    {
        if (!HasValidNumberingStyle(settings.ChapterSettings) &&
            legacyData.UseLeadingZerosInChapterSuggestions.HasValue)
        {
            settings.ChapterSettings ??= new ChapterSettings();
            settings.ChapterSettings.NumberingStyle = ChapterSettingsService
                .FromUseLeadingZeros(legacyData.UseLeadingZerosInChapterSuggestions.Value)
                .ToString();
        }

        if (!string.IsNullOrWhiteSpace(legacyData.EmbeddedChapterTextStyle) &&
            (string.IsNullOrWhiteSpace(settings.TextColorPreset) ||
             string.Equals(
                 settings.TextColorPreset,
                 LegacyDefaultTextColorPreset,
                 StringComparison.Ordinal)))
        {
            settings.TextColorPreset = legacyData.EmbeddedChapterTextStyle;
        }

        if (string.IsNullOrWhiteSpace(legacyData.SoundProfile))
            return;

        var eventProfile = EventProfileSettingsService.MigrateLegacySoundProfile(
            legacyData.SoundProfile,
            out var soundDisabled);
        settings.EventProfile = eventProfile.ToString();
        if (soundDisabled)
            settings.SoundLibrary = SoundLibrary.None.ToString();
    }

    private static bool HasValidNumberingStyle(ChapterSettings? settings) =>
        settings is not null &&
        Enum.TryParse<ChapterNumberingStyle>(
            settings.NumberingStyle,
            ignoreCase: true,
            out _);
}
