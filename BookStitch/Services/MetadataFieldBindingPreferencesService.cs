using System.Collections.ObjectModel;
using BookStitch.Models;

namespace BookStitch.Services;

public static class MetadataFieldBindingPreferencesService
{
    public static bool GetIsEnabled(
        MetadataPreferences preferences,
        ProjectMetadataFieldBindingDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        ArgumentNullException.ThrowIfNull(definition);

        return preferences.MetadataFieldBindings.TryGetValue(definition.BindingId, out var value)
            ? value
            : definition.DefaultEnabled;
    }

    public static bool SetIsEnabled(
        MetadataPreferences preferences,
        ProjectMetadataFieldBindingDefinition definition,
        bool value)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        ArgumentNullException.ThrowIfNull(definition);

        if (preferences.MetadataFieldBindings.TryGetValue(definition.BindingId, out var current) && current == value)
            return false;

        preferences.MetadataFieldBindings[definition.BindingId] = value;
        return true;
    }

    public static bool ResolveIsEnabled(
        IReadOnlyDictionary<string, bool>? snapshot,
        ProjectMetadataFieldBindingDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return snapshot is not null && snapshot.TryGetValue(definition.BindingId, out var value)
            ? value
            : definition.DefaultEnabled;
    }

    public static IReadOnlyDictionary<string, bool> CreateSnapshot(
        MetadataPreferences preferences,
        IEnumerable<ProjectMetadataFieldBindingDefinition>? definitions = null)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        var registered = (definitions ?? ProjectMetadataFieldBindingCatalog.All).ToArray();
        AssertValidDefinitions(registered);
        var snapshot = registered.ToDictionary(
            definition => definition.BindingId,
            definition => GetIsEnabled(preferences, definition),
            StringComparer.Ordinal);
        return new ReadOnlyDictionary<string, bool>(snapshot);
    }

    public static void Normalize(MetadataPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        var knownIds = ProjectMetadataFieldBindingCatalog.All
            .Select(definition => definition.BindingId)
            .ToHashSet(StringComparer.Ordinal);

        preferences.MetadataFieldBindings = (preferences.MetadataFieldBindings ?? new Dictionary<string, bool>())
            .Where(item => knownIds.Contains(item.Key))
            .ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
    }

    public static void AssertValidDefinitions(IEnumerable<ProjectMetadataFieldBindingDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        var items = definitions.ToArray();
        var duplicateId = items.GroupBy(item => item.BindingId, StringComparer.Ordinal).FirstOrDefault(group => group.Count() > 1);
        if (duplicateId is not null)
            throw new InvalidOperationException($"Duplicate metadata field binding ID '{duplicateId.Key}'.");
    }
}
