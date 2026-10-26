using BookStitch.Controls;
using BookStitch.Models;
using BookStitch.Services;
using BookStitch.Tests.TestHelpers;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Xunit;

namespace BookStitch.Tests.Ui;

public sealed partial class WpfDialogRuntimeTests
{
    [Theory]
    [InlineData(BookStitch.Dialog.ProjectSetupSourceKind.Folder)]
    [InlineData(BookStitch.Dialog.ProjectSetupSourceKind.Mp3Disc)]
    [InlineData(BookStitch.Dialog.ProjectSetupSourceKind.AudioDisc)]
    public void Genre_setup_choices_follow_live_settings_without_changing_project_metadata(BookStitch.Dialog.ProjectSetupSourceKind sourceKind)
    {
        RunOnStaThread(() => WithSettingsCulture("es", () =>
        {
            using var root = new TemporaryFolder();
            using var manager = new GlobalSettingsManager(new SettingsService(
                System.IO.Path.Combine(root.Path, "app"), root.Path));
            var state = new ProjectMetadataState { Genre = "Unverändertes Projektgenre" };
            var dialog = CreateProjectSetupDialog(sourceKind, metadataState: state, settingsManager: manager);
            try
            {
                dialog.Show(); DrainDispatcher();
                manager.SetPrivateGenres(true);
                manager.SetGenreListAlwaysEnglish(true);
                DrainDispatcher();
                var genreControl = (ConceptMetadataFieldControl)dialog.FindName("SetupGenreField");
                var field = Assert.IsType<BookStitch.Dialog.ConceptMetadataFieldViewModel>(genreControl.DataContext);
                Assert.Equal(GenreListService.GetGenres(manager.Current),
                    field.Candidates.Take(79).Select(candidate => candidate.DisplayValue));
                manager.SetGenreGroups(libraryTags: false);
                manager.SetPrivateGenres(false);
                DrainDispatcher();
                Assert.Equal(62, field.Candidates.Count(candidate => candidate.EvidenceOrigin == MetadataOrigin.StaticList));
                Assert.Equal("Unverändertes Projektgenre", state.Genre);
            }
            finally { dialog.Close(); DrainDispatcher(); }
        }));
    }

    [Fact]
    public void Genre_catalogue_order_reaches_metadata_candidates_without_rewriting_existing_values()
    {
        RunOnStaThread(() => WithSettingsCulture("de", () =>
        {
            using var root = new TemporaryFolder();
            using var manager = new GlobalSettingsManager(new SettingsService(
                System.IO.Path.Combine(root.Path, "app"), root.Path));
            var state = new ProjectMetadataState { Genre = "Mein vorhandenes Genre" };
            var dialog = new BookStitch.Dialog.MetadataDialog(false, _ => { }, state, manager.Current,
                false, _ => { }, () => { }, genreSettingsManager: manager);
            try
            {
                dialog.Show(); DrainDispatcher();
                manager.SetPrivateGenres(true);
                DrainDispatcher();
                Assert.Equal(GenreListService.GetGenres(manager.Current),
                    dialog.GenresField.Candidates.Take(79).Select(candidate => candidate.DisplayValue));
                Assert.Contains(dialog.GenresField.Candidates, candidate => candidate.DisplayValue == "Mein vorhandenes Genre");
                manager.SetGenreListAlwaysEnglish(true);
                DrainDispatcher();
                Assert.Contains(dialog.GenresField.Candidates, candidate => candidate.DisplayValue == "Crime Fiction");
                Assert.DoesNotContain(dialog.GenresField.Candidates, candidate => candidate.DisplayValue == "Krimi");
                manager.SetPrivateGenres(false);
                DrainDispatcher();
                Assert.DoesNotContain(dialog.GenresField.Candidates, candidate => candidate.DisplayValue == "iBook Wissen");
                Assert.Equal("Mein vorhandenes Genre", state.Genre);
                Assert.Equal("Audiobook", manager.Current.DefaultGenre);
            }
            finally { dialog.Close(); DrainDispatcher(); }
        }));
    }

