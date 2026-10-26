using BookStitch.Models;
using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class MetadataPreferencesServiceTests
{
    [Fact]
    public void ConceptGroupPreference_RemembersSelectionOnlyWhileEnabled()
    {
        var preferences = new MetadataPreferences();

        Assert.Equal(
            ConceptMetadataGroupIds.QuickAccess,
            MetadataPreferencesService.GetInitialConceptGroupKey(preferences));

        Assert.True(MetadataPreferencesService.SetRememberLastConceptGroup(
            preferences,
            remember: true,
            ConceptMetadataGroupIds.People));
        Assert.Equal(ConceptMetadataGroupIds.People, preferences.LastConceptGroupKey);

        Assert.True(MetadataPreferencesService.RememberConceptGroupSelection(
            preferences,
            ConceptMetadataGroupIds.Language));
        Assert.Equal(
            ConceptMetadataGroupIds.Language,
            MetadataPreferencesService.GetInitialConceptGroupKey(preferences));

        Assert.True(MetadataPreferencesService.SetRememberLastConceptGroup(
            preferences,
            remember: false,
            ConceptMetadataGroupIds.Language));
        Assert.False(MetadataPreferencesService.RememberConceptGroupSelection(
            preferences,
            ConceptMetadataGroupIds.Cover));
        Assert.Equal(
            ConceptMetadataGroupIds.QuickAccess,
            MetadataPreferencesService.GetInitialConceptGroupKey(preferences));
    }

    [Fact]
    public void ConceptGroupPreference_NormalizesUnknownKeysToQuickAccess()
    {
        var preferences = new MetadataPreferences
        {
            RememberLastConceptGroup = true,
            LastConceptGroupKey = "localized label"
        };

        MetadataPreferencesService.Normalize(preferences);

        Assert.Equal(ConceptMetadataGroupIds.QuickAccess, preferences.LastConceptGroupKey);
        Assert.Equal(
            ConceptMetadataGroupIds.QuickAccess,
            MetadataPreferencesService.GetInitialConceptGroupKey(preferences));
    }
    [Fact]
    public void ConceptGroupPreference_maps_legacy_help_compatibility_to_help_and_accepts_compatibility()
    {
        var preferences = new MetadataPreferences
        {
            RememberLastConceptGroup = true,
            LastConceptGroupKey = "help-compatibility"
        };

        MetadataPreferencesService.Normalize(preferences);

        Assert.Equal(ConceptMetadataGroupIds.Help, preferences.LastConceptGroupKey);
        Assert.Equal(ConceptMetadataGroupIds.Compatibility, MetadataPreferencesService.NormalizeConceptGroupKey(ConceptMetadataGroupIds.Compatibility));
    }

    [Fact]
    public void Normalize_TrackLocksRequireValidPreferredValuesAndForceOutputEnabled()
    {
        var preferences = new MetadataPreferences
        {
            PreferredOutputTrackNumber = 7,
            OutputTrackNumberPreferenceLocked = true,
            PreferredOutputTrackTotal = -1,
            OutputTrackTotalPreferenceLocked = true,
            OutputPolicies = new Dictionary<string, OutputFieldPolicy>(StringComparer.Ordinal)
            {
                [ProjectMetadataField.OutputTrackNumber.ToString()] = OutputFieldPolicy.Disabled,
                [ProjectMetadataField.OutputTrackTotal.ToString()] = OutputFieldPolicy.Disabled,
                [ProjectMetadataField.Performers.ToString()] = OutputFieldPolicy.Enabled
            }
        };

        MetadataPreferencesService.Normalize(preferences);

        Assert.True(preferences.OutputTrackNumberPreferenceLocked);
        Assert.Equal(
            OutputFieldPolicy.Enabled,
            MetadataPreferencesService.GetPolicy(preferences, ProjectMetadataField.OutputTrackNumber));

        Assert.Null(preferences.PreferredOutputTrackTotal);
        Assert.False(preferences.OutputTrackTotalPreferenceLocked);
        Assert.DoesNotContain(ProjectMetadataField.Performers.ToString(), preferences.OutputPolicies.Keys);
    }

    [Fact]
    public void TechnicalConceptFieldNamePreference_ChangesOnlyWhenItsValueChanges()
    {
        var preferences = new MetadataPreferences();

        Assert.True(MetadataPreferencesService.SetShowTechnicalConceptFieldNames(preferences, true));
        Assert.True(preferences.ShowTechnicalConceptFieldNames);
        Assert.False(MetadataPreferencesService.SetShowTechnicalConceptFieldNames(preferences, true));
        Assert.True(MetadataPreferencesService.SetShowTechnicalConceptFieldNames(preferences, false));
        Assert.False(preferences.ShowTechnicalConceptFieldNames);
    }

    [Fact]
    public void ConceptDraftWarningPreference_DefaultsToEnabledAndCanBeDisabled()
    {
        var preferences = new MetadataPreferences();

        Assert.True(preferences.WarnBeforeDiscardingConceptDrafts);
        preferences.WarnBeforeDiscardingConceptDrafts = false;
        MetadataPreferencesService.Normalize(preferences);

        Assert.False(preferences.WarnBeforeDiscardingConceptDrafts);
    }

    [Fact]
    public void Composer_output_policy_uses_the_generic_canonical_field_policy_path()
    {
        var preferences = new MetadataPreferences();

        Assert.Equal(
            OutputFieldPolicy.Enabled,
            MetadataPreferencesService.GetPolicy(preferences, ProjectMetadataField.Composers));

        MetadataPreferencesService.SetPolicy(
            preferences,
            ProjectMetadataField.Composers,
            OutputFieldPolicy.Disabled);
        MetadataPreferencesService.Normalize(preferences);

        Assert.Equal(
            OutputFieldPolicy.Disabled,
            MetadataPreferencesService.GetPolicy(preferences, ProjectMetadataField.Composers));
        Assert.Equal(
            OutputFieldPolicy.Enabled,
            MetadataPreferencesService.GetPolicy(preferences, ProjectMetadataField.Narrators));
    }

}
