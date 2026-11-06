using BookStitch.Controls;
using BookStitch.Dialog;
using BookStitch.Models;
using BookStitch.Services;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Xunit;

namespace BookStitch.Tests.Ui;

public sealed partial class WpfDialogRuntimeTests
{
    [Theory]
    [InlineData(ProjectMetadataField.SortAlbum)]
    [InlineData(ProjectMetadataField.SortComposers)]
    public void Sort_suggestions_follow_active_binding_source_and_refresh_after_each_toggle(ProjectMetadataField sortField)
    {
        RunOnStaThread(() =>
        {
            var state = new ProjectMetadataState();
            state.BookTitle = "Bound title";
            state.Narrator = "Narrator A";
            var preferences = new MetadataPreferences { WarnBeforeDiscardingConceptDrafts = false };
            var dialog = CreateMetadataDialog(state, preferences);
            try
            {
                dialog.Show();
                DrainDispatcher();
                var binding = sortField == ProjectMetadataField.SortAlbum ? dialog.TitleAlbumBinding : dialog.NarratorComposerBinding;
                var sort = System.Linq.Enumerable.Single(dialog.SortingFields, field => field.Field == sortField);
                string[] Suggestions() => System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Select(sort.Candidates, candidate => candidate.DisplayValue));
                var bound = sortField == ProjectMetadataField.SortAlbum ? "Bound title" : "Narrator A";
                Assert.True(binding.IsActive);
                Assert.Contains(bound, Suggestions());
                Assert.All(sort.Candidates, candidate => { Assert.False(candidate.IsCanonicalWinner); Assert.False(candidate.IsAutomaticWinner); });

                binding.IsActive = false;
                binding.TargetField.Draft = "Manual value";
                binding.TargetField.CommitDraft();
                DrainDispatcher();
                Assert.Contains("Manual value", Suggestions());
                Assert.DoesNotContain(bound, Suggestions());

                binding.IsActive = true;
                DrainDispatcher();
                Assert.Contains(bound, Suggestions());
                Assert.DoesNotContain("Manual value", Suggestions());
                if (sortField == ProjectMetadataField.SortAlbum) state.BookTitle = "Changed title";
                else state.Narrator = "Narrator B";
                DrainDispatcher();
                var changed = sortField == ProjectMetadataField.SortAlbum ? "Changed title" : "Narrator B";
                Assert.Contains(changed, Suggestions());
                Assert.DoesNotContain(bound, Suggestions());

                if (sort.IsList) Assert.Null(state.CanonicalMetadata.GetValue<System.Collections.Generic.IReadOnlyList<string>>(sortField).Value);
                else Assert.Null(state.CanonicalMetadata.GetValue<string>(sortField).Value);
                sort.Draft = changed;
                sort.CommitDraft();
                Assert.Contains(changed, System.Linq.Enumerable.Select(sort.Values, value => value.Value));
            }
            finally { dialog.Close(); }
        });
    }

    [Fact]
    public void Release_date_views_commit_one_state_and_share_output_policy()
    {
        RunOnStaThread(() =>
        {
            var state = new ProjectMetadataState();
            var preferences = new MetadataPreferences { WarnBeforeDiscardingConceptDrafts = false };
            var dialog = CreateMetadataDialog(state, preferences);
            try
            {
                dialog.Show();
                DrainDispatcher();
                var quick = Assert.IsType<ComboBox>(dialog.FindName("ConceptQuickAccessReleaseDateComboBox"));
                var publication = Assert.IsType<ComboBox>(dialog.FindName("ConceptPublicationReleaseDateComboBox"));
                dialog.ReleaseDateDraft = "20240229";
                DrainDispatcher();
                quick.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(quick), 0, Key.Enter)
                    { RoutedEvent = Keyboard.PreviewKeyDownEvent });
                DrainDispatcher();
                Assert.Equal(new MetadataDate(2024, 2, 29), state.ReleaseDate);
                Assert.Equal("2024.02.29", Assert.Single(dialog.ReleaseDateValues).Value);
                dialog.IsReleaseDateIncluded = false;
                DrainDispatcher();
                Assert.Equal(OutputFieldPolicy.Disabled, MetadataPreferencesService.GetPolicy(preferences, ProjectMetadataField.ReleaseDate));
                Assert.Equal(string.Empty, quick.Text);
                Assert.Equal(string.Empty, publication.Text);
                Assert.Equal(new MetadataDate(2024, 2, 29), state.ReleaseDate);
                dialog.IsReleaseDateIncluded = true;
                Assert.Equal(OutputFieldPolicy.Enabled, MetadataPreferencesService.GetPolicy(preferences, ProjectMetadataField.ReleaseDate));
                Assert.Null(dialog.FindName("ConceptYearTextBox"));
                Assert.Null(dialog.FindName("ConceptPublicationYearField"));
            }
            finally { dialog.Close(); }
        });
    }

    [Fact]
    public void Manual_disc_people_split_semicolons_while_slashes_remain_text()
    {
        RunOnStaThread(() =>
        {
            var state = new ProjectMetadataState();
            var setup = CreateProjectSetupDialog(ProjectSetupSourceKind.Folder, getAnimationMilliseconds: () => 0,
                metadataState: state, metadataPreferences: new MetadataPreferences { WarnBeforeDiscardingConceptDrafts = false });
            try
            {
                var field = Assert.IsType<BookStitch.Dialog.ConceptMetadataFieldViewModel>(setup.SetupAuthorField.DataContext);
                field.Draft = "A / B; C";
                field.CommitDraft();
                Assert.Equal(new[] { "A / B", "C" }, state.CanonicalMetadata
                    .GetValue<System.Collections.Generic.IReadOnlyList<string>>(ProjectMetadataField.Authors).Value);
                Assert.IsType<ConceptSeriesFieldControl>(setup.SetupSeriesField);
            }
            finally { setup.Close(); }
        });
    }
}
