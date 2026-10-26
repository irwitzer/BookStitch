using BookStitch.Controls;
using BookStitch.Dialog;
using BookStitch.Models;
using BookStitch.Services;
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Xunit;

namespace BookStitch.Tests.Ui;

public sealed partial class WpfDialogRuntimeTests
{
    [Fact]
    public void Removing_source_year_persists_suppression_and_manual_restore_removes_it()
    {
        RunOnStaThread(() =>
        {
            using var folder = new BookStitch.Tests.TestHelpers.TemporaryFolder();
            var metadataState = new ProjectMetadataState();
            var store = new ProjectMetadataStore();
            metadataState.CanonicalMetadata.SetValue<int?>(ProjectMetadataField.Year, 2026, MetadataOrigin.SourceAggregation);
            var state = new ConceptSpecialMetadataFieldState(
                metadataState, new MetadataPreferences(), ProjectMetadataField.Year,
                metadata => store.Save(folder.Path, metadata), _ => { });

            state.RemoveValue();
            var restored = store.LoadOrCreate(folder.Path);
            Assert.Null(restored.GetValue<int?>(ProjectMetadataField.Year).Value);
            Assert.Equal(new[] { "2026" }, restored.GetSuppressedValues(ProjectMetadataField.Year));

            state.CommitValue(2026, MetadataOrigin.User);
            restored = store.LoadOrCreate(folder.Path);
            Assert.Equal(2026, restored.GetValue<int?>(ProjectMetadataField.Year).Value);
            Assert.Empty(restored.GetSuppressedValues(ProjectMetadataField.Year));
        });
    }

    [Fact]
    public void Shared_series_control_hosts_outside_metadata_dialog_and_commits_canonical_state()
    {
        RunOnStaThread(() =>
        {
            var metadataState = new ProjectMetadataState();
            var preferences = new MetadataPreferences();
            var state = new ConceptSeriesFieldState(
                metadataState,
                preferences,
                new MetadataNormalizationService(),
                _ => { },
                _ => { });
            var control = new ConceptSeriesFieldControl
            {
                DataContext = state,
                Width = 620
            };
            var host = new Window { Content = control, Width = 700, Height = 260, ShowInTaskbar = false };

            try
            {
                host.Show();
                DrainDispatcher();
                Assert.True(control.ActualWidth > 0);

                state.SeriesNameDraft = "Saga der Horizonte";
                state.SeriesPartDraft = "2.5";
                Assert.True(state.CommitDraft());

                var canonical = metadataState.CanonicalMetadata
                    .GetValue<IReadOnlyList<SeriesEntry>>(ProjectMetadataField.Series).Value;
                var entry = Assert.Single(canonical!);
                Assert.Equal("Saga der Horizonte", entry.Name);
                Assert.Equal("2.5", entry.Part);
                Assert.Single(state.SeriesValues);
            }
            finally
            {
                host.Close();
            }
        });
    }

    [Fact]
    public void Shared_description_control_hosts_outside_metadata_dialog_and_preserves_delete_semantics()
    {
        RunOnStaThread(() =>
        {
            var metadataState = new ProjectMetadataState();
            var preferences = new MetadataPreferences();
            var normalization = new MetadataNormalizationService();
            var field = new ConceptMetadataFieldViewModel(
                () => metadataState.CanonicalMetadata,
                preferences,
                normalization,
                ProjectMetadataField.Description,
                ConceptMetadataGroupCatalog.Description.Descriptor,
                _ => { },
                _ => { },
                isMultiline: true);
            var state = new ConceptDescriptionFieldState(metadataState, field, _ => { });
            var control = new ConceptDescriptionFieldControl { DataContext = state, Width = 620 };
            var host = new Window { Content = control, Width = 700, Height = 360, ShowInTaskbar = false };

            try
            {
                host.Show();
                DrainDispatcher();
                Assert.True(control.ActualWidth > 0);

                state.DescriptionText = "Gemeinsame Beschreibung";
                state.CommitDraft();
                Assert.Equal(
                    "Gemeinsame Beschreibung",
                    metadataState.CanonicalMetadata.GetValue<string>(ProjectMetadataField.Description).Value);

                state.DescriptionText = "   ";
                state.CommitDraft();
                Assert.Null(metadataState.CanonicalMetadata.GetValue<string>(ProjectMetadataField.Description).Value);
            }
            finally
            {
                host.Close();
            }
        });
    }

