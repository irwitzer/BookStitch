using System.Xml.Linq;
using BookStitch.Tests.TestHelpers;
using Xunit;

namespace BookStitch.Tests.Ui;

public sealed class GenreSettingsStructureTests
{
    private static readonly string MainWindowSource =
        RepositorySource.Read(["BookStitch", "MainWindow.xaml.cs"]);

    [Fact]
    public void Private_genre_switch_uses_shared_owner_without_resetting_metadata_or_default()
    {
        var change = SourceContract.ExtractMember(
            RepositorySource.Read(["BookStitch", "Windows", "DeveloperToolsWindow.xaml.cs"]),
            "private void PrivateGenreListCheckBox_Changed(object sender, RoutedEventArgs e)");
        Assert.Contains("_settingsManager.SetPrivateGenres(checkBox.IsChecked == true);", change);
        Assert.DoesNotContain("DefaultGenre =", change);
        Assert.DoesNotContain("_refreshGenreOptions()", change);
    }

    [Fact]
    public void Genre_refresh_preserves_a_selectable_value_unless_default_reset_is_requested()
    {
        var source = RepositorySource.Read(["BookStitch", "MainWindow.Genres.cs"]);
        var refresh = SourceContract.ExtractMember(
            source,
            "public void RefreshGenreOptions(bool resetToDefault = false)");
        Assert.Contains("OnPropertyChanged(nameof(GenreOptions));", refresh, StringComparison.Ordinal);
        Assert.Contains("var selectedGenre = Genre;", refresh, StringComparison.Ordinal);
        Assert.Contains("if (resetToDefault || !GenreListService.IsSelectableGenre(selectedGenre))", refresh, StringComparison.Ordinal);
        Assert.Contains("Genre = GenreListService.GetDefaultGenre(_settings.UsePrivateGenreList);", refresh, StringComparison.Ordinal);
        Assert.DoesNotContain("GenreComboBox", refresh, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("private ProjectMetadataInitializationOptions CreateLocalFolderAggregationOptions(")]
    [InlineData("private ProjectMetadataInitializationOptions CreateMp3DiscProjectAggregationOptions()")]
    [InlineData("private ProjectMetadataInitializationOptions CreateAudioDiscProjectAggregationOptions()")]
    public void Source_aggregation_forwards_current_genre_seed_and_lock_preferences(string signature)
    {
        var options = SourceContract.ExtractMember(MainWindowSource, signature);

        Assert.Contains("ReplaceableGenreSeed: _replaceableGenreSeed", options, StringComparison.Ordinal);
        Assert.Contains(
            "IsGenrePreferenceFixed: _settings.MetadataPreferences.GenrePreferenceLocked",
            options,
            StringComparison.Ordinal);
        Assert.Contains(
            "PreferredGenres: _settings.MetadataPreferences.PreferredGenres",
            options,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Settings_load_initializes_an_empty_genre_session_before_use()
    {
        var loadSettings = SourceContract.ExtractMember(MainWindowSource, "private void LoadSettings()");
        Assert.Contains("InitializeGenreStateForEmptySession();", loadSettings, StringComparison.Ordinal);

        var initialize = SourceContract.ExtractMember(
            MainWindowSource,
            "private void InitializeGenreStateForEmptySession()");
        SourceContract.AssertInOrder(
            initialize,
            "_replaceableGenreSeed = \"\";",
            "_projectMetadata.Genre = \"\";",
            "ApplyLockedGenrePreferencesToMetadataState();");
    }

    [Fact]
    public void Empty_session_filters_and_deduplicates_locked_genre_preferences()
    {
        var apply = SourceContract.ExtractMember(
            MainWindowSource,
            "private void ApplyLockedGenrePreferencesToMetadataState()");
        Assert.Contains("if (!_settings.MetadataPreferences.GenrePreferenceLocked)", apply, StringComparison.Ordinal);
        SourceContract.AssertInOrder(
            apply,
            "_settings.MetadataPreferences.PreferredGenres",
            ".Where(GenreListService.IsSelectableGenre)",
            ".Distinct(StringComparer.OrdinalIgnoreCase)",
            "MetadataOrigin.GlobalPreference");
    }

    private static XElement Single(
        XContainer container,
        string elementName,
        string attributeName,
        string value) =>
        container.Descendants().Single(element =>
            element.Name.LocalName == elementName && Attribute(element, attributeName) == value);

    private static string Attribute(XElement element, string name) =>
        element.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == name)?.Value ?? string.Empty;
}
