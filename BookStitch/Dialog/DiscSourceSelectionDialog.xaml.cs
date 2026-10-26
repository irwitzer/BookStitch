using BookStitch.Services;
using System.Windows;
using System.Windows.Input;

namespace BookStitch.Dialog;

public partial class DiscSourceSelectionDialog : Window
{
    private const double DriveItemHeight = 51;
    private const double MinimumDriveListHeight = 59;
    private const double MaximumDriveListHeight = 263;
    private const double BaseWindowHeightWithoutDriveList = 228;

    private readonly DiscDriveService _discDriveService;
    private readonly bool _respectDriveCircleEnabled;
    private readonly DiscDriveRefreshService _refreshService;
    private readonly DiscDriveSelectionPresentationService _presentationService = new();
    private readonly IReadOnlyList<BookStitch.Models.ConfiguredDiscDrive> _configuredDrives;
    private readonly string? _initialPreferredDriveRoot;
    private readonly OpticalMediaChangeMonitor _mediaChangeMonitor;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private IReadOnlyList<DiscDriveInfo> _driveStates = [];

    public string SelectedPath { get; private set; } = string.Empty;

    public DiscSourceSelectionDialog(
        IReadOnlyList<DiscDriveInfo> drives,
        string folderInitialDirectory,
        string? preferredDriveRoot,
        IReadOnlyList<BookStitch.Models.ConfiguredDiscDrive>? configuredDrives = null,
        DiscDriveService? discDriveService = null,
        bool respectDriveCircleEnabled = true)
    {
        InitializeComponent();
        _configuredDrives = configuredDrives ?? [];
        _respectDriveCircleEnabled = respectDriveCircleEnabled;
        _initialPreferredDriveRoot = preferredDriveRoot;
        _discDriveService = discDriveService ?? new DiscDriveService();
        _refreshService = new DiscDriveRefreshService(_discDriveService);
        _driveStates = drives ?? [];
        ApplyDriveList(_driveStates, preferredDriveRoot);

        _mediaChangeMonitor = new OpticalMediaChangeMonitor(this);
        _mediaChangeMonitor.MediaChanged += MediaChangeMonitor_MediaChanged;
        Loaded += DiscSourceSelectionDialog_Loaded;
        Closed += DiscSourceSelectionDialog_Closed;
    }

    private void UseDriveButton_Click(object sender, RoutedEventArgs e) => AcceptSelectedDrive();

    private void DriveList_MouseDoubleClick(object sender, MouseButtonEventArgs e) => AcceptSelectedDrive();

    private void AcceptSelectedDrive()
    {
        if (DriveList.SelectedItem is not DiscDriveSelectionPresentation item ||
            item.DriveInfo.IsChecking ||
            !item.IsEnabled)
        {
            return;
        }

        SelectedPath = item.DriveRoot;
        DialogResult = true;
    }

    private async void DiscSourceSelectionDialog_Loaded(object sender, RoutedEventArgs e)
    {
        await RefreshDriveListAsync();
        if (!IsVisible)
            return;

        UseDriveButton.Focus();
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
        var presentation = _presentationService.Create(drives, _configuredDrives, _respectDriveCircleEnabled);
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


    private void DiscSourceSelectionDialog_Closed(object? sender, EventArgs e)
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
}
