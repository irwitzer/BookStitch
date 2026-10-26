using BookStitch;
using BookStitch.Dialog;
using BookStitch.Controls;
using BookStitch.Models;
using BookStitch.Presentation;
using BookStitch.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using WpfApplication = System.Windows.Application;
using WpfResourceDictionary = System.Windows.ResourceDictionary;
using WpfShutdownMode = System.Windows.ShutdownMode;
using Xunit;

namespace BookStitch.Tests.Ui;

public sealed partial class WpfDialogRuntimeTests
{
    [Fact]
    public void Help_demo_resets_after_leaving_without_mutating_project_state()
    {
        RunOnStaThread(() =>
        {
            var metadataState = new ProjectMetadataState();
            var initialProjectGenres = metadataState.CanonicalMetadata
                .GetValue<IReadOnlyList<string>>(ProjectMetadataField.Genres).Value?.ToArray() ?? [];
            var preferences = new MetadataPreferences();
            var dialog = CreateMetadataDialog(metadataState, preferences);

            try
            {
                var helpDemo = Assert.IsType<ConceptMetadataFieldControl>(dialog.FindName("ConceptHelpDemoField"));
                Assert.Same(dialog.HelpDemoField, helpDemo.DataContext);

                Assert.Equal(["Abenteuer", "Meer", "Reise"], dialog.HelpDemoField.Values.Select(value => value.Value));
                Assert.True(dialog.HelpDemoField.IsOutputEnabled);
                Assert.False(dialog.HelpDemoField.IsLocked);

                var utilityList = Assert.IsType<ListBox>(dialog.FindName("ConceptUtilityFamilyList"));
                var helpItem = Assert.IsType<ListBoxItem>(dialog.FindName("ConceptHelpFamilyItem"));
                var passThroughItem = Assert.IsType<ListBoxItem>(dialog.FindName("ConceptPassThroughFamilyItem"));
                utilityList.SelectedItem = helpItem;
                DrainDispatcher();

                dialog.HelpDemoField.Draft = "Eigener Wert";
                dialog.HelpDemoField.CommitDraft();
                dialog.HelpDemoField.IsOutputEnabled = false;
                dialog.HelpDemoField.IsLocked = true;
                Assert.Contains(dialog.HelpDemoField.Values, value => value.Value == "Eigener Wert");

                utilityList.SelectedItem = passThroughItem;
                DrainDispatcher();

                Assert.Equal(["Abenteuer", "Meer", "Reise"], dialog.HelpDemoField.Values.Select(value => value.Value));
                Assert.Equal(string.Empty, dialog.HelpDemoField.Draft);
                Assert.True(dialog.HelpDemoField.IsOutputEnabled);
                Assert.False(dialog.HelpDemoField.IsLocked);
                Assert.Equal(
                    initialProjectGenres,
                    metadataState.CanonicalMetadata.GetValue<IReadOnlyList<string>>(ProjectMetadataField.Genres).Value ?? []);
                Assert.Empty(preferences.OutputPolicies);
                Assert.False(preferences.GenrePreferenceLocked);
            }
            finally
            {
                dialog.WarnBeforeDiscardingConceptDrafts = false;
                dialog.Close();
            }
        });
    }

