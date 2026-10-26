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
    public void Locked_work_type_badge_keeps_a_visible_width_after_dialog_reopen()
    {
        RunOnStaThread(() =>
        {
            var metadata = new ProjectMetadataState();
            metadata.CanonicalMetadata.SetValue<AudioWorkType?>(
                ProjectMetadataField.AudioWorkType, AudioWorkType.Audiobook, MetadataOrigin.GlobalPreference);
            var preferences = new MetadataPreferences
            {
                AudioWorkTypePreferenceLocked = true,
                PreferredAudioWorkType = AudioWorkType.Audiobook
            };
            for (var reopen = 0; reopen < 2; reopen++)
            {
                var dialog = CreateMetadataDialog(metadata, preferences);
                try
                {
                    dialog.Show();
                    Assert.IsType<ListBox>(dialog.FindName("ConceptFamilyList")).SelectedItem =
                        dialog.FindName("ConceptWorkOutputFamilyItem");
                    DrainDispatcher();
                    dialog.UpdateLayout();
                    var editor = Assert.IsType<Border>(dialog.FindName("ConceptWorkWorkTypeEditorColumnHost"));
                    var badges = Assert.IsType<ItemsControl>(dialog.FindName("ConceptWorkWorkTypeValuesControl"));
                    Assert.Equal(System.Windows.Visibility.Collapsed, editor.Visibility);
                    Assert.Single(badges.Items);
                    Assert.True(badges.IsVisible);
                    Assert.True(badges.ActualWidth > 40, $"Badge width after reopen {reopen}: {badges.ActualWidth}");
                    dialog.WorkTypeFieldState.IsLocked = false;
                    dialog.WorkTypeFieldState.IsLocked = true;
                    DrainDispatcher();
                    dialog.UpdateLayout();
                    Assert.True(badges.ActualWidth > 40);
                }
                finally { dialog.Close(); }
            }
        });
    }

    [Theory]
    [InlineData("ConceptWorkTypeComboBox", ProjectMetadataField.AudioWorkType)]
    [InlineData("ConceptAbridgedStatusComboBox", ProjectMetadataField.AbridgedStatus)]
    [InlineData("ConceptWorkWorkTypeComboBox", ProjectMetadataField.AudioWorkType)]
    [InlineData("ConceptWorkAbridgedStatusComboBox", ProjectMetadataField.AbridgedStatus)]
    public void EnumMouseSelection_CommitsImmediatelyExactlyOnce_AndKeyboardStillCommits(string controlName, ProjectMetadataField field)
    {
        RunOnStaThread(() =>
        {
            var metadata = new ProjectMetadataState();
            var saves = 0;
            var dialog = CreateMetadataDialog(metadata, new MetadataPreferences(), _ => saves++);
            try
            {
                dialog.Show();
                if (controlName.StartsWith("ConceptWorkWork", StringComparison.Ordinal) || controlName == "ConceptWorkAbridgedStatusComboBox")
                    Assert.IsType<ListBox>(dialog.FindName("ConceptFamilyList")).SelectedItem = dialog.FindName("ConceptWorkOutputFamilyItem");
                DrainDispatcher();
                var combo = Assert.IsType<ComboBox>(dialog.FindName(controlName));
                combo.IsDropDownOpen = true;
                DrainDispatcher();
                var candidate = combo.Items.OfType<ConceptMetadataCandidate>().First(item =>
                    item.ValueToCommit is AudioWorkType.Audiobook or AbridgedStatus.Unabridged);
                var container = Assert.IsType<ComboBoxItem>(combo.ItemContainerGenerator.ContainerFromItem(candidate));
                container.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
                { RoutedEvent = System.Windows.UIElement.PreviewMouseLeftButtonUpEvent });
                DrainDispatcher();
                Assert.Equal(1, saves);
                Assert.Null(combo.SelectedItem);
                if (field == ProjectMetadataField.AudioWorkType)
                    Assert.Equal(AudioWorkType.Audiobook, metadata.WorkType);
                else
                    Assert.Equal(AbridgedStatus.Unabridged, metadata.AbridgedStatus);

                combo.SelectedItem = combo.Items.OfType<ConceptMetadataCandidate>().First(item =>
                    item.ValueToCommit is AudioWorkType.AudioDrama or AbridgedStatus.Abridged);
                Assert.Equal(1, saves);
                combo.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, System.Windows.PresentationSource.FromVisual(dialog)!, 0, Key.Enter)
                { RoutedEvent = System.Windows.UIElement.PreviewKeyDownEvent });
                DrainDispatcher();
                Assert.Equal(2, saves);
                if (field == ProjectMetadataField.AudioWorkType)
                    Assert.Equal(AudioWorkType.AudioDrama, metadata.WorkType);
                else
                    Assert.Equal(AbridgedStatus.Abridged, metadata.AbridgedStatus);
            }
            finally { dialog.Close(); }
        });
    }

    [Fact]
    public void WorkTypeLock_UsesSharedPreferenceAndBlocksEditingRemovalAndOutputDisable()
    {
        RunOnStaThread(() =>
        {
            var metadata = new ProjectMetadataState();
            var preferences = new MetadataPreferences();
            var dialog = CreateMetadataDialog(metadata, preferences);
            try
            {
                var state = dialog.WorkTypeFieldState;
                state.CommitValue(AudioWorkType.AudioDrama, MetadataOrigin.User);
                state.IsLocked = true;
                Assert.True(state.IsLocked);
                Assert.Equal(AudioWorkType.AudioDrama, preferences.PreferredAudioWorkType);
                Assert.False(state.CanEditValues);
                state.DraftValue = AudioWorkType.Audiobook;
                Assert.False(state.CommitDraft());
                state.RemoveValue();
                state.IsOutputEnabled = false;
                Assert.True(state.IsOutputEnabled);
                Assert.Equal(AudioWorkType.AudioDrama, metadata.WorkType);
                state.IsLocked = false;
                state.DraftValue = AudioWorkType.Audiobook;
                Assert.True(state.CommitDraft());
                Assert.Equal(AudioWorkType.Audiobook, metadata.WorkType);
            }
            finally { dialog.Close(); }
        });
    }

    [Fact]
    public void SeriesSemicolonInput_RemainsOneStructuredNameWithExistingDuplicateHandling()
    {
        RunOnStaThread(() =>
        {
            var metadata = new ProjectMetadataState();
            var dialog = CreateMetadataDialog(metadata, new MetadataPreferences());
            try
            {
                var series = dialog.SeriesFieldState;
                series.ConfirmNameDraft("Alpha");
                Assert.True(series.CommitDraft());
                series.ConfirmNameDraft("Alpha;Beta");
                Assert.True(series.CommitDraft());
                series.ConfirmNameDraft("Alpha;Beta");
                Assert.False(series.CommitDraft());
                Assert.Equal(new[] { "Alpha", "Alpha;Beta" }, series.SeriesValues.Select(item => item.Name));
                Assert.Equal(2, metadata.CanonicalMetadata.GetValue<IReadOnlyList<SeriesEntry>>(ProjectMetadataField.Series).Value!.Count);
            }
            finally { dialog.Close(); }
        });
    }

    [Fact]
    public void Description_search_uses_one_special_view_and_long_text_value_matching_stays_conservative()
    {
        RunOnStaThread(() =>
        {
            var metadataState = new ProjectMetadataState();
            metadataState.CanonicalMetadata.SetValue<string>(
                ProjectMetadataField.Description,
                "Alpha beta gamma delta epsilon",
                MetadataOrigin.User);
            metadataState.CanonicalMetadata.SetValue<string>(
                ProjectMetadataField.Comment,
                "BookStitch Testfixture – keine echten bibliografischen Daten.",
                MetadataOrigin.User);
            var dialog = CreateMetadataDialog(metadataState, new MetadataPreferences());
            try
            {
                var searchBox = Assert.IsType<TextBox>(dialog.FindName("ConceptSearchTextBox"));
                var searchPanel = Assert.IsType<StackPanel>(dialog.FindName("ConceptSearchResultsPanel"));
                var quickDescription = Assert.IsType<ConceptDescriptionFieldControl>(dialog.FindName("ConceptQuickAccessDescriptionField"));
                var peopleFamily = Assert.IsType<ListBoxItem>(dialog.FindName("ConceptPeopleFamilyItem"));
                var compatibilityFamily = Assert.IsType<ListBoxItem>(dialog.FindName("ConceptCompatibilityFamilyItem"));

                searchBox.Text = "beschreibung";
                DrainDispatcher();
                Assert.Equal(1, searchPanel.Children.Cast<System.Windows.UIElement>().Count(element => ReferenceEquals(element, quickDescription)));
                Assert.DoesNotContain(searchPanel.Children.OfType<ConceptMetadataFieldControl>(), control => ReferenceEquals(control.DataContext, dialog.DescriptionField));

                searchBox.Text = "beschr";
                DrainDispatcher();
                Assert.DoesNotContain(searchPanel.Children.OfType<ConceptMetadataFieldControl>(), control => ReferenceEquals(control.DataContext, dialog.ComposersField));
                Assert.Equal(0.35d, peopleFamily.Opacity);
                Assert.Equal(1d, compatibilityFamily.Opacity);
                var preview = Assert.IsType<Grid>(dialog.FindName("ConceptCompatibilityDescriptionPreview"));
                Assert.Equal(12d, preview.Margin.Top);
                Assert.Equal(14d, preview.Margin.Bottom);

                searchBox.Clear();
                DrainDispatcher();
                Assert.Equal(0d, preview.Margin.Top);
                Assert.Equal(10d, preview.Margin.Bottom);

                searchBox.Text = "bibliograf";
                DrainDispatcher();
                Assert.Contains(searchPanel.Children.OfType<ConceptMetadataFieldControl>(), control =>
                    control.DataContext is ConceptMetadataFieldViewModel vm && vm.Field == ProjectMetadataField.Comment);

                var compatibilityField = Assert.IsType<ConceptCompatibilityFieldControl>(dialog.FindName("ConceptCompatibilityField"));
                var compatibilityToggle = Assert.IsType<ToggleButton>(compatibilityField.FindName("CompatibilityDescriptionToggle"));
                Assert.Equal("In Beschreibung", compatibilityToggle.Content);
                dialog.IsCompatibilityDescriptionEnabled = true;
                Assert.True(compatibilityToggle.IsChecked);
                dialog.IsDescriptionIncluded = false;
                Assert.Equal("Die Ausgabe des Feldes „Beschreibung“ ist deaktiviert.", dialog.CompatibilityDescriptionOutputPreview);
                dialog.IsDescriptionIncluded = true;
                Assert.DoesNotContain("deaktiviert", dialog.CompatibilityDescriptionOutputPreview, StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                dialog.WarnBeforeDiscardingConceptDrafts = false;
                dialog.Close();
            }
        });
    }

    [Fact]
    public void MetadataDialog_technical_name_toggle_updates_derived_labels_in_same_instance()
    {
        RunOnStaThread(() =>
        {
            var preferences = new MetadataPreferences { ShowTechnicalConceptFieldNames = false };
            var dialog = CreateMetadataDialog(new ProjectMetadataState(), preferences);
            try
            {
                Assert.Equal("Kompatibilitätsfeld", dialog.CompatibilityFieldLabel);
                Assert.Equal("Vorschau Beschreibungsausgabe", dialog.CompatibilityDescriptionOutputPreviewLabel);

                dialog.ShowTechnicalFieldNames = true;

                Assert.Equal("Compatibility Field", dialog.CompatibilityFieldLabel);
                Assert.Equal("Description Output Preview", dialog.CompatibilityDescriptionOutputPreviewLabel);
                Assert.True(preferences.ShowTechnicalConceptFieldNames);
            }
            finally
            {
                dialog.WarnBeforeDiscardingConceptDrafts = false;
                dialog.Close();
            }
        });
    }


    [Fact]
    public void Metadata_chapter_view_flattens_expanded_embedded_rows_without_copying_domain_values()
    {
        RunOnStaThread(() =>
        {
            var first = new TrackInfo
            {
                FileName = "B",
                ChapterTitle = "Parent B",
                IsChapterDetailsExpanded = true,
                EmbeddedChapters =
                [
                    new TrackEmbeddedChapterInfo { Title = "Child 1", StartTicks = 0, EndTicks = TimeSpan.TicksPerSecond },
                    new TrackEmbeddedChapterInfo { Title = "Child 2", StartTicks = TimeSpan.TicksPerSecond, EndTicks = 2 * TimeSpan.TicksPerSecond }
                ]
            };
            var firstChildren = first.EmbeddedChapterRows;
            firstChildren[0].ApplyTimelinePresentation("Child proposal 1", "00:00:00");
            firstChildren[1].ApplyTimelinePresentation("Child proposal 2", "00:00:01");
            var second = new TrackInfo { FileName = "A", ChapterTitle = "Parent A" };
            var tracks = new ObservableCollection<TrackInfo> { first, second };
            var dialog = CreateMetadataDialog(
                new ProjectMetadataState(),
                new MetadataPreferences(),
                tracks: tracks);

            try
            {
                var rows = dialog.ChapterTracksView.Cast<MetadataChapterPresentationRow>().ToArray();
                Assert.Equal(4, rows.Length);
                Assert.Same(first, rows[0].Track);
                Assert.Null(rows[0].EmbeddedChapter);
                Assert.Same(first, rows[1].Track);
                Assert.Same(firstChildren[0], rows[1].EmbeddedChapter);
                Assert.Same(firstChildren[1], rows[2].EmbeddedChapter);
                Assert.Same(second, rows[3].Track);
                Assert.Equal("Child proposal 1", rows[1].ChapterProposal);
                Assert.Equal(string.Empty, rows[1].ChapterWarning);

                var defaultView = CollectionViewSource.GetDefaultView(tracks);
                defaultView.SortDescriptions.Add(new SortDescription(nameof(TrackInfo.FileName), ListSortDirection.Ascending));
                Assert.Equal(new[] { second, first }, defaultView.Cast<TrackInfo>().ToArray());
                dialog.RefreshChapterPresentationView();
                rows = dialog.ChapterTracksView.Cast<MetadataChapterPresentationRow>().ToArray();
                Assert.Equal(new[] { first, first, first, second }, rows.Select(row => row.Track).ToArray());

                first.IsChapterDetailsExpanded = false;
                dialog.RefreshChapterPresentationView();
                rows = dialog.ChapterTracksView.Cast<MetadataChapterPresentationRow>().ToArray();
                Assert.Equal(2, rows.Length);
                Assert.Equal(new[] { first, second }, rows.Select(row => row.Track).ToArray());
                Assert.All(rows, row => Assert.Null(row.EmbeddedChapter));

                var third = new TrackInfo { FileName = "C", ChapterTitle = "Parent C" };
                tracks.Add(third);
                dialog.RefreshChapterPresentationView();
                rows = dialog.ChapterTracksView.Cast<MetadataChapterPresentationRow>().ToArray();
                Assert.Equal(new[] { first, second, third }, rows.Select(row => row.Track).ToArray());
            }
            finally
            {
                dialog.Close();
            }
        });
    }


    [Fact]
    public void External_refresh_updates_all_special_field_projections()
    {
        RunOnStaThread(() =>
        {
            var metadataState = new ProjectMetadataState();
            var dialog = CreateMetadataDialog(metadataState, new MetadataPreferences());
            try
            {
                metadataState.CanonicalMetadata.SetValue<AudioWorkType?>(
                    ProjectMetadataField.AudioWorkType,
                    AudioWorkType.AudioDrama,
                    MetadataOrigin.User);
                metadataState.CanonicalMetadata.SetValue<AbridgedStatus?>(
                    ProjectMetadataField.AbridgedStatus,
                    AbridgedStatus.Abridged,
                    MetadataOrigin.User);
                metadataState.CanonicalMetadata.SetValue<MetadataDate?>(
                    ProjectMetadataField.ReleaseDate,
                    new MetadataDate(2027, 4, 3),
                    MetadataOrigin.User);
                metadataState.CanonicalMetadata.SetValue<int?>(
                    ProjectMetadataField.Year,
                    2027,
                    MetadataOrigin.User);
                metadataState.CanonicalMetadata.SetValue<int?>(
                    ProjectMetadataField.OutputTrackNumber,
                    4,
                    MetadataOrigin.User);
                metadataState.CanonicalMetadata.SetValue<int?>(
                    ProjectMetadataField.OutputTrackTotal,
                    12,
                    MetadataOrigin.User);
                metadataState.CanonicalMetadata.SetValue<IReadOnlyList<SeriesEntry>>(
                    ProjectMetadataField.Series,
                    [new SeriesEntry("Testreihe", "2")],
                    MetadataOrigin.User);

                dialog.RefreshForExternalChanges();
                DrainDispatcher();

                Assert.Equal(
                    MetadataDisplayLabels.AudioWorkType(AudioWorkType.AudioDrama),
                    Assert.Single(dialog.WorkTypeValues).Value);
                Assert.Equal(
                    MetadataDisplayLabels.AbridgedStatus(AbridgedStatus.Abridged),
                    Assert.Single(dialog.AbridgedStatusValues).Value);
                Assert.Equal("2027-04-03", Assert.Single(dialog.ReleaseDateValues).Value);
                Assert.Equal("2027", Assert.Single(dialog.YearValues).Value);
                Assert.Equal("4", Assert.Single(dialog.TrackNumberValues).Value);
                Assert.Equal("12", Assert.Single(dialog.TrackCountValues).Value);
                var series = Assert.Single(dialog.SeriesValues);
                Assert.Equal("Testreihe", series.Name);
                Assert.Equal("2", series.Part);
            }
            finally
            {
                dialog.WarnBeforeDiscardingConceptDrafts = false;
                dialog.Close();
            }
        });
    }

    [Fact]
    public void Concept_special_multi_placement_editors_share_one_transient_draft_state()
    {
        RunOnStaThread(() =>
        {
            var dialog = CreateMetadataDialog(new ProjectMetadataState(), new MetadataPreferences());
            try
            {
                var quickYear = Assert.IsType<TextBox>(dialog.FindName("ConceptYearTextBox"));
                var publicationYear = Assert.IsType<TextBox>(dialog.FindName("ConceptPublicationYearTextBox"));
                dialog.SpecialDrafts.Year = "2027";
                DrainDispatcher();
                Assert.Equal("2027", quickYear.Text);
                Assert.Equal("2027", publicationYear.Text);

                dialog.SpecialDrafts.Year = "2028";
                DrainDispatcher();
                Assert.Equal("2028", quickYear.Text);
                Assert.Equal("2028", publicationYear.Text);

                Assert.Null(dialog.FindName("ConceptTrackNumberTextBox"));
                var sortingTrack = Assert.IsType<TextBox>(dialog.FindName("ConceptWorkTrackNumberTextBox"));
                dialog.SpecialDrafts.TrackNumber = "12";
                DrainDispatcher();
                Assert.Equal("12", sortingTrack.Text);

                dialog.WorkTypeCandidates.Add(new ConceptMetadataCandidate(
                    ProjectMetadataField.AudioWorkType,
                    "Hörbuch",
                    AudioWorkType.Audiobook,
                    1,
                    MetadataOrigin.SourceAggregation));
                var quickWorkType = Assert.IsType<ComboBox>(dialog.FindName("ConceptWorkTypeComboBox"));
                var workWorkType = Assert.IsType<ComboBox>(dialog.FindName("ConceptWorkWorkTypeComboBox"));
                dialog.SpecialDrafts.WorkType = AudioWorkType.Audiobook;
                DrainDispatcher();
                Assert.Equal(AudioWorkType.Audiobook, quickWorkType.SelectedValue);
                Assert.Equal(AudioWorkType.Audiobook, workWorkType.SelectedValue);

                dialog.SpecialDrafts.Clear();
                DrainDispatcher();
                Assert.Equal(string.Empty, quickYear.Text);
                Assert.Null(quickWorkType.SelectedValue);
            }
            finally
            {
                dialog.WarnBeforeDiscardingConceptDrafts = false;
                dialog.Close();
            }
        });
    }

    [Fact]
    public void Track_badge_permissions_keep_remove_available_when_output_is_disabled_and_block_locked_removal()
    {
        RunOnStaThread(() =>
        {
            var metadataState = new ProjectMetadataState();
            metadataState.CanonicalMetadata.SetValue<int?>(ProjectMetadataField.OutputTrackNumber, 7, MetadataOrigin.User);
            metadataState.CanonicalMetadata.SetValue<int?>(ProjectMetadataField.OutputTrackTotal, 9, MetadataOrigin.User);
            var dialog = CreateMetadataDialog(metadataState, new MetadataPreferences());

            try
            {
                Assert.True(dialog.CanEditTrackNumber);
                Assert.True(dialog.CanRemoveTrackNumber);
                Assert.True(dialog.CanEditTrackCount);
                Assert.True(dialog.CanRemoveTrackCount);

                dialog.IsTrackNumberIncluded = false;
                dialog.IsTrackCountIncluded = false;
                DrainDispatcher();

                Assert.False(dialog.CanEditTrackNumber);
                Assert.True(dialog.CanRemoveTrackNumber);
                Assert.False(dialog.CanEditTrackCount);
                Assert.True(dialog.CanRemoveTrackCount);

                dialog.IsTrackNumberLocked = true;
                dialog.IsTrackCountLocked = true;
                DrainDispatcher();

                Assert.False(dialog.IsTrackNumberIncluded);
                Assert.False(dialog.IsTrackCountIncluded);
                Assert.False(dialog.CanEditTrackNumber);
                Assert.False(dialog.CanRemoveTrackNumber);
                Assert.False(dialog.CanEditTrackCount);
                Assert.False(dialog.CanRemoveTrackCount);

                var removeMethod = typeof(MetadataDialog).GetMethod(
                    "RemoveSingleValue_Click",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.NotNull(removeMethod);

                var lockedTrackNumberButton = new Button
                {
                    Tag = "TrackNumber",
                    DataContext = Assert.Single(dialog.TrackNumberValues)
                };
                removeMethod!.Invoke(
                    dialog,
                    [lockedTrackNumberButton, new System.Windows.RoutedEventArgs(Button.ClickEvent, lockedTrackNumberButton)]);
                Assert.Single(dialog.TrackNumberValues);
                Assert.Equal(7, metadataState.CanonicalMetadata.GetValue<int?>(ProjectMetadataField.OutputTrackNumber).Value);

                var lockedTrackCountButton = new Button
                {
                    Tag = "TrackCount",
                    DataContext = Assert.Single(dialog.TrackCountValues)
                };
                removeMethod.Invoke(
                    dialog,
                    [lockedTrackCountButton, new System.Windows.RoutedEventArgs(Button.ClickEvent, lockedTrackCountButton)]);
                Assert.Single(dialog.TrackCountValues);
                Assert.Equal(9, metadataState.CanonicalMetadata.GetValue<int?>(ProjectMetadataField.OutputTrackTotal).Value);

                dialog.IsTrackNumberLocked = false;
                dialog.IsTrackCountLocked = false;
                DrainDispatcher();

                Assert.False(dialog.IsTrackNumberIncluded);
                Assert.False(dialog.IsTrackCountIncluded);
                Assert.False(dialog.CanEditTrackNumber);
                Assert.True(dialog.CanRemoveTrackNumber);
                Assert.False(dialog.CanEditTrackCount);
                Assert.True(dialog.CanRemoveTrackCount);

                var trackNumberButton = new Button
                {
                    Tag = "TrackNumber",
                    DataContext = Assert.Single(dialog.TrackNumberValues)
                };
                removeMethod.Invoke(
                    dialog,
                    [trackNumberButton, new System.Windows.RoutedEventArgs(Button.ClickEvent, trackNumberButton)]);
                Assert.Empty(dialog.TrackNumberValues);
                Assert.Null(metadataState.CanonicalMetadata.GetValue<int?>(ProjectMetadataField.OutputTrackNumber).Value);

                var trackCountButton = new Button
                {
                    Tag = "TrackCount",
                    DataContext = Assert.Single(dialog.TrackCountValues)
                };
                removeMethod.Invoke(
                    dialog,
                    [trackCountButton, new System.Windows.RoutedEventArgs(Button.ClickEvent, trackCountButton)]);
                Assert.Empty(dialog.TrackCountValues);
                Assert.Null(metadataState.CanonicalMetadata.GetValue<int?>(ProjectMetadataField.OutputTrackTotal).Value);
            }
            finally
            {
                dialog.WarnBeforeDiscardingConceptDrafts = false;
                dialog.Close();
            }
        });
    }

}
