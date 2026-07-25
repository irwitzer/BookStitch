using BookStitch.Services;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace BookStitch.Dialog;

public partial class DiscSourceSelectionDialog : Window
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(1);
    private const double DriveItemHeight = 51;
    private const double MinimumDriveListHeight = 53;
    private const double MaximumDriveListHeight = 257;
    private const double BaseWindowHeightWithoutDriveList = 228;

    private readonly DiscDriveService _discDriveService = new();
    private readonly DispatcherTimer _refreshTimer;
    private string _driveSnapshot = string.Empty;
    private bool _refreshInProgress;

    public string SelectedPath { get; private set; } = string.Empty;

    public DiscSourceSelectionDialog(IReadOnlyList<DiscDriveInfo> drives, string folderInitialDirectory, string? preferredDriveRoot)
    {
        InitializeComponent();
        ApplyDriveList(drives, preferredDriveRoot);

        _refreshTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = RefreshInterval
        };
        _refreshTimer.Tick += RefreshTimer_Tick;
        Loaded += DiscSourceSelectionDialog_Loaded;
        Closed += (_, _) => _refreshTimer.Stop();
    }

    private void UseDriveButton_Click(object sender, RoutedEventArgs e) => AcceptSelectedDrive();

    private void DriveList_MouseDoubleClick(object sender, MouseButtonEventArgs e) => AcceptSelectedDrive();

    private void AcceptSelectedDrive()
    {
        if (DriveList.SelectedItem is not DiscDriveInfo drive || drive.IsChecking)
            return;

        SelectedPath = drive.RootPath;
        DialogResult = true;
    }

    private async void DiscSourceSelectionDialog_Loaded(object sender, RoutedEventArgs e)
    {
        await RefreshDriveListAsync();
        if (!IsVisible)
            return;

        UseDriveButton.Focus();
        _refreshTimer.Start();
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

    private async void RefreshTimer_Tick(object? sender, EventArgs e)
    {
        await RefreshDriveListAsync();
    }

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
            {
                var currentSelectedRoot = (DriveList.SelectedItem as DiscDriveInfo)?.RootPath;
                ApplyDriveList(drives, currentSelectedRoot ?? selectedRoot);
            }
        }
        catch
        {
            // The dialog keeps its last valid snapshot when Windows temporarily
            // cannot query a drive during insertion, ejection, or spin-up.
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
}