    [Fact]
    public void Composer_binding_state_is_shared_across_people_compatibility_and_search_views()
    {
        RunWithComposerBindingDialog((dialog, _, _) =>
        {
            var peopleComposer = Assert.IsType<ConceptMetadataFieldControl>(dialog.FindName("ConceptPeopleComposerField"));
            var compatibilityComposer = Assert.IsType<ConceptMetadataFieldControl>(dialog.FindName("ConceptCompatibilityComposerField"));
            var peopleAccessory = Assert.IsType<ConceptMetadataFieldBindingAccessory>(peopleComposer.HeaderAccessory);
            var compatibilityAccessory = Assert.IsType<ConceptMetadataFieldBindingAccessory>(compatibilityComposer.HeaderAccessory);
            var peopleToggle = Assert.IsType<ToggleButton>(peopleAccessory.FindName("BindingToggle"));
            var compatibilityToggle = Assert.IsType<ToggleButton>(compatibilityAccessory.FindName("BindingToggle"));

            Assert.Same(dialog.ComposersField, peopleComposer.DataContext);
            Assert.Same(dialog.ComposersField, compatibilityComposer.DataContext);
            Assert.Same(dialog.NarratorComposerBinding, peopleComposer.FieldBinding);
            Assert.Same(dialog.NarratorComposerBinding, compatibilityComposer.FieldBinding);
            Assert.Same(dialog.NarratorComposerBinding, peopleAccessory.BindingState);
            Assert.Same(dialog.NarratorComposerBinding, compatibilityAccessory.BindingState);
            Assert.Equal("Sprecher übernehmen", peopleToggle.Content);
            Assert.Equal("Sprecher übernehmen", compatibilityToggle.Content);
            Assert.False(dialog.NarratorComposerBinding.IsActive);

            peopleToggle.IsChecked = true;
            DrainDispatcher();
            Assert.True(compatibilityToggle.IsChecked);

            var searchBox = Assert.IsType<TextBox>(dialog.FindName("ConceptSearchTextBox"));
            var searchPanel = Assert.IsType<StackPanel>(dialog.FindName("ConceptSearchResultsPanel"));
            var peopleFamily = Assert.IsType<ListBoxItem>(dialog.FindName("ConceptPeopleFamilyItem"));
            var compatibilityFamily = Assert.IsType<ListBoxItem>(dialog.FindName("ConceptCompatibilityFamilyItem"));
            searchBox.Text = "Komponist";
            DrainDispatcher();

            Assert.Equal(1d, peopleFamily.Opacity);
            Assert.Equal(1d, compatibilityFamily.Opacity);
            var searchComposer = Assert.Single(
                searchPanel.Children.OfType<ConceptMetadataFieldControl>(),
                control => ReferenceEquals(control.DataContext, dialog.ComposersField));
            var searchAccessory = Assert.IsType<ConceptMetadataFieldBindingAccessory>(searchComposer.HeaderAccessory);
            var searchToggle = Assert.IsType<ToggleButton>(searchAccessory.FindName("BindingToggle"));
            Assert.Same(dialog.NarratorComposerBinding, searchComposer.FieldBinding);
            Assert.Same(dialog.NarratorComposerBinding, searchAccessory.BindingState);
            Assert.Equal("Sprecher übernehmen", searchToggle.Content);

            searchToggle.IsChecked = false;
            DrainDispatcher();
            Assert.False(peopleToggle.IsChecked);
            Assert.False(compatibilityToggle.IsChecked);

            searchToggle.IsChecked = true;
            DrainDispatcher();
            Assert.True(peopleToggle.IsChecked);
            Assert.True(compatibilityToggle.IsChecked);
            searchBox.Clear();
            DrainDispatcher();
            Assert.True(dialog.NarratorComposerBinding.IsActive);
        });
    }

