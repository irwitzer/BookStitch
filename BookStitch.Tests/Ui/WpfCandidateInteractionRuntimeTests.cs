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
    [Theory]
    [InlineData(ProjectMetadataField.Languages, "fr", "français — fr", "F")]
    [InlineData(ProjectMetadataField.OriginalLanguages, "fr", "français — fr", "F")]
    [InlineData(ProjectMetadataField.Languages, "pt", "portugiesisch — pt", "PO")]
    [InlineData(ProjectMetadataField.OriginalLanguages, "pt", "portugiesisch — pt", "PO")]
    public void Language_enter_commits_technical_value_of_automatic_suggestion(
        ProjectMetadataField key, string code, string display, string input)
    {
        RunOnStaThread(() =>
        {
            var metadata = new ProjectMetadata();
            var field = new ConceptMetadataFieldViewModel(() => metadata,
                new MetadataPreferences { LanguagePreferenceLocked = false }, new MetadataNormalizationService(),
                key, ConceptMetadataFieldCatalog.Get(key), _ => { }, _ => { });
            field.SetCandidates([new ConceptMetadataCandidate(key, display, code, 1, MetadataOrigin.SourceAggregation)]);
            var control = new ConceptMetadataFieldControl { DataContext = field };
            PrepareControl(control);
            Assert.True(CandidateComboBoxInteraction.ProcessTextInput(control.EditorComboBox, input));
            control.EditorComboBox.GetBindingExpression(ComboBox.TextProperty)?.UpdateSource();
            RaiseConceptFieldEnter(control);
            Assert.Equal(code, Assert.Single(field.Values).Value);
        });
    }

    [Fact]
    public void Candidate_combo_interaction_keeps_typed_prefix_and_selects_only_completion()
    {
        RunOnStaThread(() =>
        {
            var candidate = new ConceptMetadataCandidate(
                ProjectMetadataField.Title,
                "Hallo",
                "Hallo",
                1,
                MetadataOrigin.SourceAggregation);
            var comboBox = CreateCandidateComboBox(candidate);
            var textBox = GetEditableTextBox(comboBox);

            AssertAutocompleteStep(comboBox, textBox, "H", "H", "allo");
            AssertAutocompleteStep(comboBox, textBox, "A", "HA", "llo");
            AssertAutocompleteStep(comboBox, textBox, "L", "HAL", "lo");
            AssertAutocompleteStep(comboBox, textBox, "L", "HALL", "o");
            AssertAutocompleteStep(comboBox, textBox, "O", "HALLO", string.Empty);
        });
    }

    [Fact]
    public void Candidate_combo_interaction_allows_free_text_and_normal_delete_keys()
    {
        RunOnStaThread(() =>
        {
            var candidate = new ConceptMetadataCandidate(
                ProjectMetadataField.Title,
                "Hallo",
                "Hallo",
                1,
                MetadataOrigin.SourceAggregation);
            var comboBox = CreateCandidateComboBox(candidate);
            var textBox = GetEditableTextBox(comboBox);

            Assert.True(CandidateComboBoxInteraction.ProcessTextInput(comboBox, "X"));
            Assert.Equal("X", textBox.Text);
            Assert.Equal(1, textBox.CaretIndex);
            Assert.Equal(0, textBox.SelectionLength);
            Assert.Null(comboBox.SelectedItem);

            Assert.True(CandidateComboBoxInteraction.ProcessTextInput(comboBox, "Y"));
            Assert.Equal("XY", textBox.Text);

            Assert.True(CandidateComboBoxInteraction.ProcessDeleteKey(comboBox, Key.Back));
            Assert.Equal("X", textBox.Text);
            Assert.Equal(1, textBox.CaretIndex);

            textBox.CaretIndex = 0;
            Assert.True(CandidateComboBoxInteraction.ProcessDeleteKey(comboBox, Key.Delete));
            Assert.Equal(string.Empty, textBox.Text);
            Assert.Equal(0, textBox.CaretIndex);
        });
    }


    [Fact]
    public void Candidate_combo_interaction_enforces_400_character_user_input_without_truncating_existing_text()
    {
        RunOnStaThread(() =>
        {
            var comboBox = CreateCandidateComboBox();
            CandidateComboBoxInteraction.SetMaxInputLength(comboBox, 400);
            var textBox = GetEditableTextBox(comboBox);

            Assert.True(CandidateComboBoxInteraction.ProcessTextInput(comboBox, new string('A', 401)));
            Assert.Equal(400, textBox.Text.Length);
            Assert.True(CandidateComboBoxInteraction.ProcessTextInput(comboBox, "B"));
            Assert.Equal(400, textBox.Text.Length);

            var existing = new string('X', 440);
            comboBox.Text = existing;
            textBox.Text = existing;
            textBox.CaretIndex = existing.Length;
            Assert.Equal(existing, textBox.Text);
            Assert.True(CandidateComboBoxInteraction.ProcessTextInput(comboBox, "Y"));
            Assert.Equal(existing, textBox.Text);
        });
    }

    [Fact]
    public void Candidate_combo_interaction_does_not_use_substring_or_commit_value_for_autocomplete()
    {
        RunOnStaThread(() =>
        {
            var wordBoundaryCandidate = new ConceptMetadataCandidate(
                ProjectMetadataField.Title,
                "Say Hallo",
                "Say Hallo",
                1,
                MetadataOrigin.SourceAggregation);
            var hiddenCommitCandidate = new ConceptMetadataCandidate(
                ProjectMetadataField.Title,
                "Anzeige",
                "Hallo",
                1,
                MetadataOrigin.SourceAggregation);
            var comboBox = CreateCandidateComboBox(wordBoundaryCandidate, hiddenCommitCandidate);
            var textBox = GetEditableTextBox(comboBox);

            Assert.True(CandidateComboBoxInteraction.ProcessTextInput(comboBox, "H"));
            Assert.Equal("H", textBox.Text);
            Assert.Equal(0, textBox.SelectionLength);
            Assert.Null(comboBox.SelectedItem);
        });
    }

    [Fact]
    public void Candidate_combo_interaction_explicit_selection_moves_caret_to_end()
    {
        RunOnStaThread(() =>
        {
            var candidate = new ConceptMetadataCandidate(
                ProjectMetadataField.Title,
                "Hallo",
                "Hallo",
                1,
                MetadataOrigin.SourceAggregation);
            var comboBox = CreateCandidateComboBox(candidate);
            var textBox = GetEditableTextBox(comboBox);

            comboBox.SelectedItem = candidate;
            DrainDispatcher();

            Assert.Equal("Hallo", textBox.Text);
            Assert.Equal(textBox.Text.Length, textBox.CaretIndex);
            Assert.Equal(0, textBox.SelectionLength);
            Assert.False(CandidateComboBoxInteraction.IsAutomaticSelection(comboBox));
        });
    }

    [Fact]
    public void Candidate_combo_interaction_confirmation_clears_autocomplete_selection_and_moves_caret_to_end()
    {
        RunOnStaThread(() =>
        {
            var candidate = new ConceptMetadataCandidate(
                ProjectMetadataField.Title,
                "Hallo",
                "Hallo",
                1,
                MetadataOrigin.SourceAggregation);
            var comboBox = CreateCandidateComboBox(candidate);
            var textBox = GetEditableTextBox(comboBox);

            AssertAutocompleteStep(comboBox, textBox, "H", "H", "allo");
            Assert.True(CandidateComboBoxInteraction.IsAutomaticSelection(comboBox));

            CandidateComboBoxInteraction.FinalizeExplicitSelection(comboBox);
            DrainDispatcher();

            Assert.Equal("Hallo", textBox.Text);
            Assert.Equal(textBox.Text.Length, textBox.CaretIndex);
            Assert.Equal(0, textBox.SelectionLength);
            Assert.False(CandidateComboBoxInteraction.IsAutomaticSelection(comboBox));
        });
    }

    [Fact]
    public void Generic_candidate_field_uses_shared_prefix_interaction()
    {
        RunOnStaThread(() =>
        {
            var metadata = new ProjectMetadata();
            var field = new ConceptMetadataFieldViewModel(
                () => metadata,
                new MetadataPreferences(),
                new MetadataNormalizationService(),
                ProjectMetadataField.Barcode,
                ConceptMetadataFieldCatalog.Get(ProjectMetadataField.Barcode),
                _ => { },
                _ => { });
            field.SetCandidates(
            [
                new ConceptMetadataCandidate(
                    ProjectMetadataField.Barcode,
                    "Hallo",
                    "Hallo",
                    1,
                    MetadataOrigin.SourceAggregation)
            ]);

            var control = new ConceptMetadataFieldControl { DataContext = field };
            PrepareControl(control);
            var textBox = GetEditableTextBox(control.EditorComboBox);

            Assert.True(CandidateComboBoxInteraction.GetIsEnabled(control.EditorComboBox));
            Assert.False(control.EditorComboBox.IsTextSearchEnabled);
            AssertAutocompleteStep(control.EditorComboBox, textBox, "H", "H", "allo");
            AssertAutocompleteStep(control.EditorComboBox, textBox, "A", "HA", "llo");
            AssertAutocompleteStep(control.EditorComboBox, textBox, "L", "HAL", "lo");
        });
    }

    [Fact]
    public void Concept_multi_value_enter_commits_user_casing_when_candidate_selection_is_automatic()
    {
        RunOnStaThread(() =>
        {
            var metadata = new ProjectMetadata();
            metadata.SetValue<IReadOnlyList<string>>(
                ProjectMetadataField.Authors,
                ["hallo"],
                MetadataOrigin.SourceAggregation);
            var field = new ConceptMetadataFieldViewModel(
                () => metadata,
                new MetadataPreferences(),
                new MetadataNormalizationService(),
                ProjectMetadataField.Authors,
                ConceptMetadataFieldCatalog.Get(ProjectMetadataField.Authors),
                _ => { },
                _ => { });
            field.SetCandidates(
            [
                new ConceptMetadataCandidate(
                    ProjectMetadataField.Authors,
                    "hallo",
                    "hallo",
                    2,
                    MetadataOrigin.SourceAggregation)
            ]);

            var control = new ConceptMetadataFieldControl { DataContext = field };
            PrepareControl(control);
            var textBox = GetEditableTextBox(control.EditorComboBox);

            Assert.True(CandidateComboBoxInteraction.ProcessTextInput(control.EditorComboBox, "H"));
            Assert.Equal("Hallo", textBox.Text);
            Assert.True(CandidateComboBoxInteraction.IsAutomaticSelection(control.EditorComboBox));
            control.EditorComboBox.GetBindingExpression(ComboBox.TextProperty)?.UpdateSource();
            Assert.Equal("Hallo", field.Draft);

            RaiseConceptFieldEnter(control);

            var value = Assert.Single(field.Values);
            Assert.Equal("Hallo", value.Value);
            Assert.True(value.IsDuplicateHighlighted);
            Assert.Equal(["Hallo"], metadata.GetValue<IReadOnlyList<string>>(ProjectMetadataField.Authors).Value);
            Assert.Equal(MetadataOrigin.User, metadata.GetValue<IReadOnlyList<string>>(ProjectMetadataField.Authors).Origin);
        });
    }

    [Fact]
    public void Concept_scalar_enter_commits_user_casing_when_candidate_selection_is_automatic()
    {
        RunOnStaThread(() =>
        {
            var metadata = new ProjectMetadata();
            metadata.SetValue(ProjectMetadataField.Title, "hallo", MetadataOrigin.SourceAggregation);
            var field = new ConceptMetadataFieldViewModel(
                () => metadata,
                new MetadataPreferences(),
                new MetadataNormalizationService(),
                ProjectMetadataField.Title,
                ConceptMetadataFieldCatalog.Get(ProjectMetadataField.Title),
                _ => { },
                _ => { });
            field.SetCandidates(
            [
                new ConceptMetadataCandidate(
                    ProjectMetadataField.Title,
                    "hallo",
                    "hallo",
                    2,
                    MetadataOrigin.SourceAggregation)
            ]);

            var control = new ConceptMetadataFieldControl { DataContext = field };
            PrepareControl(control);
            Assert.True(CandidateComboBoxInteraction.ProcessTextInput(control.EditorComboBox, "H"));
            control.EditorComboBox.GetBindingExpression(ComboBox.TextProperty)?.UpdateSource();
            Assert.True(CandidateComboBoxInteraction.IsAutomaticSelection(control.EditorComboBox));

            RaiseConceptFieldEnter(control);

            var value = Assert.Single(field.Values);
            Assert.Equal("Hallo", value.Value);
            Assert.True(value.IsDuplicateHighlighted);
            Assert.Equal("Hallo", metadata.GetValue<string>(ProjectMetadataField.Title).Value);
            Assert.Equal(MetadataOrigin.User, metadata.GetValue<string>(ProjectMetadataField.Title).Origin);
        });
    }

    [Fact]
    public void Concept_enter_keeps_exact_duplicate_flash_and_explicit_candidate_selection_contract()
    {
        RunOnStaThread(() =>
        {
            var metadata = new ProjectMetadata();
            metadata.SetValue(ProjectMetadataField.Title, "Hallo", MetadataOrigin.SourceAggregation);
            var field = new ConceptMetadataFieldViewModel(
                () => metadata,
                new MetadataPreferences(),
                new MetadataNormalizationService(),
                ProjectMetadataField.Title,
                ConceptMetadataFieldCatalog.Get(ProjectMetadataField.Title),
                _ => { },
                _ => { });
            var exact = new ConceptMetadataCandidate(
                ProjectMetadataField.Title,
                "Hallo",
                "Hallo",
                2,
                MetadataOrigin.SourceAggregation);
            var explicitCandidate = new ConceptMetadataCandidate(
                ProjectMetadataField.Title,
                "Auswahl",
                "Auswahl",
                1,
                MetadataOrigin.SourceAggregation);
            field.SetCandidates([exact, explicitCandidate]);

            var control = new ConceptMetadataFieldControl { DataContext = field };
            PrepareControl(control);
            Assert.True(CandidateComboBoxInteraction.ProcessTextInput(control.EditorComboBox, "H"));
            control.EditorComboBox.GetBindingExpression(ComboBox.TextProperty)?.UpdateSource();
            RaiseConceptFieldEnter(control);
            Assert.True(Assert.Single(field.Values).IsDuplicateHighlighted);
            Assert.Equal(MetadataOrigin.SourceAggregation, metadata.GetValue<string>(ProjectMetadataField.Title).Origin);

            field.DiscardDraft();
            control.EditorComboBox.SelectedItem = explicitCandidate;
            DrainDispatcher();
            Assert.False(CandidateComboBoxInteraction.IsAutomaticSelection(control.EditorComboBox));
            RaiseConceptFieldEnter(control);
            Assert.Equal("Auswahl", metadata.GetValue<string>(ProjectMetadataField.Title).Value);
            Assert.Equal(MetadataOrigin.User, metadata.GetValue<string>(ProjectMetadataField.Title).Origin);
        });
    }

    [Fact]
    public void Generic_candidate_refresh_preserves_autocomplete_selection_state()
    {
        RunOnStaThread(() =>
        {
            var metadata = new ProjectMetadata();
            var field = new ConceptMetadataFieldViewModel(
                () => metadata,
                new MetadataPreferences(),
                new MetadataNormalizationService(),
                ProjectMetadataField.Barcode,
                ConceptMetadataFieldCatalog.Get(ProjectMetadataField.Barcode),
                _ => { },
                _ => { });
            field.SetCandidates(
            [
                new ConceptMetadataCandidate(
                    ProjectMetadataField.Barcode,
                    "Hallo",
                    "Hallo",
                    1,
                    MetadataOrigin.SourceAggregation)
            ]);

            var control = new ConceptMetadataFieldControl { DataContext = field };
            PrepareControl(control);
            var textBox = GetEditableTextBox(control.EditorComboBox);
            AssertAutocompleteStep(control.EditorComboBox, textBox, "H", "H", "allo");

            var snapshot = control.CaptureCandidateDraftState();
            field.SetCandidates(
            [
                new ConceptMetadataCandidate(
                    ProjectMetadataField.Barcode,
                    "Hallo",
                    "Hallo",
                    4,
                    MetadataOrigin.SourceAggregation)
            ]);
            DrainDispatcher();
            control.RestoreCandidateDraftState(snapshot);
            DrainDispatcher();

            Assert.Equal("Hallo", textBox.Text);
            Assert.Equal(1, textBox.SelectionStart);
            Assert.Equal(4, textBox.SelectionLength);
            Assert.Equal("allo", textBox.SelectedText);
            Assert.True(CandidateComboBoxInteraction.IsAutomaticSelection(control.EditorComboBox));
        });
    }

    [Theory]
    [InlineData("ConceptQuickAccessTitleField", ProjectMetadataField.Title)]
    [InlineData("ConceptQuickAccessAuthorField", ProjectMetadataField.Authors)]
    public void Metadata_candidate_placements_use_the_shared_candidate_interaction(
        string controlName,
        ProjectMetadataField expectedField)
    {
        RunOnStaThread(() =>
        {
            var dialog = CreateMetadataDialog(new ProjectMetadataState(), new MetadataPreferences());

            try
            {
                var control = Assert.IsType<ConceptMetadataFieldControl>(dialog.FindName(controlName));
                PrepareControl(control);

                var field = Assert.IsType<ConceptMetadataFieldViewModel>(control.DataContext);
                Assert.Equal(expectedField, field.Field);
                Assert.True(CandidateComboBoxInteraction.GetIsEnabled(control.EditorComboBox));
                Assert.False(control.EditorComboBox.IsTextSearchEnabled);
            }
            finally
            {
                dialog.WarnBeforeDiscardingConceptDrafts = false;
                dialog.Close();
            }
        });
    }

    [Theory]
    [InlineData(ProjectMetadataField.Barcode, false)]
    [InlineData(ProjectMetadataField.SortNarrators, true)]
    public void Generic_candidate_refresh_preserves_explicit_edit_draft_and_caret(
        ProjectMetadataField fieldId,
        bool isMultiValue)
    {
        RunOnStaThread(() =>
        {
            const string value = "Draft-Wert";
            var metadata = new ProjectMetadata();
            if (isMultiValue)
            {
                metadata.SetValue<IReadOnlyList<string>>(
                    fieldId,
                    [value],
                    MetadataOrigin.User);
            }
            else
            {
                metadata.SetValue(fieldId, value, MetadataOrigin.User);
            }

            var field = new ConceptMetadataFieldViewModel(
                () => metadata,
                new MetadataPreferences(),
                new MetadataNormalizationService(),
                fieldId,
                ConceptMetadataFieldCatalog.Get(fieldId),
                _ => { },
                _ => { },
                candidateRefreshRequested: _ => { });

            field.SetCandidates(
            [
                new ConceptMetadataCandidate(
                    fieldId,
                    value,
                    value,
                    1,
                    MetadataOrigin.SourceAggregation,
                    IsCanonicalWinner: true)
            ]);

            var control = new ConceptMetadataFieldControl
            {
                DataContext = field
            };
            PrepareControl(control);

            var editedValue = Assert.Single(field.Values);
            Assert.True(control.BeginEditValue(editedValue));
            DrainDispatcher();

            Assert.Equal(value, Assert.Single(field.Values).Value);
            Assert.Equal(value, field.Draft);
            Assert.Equal(value, control.EditorComboBox.Text);

            control.EditorComboBox.ApplyTemplate();
            var editableTextBox = Assert.IsType<TextBox>(
                control.EditorComboBox.Template.FindName(
                    "PART_EditableTextBox",
                    control.EditorComboBox));
            Assert.Equal(value, editableTextBox.Text);
            Assert.Equal(value.Length, editableTextBox.CaretIndex);
            Assert.Equal(0, editableTextBox.SelectionLength);

            var snapshot = control.CaptureCandidateDraftState();
            field.SetCandidates(
            [
                new ConceptMetadataCandidate(
                    fieldId,
                    "Andere Quelle",
                    "Andere Quelle",
                    2,
                    MetadataOrigin.SourceAggregation),
                new ConceptMetadataCandidate(
                    fieldId,
                    value,
                    value,
                    1,
                    MetadataOrigin.User)
            ]);
            DrainDispatcher();
            control.RestoreCandidateDraftState(snapshot);
            DrainDispatcher();

            Assert.Equal(value, field.Draft);
            Assert.Equal(value, control.EditorComboBox.Text);
            Assert.Equal(value, editableTextBox.Text);
            Assert.Equal(value.Length, editableTextBox.CaretIndex);
            Assert.Equal(0, editableTextBox.SelectionLength);
        });
    }


}
