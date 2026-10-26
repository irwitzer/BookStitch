using System.IO;
using System.Text;
using System.Text.Json;
using BookStitch.Models;

namespace BookStitch.Services;

public sealed class SettingsService
{
    public const string SoftwareSettingsFolderName = "software-settings";
    public const string GlobalSettingsFileName = "global-settings.json";
    public const string SettingsLocationFileName = "settings-location.json";
    public const string LegacySettingsFileName = "settings.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };
    private static readonly AtomicJsonStore JsonStore = new();

    private readonly string _appDataFolder;
    private readonly string _defaultProjectRootFolder;
    private readonly bool _allowLegacySettingsMigration;
    private string? _resolvedProjectRootFolder;

    public SettingsService()
        : this(new BookStitchEnvironmentResolver().Resolve())
    {
    }

    public SettingsService(BookStitchEnvironmentContext environmentContext)
        : this(
            environmentContext.AppDataRoot,
            environmentContext.DefaultProjectRoot,
            allowLegacySettingsMigration: environmentContext.IsProduction)
    {
    }

    public SettingsService(string appDataFolder, string defaultProjectRootFolder)
        : this(appDataFolder, defaultProjectRootFolder, allowLegacySettingsMigration: true)
    {
    }

    public SettingsService(
        string appDataFolder,
        string defaultProjectRootFolder,
        bool allowLegacySettingsMigration)
    {
        if (string.IsNullOrWhiteSpace(appDataFolder))
            throw new ArgumentException("Es wurde kein AppData-Ordner übergeben.", nameof(appDataFolder));

        if (string.IsNullOrWhiteSpace(defaultProjectRootFolder))
            throw new ArgumentException("Es wurde kein Standard-Projektordner übergeben.", nameof(defaultProjectRootFolder));

        _appDataFolder = Path.GetFullPath(appDataFolder);
        _defaultProjectRootFolder = Path.GetFullPath(defaultProjectRootFolder);
        _allowLegacySettingsMigration = allowLegacySettingsMigration;
    }

    public string AppDataFolder => _appDataFolder;

    public string LocationFilePath => Path.Combine(_appDataFolder, SettingsLocationFileName);

    public string LegacySettingsFilePath => Path.Combine(_appDataFolder, LegacySettingsFileName);

    public string ProjectRootFolder => ResolveProjectRootFolder();

    public string SettingsFolder => GetSettingsFolder(ProjectRootFolder);

    public string SettingsFilePath => GetSettingsFilePath(ProjectRootFolder);

    public string LogsFolder => Path.Combine(SettingsFolder, "logs");

    // The rejected second instance must not migrate, archive, normalize or save the first instance's settings.
    public string ReadUiLanguageAtStartup()
    {
        try
        {
            var location = ReadLocation(LocationFilePath);
            var legacy = SettingsReadResult.Missing;
            var root = _defaultProjectRootFolder;
            if (location.Status == JsonReadStatus.Success && !string.IsNullOrWhiteSpace(location.Location!.BookStitchProjectsRoot))
                root = ResolveWorkingFolder(location.Location.BookStitchProjectsRoot);
            else if (_allowLegacySettingsMigration)
            {
                legacy = ReadSettings(LegacySettingsFilePath);
                if (legacy.Status == JsonReadStatus.Success) root = ResolveWorkingFolder(legacy.Settings!.WorkingFolder);
            }
            var current = ReadSettings(GetSettingsFilePath(root));
            if (current.Status == JsonReadStatus.Success) return UiLanguageService.Normalize(current.Settings!.UiLanguage);
            if (_allowLegacySettingsMigration && legacy.Status == JsonReadStatus.Missing)
                legacy = ReadSettings(LegacySettingsFilePath);
            return UiLanguageService.Normalize(legacy.Settings?.UiLanguage);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or
            PersistentJsonSchemaException or ArgumentException or NotSupportedException)
        {
            return UiLanguageService.System;
        }
    }

