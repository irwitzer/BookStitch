using BookStitch.Services;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;

namespace BookStitch;

public partial class MainWindow
{
    public IReadOnlyList<string> GenreOptions => GenreListService.GetGenres(_settings);

    public void RefreshGenreOptions(bool resetToDefault = false)
    {
        OnPropertyChanged(nameof(GenreOptions));
        var selectedGenre = Genre;
        if (resetToDefault || !GenreListService.IsSelectableGenre(selectedGenre))
            Genre = GenreListService.GetDefaultGenre(_settings.UsePrivateGenreList);
    }

    private void GlobalSettingsManager_GenreOptionsChanged(object? sender, EventArgs e) =>
        RefreshGenreOptions();

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        PolishAutoMergeCheckBoxSpacing();
    }

    private void PolishAutoMergeCheckBoxSpacing()
    {
        foreach (var checkBox in FindVisualChildren<CheckBox>(this))
        {
            var binding = BindingOperations.GetBindingExpression(checkBox, ToggleButton.IsCheckedProperty);
            if (!string.Equals(binding?.ParentBinding.Path?.Path, nameof(MergeAutomaticallyAfterConversion), StringComparison.Ordinal))
                continue;

            checkBox.Margin = new Thickness(0, 0, 0, 0);
            break;
        }
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
    {
        var childCount = VisualTreeHelper.GetChildrenCount(parent);
        for (var index = 0; index < childCount; index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T typedChild)
                yield return typedChild;

            foreach (var descendant in FindVisualChildren<T>(child))
                yield return descendant;
        }
    }
}
