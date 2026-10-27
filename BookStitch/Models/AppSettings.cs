using System.Text.Json.Serialization;

namespace BookStitch.Models;

public static class AppSettingsVersions
{
    public const int CurrentSchemaVersion = 3;
}

public class AppSettings
{
    [JsonPropertyName("schemaVersion")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public int SchemaVersion { get; set; } = AppSettingsVersions.CurrentSchemaVersion;

    // Product and user preferences.
    public string DefaultGenre { get; set; } = "Audiobook";
    public bool IncludeGenreLibraryTags { get; set; } = true;
    public bool IncludeGenreCategories { get; set; } = true;
    public bool GenreListAlwaysEnglish { get; set; }

    [JsonConverter(typeof(UiLanguageJsonConverter))]
    public string UiLanguage { get; set; } = UiLanguageIds.System;

    // Null preserves environment defaults for settings written before developer visibility existed.
    public bool? DeveloperToolsVisible { get; set; }
    // Null preserves legacy environment defaults; preview choices are never persisted here.
    public bool? DeveloperToolsEnabled { get; set; }
    public bool DeveloperToolsSelectorUnlocked { get; set; }

    // Stable warning identifiers, independent of UI language. Unknown IDs are retained.
    public List<string> SuppressedWarnings { get; set; } = new();

    public string DefaultOutputExtension { get; set; } = ".m4a";

    public string DefaultFileNameTemplate { get; set; } = "{Autor} - {Titel}";

    public bool CompatibilityDescriptionEnabled { get; set; } = true;

    public MetadataPreferences MetadataPreferences { get; set; } = new();

    public ChapterSettings? ChapterSettings { get; set; }

    // Persistent recent/UI state.
    public string? LastSelectedFolder { get; set; }

    public string? LastLocalProjectFolder { get; set; }

    public string? LastDiscSourceFolder { get; set; }

    public string? LastSelectedOpticalDrive { get; set; }

    public string? LastCoverFolder { get; set; }

    public bool RememberLastCoverPickerFolder { get; set; } = false;

    public string? LastUserSelectedCoverFolder { get; set; }

    public string CoverTransparencyMode { get; set; } = Services.CoverTransparencySettingsService.DefaultMode;

    public int CoverWarningThresholdPixels { get; set; } = Services.CoverWarningSettingsService.DefaultThresholdPixels;

    // Export and workspace preferences.
    public string SelectedExportPreset { get; set; } = ExportPreset.DefaultDisplayName;

    public string SelectedParallelJobs { get; set; } = "Auto";

    [JsonConverter(typeof(Services.DefaultSampleRateJsonConverter))]
    public int DefaultSampleRateHz { get; set; } = 44100;

    public string? FfmpegPath { get; set; }

    public string? FfprobePath { get; set; }

    public string? WorkingFolder { get; set; }

    public string? OutputFolder { get; set; } = Services.OutputFolderFallbackService.GetDesktopFallbackFolder();

    public string OutputFolderPrimaryAction { get; set; } = "Open";

    public string AudioDiscWorkingFormat { get; set; } = "Flac";

    public string EventProfile { get; set; } = "Important";

    public string FocusProfile { get; set; } = "Standard";

    public int SoundVolumePercent { get; set; } = 65;

    public string SoundLibrary { get; set; } = Services.SoundSettingsService.DefaultLibrary.ToString();

    public bool MergeAutomaticallyAfterConversion { get; set; } = true;

    public bool OverwriteFinalOutputWithoutAsking { get; set; } = true;

    public bool KeepComputerAwakeDuringLongOperations { get; set; } = false;

    public string BackgroundPreset { get; set; } = "Black";

    public string TextColorPreset { get; set; } = "WarmSand";

    public string LastSettingsTab { get; set; } = "General";

    // Reservation of the last automatic maintenance attempt; absent in older settings.
    public DateTime? LastAutomaticMaintenanceUtc { get; set; }

    public int ProjectRetentionDays { get; set; } = 180;

    public string ProjectRetentionMode { get; set; } = ProjectRetentionModes.Days;

    public string OutputFolderLayout { get; set; } = "AuthorTitleNested";

    public bool TrackGridAutoFitEnabled { get; set; } = true;

    public List<TrackGridColumnLayoutItem> TrackGridColumns { get; set; } = new();

    public bool ExperimentalDriveRoundEnabled { get; set; } = false;

    public bool UsePrivateGenreList { get; set; } = false;

    // Hardware/disc configuration.
    public List<ConfiguredDiscDrive> DiscDriveOrder { get; set; } = new();
}

public class TrackGridColumnLayoutItem
{
    public string Key { get; set; } = "";

    public int DisplayIndex { get; set; }

    public bool IsVisible { get; set; } = true;

    public double? Width { get; set; }
}

public sealed class MetadataPreferences
{
    public Dictionary<string, OutputFieldPolicy> OutputPolicies { get; set; } = new(StringComparer.Ordinal);

    public Dictionary<string, bool> MetadataFieldBindings { get; set; } = new(StringComparer.Ordinal);

    public List<string> PreferredGenres { get; set; } = new();

    public bool GenrePreferenceLocked { get; set; }

    public List<string> PreferredLanguages { get; set; } = new();

    public bool LanguagePreferenceLocked { get; set; }

    public AudioWorkType? PreferredAudioWorkType { get; set; }

    public bool AudioWorkTypePreferenceLocked { get; set; }

    public int? PreferredOutputTrackNumber { get; set; }

    public bool OutputTrackNumberPreferenceLocked { get; set; }

    public int? PreferredOutputTrackTotal { get; set; }

    public bool OutputTrackTotalPreferenceLocked { get; set; }

    public bool RememberLastConceptGroup { get; set; }

    public string LastConceptGroupKey { get; set; } = ConceptMetadataGroupIds.QuickAccess;

    public bool ShowTechnicalMetadataOrigins { get; set; }

    public bool ShowTechnicalConceptFieldNames { get; set; }

    public bool WarnBeforeDiscardingConceptDrafts { get; set; } = true;
}

public class ChapterSettings
{
    public string Mode { get; set; } = "Auto";

    public string NumberingStyle { get; set; } = Services.ChapterSettingsService.DefaultNumberingStyle.ToString();

    public string PreviewDataSource { get; set; } = "Sample";

    public string Slot1 { get; set; } = "Number";

    public string Separator1 { get; set; } = "Dash";

    public string Slot2 { get; set; } = "CleanFileName";

    public string Separator2 { get; set; } = "Dash";

    public string Slot3 { get; set; } = "Empty";
}

public static class ProjectRetentionModes
{
    public const string Days = "Days";
    public const string Never = "Never";
}

public class ConfiguredDiscDrive
{
    public string StableId { get; set; } = "";

    public string DriveRoot { get; set; } = "";

    public string DisplayName { get; set; } = "";

    public string UserName { get; set; } = "";

    public string DevicePath { get; set; } = "";

    public string StorageSerialNumber { get; set; } = "";

    public string PnpDeviceInstanceId { get; set; } = "";

    public string Vendor { get; set; } = "";

    public string Product { get; set; } = "";

    public string Revision { get; set; } = "";

    public string HardwareFingerprint { get; set; } = "";

    public bool IsEnabled { get; set; } = true;

    public int Order { get; set; }

    public DateTime? LastSeenUtc { get; set; }
}
