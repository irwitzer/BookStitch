using System.Diagnostics;
using System.Text.Json;

namespace BookStitch.Services;

public sealed class PersistentJsonSchemaException : InvalidOperationException
{
    public PersistentJsonSchemaException(string message)
        : this(message, isUnsupportedFutureVersion: false)
    {
    }

    internal PersistentJsonSchemaException(string message, bool isUnsupportedFutureVersion)
        : base(message)
    {
        IsUnsupportedFutureVersion = isUnsupportedFutureVersion;
    }

    internal bool IsUnsupportedFutureVersion { get; }
}

internal readonly record struct PersistentJsonSchemaReadResult(int Version, bool IsLegacy);

internal static class PersistentJsonSchema
{
    public const int CurrentVersion = 1;
    public const int LegacyVersion = 0;

    internal static void RequireState(string json, string path, params string[] stateProperties)
    {
        using var document = JsonDocument.Parse(json);
        if (!stateProperties.Any(property => document.RootElement.TryGetProperty(property, out _)))
            throw new PersistentJsonSchemaException(LocalizationText.Get("Persistence.Manifest.Invalid"));
    }

    public static PersistentJsonSchemaReadResult Read(
        string json,
        string fileType,
        string filePath,
        int currentVersion = CurrentVersion,
        bool allowLegacyArray = false)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        if (root.ValueKind == JsonValueKind.Array && allowLegacyArray)
            return new PersistentJsonSchemaReadResult(LegacyVersion, IsLegacy: true);

        if (root.ValueKind != JsonValueKind.Object)
            throw Invalid(fileType, filePath, "Die JSON-Wurzel ist kein Objekt.");

        if (!TryGetSchemaVersion(root, out var version))
            return new PersistentJsonSchemaReadResult(LegacyVersion, IsLegacy: true);

        if (version <= LegacyVersion)
            throw Invalid(fileType, filePath, $"schemaVersion {version} ist nicht gültig.");

        if (version > currentVersion)
        {
            var message =
                $"Dieses Projekt wurde mit einer neueren BookStitch-Version erstellt und kann mit dieser Version nicht sicher geöffnet werden. " +
                $"{fileType}: schemaVersion {version}, unterstützt bis {currentVersion}. Datei: {filePath}";
            Trace.TraceWarning(message);
            throw new PersistentJsonSchemaException(message, isUnsupportedFutureVersion: true);
        }

        return new PersistentJsonSchemaReadResult(version, IsLegacy: false);
    }

    public static void TraceLegacyMigration(string fileType, string filePath, int targetVersion = CurrentVersion)
    {
        Trace.TraceInformation(
            $"{fileType}: Legacy/unversionierte JSON-Datei wurde auf schemaVersion {targetVersion} migriert. Datei: {filePath}");
    }

    private static bool TryGetSchemaVersion(JsonElement root, out int version)
    {
        version = default;

        if (!root.TryGetProperty("schemaVersion", out var property) &&
            !root.TryGetProperty("SchemaVersion", out property))
        {
            return false;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out version))
            return true;

        if (property.ValueKind == JsonValueKind.String &&
            int.TryParse(property.GetString(), out version))
        {
            return true;
        }

        throw Invalid("JSON-Datei", string.Empty, "schemaVersion ist kein gültiger Integer.");
    }

    private static PersistentJsonSchemaException Invalid(string fileType, string filePath, string reason)
    {
        var message = string.IsNullOrWhiteSpace(filePath)
            ? $"{fileType}: {reason}"
            : $"{fileType}: {reason} Datei: {filePath}";
        Trace.TraceWarning(message);
        return new PersistentJsonSchemaException(message);
    }
}
