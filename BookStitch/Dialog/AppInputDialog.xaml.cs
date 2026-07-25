using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Input;

namespace BookStitch.Dialog;

public partial class AppInputDialog : Window
{
    private readonly int _minimum;
    private readonly int _maximum;

    public int Value { get; private set; }

    public AppInputDialog(
        string title,
        string heading,
        string message,
        int defaultValue,
        int minimum,
        int maximum)
    {
        InitializeComponent();

        _minimum = minimum;
        _maximum = maximum;

        Title = title;
        TitleText.Text = title;
        HeadingText.Text = heading;
        MessageText.Text = message;
        InputTextBox.Text = defaultValue.ToString(CultureInfo.InvariantCulture);
        InputTextBox.SelectAll();

        Loaded += (_, _) => InputTextBox.Focus();
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(InputTextBox.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ||
            value < _minimum ||
            value > _maximum)
        {
            ErrorText.Text = $"Bitte eine Zahl von {_minimum} bis {_maximum} eingeben.";
            ErrorText.Visibility = Visibility.Visible;
            InputTextBox.Focus();
            InputTextBox.SelectAll();
            return;
        }

        Value = value;
        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void InputTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        e.Handled = !e.Text.All(char.IsDigit);
    }

    private void InputTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            OkButton_Click(sender, e);
            e.Handled = true;
        }
    }
}