    [Fact]
    public void Composer_binding_activation_suppresses_target_values_and_projects_live_narrators()
    {
        RunWithComposerBindingDialog((dialog, metadataState, preferences) =>
        {
            var peopleComposer = Assert.IsType<ConceptMetadataFieldControl>(dialog.FindName("ConceptPeopleComposerField"));
            var peopleAccessory = Assert.IsType<ConceptMetadataFieldBindingAccessory>(peopleComposer.HeaderAccessory);
            var peopleToggle = Assert.IsType<ToggleButton>(peopleAccessory.FindName("BindingToggle"));

            peopleToggle.IsChecked = true;
            DrainDispatcher();

            Assert.True(dialog.NarratorComposerBinding.IsActive);
            Assert.True(MetadataFieldBindingPreferencesService.GetIsEnabled(
                preferences,
                ProjectMetadataFieldBindingCatalog.NarratorsToComposers));
            Assert.False(metadataState.CanonicalMetadata.UseNarratorsAsComposers);
            Assert.Empty(metadataState.CanonicalMetadata.GetValue<IReadOnlyList<string>>(ProjectMetadataField.Composers).Value ?? []);
            Assert.Equal(
                ["Komponist A", "Komponist B"],
                metadataState.CanonicalMetadata.GetSuppressedValues(ProjectMetadataField.Composers));
            Assert.Same(dialog.NarratorsField.Values, dialog.NarratorComposerBinding.ProjectedValues);
            Assert.Equal(["Sprecher A"], dialog.NarratorComposerBinding.ProjectedValues.Select(value => value.Value));

            dialog.NarratorsField.Draft = "Sprecher B";
            dialog.NarratorsField.CommitDraft();
            dialog.NarratorsField.MoveValue(dialog.NarratorsField.Values[1], 0);
            DrainDispatcher();
            Assert.Equal(["Sprecher B", "Sprecher A"], dialog.NarratorComposerBinding.ProjectedValues.Select(value => value.Value));

            dialog.NarratorsField.EditValue(dialog.NarratorsField.Values[0]);
            dialog.NarratorsField.Draft = "Sprecher B2";
            dialog.NarratorsField.CommitDraft();
            dialog.NarratorsField.RemoveValue(dialog.NarratorsField.Values[1]);
            DrainDispatcher();
            Assert.Equal("Sprecher B2", Assert.Single(dialog.NarratorComposerBinding.ProjectedValues).Value);
        });
    }

    [Fact]
    public void Composer_binding_active_and_output_states_control_editor_and_projection_presentation()
    {
        RunWithComposerBindingDialog((dialog, metadataState, _) =>
        {
            var peopleComposer = Assert.IsType<ConceptMetadataFieldControl>(dialog.FindName("ConceptPeopleComposerField"));
            var compatibilityComposer = Assert.IsType<ConceptMetadataFieldControl>(dialog.FindName("ConceptCompatibilityComposerField"));
            var compatibilityAccessory = Assert.IsType<ConceptMetadataFieldBindingAccessory>(compatibilityComposer.HeaderAccessory);
            var compatibilityToggle = Assert.IsType<ToggleButton>(compatibilityAccessory.FindName("BindingToggle"));
            var peopleEditor = Assert.IsType<Border>(peopleComposer.FindName("EditorColumnHost"));
            var compatibilityEditor = Assert.IsType<Border>(compatibilityComposer.FindName("EditorColumnHost"));
            var peopleProjection = Assert.IsType<ItemsControl>(peopleComposer.FindName("ReadOnlyProjectionValues"));
            var compatibilityProjection = Assert.IsType<ItemsControl>(compatibilityComposer.FindName("ReadOnlyProjectionValues"));

            compatibilityToggle.IsChecked = true;
            DrainDispatcher();

            Assert.Equal(System.Windows.Visibility.Collapsed, peopleEditor.Visibility);
            Assert.Equal(System.Windows.Visibility.Collapsed, compatibilityEditor.Visibility);
            Assert.Equal(System.Windows.Visibility.Visible, peopleProjection.Visibility);
            Assert.Equal(System.Windows.Visibility.Visible, compatibilityProjection.Visibility);
            Assert.Equal(dialog.NarratorComposerBinding.ProjectedValues.Select(value => value.Value), peopleProjection.Items.Cast<ConceptSingleValue>().Select(value => value.Value));
            Assert.Equal(dialog.NarratorComposerBinding.ProjectedValues.Select(value => value.Value), compatibilityProjection.Items.Cast<ConceptSingleValue>().Select(value => value.Value));
            Assert.Equal(1d, peopleProjection.Opacity);
            Assert.Equal(1d, compatibilityProjection.Opacity);

            dialog.ComposersField.IsOutputEnabled = false;
            DrainDispatcher();
            Assert.True(dialog.NarratorComposerBinding.IsActive);
            Assert.Equal(System.Windows.Visibility.Visible, peopleProjection.Visibility);
            Assert.Equal(System.Windows.Visibility.Visible, compatibilityProjection.Visibility);
            Assert.Equal(0.62d, peopleProjection.Opacity, 3);
            Assert.Equal(0.62d, compatibilityProjection.Opacity, 3);

            dialog.ComposersField.IsOutputEnabled = true;
            compatibilityToggle.IsChecked = false;
            DrainDispatcher();

            Assert.False(dialog.NarratorComposerBinding.IsActive);
            Assert.Equal(System.Windows.Visibility.Visible, peopleEditor.Visibility);
            Assert.Equal(System.Windows.Visibility.Visible, compatibilityEditor.Visibility);
            Assert.Equal(System.Windows.Visibility.Collapsed, peopleProjection.Visibility);
            Assert.Equal(System.Windows.Visibility.Collapsed, compatibilityProjection.Visibility);
            Assert.Empty(metadataState.CanonicalMetadata.GetValue<IReadOnlyList<string>>(ProjectMetadataField.Composers).Value ?? []);
        });
    }

