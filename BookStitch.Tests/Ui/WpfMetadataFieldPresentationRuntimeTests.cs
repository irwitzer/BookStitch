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
    public void Concept_metadata_origin_text_is_visible_by_default_and_can_be_collapsed_without_hiding_header()
    {
        RunOnStaThread(() =>
        {
            var metadata = new ProjectMetadata();
            metadata.SetValue(ProjectMetadataField.Album, "Album", MetadataOrigin.User);
            var field = CreateConceptField(metadata, ProjectMetadataField.Album);
            var control = new ConceptMetadataFieldControl { DataContext = field };
            PrepareControl(control);

            var origin = Assert.IsType<TextBlock>(control.FindName("OriginTextBlock"));
            var output = Assert.IsType<ToggleButton>(control.FindName("OutputToggle"));
            Assert.True(control.ShowOriginText);
            Assert.Equal(System.Windows.Visibility.Visible, origin.Visibility);
            Assert.Equal(System.Windows.Visibility.Visible, output.Visibility);

            control.ShowOriginText = false;
            control.UpdateLayout();
            Assert.Equal(System.Windows.Visibility.Collapsed, origin.Visibility);
            Assert.Equal(System.Windows.Visibility.Visible, output.Visibility);

            control.ShowOriginText = true;
            control.PresentationMode = ConceptMetadataFieldPresentationMode.Minimal;
            control.UpdateLayout();
            Assert.Equal(System.Windows.Visibility.Collapsed, origin.Visibility);
            Assert.Equal(System.Windows.Visibility.Visible, output.Visibility);

            control.PresentationMode = ConceptMetadataFieldPresentationMode.Standard;
            control.UpdateLayout();
            Assert.Equal(System.Windows.Visibility.Visible, origin.Visibility);
        });
    }

    [Fact]
    public void Minimal_single_value_presentation_reuses_edit_commit_and_explicit_discard_flow()
    {
        RunOnStaThread(() =>
        {
            var metadata = new ProjectMetadata();
            metadata.SetValue(ProjectMetadataField.Title, "Alter Titel", MetadataOrigin.User);
            var field = CreateConceptField(metadata, ProjectMetadataField.Title);
            var control = new ConceptMetadataFieldControl
            {
                DataContext = field,
                PresentationMode = ConceptMetadataFieldPresentationMode.Minimal
            };
            PrepareControl(control);

            var editorHost = Assert.IsType<Border>(control.FindName("EditorColumnHost"));
            var badges = Assert.IsType<ItemsControl>(control.FindName("ValueBadges"));
            var valueArea = Assert.IsType<Grid>(control.FindName("ValueAreaHost"));
            var toggle = Assert.IsType<Button>(control.FindName("MinimalEditorToggleButton"));

            Assert.False(control.IsMinimalEditorOpen);
            Assert.Equal(System.Windows.Visibility.Collapsed, editorHost.Visibility);
            Assert.Equal(System.Windows.Visibility.Visible, badges.Visibility);
            Assert.Equal(27d, valueArea.MinHeight);
            Assert.Equal("+", Assert.IsType<TextBlock>(toggle.Content).Text);
            Assert.Equal($"{field.Label} Eingabe öffnen", System.Windows.Automation.AutomationProperties.GetName(toggle));

            ClickMinimalEditorToggle(control);

            Assert.True(control.IsMinimalEditorOpen);
            Assert.Equal("Alter Titel", Assert.Single(field.Values).Value);
            Assert.Equal("Alter Titel", field.Draft);
            Assert.Equal(System.Windows.Visibility.Visible, editorHost.Visibility);
            Assert.Equal(System.Windows.Visibility.Collapsed, badges.Visibility);
            Assert.Equal(0d, valueArea.MinHeight);
            Assert.Equal("−", Assert.IsType<TextBlock>(toggle.Content).Text);
            Assert.Equal($"{field.Label} Eingabe schließen", System.Windows.Automation.AutomationProperties.GetName(toggle));

            field.Draft = "Neuer Titel";
            RaiseConceptFieldEnter(control);

            Assert.False(control.IsMinimalEditorOpen);
            Assert.Equal("Neuer Titel", Assert.Single(field.Values).Value);
            Assert.Equal(System.Windows.Visibility.Collapsed, editorHost.Visibility);
            Assert.Equal(System.Windows.Visibility.Visible, badges.Visibility);
            Assert.Equal("+", Assert.IsType<TextBlock>(toggle.Content).Text);

            ClickMinimalEditorToggle(control);
            Assert.True(control.IsMinimalEditorOpen);
            Assert.Equal("Neuer Titel", Assert.Single(field.Values).Value);
            Assert.Equal("Neuer Titel", field.Draft);

            field.Draft = "Nicht bestätigt";
            ClickMinimalEditorToggle(control);

            Assert.False(control.IsMinimalEditorOpen);
            Assert.Equal("Neuer Titel", Assert.Single(field.Values).Value);
            Assert.Equal(string.Empty, field.Draft);
            Assert.Equal(System.Windows.Visibility.Collapsed, editorHost.Visibility);
        });
    }

    [Fact]
    public void Minimal_multi_value_presentation_keeps_badges_visible_and_editor_open_across_commits()
    {
        RunOnStaThread(() =>
        {
            var metadata = new ProjectMetadata();
            metadata.SetValue<IReadOnlyList<string>>(
                ProjectMetadataField.Authors,
                ["Autor A", "Autor B"],
                MetadataOrigin.User);
            var field = CreateConceptField(metadata, ProjectMetadataField.Authors);
            var control = new ConceptMetadataFieldControl
            {
                DataContext = field,
                PresentationMode = ConceptMetadataFieldPresentationMode.Minimal
            };
            PrepareControl(control);

            var editorHost = Assert.IsType<Border>(control.FindName("EditorColumnHost"));
            var badges = Assert.IsType<ItemsControl>(control.FindName("ValueBadges"));
            var valueArea = Assert.IsType<Grid>(control.FindName("ValueAreaHost"));
            var toggle = Assert.IsType<Button>(control.FindName("MinimalEditorToggleButton"));

            Assert.False(field.IsSingle);
            Assert.False(control.IsMinimalEditorOpen);
            Assert.Equal(System.Windows.Visibility.Collapsed, editorHost.Visibility);
            Assert.Equal(System.Windows.Visibility.Visible, badges.Visibility);
            Assert.Equal(27d, valueArea.MinHeight);

            ClickMinimalEditorToggle(control);

            Assert.True(control.IsMinimalEditorOpen);
            Assert.Equal(2, field.Values.Count);
            Assert.Equal(System.Windows.Visibility.Visible, editorHost.Visibility);
            Assert.Equal(System.Windows.Visibility.Visible, badges.Visibility);
            Assert.Equal(8d, badges.Margin.Top);

            field.Draft = "Autor C";
            RaiseConceptFieldEnter(control);

            Assert.True(control.IsMinimalEditorOpen);
            Assert.Equal(["Autor A", "Autor B", "Autor C"], field.Values.Select(value => value.Value).ToArray());
            Assert.Equal(System.Windows.Visibility.Visible, editorHost.Visibility);

            ClickMinimalEditorToggle(control);
            Assert.False(control.IsMinimalEditorOpen);
            Assert.Equal(string.Empty, field.Draft);

            var edited = field.Values[0];
            Assert.True(control.BeginEditValue(edited));
            control.UpdateLayout();
            DrainDispatcher();

            Assert.True(control.IsMinimalEditorOpen);
            Assert.Contains(edited, field.Values);
            Assert.Equal("Autor A", field.Draft);
            Assert.Equal(System.Windows.Visibility.Visible, editorHost.Visibility);
            Assert.Equal(System.Windows.Visibility.Visible, badges.Visibility);

            ClickMinimalEditorToggle(control);
            field.IsOutputEnabled = false;
            control.UpdateLayout();
            Assert.False(control.IsMinimalEditorOpen);
            Assert.False(toggle.IsEnabled);
        });
    }

    [Fact]
    public void Minimal_presentation_can_be_opened_programmatically_only_when_editable_and_unbound()
    {
        RunOnStaThread(() =>
        {
            var metadata = new ProjectMetadata();
            var field = CreateConceptField(metadata, ProjectMetadataField.Album);
            var control = new ConceptMetadataFieldControl
            {
                DataContext = field,
                PresentationMode = ConceptMetadataFieldPresentationMode.Minimal
            };
            PrepareControl(control);

            var editorHost = Assert.IsType<Border>(control.FindName("EditorColumnHost"));
            var toggle = Assert.IsType<Button>(control.FindName("MinimalEditorToggleButton"));

            Assert.True(control.TryOpenMinimalEditor());
            DrainDispatcher();

            Assert.True(control.IsMinimalEditorOpen);
            Assert.Equal(System.Windows.Visibility.Visible, editorHost.Visibility);
            Assert.Equal("−", Assert.IsType<TextBlock>(toggle.Content).Text);
            Assert.False(control.TryOpenMinimalEditor());
        });
    }

    [Fact]
    public void Minimal_scope_discards_previous_draft_without_changing_committed_value()
    {
        RunOnStaThread(() =>
        {
            var metadata = new ProjectMetadata();
            metadata.SetValue(ProjectMetadataField.Title, "Bestätigt", MetadataOrigin.User);
            var scope = new ConceptMinimalPresentationScope();
            var title = CreateConceptField(metadata, ProjectMetadataField.Title);
            var album = CreateConceptField(metadata, ProjectMetadataField.Album);
            var titleControl = new ConceptMetadataFieldControl
            {
                DataContext = title,
                PresentationMode = ConceptMetadataFieldPresentationMode.Minimal,
                MinimalPresentationScope = scope
            };
            var albumControl = new ConceptMetadataFieldControl
            {
                DataContext = album,
                PresentationMode = ConceptMetadataFieldPresentationMode.Minimal,
                MinimalPresentationScope = scope
            };
            PrepareControl(titleControl);
            PrepareControl(albumControl);

            Assert.True(titleControl.BeginEditValue(Assert.Single(title.Values)));
            title.Draft = "Verwerfen";
            Assert.True(albumControl.TryOpenMinimalEditor());

            Assert.False(titleControl.IsMinimalEditorOpen);
            Assert.False(title.HasDraft);
            Assert.Equal("Bestätigt", Assert.Single(title.Values).Value);
            Assert.Equal("Bestätigt", metadata.GetValue<string>(ProjectMetadataField.Title).Value);
            Assert.True(albumControl.IsMinimalEditorOpen);
            Assert.Same(albumControl, scope.ActiveParticipant);
        });
    }

    [Fact]
    public void Separate_minimal_scopes_do_not_close_each_other()
    {
        RunOnStaThread(() =>
        {
            var metadata = new ProjectMetadata();
            var first = new ConceptMetadataFieldControl
            {
                DataContext = CreateConceptField(metadata, ProjectMetadataField.Title),
                PresentationMode = ConceptMetadataFieldPresentationMode.Minimal,
                MinimalPresentationScope = new ConceptMinimalPresentationScope()
            };
            var second = new ConceptMetadataFieldControl
            {
                DataContext = CreateConceptField(metadata, ProjectMetadataField.Album),
                PresentationMode = ConceptMetadataFieldPresentationMode.Minimal,
                MinimalPresentationScope = new ConceptMinimalPresentationScope()
            };
            PrepareControl(first);
            PrepareControl(second);

            Assert.True(first.TryOpenMinimalEditor());
            Assert.True(second.TryOpenMinimalEditor());
            Assert.True(first.IsMinimalEditorOpen);
            Assert.True(second.IsMinimalEditorOpen);
        });
    }

    [Fact]
    public void Minimal_presentation_cannot_open_an_editor_while_field_binding_is_active()
    {
        RunOnStaThread(() =>
        {
            var metadata = new ProjectMetadata();
            metadata.SetValue(ProjectMetadataField.Title, "Gebundener Titel", MetadataOrigin.User);
            var source = CreateConceptField(metadata, ProjectMetadataField.Title);
            var target = CreateConceptField(metadata, ProjectMetadataField.Album);
            var isActive = true;
            var binding = new ConceptMetadataFieldBindingState(
                ProjectMetadataFieldBindingCatalog.TitleToAlbum,
                source,
                target,
                () => isActive,
                value => isActive = value);
            var control = new ConceptMetadataFieldControl
            {
                DataContext = target,
                FieldBinding = binding,
                PresentationMode = ConceptMetadataFieldPresentationMode.Minimal
            };
            PrepareControl(control);

            var toggle = Assert.IsType<Button>(control.FindName("MinimalEditorToggleButton"));
            Assert.False(toggle.IsEnabled);
            Assert.False(control.IsMinimalEditorOpen);
            Assert.False(control.TryOpenMinimalEditor());
            Assert.Equal(
                System.Windows.Visibility.Collapsed,
                Assert.IsType<Border>(control.FindName("EditorColumnHost")).Visibility);

            ClickMinimalEditorToggle(control);

            Assert.False(control.IsMinimalEditorOpen);
            Assert.Equal(string.Empty, target.Draft);
        });
    }

    [Fact]
    public void Concept_metadata_standard_presentation_keeps_editor_visible_with_existing_single_value()
    {
        RunOnStaThread(() =>
        {
            var metadata = new ProjectMetadata();
            metadata.SetValue(ProjectMetadataField.Title, "Titel", MetadataOrigin.User);
            var field = CreateConceptField(metadata, ProjectMetadataField.Title);
            var control = new ConceptMetadataFieldControl { DataContext = field };
            PrepareControl(control);

            Assert.Equal(ConceptMetadataFieldPresentationMode.Standard, control.PresentationMode);
            Assert.Equal(
                System.Windows.Visibility.Visible,
                Assert.IsType<Border>(control.FindName("EditorColumnHost")).Visibility);
            Assert.Equal(8d, Assert.IsType<ItemsControl>(control.FindName("ValueBadges")).Margin.Top);
        });
    }

    [Fact]
    public void Generic_language_candidate_refresh_preserves_selected_semantic_candidate()
    {
        RunOnStaThread(() =>
        {
            var metadata = new ProjectMetadata();
            var field = new ConceptMetadataFieldViewModel(
                () => metadata,
                new MetadataPreferences(),
                new MetadataNormalizationService(),
                ProjectMetadataField.Languages,
                ConceptMetadataFieldCatalog.Get(ProjectMetadataField.Languages),
                _ => { },
                _ => { });

            var originalCandidate = new ConceptMetadataCandidate(
                ProjectMetadataField.Languages,
                "Deutsch (Schweiz) — de-CH",
                "de-CH",
                0,
                MetadataOrigin.StaticList);
            field.SetCandidates([originalCandidate]);

            var control = new ConceptMetadataFieldControl
            {
                DataContext = field
            };
            PrepareControl(control);

            control.EditorComboBox.SelectedItem = originalCandidate;
            DrainDispatcher();
            Assert.Equal("Deutsch (Schweiz) — de-CH", field.Draft);

            var snapshot = control.CaptureCandidateDraftState();
            var refreshedCandidate = new ConceptMetadataCandidate(
                ProjectMetadataField.Languages,
                "Deutsch (Schweiz) — de-CH",
                "de-CH",
                3,
                MetadataOrigin.SourceAggregation);
            field.SetCandidates([refreshedCandidate]);
            DrainDispatcher();
            control.RestoreCandidateDraftState(snapshot);
            DrainDispatcher();

            Assert.Equal("Deutsch (Schweiz) — de-CH", field.Draft);
            var restored = Assert.IsType<ConceptMetadataCandidate>(
                control.EditorComboBox.SelectedItem);
            Assert.Equal("de-CH", Assert.IsType<string>(restored.ValueToCommit));

            field.ApplyCandidate(restored);
            Assert.Equal(
                ["de-CH"],
                metadata.GetValue<IReadOnlyList<string>>(ProjectMetadataField.Languages).Value);
        });
    }

}
