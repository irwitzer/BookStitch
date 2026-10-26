using BookStitch.Models;
using BookStitch.Services;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace BookStitch.Controls;

public partial class SettingsDriveControl : UserControl
{
    private readonly Func<CancellationToken, Task<IReadOnlyList<DiscDriveInfo>>> _getDrives;
    private readonly DiscDriveConfigurationService _configurationService = new();
    private GlobalSettingsManager? _settingsManager;
    private string? _draggedDriveId;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private bool _isLoading = true;
    private int _refreshVersion;
    private CancellationTokenSource _refreshLifetime = new();

    public SettingsDriveControl() : this(new DiscDriveService().GetCdDrivesAsync) { }

    internal SettingsDriveControl(Func<CancellationToken, Task<IReadOnlyList<DiscDriveInfo>>> getDrives)
    {
        _getDrives = getDrives ?? throw new ArgumentNullException(nameof(getDrives));
        InitializeComponent();
        Unloaded += (_, _) =>
        {
            _refreshLifetime.Cancel();
            if (_settingsManager is not null)
                _settingsManager.DriveCircleChanged -= GlobalSettingsManager_DriveCircleChanged;
        };
        Loaded += (_, _) =>
        {
            if (_settingsManager is not null)
            {
                _settingsManager.DriveCircleChanged -= GlobalSettingsManager_DriveCircleChanged;
                _settingsManager.DriveCircleChanged += GlobalSettingsManager_DriveCircleChanged;
                GlobalSettingsManager_DriveCircleChanged(this, EventArgs.Empty);
            }
            if (!_refreshLifetime.IsCancellationRequested)
                return;
            _refreshLifetime.Dispose();
            _refreshLifetime = new CancellationTokenSource();
            if (!_isLoading)
            {
                ShowConfiguredDrives();
                _ = RefreshDriveCircleListAsync(saveSettings: false);
            }
        };
    }

    public void Initialize(GlobalSettingsManager settingsManager)
    {
        _isLoading = true;
        if (_settingsManager is not null)
            _settingsManager.DriveCircleChanged -= GlobalSettingsManager_DriveCircleChanged;
        _settingsManager = settingsManager ?? throw new ArgumentNullException(nameof(settingsManager));
        _settingsManager.DriveCircleChanged += GlobalSettingsManager_DriveCircleChanged;
        ExperimentalDriveRoundEnabledCheckBox.IsChecked = Settings.ExperimentalDriveRoundEnabled;
        _isLoading = false;
        ShowConfiguredDrives();
        _ = RefreshDriveCircleListAsync(saveSettings: false);
    }

    private void GlobalSettingsManager_DriveCircleChanged(object? sender, EventArgs e)
    {
        var wasLoading = _isLoading;
        _isLoading = true;
        try
        {
            ExperimentalDriveRoundEnabledCheckBox.IsChecked = Settings.ExperimentalDriveRoundEnabled;
            DiscDriveSlotPanel.Opacity = Settings.ExperimentalDriveRoundEnabled ? 1d : 0.62d;
        }
        finally { _isLoading = wasLoading; }
    }

    private AppSettings Settings => _settingsManager?.Current
        ?? throw new InvalidOperationException("The drive settings control is not initialized.");

    private async void ExperimentalDriveRoundEnabledCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_isLoading)
            return;
        _settingsManager!.DriveCircleEnabled = ExperimentalDriveRoundEnabledCheckBox.IsChecked == true;
        await RefreshDriveCircleListAsync(saveSettings: true);
    }

    private async void DriveRoundRefresh_Click(object sender, RoutedEventArgs e) => await RefreshDriveCircleListAsync(saveSettings: true, reset: true);

    private async void DriveRoundUpdate_Click(object sender, RoutedEventArgs e) => await RefreshDriveCircleListAsync(saveSettings: true);

    internal async Task RefreshDriveCircleListAsync(bool saveSettings, bool reset = false)
    {
        var token = _refreshLifetime.Token;
        var version = ++_refreshVersion;
        var entered = false;
        try
        {
            await _refreshGate.WaitAsync(token);
            entered = true;
            token.ThrowIfCancellationRequested();
            if (version != _refreshVersion) return;
            DriveRoundRefreshButton.IsEnabled = false;
            DriveRoundUpdateButton.IsEnabled = false;
            var drives = await _getDrives(token);
            // A probe may finish despite cancellation. Never persist its late result after unloading.
            token.ThrowIfCancellationRequested();
            if (version != _refreshVersion) return;
            var items = reset
                ? _configurationService.Reset(Settings, drives)
                : _configurationService.Synchronize(Settings, drives);
            if (saveSettings || items.Any(item => item.IsConnected))
                _settingsManager!.SaveImmediate();

            DiscDriveSlotPanel.Children.Clear();
            for (var index = 0; index < items.Count; index++)
                DiscDriveSlotPanel.Children.Add(CreateDriveCircleSlot(items[index], index));

            UpdateDriveCircleHint(items);
            DiscDriveSlotPanel.Opacity = Settings.ExperimentalDriveRoundEnabled ? 1d : 0.62d;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Closing/unloading the settings view cancels active and queued refreshes.
        }
        catch (Exception exception)
        {
            System.Diagnostics.Trace.TraceWarning("Drive settings refresh failed: {0}", exception);
            if (!token.IsCancellationRequested && version == _refreshVersion)
                DiscDriveRoundHintTextBlock.Text = LocalizationText.Get("Settings.Drive.RefreshFailed");
        }
        finally
        {
            if (entered)
            {
                if (version == _refreshVersion || (token.IsCancellationRequested && token == _refreshLifetime.Token))
                {
                    DriveRoundRefreshButton.IsEnabled = true;
                    DriveRoundUpdateButton.IsEnabled = true;
                }
                _refreshGate.Release();
            }
        }
    }

    private void ShowConfiguredDrives()
    {
        // A display-only projection: no normalization, discovery, or settings writes.
        var items = Settings.DiscDriveOrder?.OrderBy(item => item.Order)
            .Select(configuration => new DiscDriveConfigurationItem(configuration, false, null)).ToList() ?? [];
        DiscDriveSlotPanel.Children.Clear();
        for (var index = 0; index < items.Count; index++)
            DiscDriveSlotPanel.Children.Add(CreateDriveCircleSlot(items[index], index, connectionVerified: false));
        DiscDriveRoundHintTextBlock.Text = LocalizationText.Get(items.Count == 0
            ? "UiKeineCDDVDLaufwerkeErkannt" : "Disc.Drive.Checking");
        DiscDriveSlotPanel.Opacity = Settings.ExperimentalDriveRoundEnabled ? 1d : 0.62d;
    }

    private DriveCircleSlot CreateDriveCircleSlot(DiscDriveConfigurationItem item, int index, bool connectionVerified = true)
    {
        var configuration = item.Configuration;
        var info = DiscDriveInfoPresentationService.Create(configuration, item.DriveInfo, item.IsConnected, connectionVerified);
        var slot = new DriveCircleSlot
        {
            StableId = configuration.StableId,
            DriveLabel = BuildDiscDriveSlotLabel(configuration.DriveRoot),
            UserName = configuration.UserName,
            IsConnected = item.IsConnected,
            IsDriveEnabled = configuration.IsEnabled,
            InfoTitle = info.Title,
            InfoBody = info.Body,
            Margin = new Thickness(0, index == 0 ? 0 : 6, 0, 0),
            AllowDrop = true
        };

        AutomationProperties.SetName(slot, BuildAccessibleSlotName(configuration, item.IsConnected, connectionVerified));
        slot.RenameCommitted += DriveCircleSlot_RenameCommitted;
        slot.ToggleEnabledRequested += DriveCircleSlot_ToggleEnabledRequested;
        slot.DragRequested += DriveCircleSlot_DragRequested;
        slot.PreviewDragOver += DriveRoundSlotPanel_PreviewDragOver;
        slot.Drop += DiscDriveSlotPanel_Drop;
        return slot;
    }

    private void DriveCircleSlot_RenameCommitted(object? sender, DriveCircleRenameEventArgs e)
    {
        _configurationService.RenameDrive(Settings, e.StableId, e.UserName);
        _settingsManager!.SaveImmediate();
        _ = RefreshDriveCircleListAsync(saveSettings: false);
    }

    private void DriveCircleSlot_ToggleEnabledRequested(object? sender, EventArgs e)
    {
        if (sender is not DriveCircleSlot slot || FindConfiguration(slot.StableId) is not { } configuration)
            return;
        _configurationService.SetEnabled(Settings, slot.StableId, !configuration.IsEnabled);
        _settingsManager!.SaveImmediate();
        _ = RefreshDriveCircleListAsync(saveSettings: false);
    }

    private void DriveCircleSlot_DragRequested(object? sender, DriveCircleDragEventArgs e)
    {
        if (!Settings.ExperimentalDriveRoundEnabled || sender is not DriveCircleSlot slot || string.IsNullOrWhiteSpace(e.StableId))
            return;

        _draggedDriveId = e.StableId;
        slot.IsDragged = true;
        try
        {
            DragDrop.DoDragDrop(slot, new DataObject(typeof(string), e.StableId), DragDropEffects.Move);
        }
        finally
        {
            slot.IsDragged = false;
            _draggedDriveId = null;
        }
    }

    private void DriveRoundSlotPanel_PreviewDragOver(object sender, DragEventArgs e)
    {
        e.Effects = Settings.ExperimentalDriveRoundEnabled && TryGetDraggedId(e.Data, out _)
            ? DragDropEffects.Move
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void DiscDriveSlotPanel_DragLeave(object sender, DragEventArgs e) => e.Handled = true;

    private void DiscDriveSlotPanel_Drop(object sender, DragEventArgs e)
    {
        if (!Settings.ExperimentalDriveRoundEnabled || !TryGetDraggedId(e.Data, out var draggedId))
            return;

        var ordered = Settings.DiscDriveOrder?.OrderBy(item => item.Order).ToList() ?? [];
        if (ordered.Count == 0)
            return;

        var targetIndex = ordered.Count;
        if (FindAncestor<DriveCircleSlot>(e.OriginalSource as DependencyObject) is { StableId: var targetId } targetSlot)
        {
            targetIndex = ordered.FindIndex(item => string.Equals(item.StableId, targetId, StringComparison.OrdinalIgnoreCase));
            if (targetIndex < 0)
                return;
            if (e.GetPosition(targetSlot).Y > targetSlot.ActualHeight / 2d)
                targetIndex++;
        }

        var sourceIndex = ordered.FindIndex(item => string.Equals(item.StableId, draggedId, StringComparison.OrdinalIgnoreCase));
        if (sourceIndex < 0)
            return;
        if (targetIndex > sourceIndex)
            targetIndex--;

        _configurationService.MoveDriveToIndex(Settings, draggedId, targetIndex);
        _settingsManager!.SaveImmediate();
        _ = RefreshDriveCircleListAsync(saveSettings: false);
        e.Effects = DragDropEffects.Move;
        e.Handled = true;
    }

    private static bool TryGetDraggedId(IDataObject data, out string stableId)
    {
        stableId = data.GetDataPresent(typeof(string)) ? data.GetData(typeof(string)) as string ?? string.Empty : string.Empty;
        return !string.IsNullOrWhiteSpace(stableId);
    }

    private ConfiguredDiscDrive? FindConfiguration(string stableId) => Settings.DiscDriveOrder?.FirstOrDefault(item =>
        string.Equals(item.StableId, stableId, StringComparison.OrdinalIgnoreCase));

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T match)
                return match;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    private static string BuildAccessibleSlotName(ConfiguredDiscDrive configuration, bool isConnected, bool connectionVerified)
    {
        var parts = new[]
        {
            BuildDiscDriveSlotLabel(configuration.DriveRoot),
            configuration.UserName,
            LocalizationText.Get(!connectionVerified ? "Disc.Drive.Checking" : isConnected ? "Settings.Drive.Connected" : "Settings.Drive.Disconnected"),
            LocalizationText.Get(configuration.IsEnabled ? "Settings.Drive.Active" : "Settings.Drive.Inactive")
        };
        return string.Join(", ", parts.Where(part => !string.IsNullOrWhiteSpace(part)));
    }

    private static string BuildDiscDriveSlotLabel(string root)
    {
        if (string.IsNullOrWhiteSpace(root))
            return "?";
        var trimmed = root.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.IsNullOrWhiteSpace(trimmed) ? "?" : trimmed;
    }

    private void UpdateDriveCircleHint(IReadOnlyList<DiscDriveConfigurationItem> items)
    {
        var activeConnected = items.Count(item => item.Configuration.IsEnabled && item.IsConnected);
        var inactiveConnected = items.Count(item => !item.Configuration.IsEnabled && item.IsConnected);
        var disconnected = items.Count(item => !item.IsConnected);
        DiscDriveRoundHintTextBlock.Text = SettingsDriveTextService.BuildCircleHint(
            activeConnected, inactiveConnected, disconnected, DiscDriveConfigurationService.MaximumActiveDrives);
    }
}