    [Fact]
    public void Composer_binding_remains_active_for_empty_and_replaced_narrator_sources()
    {
        RunWithComposerBindingDialog((dialog, metadataState, _) =>
        {
            var peopleComposer = Assert.IsType<ConceptMetadataFieldControl>(dialog.FindName("ConceptPeopleComposerField"));
            var peopleAccessory = Assert.IsType<ConceptMetadataFieldBindingAccessory>(peopleComposer.HeaderAccessory);
            var peopleToggle = Assert.IsType<ToggleButton>(peopleAccessory.FindName("BindingToggle"));
            var peopleEditor = Assert.IsType<Border>(peopleComposer.FindName("EditorColumnHost"));
            var compatibilityComposer = Assert.IsType<ConceptMetadataFieldControl>(dialog.FindName("ConceptCompatibilityComposerField"));
            var compatibilityEditor = Assert.IsType<Border>(compatibilityComposer.FindName("EditorColumnHost"));

            peopleToggle.IsChecked = true;
            DrainDispatcher();
            foreach (var value in dialog.NarratorsField.Values.ToArray())
                dialog.NarratorsField.RemoveValue(value);
            DrainDispatcher();

            Assert.Empty(dialog.NarratorComposerBinding.ProjectedValues);
            Assert.True(dialog.NarratorComposerBinding.IsActive);
            Assert.Equal(System.Windows.Visibility.Collapsed, peopleEditor.Visibility);
            Assert.Equal(System.Windows.Visibility.Collapsed, compatibilityEditor.Visibility);

            var nextProject = new ProjectMetadata();
            nextProject.SetValue<IReadOnlyList<string>>(ProjectMetadataField.Narrators, ["Projekt B Sprecher"], MetadataOrigin.User);
            nextProject.SetValue<IReadOnlyList<string>>(ProjectMetadataField.Composers, ["Projekt B Komponist"], MetadataOrigin.User);
            metadataState.ApplyCanonicalMetadata(nextProject);
            DrainDispatcher();

            Assert.True(dialog.NarratorComposerBinding.IsActive);
            Assert.Equal(["Projekt B Sprecher"], dialog.NarratorComposerBinding.ProjectedValues.Select(value => value.Value));
            Assert.Empty(metadataState.CanonicalMetadata.GetValue<IReadOnlyList<string>>(ProjectMetadataField.Composers).Value ?? []);
            Assert.Equal(
                ["Projekt B Komponist"],
                metadataState.CanonicalMetadata.GetSuppressedValues(ProjectMetadataField.Composers));
        });
    }

}
