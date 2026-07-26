using BookStitch.Services;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace BookStitch;

public partial class SettingsWindow
{
    private CheckBox? _privateGenreListCheckBox;

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        EnsurePrivateGenreListCheckBox();
    }

    private void EnsurePrivateGenreListCheckBox()
    {
        if (_privateGenreListCheckBox is not null)
            return;

        if (FindName("ForceShowFfmpegSetupButtonCheckBox") is not CheckBox ffmpegCheckBox)
            return;

        if (ffmpegCheckBox.Parent is not Panel developerDisplayRow ||
            developerDisplayRow.Parent is not Panel developerDisplayPanel)
        {
            return;
        }

        _privateGenreListCheckBox = new CheckBox
        {
            Content = "zweite Genre-Liste",
            Foreground = TryFindResource("MainTextBrush") as Brush ?? Foreground,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 8, 0, 0),
            ToolTip = "Ersetzt die Bibliothek-Tags der Genre-Liste durch die private iBook-Liste.",
            IsChecked = _settings.UsePrivateGenreList
        };
        _privateGenreListCheckBox.Checked += PrivateGenreListCheckBox_Changed;
        _privateGenreListCheckBox.Unchecked += PrivateGenreListCheckBox_Changed;

        var rowIndex = developerDisplayPanel.Children.IndexOf(developerDisplayRow);
        if (rowIndex < 0 || rowIndex >= developerDisplayPanel.Children.Count - 1)
            developerDisplayPanel.Children.Add(_privateGenreListCheckBox);
        else
            developerDisplayPanel.Children.Insert(rowIndex + 1, _privateGenreListCheckBox);
    }

    private void PrivateGenreListCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_isLoading || sender is not CheckBox checkBox)
            return;

        _settings.UsePrivateGenreList = checkBox.IsChecked == true;
        _settings.DefaultGenre = GenreListService.GetDefaultGenre(_settings.UsePrivateGenreList);
        _settingsService.Save(_settings);

        if (Owner is MainWindow mainWindow)
            mainWindow.RefreshGenreOptions(resetToDefault: true);
    }
}
