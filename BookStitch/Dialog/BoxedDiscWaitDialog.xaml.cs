using System.Windows;

namespace BookStitch.Dialog;

public partial class BoxedDiscWaitDialog : Window
{
    public event EventHandler? ManualCheckRequested;
    public event EventHandler? DeferRequested;
    public event EventHandler? CompleteImportRequested;

    public BoxedDiscWaitDialog(string title, string driveDisplayName, string heading, string initialInstruction, string hintText, bool canCompleteImport)
    {
        InitializeComponent();
        Title = title;
        TitleTextBlock.Text = title;
        var hasDrive = !string.IsNullOrWhiteSpace(driveDisplayName);
        DriveSeparatorTextBlock.Visibility = hasDrive ? Visibility.Visible : Visibility.Collapsed;
        DriveTextBlock.Visibility = hasDrive ? Visibility.Visible : Visibility.Collapsed;
        DriveTextBlock.Text = hasDrive ? driveDisplayName.Trim() : string.Empty;
        HeadingTextBlock.Text = heading;
        StatusTextBlock.Text = initialInstruction;
        HintTextBlock.Text = hintText;
        CompleteImportButton.Visibility = canCompleteImport ? Visibility.Visible : Visibility.Collapsed;
    }

    public bool AcceptNextDuplicate => AcceptDuplicateCheckBox.IsChecked == true;

    public bool IsManualCheckEnabled
    {
        get => CheckButton.IsEnabled;
        set => CheckButton.IsEnabled = value;
    }

    public void SetStatusText(string value)
    {
        if (!string.Equals(StatusTextBlock.Text, value, StringComparison.Ordinal))
            StatusTextBlock.Text = value;
    }

    private void CheckButton_Click(object sender, RoutedEventArgs e) => ManualCheckRequested?.Invoke(this, EventArgs.Empty);
    private void DeferButton_Click(object sender, RoutedEventArgs e) => DeferRequested?.Invoke(this, EventArgs.Empty);
    private void CompleteImportButton_Click(object sender, RoutedEventArgs e) => CompleteImportRequested?.Invoke(this, EventArgs.Empty);
    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
