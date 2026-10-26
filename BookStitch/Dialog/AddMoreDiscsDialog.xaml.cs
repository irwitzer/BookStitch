using BookStitch.Services;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Input;

namespace BookStitch.Dialog;

public sealed record AddMoreDiscsDialogResult(
    int TotalDiscs,
    string SourceFolder);

public partial class AddMoreDiscsDialog : Window
{
    private const double DriveItemHeight = 51;
    private const double MinimumDriveListHeight = 59;
    private const double MaximumDriveListHeight = 263;
    private const double BaseWindowHeightWithoutDriveList = 320;

    private readonly int _minimumTotalDiscs;
    private readonly int _maximumTotalDiscs;
    private readonly DiscDriveService _discDriveService;
    private readonly DiscDriveRefreshService _refreshService;
    private readonly DiscDriveSelectionPresentationService _presentationService = new();
    private readonly IReadOnlyList<BookStitch.Models.ConfiguredDiscDrive> _configuredDrives;
    private readonly string? _initialPreferredDriveRoot;
    private readonly OpticalMediaChangeMonitor _mediaChangeMonitor;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private IReadOnlyList<DiscDriveInfo> _driveStates = [];

    public AddMoreDiscsDialogResult Result { get; private set; } = new(1, string.Empty);

    public AddMoreDiscsDialog(
        int importedDiscCount,
        int currentTotalDiscs,
        int defaultTotalDiscs,
        int minimumTotalDiscs,
        int maximumTotalDiscs,
        IReadOnlyList<DiscDriveInfo> drives,
        string? preferredDriveRoot,
        string sourceDialogInitialDirectory,
        IReadOnlyList<BookStitch.Models.ConfiguredDiscDrive>? configuredDrives = null,
        DiscDriveService? discDriveService = null,
        bool isAudioDiscProject = false)
    {
        InitializeComponent();
        _configuredDrives = configuredDrives ?? [];
        _initialPreferredDriveRoot = preferredDriveRoot;
        _discDriveService = discDriveService ?? new DiscDriveService();
        _refreshService = new DiscDriveRefreshService(_discDriveService);

        _minimumTotalDiscs = minimumTotalDiscs;
        _maximumTotalDiscs = maximumTotalDiscs;
        MessageText.Text = LocalizationText.Format(
            isAudioDiscProject ? "Disc.AddMore.AudioDescription" : "Disc.AddMore.Description",
            importedDiscCount);

        TotalDiscsTextBox.Text = Math.Clamp(defaultTotalDiscs, minimumTotalDiscs, maximumTotalDiscs)
            .ToString(CultureInfo.InvariantCulture);
        TotalDiscsTextBox.SelectAll();

        _driveStates = drives ?? [];
        ApplyDriveList(_driveStates, preferredDriveRoot);

        _mediaChangeMonitor = new OpticalMediaChangeMonitor(this);
        _mediaChangeMonitor.MediaChanged += MediaChangeMonitor_MediaChanged;
        Loaded += AddMoreDiscsDialog_Loaded;
        Closed += AddMoreDiscsDialog_Closed;
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetTotalDiscs(out var totalDiscs))
            return;

        if (DriveList.SelectedItem is not DiscDriveSelectionPresentation item ||
            item.DriveInfo.IsChecking ||
            !item.IsEnabled)
        {
            return;
        }

        Result = new AddMoreDiscsDialogResult(totalDiscs, item.DriveRoot);
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

        ShowError(LocalizationText.Format("Disc.AddMore.TotalRange", _minimumTotalDiscs, _maximumTotalDiscs));
        TotalDiscsTextBox.Focus();
        TotalDiscsTextBox.SelectAll();
        return false;
    }

    private async void AddMoreDiscsDialog_Loaded(object sender, RoutedEventArgs e)
    {
        TotalDiscsTextBox.Focus();
        await RefreshDriveListAsync();
    }

    private Task RefreshDriveListAsync() => RefreshDriveStateAsync(null);

    private async void MediaChangeMonitor_MediaChanged(object? sender, OpticalMediaChangedEventArgs e)
    {
        await RefreshDriveStateAsync(e.DriveRoot);
    }

    private async Task RefreshDriveStateAsync(string? changedDriveRoot)
    {
        if (!IsVisible)
            return;

        try
        {
            await _refreshGate.WaitAsync(_lifetimeCancellation.Token);
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
            return;
        }

        try
        {
            var preferredRoot = (DriveList.SelectedItem as DiscDriveSelectionPresentation)?.DriveRoot
                ?? _initialPreferredDriveRoot;
            Task PublishAsync(IReadOnlyList<DiscDriveInfo> snapshot) => Dispatcher.InvokeAsync(() =>
            {
                if (!IsVisible)
                    return;

                var selectedRoot = (DriveList.SelectedItem as DiscDriveSelectionPresentation)?.DriveRoot;
                _driveStates = snapshot;
                ApplyDriveList(snapshot, selectedRoot ?? preferredRoot);
            }).Task;

            if (string.IsNullOrWhiteSpace(changedDriveRoot))
            {
                await _refreshService.RefreshAsync(
                    _driveStates,
                    PublishAsync,
                    _lifetimeCancellation.Token);
            }
            else
            {
                await _refreshService.RefreshDriveAsync(
                    _driveStates,
                    changedDriveRoot,
                    PublishAsync,
                    _lifetimeCancellation.Token);
            }
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
        }
        catch
        {
            // Beim Einlegen, Auswerfen oder Hochfahren bleibt der letzte gültige Zustand sichtbar.
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    private void ApplyDriveList(IReadOnlyList<DiscDriveInfo> drives, string? preferredDriveRoot)
    {
        var presentation = _presentationService.Create(drives, _configuredDrives);
        DriveList.ItemsSource = presentation;
        DriveList.SelectedItem = DiscDriveSelectionPresentationService.SelectPreferred(presentation, preferredDriveRoot);
        if (DriveList.SelectedItem is not null)
            DriveList.ScrollIntoView(DriveList.SelectedItem);

        UpdateDriveListLayout(presentation.Count);
    }


    private void UpdateDriveListLayout(int driveCount)
    {
        var visibleItemCount = Math.Max(1, driveCount);
        var desiredHeight = Math.Clamp(
            visibleItemCount * DriveItemHeight + 8,
            MinimumDriveListHeight,
            MaximumDriveListHeight);

        DriveList.Height = desiredHeight;
        Height = BaseWindowHeightWithoutDriveList + desiredHeight;
    }


    private void AddMoreDiscsDialog_Closed(object? sender, EventArgs e)
    {
        _lifetimeCancellation.Cancel();
        _mediaChangeMonitor.MediaChanged -= MediaChangeMonitor_MediaChanged;
        _mediaChangeMonitor.Dispose();
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
        var nextIndex = currentIndex < 0
            ? (delta > 0 ? -1 : DriveList.Items.Count)
            : currentIndex;

        while (true)
        {
            nextIndex += delta;
            if (nextIndex < 0 || nextIndex >= DriveList.Items.Count)
                return;

            if (DriveList.Items[nextIndex] is DiscDriveSelectionPresentation { IsEnabled: true })
            {
                DriveList.SelectedIndex = nextIndex;
                DriveList.ScrollIntoView(DriveList.SelectedItem);
                return;
            }
        }
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
