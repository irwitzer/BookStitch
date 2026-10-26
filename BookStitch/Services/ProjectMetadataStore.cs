using BookStitch.Models;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Nodes;

namespace BookStitch.Services;

public sealed class ProjectMetadataStore
{
    private const int CurrentSchemaVersion = 4;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };
    private static readonly AtomicJsonStore JsonStore = new();

    public ProjectMetadata LoadOrCreate(string projectFolder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectFolder);

        var path = ProjectFolderLayout.GetProjectMetadataPath(projectFolder);
        if (!File.Exists(path))
            return new ProjectMetadata();

        return ToMetadata(ReadDocument(path));
    }

    public void Save(string projectFolder, ProjectMetadata metadata)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectFolder);
        ArgumentNullException.ThrowIfNull(metadata);

        ProjectFolderLayout.EnsureProjectFolders(projectFolder);
        var path = ProjectFolderLayout.GetProjectMetadataPath(projectFolder);
        JsonStore.Save(path, FromMetadata(metadata), JsonOptions);
    }

    public void Save(string projectFolder, ProjectMetadata metadata, long expectedRevision)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectFolder);
        ArgumentNullException.ThrowIfNull(metadata);

        var path = ProjectFolderLayout.GetProjectMetadataPath(projectFolder);
        if (!File.Exists(path))
        {
            throw new InvalidOperationException(
                $"Canonical ProjectMetadata disappeared before save. Expected revision {expectedRevision}. File: {path}");
        }

        var persistedRevision = ReadDocument(path).Revision;
        if (persistedRevision != expectedRevision)
        {
            throw new InvalidOperationException(
                $"Canonical ProjectMetadata changed outside the active session. Expected revision {expectedRevision}, found {persistedRevision}. File: {path}");
        }

        JsonStore.Save(path, FromMetadata(metadata), JsonOptions);
    }

    private static ProjectMetadataDocument ReadDocument(string path)
    {
        var json = File.ReadAllText(path);
        PersistentJsonSchema.Read(json, "ProjectMetadata", path, CurrentSchemaVersion);
        json = RemoveLegacyDateFields(json);
        return JsonSerializer.Deserialize<ProjectMetadataDocument>(json, JsonOptions)
            ?? throw new PersistentJsonSchemaException($"ProjectMetadata: Datei konnte nicht gelesen werden. Datei: {path}");
    }


    private static string RemoveLegacyDateFields(string json)
    {
        var root = JsonNode.Parse(json) as JsonObject;
        if (root is null)
            return json;

        var values = root["Values"] as JsonArray ?? root["values"] as JsonArray;
        if (values is null)
            return json;

        for (var index = values.Count - 1; index >= 0; index--)
        {
            if (values[index] is not JsonObject item)
                continue;

            var fieldNode = item["Field"] ?? item["field"];
            if (IsRetiredLegacyDateField(fieldNode))
                values.RemoveAt(index);
        }

        return root.ToJsonString(JsonOptions);
    }

    private static bool IsRetiredLegacyDateField(JsonNode? fieldNode)
    {
        if (fieldNode is not JsonValue fieldValue)
            return false;

        if (fieldValue.TryGetValue<string>(out var fieldName))
            return fieldName is "OriginalReleaseDate" or "PublicationDate";

        return fieldValue.TryGetValue<int>(out var legacyFieldId) && legacyFieldId is 26 or 27;
    }

    private static ProjectMetadataDocument FromMetadata(ProjectMetadata metadata)
    {
        var values = metadata.GetExplicitValues()
            .Where(item => ProjectMetadataFields.IsCanonicalField(item.Key))
            .Select(item => CreateFieldDocument(item.Key, item.Value))
            .Where(item => item is not null)
            .Select(item => item!)
            .OrderBy(item => item.Field)
            .ToList();

        return new ProjectMetadataDocument
        {
            SchemaVersion = CurrentSchemaVersion,
            Revision = metadata.Revision,
            Cover = metadata.Cover,
            UseNarratorsAsComposers = metadata.UseNarratorsAsComposers,
            Values = values,
            SuppressedValues = metadata.GetSuppressedValuesForPersistence()
                .Where(item => item.Key == ProjectMetadataField.Cover || ProjectMetadataFields.IsCanonicalField(item.Key))
                .ToDictionary(item => item.Key.ToString(), item => item.Value.ToList(), StringComparer.Ordinal)
        };
    }

    private static ProjectMetadataFieldDocument? CreateFieldDocument(ProjectMetadataField field, object? value)
    {
        var originProperty = value?.GetType().GetProperty(nameof(MetadataValue<string>.Origin));
        var valueProperty = value?.GetType().GetProperty(nameof(MetadataValue<string>.Value));
        if (originProperty is null || valueProperty is null)
            return null;

        var origin = (MetadataOrigin)(originProperty.GetValue(value) ?? MetadataOrigin.None);
        var rawValue = valueProperty.GetValue(value);
        var json = JsonSerializer.SerializeToElement(rawValue, rawValue?.GetType() ?? typeof(object), JsonOptions);

        return new ProjectMetadataFieldDocument
        {
            Field = field,
            Origin = origin,
            Value = json
        };
    }

    private static ProjectMetadata ToMetadata(ProjectMetadataDocument document)
    {
        var metadata = new ProjectMetadata();

        foreach (var item in document.Values)
        {
            if (ProjectMetadataFields.IsCanonicalField(item.Field))
                ApplyField(metadata, item);
        }

        metadata.SetCoverForPersistence(document.Cover);
        metadata.SetUseNarratorsAsComposersForPersistence(document.UseNarratorsAsComposers);
        metadata.SetSuppressedValuesForPersistence(
            (document.SuppressedValues ?? new Dictionary<string, List<string>>(StringComparer.Ordinal))
                .Where(item =>
                    Enum.TryParse<ProjectMetadataField>(item.Key, ignoreCase: false, out var field) &&
                    (field == ProjectMetadataField.Cover || ProjectMetadataFields.IsCanonicalField(field)))
                .ToDictionary(
                    item => Enum.Parse<ProjectMetadataField>(item.Key),
                    item => (IReadOnlyList<string>)(item.Value ?? []).ToArray()));
        metadata.SetRevisionForPersistence(document.Revision);
        return metadata;
    }

    private static void ApplyField(ProjectMetadata metadata, ProjectMetadataFieldDocument item)
    {
        switch (ProjectMetadataFields.GetValueKind(item.Field))
        {
            case ProjectMetadataFieldValueKind.String:
                metadata.SetValue(item.Field, Deserialize<string>(item.Value), item.Origin);
                break;
            case ProjectMetadataFieldValueKind.StringList:
                metadata.SetValue<IReadOnlyList<string>>(item.Field, Deserialize<string[]>(item.Value) ?? [], item.Origin);
                break;
            case ProjectMetadataFieldValueKind.SeriesList:
                metadata.SetValue<IReadOnlyList<SeriesEntry>>(item.Field, Deserialize<SeriesEntry[]>(item.Value) ?? [], item.Origin);
                break;
            case ProjectMetadataFieldValueKind.MetadataDate:
                metadata.SetValue(item.Field, Deserialize<MetadataDate?>(item.Value), item.Origin);
                break;
            case ProjectMetadataFieldValueKind.Integer:
                metadata.SetValue(item.Field, Deserialize<int?>(item.Value), item.Origin);
                break;
            case ProjectMetadataFieldValueKind.AudioWorkType:
                metadata.SetValue(item.Field, Deserialize<AudioWorkType?>(item.Value), item.Origin);
                break;
            case ProjectMetadataFieldValueKind.AbridgedStatus:
                metadata.SetValue(item.Field, Deserialize<AbridgedStatus?>(item.Value), item.Origin);
                break;
        }
    }

    private static T? Deserialize<T>(JsonElement element)
    {
        if (element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return default;

        return element.Deserialize<T>(JsonOptions);
    }

    private sealed class ProjectMetadataDocument
    {
        [JsonPropertyName("schemaVersion")]
        public int SchemaVersion { get; set; } = CurrentSchemaVersion;

        public long Revision { get; set; }

        public ProjectCoverMetadata? Cover { get; set; }

        public bool UseNarratorsAsComposers { get; set; }

        public List<ProjectMetadataFieldDocument> Values { get; set; } = [];

        public Dictionary<string, List<string>> SuppressedValues { get; set; } = new(StringComparer.Ordinal);

    }

    private sealed class ProjectMetadataFieldDocument
    {
        public ProjectMetadataField Field { get; set; }

        public MetadataOrigin Origin { get; set; }

        public JsonElement Value { get; set; }
    }
}
