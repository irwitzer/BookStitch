using BookStitch.Services;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;

namespace BookStitch;

public partial class MainWindow
{
    public IReadOnlyList<string> GenreOptions => GenreListService.GetGenres(_settings.UsePrivateGenreList);

    public void RefreshGenreOptions()
    {
        OnPropertyChanged(nameof(GenreOptions));
        ApplyGenreOptionsToComboBox();
    }

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        ApplyGenreOptionsToComboBox();
        PolishAutoMergeCheckBoxSpacing();
    }

    private void ApplyGenreOptionsToComboBox()
    {
        if (GenreComboBox is null)
            return;

        var selectedGenre = Genre;
        GenreComboBox.ItemsSource = null;
        GenreComboBox.Items.Clear();
        GenreComboBox.ItemsSource = GenreOptions;

        if (!string.IsNullOrWhiteSpace(selectedGenre))
            Genre = selectedGenre;
        else
            Genre = GenreOptions.FirstOrDefault() ?? string.Empty;
    }

    private void PolishAutoMergeCheckBoxSpacing()
    {
        foreach (var checkBox in FindVisualChildren<CheckBox>(this))
        {
            var binding = BindingOperations.GetBindingExpression(checkBox, ToggleButton.IsCheckedProperty);
            if (!string.Equals(binding?.ParentBinding.Path?.Path, nameof(MergeAutomaticallyAfterConversion), StringComparison.Ordinal))
                continue;

            checkBox.Margin = new Thickness(-3, 0, 0, 0);
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
