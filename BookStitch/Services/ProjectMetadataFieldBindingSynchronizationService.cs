using BookStitch.Models;

namespace BookStitch.Services;

public sealed class ProjectMetadataFieldBindingSynchronizationService
{
    private readonly ProjectMetadataFieldBindingProjectionService _projection = new();

    public bool SynchronizeActiveTargets(
        ProjectMetadata metadata,
        IReadOnlyDictionary<string, bool> bindingSnapshot,
        IEnumerable<ProjectMetadataFieldBindingDefinition>? definitions = null)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(bindingSnapshot);

        var registered = (definitions ?? ProjectMetadataFieldBindingCatalog.All).ToArray();
        MetadataFieldBindingPreferencesService.AssertValidDefinitions(registered);
        var changed = false;

        foreach (var targetGroup in registered
                     .Where(binding => MetadataFieldBindingPreferencesService.ResolveIsEnabled(bindingSnapshot, binding))
                     .GroupBy(binding => binding.TargetField))
        {
            var active = targetGroup.ToArray();
            if (active.Length > 1)
            {
                throw new InvalidOperationException(
                    $"Multiple active metadata field bindings target '{targetGroup.Key}': {string.Join(", ", active.Select(item => item.BindingId))}.");
            }

            var binding = active[0];
            var values = _projection.ReadTextValues(metadata, binding.TargetField);
            foreach (var value in values)
                changed |= metadata.SuppressValue(binding.TargetField, value);

            changed |= ProjectMetadataFields.GetValueKind(binding.TargetField) switch
            {
                ProjectMetadataFieldValueKind.StringList => metadata.SetValue<IReadOnlyList<string>>(
                    binding.TargetField,
                    [],
                    MetadataOrigin.None),
                ProjectMetadataFieldValueKind.String => metadata.SetValue<string>(
                    binding.TargetField,
                    null,
                    MetadataOrigin.None),
                var kind => throw new InvalidOperationException(
                    $"Field binding target synchronization does not support '{kind}' for '{binding.TargetField}'.")
            };
        }

        return changed;
    }
}
