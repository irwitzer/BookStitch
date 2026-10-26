using BookStitch.Models;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BookStitch.Services;

public sealed class SourceMetadataSnapshotStore
{
    public const int CurrentSchemaVersion = 3;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };
    private static readonly AtomicJsonStore JsonStore = new();

    public void Save(string projectFolder, SourceMetadataSnapshot snapshot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectFolder);
        ArgumentNullException.ThrowIfNull(snapshot);

        var complete = snapshot.RequireComplete();
        ProjectFolderLayout.EnsureProjectFolders(projectFolder);
        Directory.CreateDirectory(ProjectFolderLayout.GetSourceMetadataSourcesFolder(projectFolder));

        JsonStore.Save(
            ProjectFolderLayout.GetSourceMetadataSnapshotPath(projectFolder, complete.SourceId),
            new SourceMetadataSnapshotDocument
            {
                SchemaVersion = CurrentSchemaVersion,
                Snapshot = complete
            },
            JsonOptions);
    }

    public SourceMetadataSnapshot? TryLoad(string projectFolder, Guid sourceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectFolder);
        if (sourceId == Guid.Empty)
            throw new ArgumentException("A source id is required.", nameof(sourceId));

        var path = ProjectFolderLayout.GetSourceMetadataSnapshotPath(projectFolder, sourceId);
        if (!File.Exists(path))
            return null;

        return LoadDocument(path).Snapshot.RequireComplete();
    }

    public bool HasCurrentCoverage(string projectFolder, Guid sourceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectFolder);
        if (sourceId == Guid.Empty)
            throw new ArgumentException("A source id is required.", nameof(sourceId));

        var path = ProjectFolderLayout.GetSourceMetadataSnapshotPath(projectFolder, sourceId);
        return File.Exists(path) && LoadDocument(path).SchemaVersion >= CurrentSchemaVersion;
    }

    public IReadOnlyList<SourceMetadataSnapshot> LoadAll(string projectFolder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectFolder);

        var folder = ProjectFolderLayout.GetSourceMetadataSourcesFolder(projectFolder);
        if (!Directory.Exists(folder))
            return [];

        var snapshots = new List<SourceMetadataSnapshot>();
        foreach (var path in Directory.EnumerateFiles(folder, "*.json", SearchOption.TopDirectoryOnly)
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            try { snapshots.Add(LoadDocument(path).Snapshot.RequireComplete()); }
            catch (PersistentJsonSchemaException ex) when (ex.IsUnsupportedFutureVersion) { throw; }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                // Optional source snapshots can be regenerated; retain the damaged file for diagnosis.
                System.Diagnostics.Trace.TraceWarning("Invalid source snapshot retained: {0}: {1}", path, ex.Message);
            }
        }
        return snapshots.OrderBy(snapshot => snapshot.SourceId).ToList();
    }

    private static SourceMetadataSnapshotDocument LoadDocument(string path)
    {
        var json = File.ReadAllText(path);
        PersistentJsonSchema.Read(json, "SourceMetadataSnapshot", path, CurrentSchemaVersion);
        var document = JsonSerializer.Deserialize<SourceMetadataSnapshotDocument>(json, JsonOptions)
            ?? throw new PersistentJsonSchemaException($"SourceMetadataSnapshot: Datei konnte nicht gelesen werden. Datei: {path}");
        if (document.Snapshot is null)
            throw new PersistentJsonSchemaException($"SourceMetadataSnapshot: Snapshot fehlt. Datei: {path}");
        return document;
    }

    private sealed class SourceMetadataSnapshotDocument
    {
        [JsonPropertyName("schemaVersion")]
        public int SchemaVersion { get; set; } = CurrentSchemaVersion;

        public SourceMetadataSnapshot Snapshot { get; set; } = new();
    }
}
