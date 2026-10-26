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
    public void Metadata_close_request_during_closing_is_ignored_and_canceled_close_can_be_retried()
    {
        RunOnStaThread(() =>
        {
            var dialog = CreateMetadataDialog(new ProjectMetadataState(), new MetadataPreferences());
            dialog.WarnBeforeDiscardingConceptDrafts = false;
            var close = typeof(MetadataDialog).GetMethod("Close_Click", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var closingCount = 0;
            CancelEventHandler nestedRequest = (_, args) =>
            {
                closingCount++;
                close.Invoke(dialog, [dialog, new System.Windows.RoutedEventArgs()]);
                args.Cancel = true;
            };
            try
            {
                dialog.Show();
                dialog.Closing += nestedRequest;
                close.Invoke(dialog, [dialog, new System.Windows.RoutedEventArgs()]);
                Assert.Equal(1, closingCount);
                Assert.True(dialog.IsVisible);
                dialog.Closing -= nestedRequest;
                close.Invoke(dialog, [dialog, new System.Windows.RoutedEventArgs()]);
                Assert.False(dialog.IsVisible);
            }
            finally
            {
                dialog.Closing -= nestedRequest;
                if (dialog.IsVisible)
                    dialog.Close();
            }
        });
    }

    [Fact]
    public void Concept_title_state_commits_to_canonical_metadata_and_shares_output_policy()
    {
        RunOnStaThread(() =>
        {
            ProjectMetadata? savedMetadata = null;
            var metadataState = new ProjectMetadataState();
            var preferences = new MetadataPreferences();
            var dialog = CreateMetadataDialog(metadataState, preferences, metadata => savedMetadata = metadata);

            try
            {
                dialog.TitleField.Draft = "Hallo";
                dialog.TitleField.CommitDraft();

                Assert.Equal("Hallo", metadataState.CanonicalMetadata.GetValue<string>(ProjectMetadataField.Title).Value);
                Assert.Same(metadataState.CanonicalMetadata, savedMetadata);
                Assert.Equal("Hallo", Assert.Single(dialog.TitleField.Values).Value);
                Assert.Equal(string.Empty, dialog.TitleField.Draft);

                dialog.TitleField.IsOutputEnabled = false;
                Assert.False(dialog.TitleField.IsOutputEnabled);
                Assert.Equal(OutputFieldPolicy.Disabled, MetadataPreferencesService.GetPolicy(preferences, ProjectMetadataField.Title));
                var quickTitle = Assert.IsType<ConceptMetadataFieldControl>(dialog.FindName("ConceptQuickAccessTitleField"));
                var workTitle = Assert.IsType<ConceptMetadataFieldControl>(dialog.FindName("ConceptWorkOutputTitleField"));
                Assert.Same(dialog.TitleField, quickTitle.DataContext);
                Assert.Same(dialog.TitleField, workTitle.DataContext);
            }
            finally
            {
                dialog.WarnBeforeDiscardingConceptDrafts = false;
                dialog.Close();
            }
        });
    }

    [Fact]
    public void Concept_title_and_album_drafts_are_included_in_shared_draft_inventory()
    {
        RunOnStaThread(() =>
        {
            var dialog = CreateMetadataDialog(new ProjectMetadataState(), new MetadataPreferences());

            try
            {
                var hasConceptDrafts = typeof(MetadataDialog).GetMethod(
                    "HasConceptDrafts",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                Assert.NotNull(hasConceptDrafts);

                Assert.False(Assert.IsType<bool>(hasConceptDrafts.Invoke(dialog, null)));
                dialog.TitleField.Draft = "Offener Titel";
                Assert.True(Assert.IsType<bool>(hasConceptDrafts.Invoke(dialog, null)));

                dialog.TitleField.DiscardDraft();
                Assert.False(Assert.IsType<bool>(hasConceptDrafts.Invoke(dialog, null)));
                dialog.AlbumField.Draft = "Offenes Album";
                Assert.True(Assert.IsType<bool>(hasConceptDrafts.Invoke(dialog, null)));

                dialog.AlbumField.DiscardDraft();
                dialog.AuthorsField.Draft = "Offener Autor";
                Assert.True(Assert.IsType<bool>(hasConceptDrafts.Invoke(dialog, null)));
            }
            finally
            {
                dialog.WarnBeforeDiscardingConceptDrafts = false;
                dialog.Close();
            }
        });
    }

    [Fact]
    public void Concept_draft_inventory_reports_stable_families_and_discards_active_drafts()
    {
        RunOnStaThread(() =>
        {
            var dialog = CreateMetadataDialog(new ProjectMetadataState(), new MetadataPreferences());

            try
            {
                dialog.TitleField.Draft = "Offener Titel";
                dialog.AuthorsField.Draft = "Offener Autor";
                dialog.DescriptionField.Draft = "Offene Beschreibung";
                dialog.SpecialDrafts.WorkType = AudioWorkType.Audiobook;
                dialog.SpecialDrafts.Year = "2027";
                dialog.SpecialDrafts.TrackNumber = "4";

                var getActiveDrafts = typeof(MetadataDialog).GetMethod(
                    "GetActiveMetadataDrafts",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.NotNull(getActiveDrafts);
                var activeDrafts = Assert.IsAssignableFrom<Array>(getActiveDrafts.Invoke(dialog, null));

                static (string Key, string Label, string FamilyKey, Action Discard) ReadDescriptor(object descriptor)
                {
                    var type = descriptor.GetType();
                    return (
                        Assert.IsType<string>(type.GetProperty("Key")?.GetValue(descriptor)),
                        Assert.IsType<Func<string>>(type.GetProperty("Label")?.GetValue(descriptor))(),
                        Assert.IsType<string>(type.GetProperty("FamilyKey")?.GetValue(descriptor)),
                        Assert.IsType<Action>(type.GetProperty("Discard")?.GetValue(descriptor)));
                }

                var drafts = activeDrafts.Cast<object>()
                    .Select(ReadDescriptor)
                    .ToDictionary(draft => draft.Key, StringComparer.Ordinal);

                Assert.Equal(ConceptMetadataGroupIds.QuickAccess, drafts[nameof(ProjectMetadataField.Title)].FamilyKey);
                Assert.Equal(ConceptMetadataGroupIds.People, drafts[nameof(ProjectMetadataField.Authors)].FamilyKey);
                Assert.Equal(ConceptMetadataGroupIds.Description, drafts[nameof(ProjectMetadataField.Description)].FamilyKey);
                Assert.Equal(ConceptMetadataGroupIds.Work, drafts[nameof(ProjectMetadataField.AudioWorkType)].FamilyKey);
                Assert.Equal(ConceptMetadataGroupIds.Publication, drafts[nameof(ProjectMetadataField.Year)].FamilyKey);
                Assert.Equal(ConceptMetadataGroupIds.Sorting, drafts[nameof(ProjectMetadataField.OutputTrackNumber)].FamilyKey);
                Assert.All(drafts.Values, draft => Assert.False(string.IsNullOrWhiteSpace(draft.Label)));

                foreach (var draft in drafts.Values)
                    draft.Discard();

                Assert.False(dialog.TitleField.HasDraft);
                Assert.False(dialog.AuthorsField.HasDraft);
                Assert.False(dialog.DescriptionField.HasDraft);
                Assert.Null(dialog.SpecialDrafts.WorkType);
                Assert.Equal(string.Empty, dialog.SpecialDrafts.Year);
                Assert.Equal(string.Empty, dialog.SpecialDrafts.TrackNumber);
            }
            finally
            {
                dialog.WarnBeforeDiscardingConceptDrafts = false;
                dialog.Close();
            }
        });
    }

    [Fact]
    public void Cover_selection_switches_between_audiobook_and_cover_contexts()
    {
        RunOnStaThread(() =>
        {
            var metadataState = new ProjectMetadataState
            {
                BookTitle = "Testtitel",
                Author = "Testautor",
                Narrator = "Testsprecher"
            };
            var dialog = CreateMetadataDialog(metadataState, new MetadataPreferences());

            try
            {
                var familyList = Assert.IsType<ListBox>(dialog.FindName("ConceptFamilyList"));
                var quickItem = Assert.IsType<ListBoxItem>(dialog.FindName("ConceptQuickAccessFamilyItem"));
                var coverItem = Assert.IsType<ListBoxItem>(dialog.FindName("ConceptCoverFamilyItem"));
                var audiobookContext = Assert.IsType<StackPanel>(dialog.FindName("ConceptMetadataContextAudiobook"));
                var coverContext = Assert.IsType<Grid>(dialog.FindName("ConceptMetadataContextCover"));

                Assert.Equal("Testtitel", dialog.ContextBookTitle);
                Assert.Equal("Testautor", dialog.ContextAuthor);
                Assert.Equal("Testsprecher", dialog.ContextNarrator);
                Assert.Equal(System.Windows.Visibility.Visible, audiobookContext.Visibility);
                Assert.Equal(System.Windows.Visibility.Collapsed, coverContext.Visibility);

                familyList.SelectedItem = coverItem;
                DrainDispatcher();
                Assert.Equal(System.Windows.Visibility.Collapsed, audiobookContext.Visibility);
                Assert.Equal(System.Windows.Visibility.Visible, coverContext.Visibility);

                familyList.SelectedItem = quickItem;
                DrainDispatcher();
                Assert.Equal(System.Windows.Visibility.Visible, audiobookContext.Visibility);
                Assert.Equal(System.Windows.Visibility.Collapsed, coverContext.Visibility);
            }
            finally
            {
                dialog.WarnBeforeDiscardingConceptDrafts = false;
                dialog.Close();
            }
        });
    }

    [Fact]
    public void Description_selection_keeps_the_shared_audiobook_context_visible()
    {
        RunOnStaThread(() =>
        {
            var dialog = CreateMetadataDialog(new ProjectMetadataState(), new MetadataPreferences());

            try
            {
                var familyList = Assert.IsType<ListBox>(dialog.FindName("ConceptFamilyList"));
                var descriptionItem = Assert.IsType<ListBoxItem>(dialog.FindName("ConceptDescriptionFamilyItem"));
                var contextColumn = Assert.IsType<Grid>(dialog.FindName("ConceptMetadataContextColumn"));
                var audiobookContext = Assert.IsType<StackPanel>(dialog.FindName("ConceptMetadataContextAudiobook"));

                familyList.SelectedItem = descriptionItem;
                DrainDispatcher();

                Assert.Equal(System.Windows.Visibility.Visible, contextColumn.Visibility);
                Assert.Equal(System.Windows.Visibility.Visible, audiobookContext.Visibility);
            }
            finally
            {
                dialog.WarnBeforeDiscardingConceptDrafts = false;
                dialog.Close();
            }
        });
    }

    [Fact]
    public void Concept_normal_multi_placement_fields_share_one_state_and_draft()
    {
        RunOnStaThread(() =>
        {
            var dialog = CreateMetadataDialog(new ProjectMetadataState(), new MetadataPreferences());

            try
            {
                var quickAuthors = Assert.IsType<ConceptMetadataFieldControl>(dialog.FindName("ConceptQuickAccessAuthorField"));
                var peopleAuthors = Assert.IsType<ConceptMetadataFieldControl>(dialog.FindName("ConceptPeopleAuthorField"));
                var quickNarrators = Assert.IsType<ConceptMetadataFieldControl>(dialog.FindName("ConceptQuickAccessSpeakerField"));
                var peopleNarrators = Assert.IsType<ConceptMetadataFieldControl>(dialog.FindName("ConceptPeopleSpeakerField"));
                var quickGenres = Assert.IsType<ConceptMetadataFieldControl>(dialog.FindName("ConceptQuickAccessGenreField"));
                var classificationGenres = Assert.IsType<ConceptMetadataFieldControl>(dialog.FindName("ConceptCatalogGenreField"));

                Assert.Same(dialog.AuthorsField, quickAuthors.DataContext);
                Assert.Same(dialog.AuthorsField, peopleAuthors.DataContext);
                Assert.Same(dialog.NarratorsField, quickNarrators.DataContext);
                Assert.Same(dialog.NarratorsField, peopleNarrators.DataContext);
                Assert.Same(dialog.GenresField, quickGenres.DataContext);
                Assert.Same(dialog.GenresField, classificationGenres.DataContext);

                dialog.AuthorsField.Draft = "Hallo";
                Assert.Equal("Hallo", ((ConceptMetadataFieldViewModel)peopleAuthors.DataContext).Draft);
                ((ConceptMetadataFieldViewModel)peopleAuthors.DataContext).Draft = "Hallo Welt";
                Assert.Equal("Hallo Welt", ((ConceptMetadataFieldViewModel)quickAuthors.DataContext).Draft);

                dialog.GenresField.Draft = "Fantasy";
                Assert.Equal("Fantasy", ((ConceptMetadataFieldViewModel)classificationGenres.DataContext).Draft);
            }
            finally
            {
                dialog.WarnBeforeDiscardingConceptDrafts = false;
                dialog.Close();
            }
        });
    }

    [Fact]
    public void Concept_search_transient_view_removal_preserves_shared_field_draft()
    {
        RunOnStaThread(() =>
        {
            var dialog = CreateMetadataDialog(new ProjectMetadataState(), new MetadataPreferences());

            try
            {
                var searchBox = Assert.IsType<TextBox>(dialog.FindName("ConceptSearchTextBox"));
                var searchPanel = Assert.IsType<StackPanel>(dialog.FindName("ConceptSearchResultsPanel"));
                var quickTitle = Assert.IsType<ConceptMetadataFieldControl>(dialog.FindName("ConceptQuickAccessTitleField"));

                searchBox.Text = dialog.TitleField.Label;
                DrainDispatcher();
                var searchTitle = Assert.Single(searchPanel.Children
                    .OfType<ConceptMetadataFieldControl>(), control => ReferenceEquals(control.DataContext, dialog.TitleField));
                PrepareControl(searchTitle);
                searchTitle.EditorComboBox.Text = "Hallo Welt";
                searchTitle.EditorComboBox.GetBindingExpression(ComboBox.TextProperty)?.UpdateSource();
                DrainDispatcher();
                Assert.Equal("Hallo Welt", dialog.TitleField.Draft);

                searchBox.Text = string.Empty;
                DrainDispatcher();
                Assert.Equal("Hallo Welt", dialog.TitleField.Draft);
                PrepareControl(quickTitle);
                DrainDispatcher();
                Assert.Equal("Hallo Welt", quickTitle.EditorComboBox.Text);

                searchBox.Text = dialog.AuthorsField.Label;
                DrainDispatcher();
                var searchAuthors = Assert.Single(searchPanel.Children
                    .OfType<ConceptMetadataFieldControl>(), control => ReferenceEquals(control.DataContext, dialog.AuthorsField));
                PrepareControl(searchAuthors);
                searchAuthors.EditorComboBox.Text = "Autor Draft";
                searchAuthors.EditorComboBox.GetBindingExpression(ComboBox.TextProperty)?.UpdateSource();
                DrainDispatcher();
                searchBox.Text = "kein treffer xyz";
                DrainDispatcher();
                Assert.Equal("Autor Draft", dialog.AuthorsField.Draft);
            }
            finally
            {
                dialog.WarnBeforeDiscardingConceptDrafts = false;
                dialog.Close();
            }
        });
    }


}
