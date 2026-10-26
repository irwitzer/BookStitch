using BookStitch.Services;
using System.ComponentModel;
using System.Windows;

namespace BookStitch.Controls;

public partial class SettingsTagsChaptersControl
{
    private bool _genreSettingsSubscribed;
    private Window? _genreSettingsOwner;

    private void SubscribeGenreSettings()
    {
        if (_settingsManager is null || _genreSettingsSubscribed) return;
        WeakEventManager<GlobalSettingsManager, EventArgs>.AddHandler(
            _settingsManager, nameof(GlobalSettingsManager.GenreOptionsChanged), GenreSettingsChanged);
        PropertyChangedEventManager.AddHandler(DeveloperToolsCommands.AccessState, GenreAccessChanged,
            nameof(DeveloperToolsAccessState.IsEnabled));
        _genreSettingsOwner = Window.GetWindow(this);
        if (_genreSettingsOwner is not null) _genreSettingsOwner.Closed += GenreSettingsOwnerClosed;
        _genreSettingsSubscribed = true;
        RefreshGenreSettings();
    }

    private void UnsubscribeGenreSettings()
    {
        if (_settingsManager is null || !_genreSettingsSubscribed) return;
        WeakEventManager<GlobalSettingsManager, EventArgs>.RemoveHandler(
            _settingsManager, nameof(GlobalSettingsManager.GenreOptionsChanged), GenreSettingsChanged);
        PropertyChangedEventManager.RemoveHandler(DeveloperToolsCommands.AccessState, GenreAccessChanged,
            nameof(DeveloperToolsAccessState.IsEnabled));
        if (_genreSettingsOwner is not null) _genreSettingsOwner.Closed -= GenreSettingsOwnerClosed;
        _genreSettingsOwner = null;
        _genreSettingsSubscribed = false;
    }

    private void GenreSettingsOwnerClosed(object? sender, EventArgs e) => UnsubscribeGenreSettings();

    private void GenreSettingsChanged(object? sender, EventArgs e) => RefreshLiveGenreSettings();
    private void GenreAccessChanged(object? sender, PropertyChangedEventArgs e) => RefreshLiveGenreSettings();

    private void RefreshLiveGenreSettings()
    {
        if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished) return;
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.InvokeAsync(RefreshLiveGenreSettings);
            return;
        }
        if (_genreSettingsSubscribed && IsLoaded) RefreshGenreSettings();
    }

    private void RefreshGenreSettings()
    {
        if (_settingsManager is null) return;
        var wasLoading = _isLoading;
        _isLoading = true;
        try
        {
            GenreLibraryTagsCheckBox.IsChecked = Settings.IncludeGenreLibraryTags;
            GenreCategoriesCheckBox.IsChecked = Settings.IncludeGenreCategories;
            GenreEnglishCheckBox.IsChecked = Settings.GenreListAlwaysEnglish;
            PrivateGenresCheckBox.IsChecked = Settings.UsePrivateGenreList;
            PrivateGenresCheckBox.Visibility = DeveloperToolsCommands.AccessState.IsEnabled
                ? Visibility.Visible : Visibility.Collapsed;
            var selected = GenreListPreviewComboBox.SelectedItem as string;
            GenreListPreviewComboBox.ItemsSource = GenreListService.GetGenres(Settings);
            GenreListPreviewComboBox.SelectedItem = selected ?? Settings.DefaultGenre;
            if (GenreListPreviewComboBox.SelectedIndex < 0) GenreListPreviewComboBox.SelectedIndex = 0;
        }
        finally { _isLoading = wasLoading; }
    }

    private void GenreLibraryTags_Changed(object sender, RoutedEventArgs e)
    {
        if (!_isLoading) _settingsManager?.SetGenreGroups(libraryTags: GenreLibraryTagsCheckBox.IsChecked == true);
    }

    private void GenreCategories_Changed(object sender, RoutedEventArgs e)
    {
        if (!_isLoading) _settingsManager?.SetGenreGroups(categories: GenreCategoriesCheckBox.IsChecked == true);
    }

    private void GenreEnglish_Changed(object sender, RoutedEventArgs e)
    {
        if (!_isLoading) _settingsManager?.SetGenreListAlwaysEnglish(GenreEnglishCheckBox.IsChecked == true);
    }

    private void PrivateGenres_Changed(object sender, RoutedEventArgs e)
    {
        if (!_isLoading) _settingsManager?.SetPrivateGenres(PrivateGenresCheckBox.IsChecked == true);
    }
}
