using BookStitch.Models;
using System.Globalization;

namespace BookStitch.Services;

public static class MetadataPreferencesService
{
    public static OutputFieldPolicy GetPolicy(
        MetadataPreferences preferences,
        ProjectMetadataField field)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        return preferences.OutputPolicies.TryGetValue(field.ToString(), out var policy)
            ? policy
            : OutputFieldPolicy.Enabled;
    }

    public static bool SetPolicy(
        MetadataPreferences preferences,
        ProjectMetadataField field,
        OutputFieldPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        if (GetPolicy(preferences, field) == policy)
            return false;

        if (policy == OutputFieldPolicy.Enabled)
            preferences.OutputPolicies.Remove(field.ToString());
        else
            preferences.OutputPolicies[field.ToString()] = policy;

        return true;
    }

    public static IReadOnlyDictionary<ProjectMetadataField, OutputFieldPolicy> CreateSnapshot(
        MetadataPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        return ProjectMetadataFields.OutputFields.ToDictionary(
            field => field,
            field => GetPolicy(preferences, field));
    }

    public static void Normalize(MetadataPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);

        preferences.OutputPolicies = (preferences.OutputPolicies ?? new Dictionary<string, OutputFieldPolicy>())
            .Where(item => Enum.TryParse<ProjectMetadataField>(item.Key, ignoreCase: false, out var field) &&
                           ProjectMetadataFields.IsCanonicalField(field) &&
                           item.Value == OutputFieldPolicy.Disabled)
            .ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);

        MetadataFieldBindingPreferencesService.Normalize(preferences);

        preferences.PreferredGenres = NormalizeList(preferences.PreferredGenres);
        preferences.PreferredLanguages = NormalizeLanguages(preferences.PreferredLanguages);

        if (preferences.PreferredGenres.Count == 0)
            preferences.GenrePreferenceLocked = false;
        if (preferences.PreferredLanguages.Count == 0)
            preferences.LanguagePreferenceLocked = false;

        if (preferences.LanguagePreferenceLocked)
            SetPolicy(preferences, ProjectMetadataField.Languages, OutputFieldPolicy.Enabled);

        if (preferences.PreferredAudioWorkType is not (AudioWorkType.Audiobook or AudioWorkType.AudioDrama))
        {
            preferences.PreferredAudioWorkType = null;
            preferences.AudioWorkTypePreferenceLocked = false;
        }
        if (preferences.AudioWorkTypePreferenceLocked)
            SetPolicy(preferences, ProjectMetadataField.AudioWorkType, OutputFieldPolicy.Enabled);

        if (preferences.PreferredOutputTrackNumber is < 0)
            preferences.PreferredOutputTrackNumber = null;
        if (preferences.PreferredOutputTrackTotal is < 0)
            preferences.PreferredOutputTrackTotal = null;

        if (!preferences.PreferredOutputTrackNumber.HasValue)
            preferences.OutputTrackNumberPreferenceLocked = false;
        if (!preferences.PreferredOutputTrackTotal.HasValue)
            preferences.OutputTrackTotalPreferenceLocked = false;

        if (preferences.OutputTrackNumberPreferenceLocked)
            SetPolicy(preferences, ProjectMetadataField.OutputTrackNumber, OutputFieldPolicy.Enabled);
        if (preferences.OutputTrackTotalPreferenceLocked)
            SetPolicy(preferences, ProjectMetadataField.OutputTrackTotal, OutputFieldPolicy.Enabled);

        preferences.LastConceptGroupKey = NormalizeConceptGroupKey(preferences.LastConceptGroupKey);
    }

    public static string GetInitialConceptGroupKey(MetadataPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        return preferences.RememberLastConceptGroup
            ? NormalizeConceptGroupKey(preferences.LastConceptGroupKey)
            : ConceptMetadataGroupIds.QuickAccess;
    }

    public static bool SetRememberLastConceptGroup(
        MetadataPreferences preferences,
        bool remember,
        string? selectedGroupKey)
    {
        ArgumentNullException.ThrowIfNull(preferences);

        var changed = preferences.RememberLastConceptGroup != remember;
        preferences.RememberLastConceptGroup = remember;

        if (remember)
        {
            var normalizedKey = NormalizeConceptGroupKey(selectedGroupKey);
            if (!string.Equals(preferences.LastConceptGroupKey, normalizedKey, StringComparison.Ordinal))
            {
                preferences.LastConceptGroupKey = normalizedKey;
                changed = true;
            }
        }

        return changed;
    }

    public static bool RememberConceptGroupSelection(
        MetadataPreferences preferences,
        string? selectedGroupKey)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        if (!preferences.RememberLastConceptGroup)
            return false;

        var normalizedKey = NormalizeConceptGroupKey(selectedGroupKey);
        if (string.Equals(preferences.LastConceptGroupKey, normalizedKey, StringComparison.Ordinal))
            return false;

        preferences.LastConceptGroupKey = normalizedKey;
        return true;
    }

    public static bool SetShowTechnicalConceptFieldNames(
        MetadataPreferences preferences,
        bool showTechnicalFieldNames)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        if (preferences.ShowTechnicalConceptFieldNames == showTechnicalFieldNames)
            return false;

        preferences.ShowTechnicalConceptFieldNames = showTechnicalFieldNames;
        return true;
    }

    public static string NormalizeConceptGroupKey(string? groupKey)
    {
        if (string.Equals(groupKey, "help-compatibility", StringComparison.Ordinal))
            return ConceptMetadataGroupIds.Help;

        return !string.IsNullOrWhiteSpace(groupKey) &&
               ConceptMetadataGroupIds.All.Contains(groupKey, StringComparer.Ordinal)
            ? groupKey
            : ConceptMetadataGroupIds.QuickAccess;
    }

    public static bool TryCanonicalizeLanguage(string? value, out string canonical)
    {
        canonical = string.Empty;
        var candidate = value?.Trim().Replace('_', '-') ?? string.Empty;
        if (candidate.Length == 0)
            return false;

        try
        {
            canonical = CultureInfo.GetCultureInfo(candidate).Name;
            return canonical.Length > 0;
        }
        catch (CultureNotFoundException)
        {
            var parts = candidate.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 0 || parts[0].Length is < 2 or > 8 ||
                parts.Any(part => part.Length is < 1 or > 8 || !part.All(char.IsLetterOrDigit)))
            {
                return false;
            }

            canonical = string.Join('-', parts.Select((part, index) =>
                index == 0 ? part.ToLowerInvariant() : part));
            return true;
        }
    }

    private static List<string> NormalizeList(IEnumerable<string>? values) =>
        (values ?? [])
            .Select(value => value?.Trim() ?? string.Empty)
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static List<string> NormalizeLanguages(IEnumerable<string>? values)
    {
        var result = new List<string>();
        foreach (var value in values ?? [])
        {
            if (TryCanonicalizeLanguage(value, out var canonical) &&
                !result.Contains(canonical, StringComparer.OrdinalIgnoreCase))
            {
                result.Add(canonical);
            }
        }

        return result;
    }
}