    [Fact]
    public void Minimal_description_uses_full_top_aligned_editor_and_scope_discard_survives_lost_focus()
    {
        RunOnStaThread(() =>
        {
            var metadataState = new ProjectMetadataState();
            metadataState.CanonicalMetadata.SetValue(ProjectMetadataField.Description, "Bestätigt", MetadataOrigin.User);
            var preferences = new MetadataPreferences();
            var saves = 0;
            var descriptionField = new ConceptMetadataFieldViewModel(
                () => metadataState.CanonicalMetadata,
                preferences,
                new MetadataNormalizationService(),
                ProjectMetadataField.Description,
                ConceptMetadataGroupCatalog.Description.Descriptor,
                _ => saves++,
                _ => { },
                isMultiline: true);
            var descriptionState = new ConceptDescriptionFieldState(metadataState, descriptionField, _ => saves++);
            var scope = new ConceptMinimalPresentationScope();
            var description = new ConceptDescriptionFieldControl
            {
                DataContext = descriptionState,
                PresentationMode = ConceptDescriptionPresentationMode.Minimal,
                MinimalPresentationScope = scope,
                Height = 220
            };
            var titleControl = new ConceptMetadataFieldControl
            {
                DataContext = CreateConceptField(metadataState.CanonicalMetadata, ProjectMetadataField.Title),
                PresentationMode = ConceptMetadataFieldPresentationMode.Minimal,
                MinimalPresentationScope = scope
            };
            var host = new Window
            {
                Content = new StackPanel { Children = { description, titleControl } },
                Width = 700,
                Height = 420,
                ShowInTaskbar = false
            };

            try
            {
                host.Show();
                DrainDispatcher();
                Assert.True(description.TryOpenMinimalEditor());
                descriptionState.DescriptionText = "Nicht bestätigt";
                description.Editor.CaretIndex = 0;

                Assert.Equal(VerticalAlignment.Stretch, description.Editor.VerticalAlignment);
                Assert.Equal(VerticalAlignment.Top, description.Editor.VerticalContentAlignment);
                Assert.Equal(int.MaxValue, description.Editor.MaxLines);
                Assert.Equal(2, Grid.GetColumnSpan(Assert.IsType<Border>(description.FindName("DescriptionEditorBorder"))));
                Assert.Equal(0, description.Editor.CaretIndex);

                Assert.True(titleControl.TryOpenMinimalEditor());
                DrainDispatcher();

                Assert.False(description.IsMinimalEditorOpen);
                Assert.False(descriptionState.HasDraft);
                Assert.Equal("Bestätigt", metadataState.CanonicalMetadata.GetValue<string>(ProjectMetadataField.Description).Value);
                Assert.Equal(0, saves);
            }
            finally
            {
                host.Close();
            }
        });
    }

    [Fact]
    public void Project_setup_and_metadata_dialog_share_series_and_description_through_canonical_state()
    {
        RunOnStaThread(() =>
        {
            var metadataState = new ProjectMetadataState();
            var preferences = new MetadataPreferences { WarnBeforeDiscardingConceptDrafts = false };
            var setup = CreateProjectSetupDialog(
                ProjectSetupSourceKind.Folder,
                getAnimationMilliseconds: () => 0,
                metadataState: metadataState,
                metadataPreferences: preferences);

            try
            {
                setup.Show();
                DrainDispatcher();
                Assert.True(setup.NavigateSetupPage(1));
                DrainDispatcher();

                var setupSeries = Assert.IsType<ConceptSeriesFieldState>(setup.SetupSeriesField.DataContext);
                var setupDescription = Assert.IsType<ConceptDescriptionFieldState>(setup.SetupDescriptionField.DataContext);
                setupSeries.SeriesNameDraft = "Setup-Reihe";
                setupSeries.SeriesPartDraft = "4";
                Assert.True(setupSeries.CommitDraft());
                setupDescription.DescriptionText = "Setup-Beschreibung";
                setupDescription.CommitDraft();

                var metadataDialog = CreateMetadataDialog(metadataState, preferences);
                try
                {
                    var visibleSeries = Assert.Single(metadataDialog.SeriesValues);
                    Assert.Equal("Setup-Reihe", visibleSeries.Name);
                    Assert.Equal("4", visibleSeries.Part);
                    Assert.Equal("Setup-Beschreibung", metadataDialog.DescriptionText);

                    metadataDialog.SeriesFieldState.RemoveValue(visibleSeries);
                    metadataDialog.SeriesFieldState.SeriesNameDraft = "Dialog-Reihe";
                    metadataDialog.SeriesFieldState.SeriesPartDraft = "7";
                    Assert.True(metadataDialog.SeriesFieldState.CommitDraft());
                    metadataDialog.DescriptionFieldState.DescriptionText = "Dialog-Beschreibung";
                    metadataDialog.DescriptionFieldState.CommitDraft();
                    DrainDispatcher();

                    var refreshed = Assert.Single(setupSeries.SeriesValues);
                    Assert.Equal("Dialog-Reihe", refreshed.Name);
                    Assert.Equal("7", refreshed.Part);
                    Assert.Equal("Dialog-Beschreibung", setupDescription.DescriptionText);
                }
                finally
                {
                    metadataDialog.Close();
                }
            }
            finally
            {
                setup.Close();
            }
        });
    }

