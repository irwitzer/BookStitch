using BookStitch.Models;

namespace BookStitch.Services;

public sealed class ProjectMetadataFieldBindingProjectionService
{
    public IReadOnlyList<string> ResolveTargetTextValues(
        ProjectMetadata metadata,
        ProjectMetadataField targetField,
        IReadOnlyDictionary<string, bool>? bindingSnapshot = null,
        IEnumerable<ProjectMetadataFieldBindingDefinition>? definitions = null)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        var registered = (definitions ?? ProjectMetadataFieldBindingCatalog.All).ToArray();
        MetadataFieldBindingPreferencesService.AssertValidDefinitions(registered);

        var active = registered
            .Where(binding => binding.TargetField == targetField)
            .Where(binding => MetadataFieldBindingPreferencesService.ResolveIsEnabled(bindingSnapshot, binding))
            .ToArray();
        if (active.Length > 1)
        {
            throw new InvalidOperationException(
                $"Multiple active metadata field bindings target '{targetField}': {string.Join(", ", active.Select(item => item.BindingId))}.");
        }

        var effectiveField = active.Length == 1 ? active[0].SourceField : targetField;
        return ReadTextValues(metadata, effectiveField);
    }

    public IReadOnlyList<string> ResolveTargetTextValues(
        ProjectMetadata metadata,
        ProjectMetadataFieldBindingDefinition binding,
        bool isActive)
    {
        ArgumentNullException.ThrowIfNull(binding);
        var snapshot = new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            [binding.BindingId] = isActive
        };
        return ResolveTargetTextValues(metadata, binding.TargetField, snapshot, [binding]);
    }

    public IReadOnlyList<string> ReadTextValues(ProjectMetadata metadata, ProjectMetadataField field)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        return ProjectMetadataFields.GetValueKind(field) switch
        {
            ProjectMetadataFieldValueKind.StringList =>
                metadata.GetValue<IReadOnlyList<string>>(field).Value?
                    .Select(Normalize)
                    .Where(value => value.Length > 0)
                    .ToArray() ?? [],
            ProjectMetadataFieldValueKind.String =>
                Normalize(metadata.GetValue<string>(field).Value) is { Length: > 0 } value
                    ? [value]
                    : [],
            _ => throw new InvalidOperationException(
                $"Field binding text projection does not support '{ProjectMetadataFields.GetValueKind(field)}' for '{field}'.")
        };
    }

    private static string Normalize(string? value) => value?.Trim() ?? string.Empty;
}
