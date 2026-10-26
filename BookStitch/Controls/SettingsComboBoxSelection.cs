using System.Windows.Controls;

namespace BookStitch.Controls;

internal static class SettingsComboBoxSelection
{
    public static void Populate(ComboBox comboBox, params (string Tag, string Text)[] items)
    {
        comboBox.Items.Clear();
        foreach (var item in items)
        {
            comboBox.Items.Add(new ComboBoxItem
            {
                Tag = item.Tag,
                Content = item.Text
            });
        }
    }

    public static void SelectByTag(ComboBox comboBox, string tag)
    {
        foreach (var item in comboBox.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(item.Tag?.ToString(), tag, StringComparison.Ordinal))
            {
                comboBox.SelectedItem = item;
                return;
            }
        }

        comboBox.SelectedIndex = comboBox.Items.Count > 0 ? 0 : -1;
    }

    public static void SelectByContent(ComboBox comboBox, string value)
    {
        foreach (var item in comboBox.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(item.Content?.ToString(), value, StringComparison.OrdinalIgnoreCase))
            {
                comboBox.SelectedItem = item;
                return;
            }
        }

        comboBox.SelectedIndex = 0;
    }

    public static string GetSelectedTag(ComboBox comboBox, string fallback) =>
        comboBox.SelectedItem is ComboBoxItem { Tag: not null } item
            ? item.Tag.ToString() ?? fallback
            : fallback;

    public static string GetText(ComboBox comboBox) =>
        comboBox.SelectedItem is ComboBoxItem selectedItem
            ? selectedItem.Content?.ToString() ?? comboBox.Text
            : comboBox.Text;
}