    [Fact]
    public void Project_setup_minimal_scope_is_limited_to_page1_and_special_fields_share_canonical_state()
    {
        RunOnStaThread(() =>
        {
            var metadataState = new ProjectMetadataState();
            var preferences = new MetadataPreferences { WarnBeforeDiscardingConceptDrafts = false };
            var setup = CreateProjectSetupDialog(
                ProjectSetupSourceKind.Folder,
                getAnimationMilliseconds: () => 0,
                metadataState: metadataState,
                metadataPreferences: preferences);

            try
            {
                setup.Show();
                DrainDispatcher();
                var scope = Assert.IsType<ConceptMinimalPresentationScope>(setup.SetupAuthorField.MinimalPresentationScope);
                Assert.Same(scope, setup.SetupTitleField.MinimalPresentationScope);
                Assert.Same(scope, setup.SetupAlbumField.MinimalPresentationScope);
                Assert.Same(scope, setup.SetupNarratorField.MinimalPresentationScope);
                Assert.Same(scope, setup.SetupGenreField.MinimalPresentationScope);
                Assert.Null(setup.SetupIsbnField.MinimalPresentationScope);
                Assert.Null(setup.SetupDescriptionField.MinimalPresentationScope);
                Assert.False(typeof(IConceptMinimalPresentationParticipant).IsAssignableFrom(typeof(ConceptSpecialMetadataFieldControl)));
                Assert.Equal(ConceptDescriptionPresentationMode.Stretch, setup.SetupDescriptionField.PresentationMode);

                var author = Assert.IsType<ConceptMetadataFieldViewModel>(setup.SetupAuthorField.DataContext);
                Assert.True(setup.SetupAuthorField.TryOpenMinimalEditor());
                author.Draft = "Seiten-Draft";

                Assert.True(setup.NavigateSetupPage(1));
                DrainDispatcher();
                Assert.True(setup.SetupAuthorField.IsMinimalEditorOpen);
                Assert.True(author.HasDraft);
                Assert.False(setup.SetupSeriesField is IConceptMinimalPresentationParticipant);

                var abridgedStatus = Assert.IsType<ConceptSpecialMetadataFieldState>(setup.SetupAbridgedStatusField.DataContext);
                var year = Assert.IsType<ConceptSpecialMetadataFieldState>(setup.SetupYearField.DataContext);
                var workType = Assert.IsType<ConceptSpecialMetadataFieldState>(setup.SetupWorkTypeField.DataContext);
                var isbn = Assert.IsType<ConceptMetadataFieldViewModel>(setup.SetupIsbnField.DataContext);
                abridgedStatus.DraftValue = AbridgedStatus.Abridged;
                Assert.True(abridgedStatus.CommitDraft());
                year.DraftText = "2027";
                Assert.True(year.CommitDraft());
                workType.DraftValue = AudioWorkType.AudioDrama;
                Assert.True(workType.CommitDraft());
                isbn.Draft = "978-3-16-148410-0";
                isbn.CommitDraft();

                var metadataDialog = CreateMetadataDialog(metadataState, preferences);
                try
                {
                    var catalogIsbn = metadataDialog.CatalogDataFields.Single(field => field.Field == ProjectMetadataField.ISBN);
                    Assert.Same(catalogIsbn, metadataDialog.ConceptQuickAccessIsbnField.DataContext);
                    Assert.Equal(
                        MetadataDisplayLabels.AbridgedStatus(AbridgedStatus.Abridged),
                        Assert.Single(metadataDialog.AbridgedStatusValues).Value);
                    Assert.Equal("2027", Assert.Single(metadataDialog.YearValues).Value);
                    Assert.Equal("Hörspiel", Assert.Single(metadataDialog.WorkTypeValues).Value);
                    Assert.Equal(
                        "978-3-16-148410-0",
                        metadataDialog.CatalogDataFields.Single(field => field.Field == ProjectMetadataField.ISBN).Values.Single().Value);

                    metadataDialog.AbridgedStatusFieldState.CommitValue(AbridgedStatus.Unabridged, MetadataOrigin.User);
                    metadataDialog.YearFieldState.CommitValue(2028, MetadataOrigin.User);
                    metadataDialog.WorkTypeFieldState.CommitValue(AudioWorkType.Audiobook, MetadataOrigin.User);
                    metadataState.CanonicalMetadata.SetValue(ProjectMetadataField.ISBN, "978-1-4028-9462-6", MetadataOrigin.User);
                    DrainDispatcher();

                    Assert.Equal(
                        MetadataDisplayLabels.AbridgedStatus(AbridgedStatus.Unabridged),
                        Assert.Single(abridgedStatus.Values).Value);
                    Assert.Equal("2028", Assert.Single(year.Values).Value);
                    Assert.Equal("Hörbuch", Assert.Single(workType.Values).Value);
                    Assert.Equal("978-1-4028-9462-6", Assert.Single(isbn.Values).Value);
                }
                finally
                {
                    metadataDialog.Close();
                }
            }
            finally
            {
                setup.Close();
            }
        });
    }
}
