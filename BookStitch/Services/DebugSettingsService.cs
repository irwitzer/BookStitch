using BookStitch.Models;
using System.IO;
using System.Text;
using System.Text.Json;

namespace BookStitch.Services;

public sealed class DebugSettingsService
{
    public const string FileName = "debug-settings.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private static readonly AtomicJsonStore JsonStore = new();
    private readonly SettingsService _settingsService;

    public DebugSettingsService(SettingsService settingsService)
    {
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
    }

    public string FilePath => Path.Combine(_settingsService.SettingsFolder, FileName);

    public DebugSettings Load()
    {
        if (TryRead(FilePath, out var settings))
        {
            Normalize(settings!);
            return settings!;
        }

        var migrated = TryLoadLegacyGlobalSettings();
        Normalize(migrated);
        try
        {
            Save(migrated);
        }
        catch
        {
            // Debug-Konfiguration darf den normalen Programmstart nie blockieren.
        }

        return migrated;
    }

    public void Save(DebugSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Normalize(settings);
        JsonStore.Save(FilePath, settings, JsonOptions);
    }

    public static void Normalize(DebugSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        settings.WindowWidth = NormalizeDimension(
            settings.WindowWidth,
            DebugSettings.DefaultWindowWidth,
            minimum: 820,
            maximum: 5000);
        settings.WindowHeight = NormalizeDimension(
            settings.WindowHeight,
            DebugSettings.DefaultWindowHeight,
            minimum: 520,
            maximum: 3000);
        settings.WindowLeft = NormalizeCoordinate(settings.WindowLeft);
        settings.WindowTop = NormalizeCoordinate(settings.WindowTop);
        settings.LastTab = NormalizeTab(settings.LastTab);
        settings.TrackGridRowHeightLevel = TrackGridRowHeightService.NormalizeLevel(settings.TrackGridRowHeightLevel);
        settings.MetadataPanelAnimationMillisecondsOverride =
            NormalizeMetadataPanelAnimationOverride(settings.MetadataPanelAnimationMillisecondsOverride);
        settings.ProjectSetupPageAnimationMillisecondsOverride =
            NormalizeProjectSetupPageAnimationOverride(settings.ProjectSetupPageAnimationMillisecondsOverride);
        settings.GlobalSettingsWindowPlacement = NormalizePlacement(settings.GlobalSettingsWindowPlacement);
        settings.ProjectSetupDialogPlacement = NormalizePlacement(settings.ProjectSetupDialogPlacement);
        settings.ConceptDialogPlacement = NormalizePlacement(settings.ConceptDialogPlacement);
        settings.ResumeProjectDialogPlacement = NormalizePlacement(settings.ResumeProjectDialogPlacement);
    }

    internal void MigrateLegacyGlobalSettingsBeforeUpgrade(AppSettingsLegacyData legacyData)
    {
        ArgumentNullException.ThrowIfNull(legacyData);

        var debugFileExists = File.Exists(FilePath);
        DebugSettings settings;
        if (debugFileExists)
        {
            if (!TryRead(FilePath, out var existingSettings))
                return;

            settings = existingSettings!;
        }
        else
        {
            settings = new DebugSettings
            {
                ForceShowFfmpegSetupButton = legacyData.ForceShowFfmpegSetupButton == true,
                PerformanceDiagnosticsEnabled = legacyData.PerformanceDiagnosticsEnabled == true,
                UseBoxedDiscWaitDialog = legacyData.UseBoxedDiscWaitDialog == true
            };
        }

        var changed = !debugFileExists && HasLegacyDebugSettings(legacyData);
        changed |= TryMigrateMetadataAnimationOverride(settings, legacyData.MetadataPanelAnimationMilliseconds);
        changed |= TryMigrateProjectSetupAnimationOverride(settings, legacyData.ProjectSetupPageAnimationMilliseconds);
        if (changed)
            Save(settings);
    }

