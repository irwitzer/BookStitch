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

    private readonly string _appDataFolder;
    private readonly string _defaultProjectRootFolder;
    private string? _resolvedProjectRootFolder;

    public SettingsService()
        : this(GetDefaultAppDataFolder(), GetDefaultProjectRootFolder())
    {
    }

    public SettingsService(string appDataFolder, string defaultProjectRootFolder)
    {
        if (string.IsNullOrWhiteSpace(appDataFolder))
            throw new ArgumentException("Es wurde kein AppData-Ordner übergeben.", nameof(appDataFolder));

        if (string.IsNullOrWhiteSpace(defaultProjectRootFolder))
            throw new ArgumentException("Es wurde kein Standard-Projektordner übergeben.", nameof(defaultProjectRootFolder));

        _appDataFolder = Path.GetFullPath(appDataFolder);
        _defaultProjectRootFolder = Path.GetFullPath(defaultProjectRootFolder);
    }

    public string AppDataFolder => _appDataFolder;

    public string LocationFilePath => Path.Combine(_appDataFolder, SettingsLocationFileName);

    public string LegacySettingsFilePath => Path.Combine(_appDataFolder, LegacySettingsFileName);

    public string ProjectRootFolder => ResolveProjectRootFolder();

    public string SettingsFolder => GetSettingsFolder(ProjectRootFolder);

    public string SettingsFilePath => GetSettingsFilePath(ProjectRootFolder);

    public string LogsFolder => Path.Combine(SettingsFolder, "logs");

    public AppSettings Load()
    {
        var projectRootFolder = ResolveProjectRootFolder();
        var settingsFilePath = GetSettingsFilePath(projectRootFolder);

        if (TryRead<AppSettings>(settingsFilePath, out var currentSettings))
        {
            NormalizeWorkingFolder(currentSettings!, projectRootFolder);
            EnsureLocationFile(projectRootFolder);
            return currentSettings!;
        }

        if (File.Exists(settingsFilePath))
            TryArchiveCorruptFile(settingsFilePath);

        if (TryRead<AppSettings>(LegacySettingsFilePath, out var legacySettings))
        {
            var legacyProjectRoot = ResolveWorkingFolder(legacySettings!.WorkingFolder);
            NormalizeWorkingFolder(legacySettings, legacyProjectRoot);
            SaveToProjectRoot(legacySettings, legacyProjectRoot);
            TryArchiveLegacySettings();
            return legacySettings;
        }

        var defaults = new AppSettings
        {
            WorkingFolder = projectRootFolder
        };

        EnsureLocationFile(projectRootFolder);
        return defaults;
    }

    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var projectRootFolder = ResolveWorkingFolder(settings.WorkingFolder);
        NormalizeWorkingFolder(settings, projectRootFolder);
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
        var settingsFilePath = GetSettingsFilePath(projectRootFolder);
        AtomicWriteJson(settingsFilePath, settings);
        AtomicWriteJson(LocationFilePath, new SettingsLocation { BookStitchProjectsRoot = projectRootFolder });
        _resolvedProjectRootFolder = projectRootFolder;
    }

    private string ResolveProjectRootFolder()
    {
        if (!string.IsNullOrWhiteSpace(_resolvedProjectRootFolder))
            return _resolvedProjectRootFolder;

        if (TryRead<SettingsLocation>(LocationFilePath, out var location) &&
            !string.IsNullOrWhiteSpace(location!.BookStitchProjectsRoot))
        {
            _resolvedProjectRootFolder = ResolveWorkingFolder(location.BookStitchProjectsRoot);
            return _resolvedProjectRootFolder;
        }

        if (TryRead<AppSettings>(LegacySettingsFilePath, out var legacySettings))
        {
            _resolvedProjectRootFolder = ResolveWorkingFolder(legacySettings!.WorkingFolder);
            return _resolvedProjectRootFolder;
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
        try
        {
            AtomicWriteJson(LocationFilePath, new SettingsLocation { BookStitchProjectsRoot = projectRootFolder });
        }
        catch
        {
            // Das Laden der eigentlichen Einstellungen bleibt möglich, auch wenn
            // die kleine Standortdatei vorübergehend nicht geschrieben werden kann.
        }
    }

    private static void NormalizeWorkingFolder(AppSettings settings, string projectRootFolder)
    {
        settings.WorkingFolder = Path.GetFullPath(projectRootFolder);
    }

    private static bool TryRead<T>(string filePath, out T? value)
    {
        value = default;

        try
        {
            if (!File.Exists(filePath))
                return false;

            var json = File.ReadAllText(filePath, Encoding.UTF8);
            value = JsonSerializer.Deserialize<T>(json, JsonOptions);
            return value is not null;
        }
        catch
        {
            value = default;
            return false;
        }
    }

    private static void AtomicWriteJson<T>(string filePath, T value)
    {
        var directory = Path.GetDirectoryName(filePath)
            ?? throw new InvalidOperationException("Für die Zieldatei konnte kein Ordner ermittelt werden.");

        Directory.CreateDirectory(directory);

        var temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(filePath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            var json = JsonSerializer.Serialize(value, JsonOptions);
            using (var stream = new FileStream(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       bufferSize: 16 * 1024,
                       FileOptions.WriteThrough))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
            {
                writer.Write(json);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, filePath, overwrite: true);
        }
        finally
        {
            TryDeleteFile(temporaryPath);
        }
    }

    private void TryArchiveLegacySettings()
    {
        if (!File.Exists(LegacySettingsFilePath))
            return;

        var archivePath = Path.Combine(_appDataFolder, "settings.migrated-backup.json");
        TryMoveWithoutOverwrite(LegacySettingsFilePath, archivePath);
    }

    private static void TryArchiveCorruptFile(string filePath)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (string.IsNullOrWhiteSpace(directory))
            return;

        var name = Path.GetFileNameWithoutExtension(filePath);
        var extension = Path.GetExtension(filePath);
        var archivePath = Path.Combine(
            directory,
            $"{name}.corrupt-{DateTime.Now:yyyy.MM.dd-HHmmss}{extension}");

        TryMoveWithoutOverwrite(filePath, archivePath);
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

    private static void TryDeleteFile(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
                File.Delete(filePath);
        }
        catch
        {
            // Eine verwaiste temporäre Datei ist besser als ein fehlgeschlagener
            // Settings-Schreibvorgang.
        }
    }

    private static string GetDefaultAppDataFolder()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "BookStitch");
    }

    private static string GetDefaultProjectRootFolder()
    {
        var musicFolder = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
        if (string.IsNullOrWhiteSpace(musicFolder))
            musicFolder = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        return Path.Combine(musicFolder, "BookStitchProjects");
    }

    private sealed class SettingsLocation
    {
        public string BookStitchProjectsRoot { get; set; } = string.Empty;
    }
}