    [Theory]
    [InlineData("de", "Black")][InlineData("es", "Ivory")][InlineData("ja", "Black")]
    public void Genre_settings_views_share_options_immediately_preserve_values_and_follow_enabled_access(string language, string background)
    {
        RunOnStaThread(() => WithSettingsCulture(language, () =>
        {
            using var root = new TemporaryFolder();
            var store = new SettingsService(System.IO.Path.Combine(root.Path, "app"), root.Path);
            using var manager = new GlobalSettingsManager(store);
            manager.Current.DefaultGenre = "Mein Genre";
            manager.Current.BackgroundPreset = background;
            var access = DeveloperToolsCommands.AccessState;
            access.Initialize(manager.Current, BookStitchEnvironment.Production, () => true);
            var first = new SettingsTagsChaptersControl();
            var second = new SettingsTagsChaptersControl();
            foreach (var view in new[] { first, second })
            {
                view.Initialize(manager, () => null, null, null);
                view.ShowSection(SettingsTagsChaptersSection.Metadata);
            }
            var host = new Window { Content = new StackPanel { Children = { first, second } },
                Width = 1050, Height = 800, ShowInTaskbar = false };
            try
            {
                AppearanceSettingsService.ApplyToResources(host.Resources, manager.Current);
                host.Show(); DrainDispatcher();
                CheckBox Toggle(SettingsTagsChaptersControl view, string name) => (CheckBox)view.FindName(name);
                ComboBox Preview(SettingsTagsChaptersControl view) => (ComboBox)view.FindName("GenreListPreviewComboBox");
                Assert.Equal(74, Preview(first).Items.Count);
                SaveLayoutRepairPreview(first, $"genres-{language}-{background}");
                Assert.Equal(Visibility.Collapsed, Toggle(first, "PrivateGenresCheckBox").Visibility);
                manager.Current.DeveloperToolsEnabled = true;
                manager.Current.DeveloperToolsVisible = false;
                access.Initialize(manager.Current, BookStitchEnvironment.Production, () => true);
                DrainDispatcher();
                Assert.True(access.IsEnabled);
                Assert.False(access.IsVisible);
                // Access notifications may originate on another STA during lifecycle tests.
                System.Threading.Tasks.Task.Run(() => access.Initialize(
                    new AppSettings(), BookStitchEnvironment.Production, () => true)).GetAwaiter().GetResult();
                DrainDispatcher();
                Assert.Equal(Visibility.Collapsed, Toggle(first, "PrivateGenresCheckBox").Visibility);
                access.Initialize(manager.Current, BookStitchEnvironment.Production, () => true);
                Assert.Equal(Visibility.Visible, Toggle(first, "PrivateGenresCheckBox").Visibility);
                Toggle(first, "PrivateGenresCheckBox").IsChecked = true;
                Assert.True(manager.Current.UsePrivateGenreList);
                Assert.True(Toggle(second, "PrivateGenresCheckBox").IsChecked);
                Assert.Equal(GenreListService.PrivateLibraryTags, Preview(second).Items.Cast<string>().Take(5));
                Toggle(first, "GenreLibraryTagsCheckBox").IsChecked = false;
                Assert.Equal(67, Preview(second).Items.Count);
                Toggle(second, "GenreCategoriesCheckBox").IsChecked = false;
                Assert.True(Toggle(first, "GenreLibraryTagsCheckBox").IsChecked);
                Assert.Equal(17, Preview(first).Items.Count);
                Toggle(first, "GenreEnglishCheckBox").IsChecked = true;
                Assert.True(Toggle(second, "GenreEnglishCheckBox").IsChecked);
                Assert.Contains("Audiobook", Preview(first).Items.Cast<string>());
                Assert.DoesNotContain("Genre.Tag.Audiobook", Preview(first).Items.Cast<string>());
                manager.SetPrivateGenres(false); // Same owner used by the Developer Tools view.
                Assert.False(Toggle(first, "PrivateGenresCheckBox").IsChecked);
                Assert.Equal(12, Preview(first).Items.Count);
                access.VisibilityChoice = DeveloperToolsVisibilityChoice.Conceal;
                Assert.Equal(Visibility.Collapsed, Toggle(first, "PrivateGenresCheckBox").Visibility);
                Assert.Equal("Mein Genre", manager.Current.DefaultGenre);
                manager.Flush();
                var loaded = store.Load();
                Assert.True(loaded.GenreListAlwaysEnglish);
                Assert.True(loaded.IncludeGenreLibraryTags);
                Assert.False(loaded.IncludeGenreCategories);
                Assert.Equal("Mein Genre", loaded.DefaultGenre);
            }
            finally
            {
                host.Close(); DrainDispatcher();
                access.Initialize(new AppSettings(), BookStitchEnvironment.Production, () => false);
            }
        }));
    }
}