    private DebugSettings TryLoadLegacyGlobalSettings()
    {
        var migrated = new DebugSettings();
        var path = _settingsService.SettingsFilePath;
        if (!File.Exists(path))
            return migrated;

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8));
            var root = document.RootElement;
            migrated.ForceShowFfmpegSetupButton = ReadBoolean(root, "ForceShowFfmpegSetupButton");
            migrated.PerformanceDiagnosticsEnabled = ReadBoolean(root, "PerformanceDiagnosticsEnabled");
            migrated.UseBoxedDiscWaitDialog = ReadBoolean(root, "UseBoxedDiscWaitDialog");
        }
        catch
        {
            // Debug-Konfiguration darf den normalen Programmstart nie blockieren.
        }

        return migrated;
    }

    private static bool TryRead(string filePath, out DebugSettings? value)
    {
        value = null;
        try
        {
            if (!File.Exists(filePath))
                return false;

            value = JsonSerializer.Deserialize<DebugSettings>(File.ReadAllText(filePath, Encoding.UTF8), JsonOptions);
            return value is not null;
        }
        catch
        {
            value = null;
            return false;
        }
    }

    private static double NormalizeDimension(double value, double fallback, double minimum, double maximum) =>
        double.IsFinite(value)
            ? Math.Clamp(value, minimum, maximum)
            : fallback;

    private static double? NormalizeCoordinate(double? value) =>
        value.HasValue && double.IsFinite(value.Value)
            ? value.Value
            : null;

    private static string NormalizeTab(string? value) => value switch
    {
        "Status" => "Status",
        "Images" => "Images",
        "Tools" => "Tools",
        "Appearance" => "Appearance",
        "Chapters" => "Chapters",
        "Performance" => "Performance",
        "Sounds" => "Sounds",
        "Projects" => "Projects",
        _ => "Status"
    };

    private static DebugWindowPlacement NormalizePlacement(DebugWindowPlacement? placement)
    {
        placement ??= new DebugWindowPlacement();
        placement.Left = NormalizeCoordinate(placement.Left);
        placement.Top = NormalizeCoordinate(placement.Top);
        placement.Width = NormalizeOptionalDimension(placement.Width);
        placement.Height = NormalizeOptionalDimension(placement.Height);
        return placement;
    }

    private static double? NormalizeOptionalDimension(double? value) =>
        value.HasValue && double.IsFinite(value.Value) && value.Value > 0
            ? Math.Clamp(value.Value, 100, 5000)
            : null;

    private static int? NormalizeMetadataPanelAnimationOverride(int? value) =>
        value.HasValue
            ? AnimationTimingSettingsService.ToMetadataPanelOverride(value.Value)
            : null;

    private static int? NormalizeProjectSetupPageAnimationOverride(int? value) =>
        value.HasValue
            ? AnimationTimingSettingsService.ToProjectSetupPageOverride(value.Value)
            : null;

    private static bool TryMigrateMetadataAnimationOverride(DebugSettings settings, int? legacyValue)
    {
        if (settings.MetadataPanelAnimationMillisecondsOverride.HasValue || !legacyValue.HasValue)
            return false;

        settings.MetadataPanelAnimationMillisecondsOverride =
            AnimationTimingSettingsService.ToMetadataPanelOverride(legacyValue.Value);
        return settings.MetadataPanelAnimationMillisecondsOverride.HasValue;
    }

    private static bool TryMigrateProjectSetupAnimationOverride(DebugSettings settings, int? legacyValue)
    {
        if (settings.ProjectSetupPageAnimationMillisecondsOverride.HasValue || !legacyValue.HasValue)
            return false;

        settings.ProjectSetupPageAnimationMillisecondsOverride =
            AnimationTimingSettingsService.ToProjectSetupPageOverride(legacyValue.Value);
        return settings.ProjectSetupPageAnimationMillisecondsOverride.HasValue;
    }

    private static bool HasLegacyDebugSettings(AppSettingsLegacyData legacyData) =>
        legacyData.ForceShowFfmpegSetupButton == true ||
        legacyData.PerformanceDiagnosticsEnabled == true ||
        legacyData.UseBoxedDiscWaitDialog == true;

    private static bool ReadBoolean(JsonElement root, string propertyName) =>
        root.TryGetProperty(propertyName, out var property) &&
        property.ValueKind is JsonValueKind.True or JsonValueKind.False &&
        property.GetBoolean();

}