    public AppSettings Load()
    {
        var projectRootFolder = ResolveProjectRootFolder();
        var settingsFilePath = GetSettingsFilePath(projectRootFolder);
        var currentRead = ReadSettings(settingsFilePath);

        if (currentRead.Status == JsonReadStatus.Success)
        {
            var currentSettings = currentRead.Settings!;
            MigrateDebugSettingsBeforeGlobalUpgrade(currentRead);
            var migrated = AppSettingsMigrationService.MigrateToCurrent(
                currentSettings,
                currentRead.SchemaVersion,
                currentRead.LegacyData!);
            NormalizeSettings(currentSettings, projectRootFolder);
            if (migrated)
            {
                AtomicWriteJson(settingsFilePath, currentSettings);
                PersistentJsonSchema.TraceLegacyMigration("globale Einstellungen", settingsFilePath);
            }

            EnsureLocationFile(projectRootFolder);
            return currentSettings;
        }

        if (currentRead.Status == JsonReadStatus.Corrupt)
            ArchiveCorruptFileOrThrow(settingsFilePath);

        if (_allowLegacySettingsMigration)
        {
            var legacyRead = ReadSettings(LegacySettingsFilePath);
            if (legacyRead.Status == JsonReadStatus.Success)
            {
                var legacySettings = legacyRead.Settings!;
                var legacyProjectRoot = ResolveWorkingFolder(legacySettings.WorkingFolder);
                MigrateDebugSettingsBeforeGlobalUpgrade(legacyRead);
                AppSettingsMigrationService.MigrateToCurrent(
                    legacySettings,
                    legacyRead.SchemaVersion,
                    legacyRead.LegacyData!);
                NormalizeSettings(legacySettings, legacyProjectRoot);
                SaveToProjectRoot(legacySettings, legacyProjectRoot);
                TryArchiveLegacySettings();
                return legacySettings;
            }
        }

        var defaults = new AppSettings
        {
            WorkingFolder = projectRootFolder
        };
        defaults.SchemaVersion = AppSettingsVersions.CurrentSchemaVersion;
        AppSettingsNormalizationService.Normalize(defaults);

        EnsureLocationFile(projectRootFolder);
        return defaults;
    }

    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var projectRootFolder = ResolveWorkingFolder(settings.WorkingFolder);
        settings.SchemaVersion = AppSettingsVersions.CurrentSchemaVersion;
        NormalizeSettings(settings, projectRootFolder);
        SaveToProjectRoot(settings, projectRootFolder);
    }

    public static string GetSettingsFolder(string projectRootFolder)
    {
        if (string.IsNullOrWhiteSpace(projectRootFolder))
            throw new ArgumentException("Es wurde kein Projektordner übergeben.", nameof(projectRootFolder));

        return Path.Combine(Path.GetFullPath(projectRootFolder), SoftwareSettingsFolderName);
    }

    public static string GetSettingsFilePath(string projectRootFolder) =>
        Path.Combine(GetSettingsFolder(projectRootFolder), GlobalSettingsFileName);

    private void SaveToProjectRoot(AppSettings settings, string projectRootFolder)
    {
        settings.SchemaVersion = AppSettingsVersions.CurrentSchemaVersion;
        var normalizedRoot = Path.GetFullPath(projectRootFolder);
        var locationRead = ReadLocation(LocationFilePath);
        if (locationRead.Status == JsonReadStatus.Corrupt)
            ArchiveCorruptFileOrThrow(LocationFilePath);

        var locationNeedsUpdate =
            locationRead.Status != JsonReadStatus.Success ||
            !PathsEqual(locationRead.Location!.BookStitchProjectsRoot, normalizedRoot);
        var settingsFilePath = GetSettingsFilePath(projectRootFolder);
        AtomicWriteJson(settingsFilePath, settings);
        if (locationNeedsUpdate)
        {
            AtomicWriteJson(
                LocationFilePath,
                new SettingsLocation { BookStitchProjectsRoot = normalizedRoot });
        }

        _resolvedProjectRootFolder = normalizedRoot;
    }

    private string ResolveProjectRootFolder()
    {
        if (!string.IsNullOrWhiteSpace(_resolvedProjectRootFolder))
            return _resolvedProjectRootFolder;

        var locationRead = ReadLocation(LocationFilePath);
        if (locationRead.Status == JsonReadStatus.Success &&
            !string.IsNullOrWhiteSpace(locationRead.Location!.BookStitchProjectsRoot))
        {
            _resolvedProjectRootFolder = ResolveWorkingFolder(locationRead.Location.BookStitchProjectsRoot);
            return _resolvedProjectRootFolder;
        }

        if (locationRead.Status == JsonReadStatus.Corrupt ||
            locationRead.Status == JsonReadStatus.Success)
        {
            ArchiveCorruptFileOrThrow(LocationFilePath);
        }

        if (_allowLegacySettingsMigration)
        {
            var legacyRead = ReadSettings(LegacySettingsFilePath);
            if (legacyRead.Status == JsonReadStatus.Success)
            {
                _resolvedProjectRootFolder = ResolveWorkingFolder(legacyRead.Settings!.WorkingFolder);
                return _resolvedProjectRootFolder;
            }
        }

        _resolvedProjectRootFolder = _defaultProjectRootFolder;
        return _resolvedProjectRootFolder;
    }

    private string ResolveWorkingFolder(string? configuredFolder)
    {
        if (string.IsNullOrWhiteSpace(configuredFolder))
            return _defaultProjectRootFolder;

        try
        {
            return Path.GetFullPath(configuredFolder);
        }
        catch
        {
            return _defaultProjectRootFolder;
        }
    }

    private void EnsureLocationFile(string projectRootFolder)
    {
        UpdateLocationFileIfNeeded(projectRootFolder, suppressWriteFailure: true);
    }

    private static void NormalizeWorkingFolder(AppSettings settings, string projectRootFolder)
    {
        settings.WorkingFolder = Path.GetFullPath(projectRootFolder);
    }

    private static void NormalizeSettings(AppSettings settings, string projectRootFolder)
    {
        NormalizeWorkingFolder(settings, projectRootFolder);
        AppSettingsNormalizationService.Normalize(settings);
    }

    private static SettingsReadResult ReadSettings(string filePath)
    {
        string json;
        try
        {
            json = File.ReadAllText(filePath, Encoding.UTF8);
        }
        catch (FileNotFoundException)
        {
            return SettingsReadResult.Missing;
        }
        catch (DirectoryNotFoundException)
        {
            return SettingsReadResult.Missing;
        }

        try
        {
            var schema = PersistentJsonSchema.Read(
                json,
                "globale Einstellungen",
                filePath,
                AppSettingsVersions.CurrentSchemaVersion);
            using var document = JsonDocument.Parse(json);
            var settings = document.RootElement.Deserialize<AppSettings>(JsonOptions);
            return settings is null
                ? SettingsReadResult.Corrupt
                : new SettingsReadResult(
                    JsonReadStatus.Success,
                    settings,
                    schema.Version,
                    AppSettingsLegacyData.Read(document.RootElement));
        }
        catch (JsonException)
        {
            return SettingsReadResult.Corrupt;
        }
        catch (PersistentJsonSchemaException exception) when (!exception.IsUnsupportedFutureVersion)
        {
            return SettingsReadResult.Corrupt;
        }
    }

    private void MigrateDebugSettingsBeforeGlobalUpgrade(SettingsReadResult readResult)
    {
        if (readResult.SchemaVersion >= AppSettingsVersions.CurrentSchemaVersion)
            return;

        new DebugSettingsService(this)
            .MigrateLegacyGlobalSettingsBeforeUpgrade(readResult.LegacyData!);
    }

    private static LocationReadResult ReadLocation(string filePath)
    {
        string json;
        try
        {
            json = File.ReadAllText(filePath, Encoding.UTF8);
        }
        catch (FileNotFoundException)
        {
            return LocationReadResult.Missing;
        }
        catch (DirectoryNotFoundException)
        {
            return LocationReadResult.Missing;
        }

        try
        {
            var location = JsonSerializer.Deserialize<SettingsLocation>(json, JsonOptions);
            return location is null
                ? LocationReadResult.Corrupt
                : new LocationReadResult(JsonReadStatus.Success, location);
        }
        catch (JsonException)
        {
            return LocationReadResult.Corrupt;
        }
    }

    private void UpdateLocationFileIfNeeded(string projectRootFolder, bool suppressWriteFailure)
    {
        var normalizedRoot = Path.GetFullPath(projectRootFolder);
        var locationRead = ReadLocation(LocationFilePath);
        if (locationRead.Status == JsonReadStatus.Success &&
            PathsEqual(locationRead.Location!.BookStitchProjectsRoot, normalizedRoot))
        {
            return;
        }

        if (locationRead.Status == JsonReadStatus.Corrupt)
            ArchiveCorruptFileOrThrow(LocationFilePath);

        try
        {
            AtomicWriteJson(
                LocationFilePath,
                new SettingsLocation { BookStitchProjectsRoot = normalizedRoot });
        }
        catch when (suppressWriteFailure)
        {
            // Das Laden der eigentlichen Einstellungen bleibt möglich, wenn eine
            // fehlende Standortdatei vorübergehend nicht angelegt werden kann.
        }
    }

    private static bool PathsEqual(string? left, string right)
    {
        if (string.IsNullOrWhiteSpace(left))
            return false;

        try
        {
            return string.Equals(
                Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    private static void AtomicWriteJson<T>(string filePath, T value)
    {
        JsonStore.Save(filePath, value, JsonOptions);
    }

    private void TryArchiveLegacySettings()
    {
        if (!File.Exists(LegacySettingsFilePath))
            return;

        var archivePath = Path.Combine(_appDataFolder, "settings.migrated-backup.json");
        TryMoveWithoutOverwrite(LegacySettingsFilePath, archivePath);
    }

    private static void ArchiveCorruptFileOrThrow(string filePath)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (string.IsNullOrWhiteSpace(directory))
            throw new IOException($"Die beschädigte Einstellungsdatei kann nicht gesichert werden: {filePath}");

        var name = Path.GetFileNameWithoutExtension(filePath);
        var extension = Path.GetExtension(filePath);
        var archivePath = Path.Combine(
            directory,
            $"{name}.corrupt-{DateTime.UtcNow:yyyy.MM.dd-HHmmss.fff}-{Guid.NewGuid():N}{extension}");

        try
        {
            File.Move(filePath, archivePath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new IOException(
                $"Die beschädigte Einstellungsdatei konnte nicht sicher archiviert werden. Datei: {filePath}",
                exception);
        }
    }

    private static void TryMoveWithoutOverwrite(string sourcePath, string targetPath)
    {
        try
        {
            if (!File.Exists(sourcePath) || File.Exists(targetPath))
                return;

            File.Move(sourcePath, targetPath);
        }
        catch
        {
            // Eine Sicherung ist hilfreich, darf aber den erfolgreichen Wechsel
            // auf den neuen Speicherort nicht rückgängig machen.
        }
    }

    private sealed class SettingsLocation
    {
        public string BookStitchProjectsRoot { get; set; } = string.Empty;
    }

    private enum JsonReadStatus
    {
        Missing,
        Success,
        Corrupt
    }

    private readonly record struct SettingsReadResult(
        JsonReadStatus Status,
        AppSettings? Settings,
        int SchemaVersion,
        AppSettingsLegacyData? LegacyData)
    {
        public static SettingsReadResult Missing { get; } =
            new(JsonReadStatus.Missing, null, PersistentJsonSchema.LegacyVersion, null);

        public static SettingsReadResult Corrupt { get; } =
            new(JsonReadStatus.Corrupt, null, PersistentJsonSchema.LegacyVersion, null);
    }

    private readonly record struct LocationReadResult(
        JsonReadStatus Status,
        SettingsLocation? Location)
    {
        public static LocationReadResult Missing { get; } =
            new(JsonReadStatus.Missing, null);

        public static LocationReadResult Corrupt { get; } =
            new(JsonReadStatus.Corrupt, null);
    }
}
