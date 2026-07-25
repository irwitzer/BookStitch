using BookStitch.Services;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace BookStitch.Dialog;

public sealed record AddMoreDiscsDialogResult(
    int TotalDiscs,
    string SourceFolder);

public partial class AddMoreDiscsDialog : Window
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(1);
    private const double DriveItemHeight = 51;
    private const double MinimumDriveListHeight = 53;
    private const double MaximumDriveListHeight = 257;
    private const double BaseWindowHeightWithoutDriveList = 320;

    private readonly int _minimumTotalDiscs;
    private readonly int _maximumTotalDiscs;
    private readonly DiscDriveService _discDriveService = new();
    private readonly DispatcherTimer _refreshTimer;
    private string _driveSnapshot = string.Empty;
    private bool _refreshInProgress;

    public AddMoreDiscsDialogResult Result { get; private set; } = new(1, string.Empty);

    public AddMoreDiscsDialog(
        int importedDiscCount,
        int currentTotalDiscs,
        int defaultTotalDiscs,
        int minimumTotalDiscs,
        int maximumTotalDiscs,
        IReadOnlyList<DiscDriveInfo> drives,
        string? preferredDriveRoot,
        string sourceDialogInitialDirectory)
    {
        InitializeComponent();

        _minimumTotalDiscs = minimumTotalDiscs;
        _maximumTotalDiscs = maximumTotalDiscs;
        MessageText.Text =
            $"Dieses MP3-CD-Projekt enthält aktuell {importedDiscCount} vollständig importierte CD(s).\n\n" +
            "Wie viele CDs soll das Projekt nach dem Hinzufügen insgesamt enthalten?";

        TotalDiscsTextBox.Text = Math.Clamp(defaultTotalDiscs, minimumTotalDiscs, maximumTotalDiscs)
            .ToString(CultureInfo.InvariantCulture);
        TotalDiscsTextBox.SelectAll();

        ApplyDriveList(drives, preferredDriveRoot);

        _refreshTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = RefreshInterval
        };
        _refreshTimer.Tick += RefreshTimer_Tick;
        Loaded += AddMoreDiscsDialog_Loaded;
        Closed += (_, _) => _refreshTimer.Stop();
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetTotalDiscs(out var totalDiscs))
            return;

        if (DriveList.SelectedItem is not DiscDriveInfo drive || drive.IsChecking)
        {
            ShowError("Bitte ein verfügbares CD-Laufwerk auswählen.");
            return;
        }

        Result = new AddMoreDiscsDialogResult(totalDiscs, drive.RootPath);
        DialogResult = true;
        Close();
    }

    private void DriveList_MouseDoubleClick(object sender, MouseButtonEventArgs e) => OkButton_Click(sender, e);

    private bool TryGetTotalDiscs(out int totalDiscs)
    {
        if (int.TryParse(TotalDiscsTextBox.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out totalDiscs) &&
            totalDiscs >= _minimumTotalDiscs &&
            totalDiscs <= _maximumTotalDiscs)
        {
            ErrorText.Visibility = Visibility.Collapsed;
            return true;
        }

        ShowError($"Bitte eine Gesamtzahl von {_minimumTotalDiscs} bis {_maximumTotalDiscs} eingeben.");
        TotalDiscsTextBox.Focus();
        TotalDiscsTextBox.SelectAll();
        return false;
    }

    private async void AddMoreDiscsDialog_Loaded(object sender, RoutedEventArgs e)
    {
        TotalDiscsTextBox.Focus();
        await RefreshDriveListAsync();
        if (IsVisible)
            _refreshTimer.Start();
    }

    private async void RefreshTimer_Tick(object? sender, EventArgs e) => await RefreshDriveListAsync();

    private async Task RefreshDriveListAsync()
    {
        if (_refreshInProgress || !IsVisible)
            return;

        _refreshInProgress = true;
        try
        {
            var selectedRoot = (DriveList.SelectedItem as DiscDriveInfo)?.RootPath;
            var drives = await Task.Run(_discDriveService.GetCdDrives);
            if (!IsVisible)
                return;

            var snapshot = CreateDriveSnapshot(drives);
            if (!string.Equals(snapshot, _driveSnapshot, StringComparison.Ordinal))
                ApplyDriveList(drives, selectedRoot);
        }
        catch
        {
            // Beim Einlegen, Auswerfen oder Hochfahren darf die letzte gültige Liste sichtbar bleiben.
        }
        finally
        {
            _refreshInProgress = false;
        }
    }

    private void ApplyDriveList(IReadOnlyList<DiscDriveInfo> drives, string? preferredDriveRoot)
    {
        DriveList.ItemsSource = drives;
        DriveList.SelectedItem = DiscDriveService.SelectPreferredDrive(drives, preferredDriveRoot);
        if (DriveList.SelectedItem is not null)
            DriveList.ScrollIntoView(DriveList.SelectedItem);

        UpdateDriveListLayout(drives.Count);
        _driveSnapshot = CreateDriveSnapshot(drives);
    }


    private void UpdateDriveListLayout(int driveCount)
    {
        var visibleItemCount = Math.Max(1, driveCount);
        var desiredHeight = Math.Clamp(
            visibleItemCount * DriveItemHeight + 2,
            MinimumDriveListHeight,
            MaximumDriveListHeight);

        DriveList.Height = desiredHeight;
        Height = BaseWindowHeightWithoutDriveList + desiredHeight;
    }

    private static string CreateDriveSnapshot(IEnumerable<DiscDriveInfo> drives) =>
        string.Join("|", drives.Select(drive =>
            $"{drive.RootPath}\u001f{drive.IsReady}\u001f{drive.VolumeLabel}\u001f{drive.MediaKind}\u001f{drive.DriveName}\u001f{drive.DevicePath}\u001f{drive.IsChecking}"));

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

    private void TotalDiscsTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        e.Handled = !e.Text.All(char.IsDigit);
    }

    private void TotalDiscsTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            OkButton_Click(sender, e);
            e.Handled = true;
        }

    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Up or Key.Down or Key.PageUp or Key.PageDown))
            return;

        var delta = e.Key is Key.Up or Key.PageUp ? -1 : 1;
        MoveDriveSelection(delta);
        e.Handled = true;
    }

    private void MoveDriveSelection(int delta)
    {
        if (DriveList.Items.Count == 0)
            return;

        var currentIndex = DriveList.SelectedIndex;
        if (currentIndex < 0)
            currentIndex = 0;

        var nextIndex = Math.Clamp(currentIndex + delta, 0, DriveList.Items.Count - 1);
        DriveList.SelectedIndex = nextIndex;
        DriveList.ScrollIntoView(DriveList.SelectedItem);
    }

    private void TotalDiscsTextBox_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        ChangeTotalDiscs(e.Delta > 0 ? 1 : -1);
        e.Handled = true;
    }

    private void IncreaseTotalDiscs_Click(object sender, RoutedEventArgs e) => ChangeTotalDiscs(1);

    private void DecreaseTotalDiscs_Click(object sender, RoutedEventArgs e) => ChangeTotalDiscs(-1);

    private void ChangeTotalDiscs(int delta)
    {
        var current = int.TryParse(TotalDiscsTextBox.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : _minimumTotalDiscs;

        var next = Math.Clamp(current + delta, _minimumTotalDiscs, _maximumTotalDiscs);
        TotalDiscsTextBox.Text = next.ToString(CultureInfo.InvariantCulture);
        TotalDiscsTextBox.SelectAll();
        TotalDiscsTextBox.Focus();
        ErrorText.Visibility = Visibility.Collapsed;
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }
}
