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
    public void Concept_series_projection_is_synchronized_on_open_reopen_and_project_change()
    {
        RunOnStaThread(() =>
        {
            var metadataState = new ProjectMetadataState();
            metadataState.CanonicalMetadata.SetValue<IReadOnlyList<SeriesEntry>>(
                ProjectMetadataField.Series,
                [
                    new SeriesEntry("Saga der Horizonte", "2.5"),
                    new SeriesEntry("Archity-Zyklus", "4")
                ],
                MetadataOrigin.SourceAggregation);

            var firstDialog = CreateMetadataDialog(metadataState, new MetadataPreferences());
            try
            {
                Assert.Equal(2, firstDialog.SeriesValues.Count);
                Assert.Equal("Saga der Horizonte", firstDialog.SeriesValues[0].Name);
                Assert.Equal("2.5", firstDialog.SeriesValues[0].Part);
                Assert.Equal("Archity-Zyklus", firstDialog.SeriesValues[1].Name);
                Assert.Equal("4", firstDialog.SeriesValues[1].Part);
            }
            finally
            {
                firstDialog.WarnBeforeDiscardingConceptDrafts = false;
                firstDialog.Close();
            }

            var reopenedDialog = CreateMetadataDialog(metadataState, new MetadataPreferences());
            try
            {
                Assert.Equal(2, reopenedDialog.SeriesValues.Count);

                metadataState.CanonicalMetadata.SetValue<IReadOnlyList<SeriesEntry>>(
                    ProjectMetadataField.Series,
                    [new SeriesEntry("Reihe C", "7")],
                    MetadataOrigin.SourceAggregation);
                reopenedDialog.NotifyProjectChanged();

                var switched = Assert.Single(reopenedDialog.SeriesValues);
                Assert.Equal("Reihe C", switched.Name);
                Assert.Equal("7", switched.Part);

                metadataState.CanonicalMetadata.SetValue<IReadOnlyList<SeriesEntry>>(
                    ProjectMetadataField.Series,
                    [],
                    MetadataOrigin.None);
                reopenedDialog.NotifyProjectChanged();

                Assert.Empty(reopenedDialog.SeriesValues);
            }
            finally
            {
                reopenedDialog.WarnBeforeDiscardingConceptDrafts = false;
                reopenedDialog.Close();
            }
        });
    }

    [Fact]
    public void Concept_title_and_album_views_share_one_field_state_and_draft()
    {
        RunOnStaThread(() =>
        {
            var metadataState = new ProjectMetadataState { BookTitle = "Alt", Album = "Album Alt" };
            var preferences = new MetadataPreferences();
            var dialog = CreateMetadataDialog(metadataState, preferences);

            try
            {
                var quickTitle = Assert.IsType<ConceptMetadataFieldControl>(dialog.FindName("ConceptQuickAccessTitleField"));
                var workTitle = Assert.IsType<ConceptMetadataFieldControl>(dialog.FindName("ConceptWorkOutputTitleField"));
                var quickAlbum = Assert.IsType<ConceptMetadataFieldControl>(dialog.FindName("ConceptQuickAccessAlbumField"));
                var workAlbum = Assert.IsType<ConceptMetadataFieldControl>(dialog.FindName("ConceptWorkOutputAlbumField"));

                Assert.Same(dialog.TitleField, quickTitle.DataContext);
                Assert.Same(dialog.TitleField, workTitle.DataContext);
                Assert.Same(dialog.AlbumField, quickAlbum.DataContext);
                Assert.Same(dialog.AlbumField, workAlbum.DataContext);

                dialog.TitleField.Draft = "Mein neuer Ti";
                Assert.Equal("Mein neuer Ti", ((ConceptMetadataFieldViewModel)workTitle.DataContext).Draft);
                ((ConceptMetadataFieldViewModel)workTitle.DataContext).Draft = "Mein neuer Titel";
                Assert.Equal("Mein neuer Titel", ((ConceptMetadataFieldViewModel)quickTitle.DataContext).Draft);

                dialog.AlbumField.Draft = "Mein Album";
                Assert.Equal("Mein Album", ((ConceptMetadataFieldViewModel)workAlbum.DataContext).Draft);
            }
            finally
            {
                dialog.WarnBeforeDiscardingConceptDrafts = false;
                dialog.Close();
            }
        });
    }

    [Fact]
    public void Title_album_binding_uses_one_global_state_across_both_placements_search_and_project_refresh()
    {
        RunOnStaThread(() =>
        {
            var metadataState = new ProjectMetadataState { BookTitle = "Titel A", Album = "Album A" };
            var preferences = new MetadataPreferences();
            preferences.MetadataFieldBindings[ProjectMetadataFieldBindingCatalog.TitleToAlbumId] = false;
            var dialog = CreateMetadataDialog(metadataState, preferences);

            try
            {
                var quickAlbum = Assert.IsType<ConceptMetadataFieldControl>(dialog.FindName("ConceptQuickAccessAlbumField"));
                var workAlbum = Assert.IsType<ConceptMetadataFieldControl>(dialog.FindName("ConceptWorkOutputAlbumField"));
                var quickAccessory = Assert.IsType<ConceptMetadataFieldBindingAccessory>(quickAlbum.HeaderAccessory);
                var workAccessory = Assert.IsType<ConceptMetadataFieldBindingAccessory>(workAlbum.HeaderAccessory);
                var quickToggle = Assert.IsType<ToggleButton>(quickAccessory.FindName("BindingToggle"));
                var workToggle = Assert.IsType<ToggleButton>(workAccessory.FindName("BindingToggle"));

                Assert.Same(dialog.AlbumField, quickAlbum.DataContext);
                Assert.Same(dialog.AlbumField, workAlbum.DataContext);
                Assert.Same(dialog.TitleAlbumBinding, quickAlbum.FieldBinding);
                Assert.Same(dialog.TitleAlbumBinding, workAlbum.FieldBinding);
                Assert.Same(dialog.TitleAlbumBinding, quickAccessory.BindingState);
                Assert.Same(dialog.TitleAlbumBinding, workAccessory.BindingState);
                Assert.Equal("Titel übernehmen", quickToggle.Content);
                Assert.Equal("Titel übernehmen", workToggle.Content);
                Assert.False(dialog.TitleAlbumBinding.IsActive);

                dialog.AlbumField.Draft = "Offener Album-Draft";
                quickToggle.IsChecked = true;
                DrainDispatcher();

                Assert.True(dialog.TitleAlbumBinding.IsActive);
                Assert.True(workToggle.IsChecked);
                Assert.True(MetadataFieldBindingPreferencesService.GetIsEnabled(
                    preferences,
                    ProjectMetadataFieldBindingCatalog.TitleToAlbum));
                Assert.Equal(string.Empty, dialog.AlbumField.Draft);
                Assert.Empty(dialog.AlbumField.Values);
                Assert.Null(metadataState.CanonicalMetadata.GetValue<string>(ProjectMetadataField.Album).Value);
                Assert.Contains("Album A", metadataState.CanonicalMetadata.GetSuppressedValues(ProjectMetadataField.Album));
                Assert.Same(dialog.TitleField.Values, dialog.TitleAlbumBinding.ProjectedValues);
                Assert.Equal("Titel A", Assert.Single(dialog.TitleAlbumBinding.ProjectedValues).Value);

                dialog.TitleField.Draft = "Titel B";
                dialog.TitleField.CommitDraft();
                Assert.Equal("Titel B", Assert.Single(dialog.TitleAlbumBinding.ProjectedValues).Value);

                dialog.TitleField.IsOutputEnabled = false;
                dialog.AlbumField.IsOutputEnabled = true;
                Assert.True(dialog.TitleAlbumBinding.IsActive);
                Assert.False(dialog.TitleField.IsOutputEnabled);
                Assert.True(dialog.AlbumField.IsOutputEnabled);
                Assert.Equal("Titel B", Assert.Single(dialog.TitleAlbumBinding.ProjectedValues).Value);

                var searchBox = Assert.IsType<TextBox>(dialog.FindName("ConceptSearchTextBox"));
                var searchPanel = Assert.IsType<StackPanel>(dialog.FindName("ConceptSearchResultsPanel"));
                searchBox.Text = "Album";
                DrainDispatcher();
                var searchAlbum = Assert.Single(
                    searchPanel.Children.OfType<ConceptMetadataFieldControl>(),
                    control => ReferenceEquals(control.DataContext, dialog.AlbumField));
                var searchAccessory = Assert.IsType<ConceptMetadataFieldBindingAccessory>(searchAlbum.HeaderAccessory);
                var searchToggle = Assert.IsType<ToggleButton>(searchAccessory.FindName("BindingToggle"));
                Assert.Same(dialog.TitleAlbumBinding, searchAlbum.FieldBinding);
                Assert.Same(dialog.TitleAlbumBinding, searchAccessory.BindingState);
                Assert.Equal("Titel übernehmen", searchToggle.Content);

                metadataState.CanonicalMetadata.SetValue(ProjectMetadataField.Title, "Projekt B Titel", MetadataOrigin.User);
                metadataState.CanonicalMetadata.SetValue(ProjectMetadataField.Album, "Projekt B Album", MetadataOrigin.User);
                dialog.NotifyProjectChanged();
                DrainDispatcher();

                Assert.True(dialog.TitleAlbumBinding.IsActive);
                Assert.True(MetadataFieldBindingPreferencesService.GetIsEnabled(
                    preferences,
                    ProjectMetadataFieldBindingCatalog.TitleToAlbum));
                Assert.Equal("Projekt B Titel", Assert.Single(dialog.TitleAlbumBinding.ProjectedValues).Value);
                Assert.Null(metadataState.CanonicalMetadata.GetValue<string>(ProjectMetadataField.Album).Value);
                Assert.Equal(
                    1,
                    metadataState.CanonicalMetadata.GetSuppressedValues(ProjectMetadataField.Album)
                        .Count(value => value == "Projekt B Album"));

                dialog.NotifyProjectChanged();
                Assert.Equal(
                    1,
                    metadataState.CanonicalMetadata.GetSuppressedValues(ProjectMetadataField.Album)
                        .Count(value => value == "Projekt B Album"));

                searchToggle.IsChecked = false;
                DrainDispatcher();
                Assert.False(dialog.TitleAlbumBinding.IsActive);
                Assert.False(quickToggle.IsChecked);
                Assert.False(workToggle.IsChecked);
                Assert.Empty(dialog.AlbumField.Values);
                Assert.Null(metadataState.CanonicalMetadata.GetValue<string>(ProjectMetadataField.Album).Value);
            }
            finally
            {
                dialog.WarnBeforeDiscardingConceptDrafts = false;
                dialog.Close();
            }
        });
    }

}
