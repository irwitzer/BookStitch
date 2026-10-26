using BookStitch.Models;
using BookStitch.Services;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;

namespace BookStitch.Controls;

public partial class SettingsGeneralControl : UserControl
{
    private const uint FlashTray = 0x00000002;

    private GlobalSettingsManager? _settingsManager;
    private NotificationService? _notificationService;
    private Action<bool>? _setMergeAutomaticallyAfterConversion;
    private Func<string>? _getOutputFolder;
    private Action<Window>? _chooseOutputFolder;
    private Action<Window>? _openOutputFolder;
    private INotifyPropertyChanged? _outputFolderChangeSource;
    private Window? _ownerWindow;
    private CancellationTokenSource? _focusTestCancellation;
    private bool _isLoading = true;
    private bool _isRunningFocusTest;
    private bool _restoreShowInTaskbarAfterFocusTest;

    public SettingsGeneralControl()
    {
        InitializeComponent();
        Loaded += SettingsGeneralControl_Loaded;
        Unloaded += SettingsGeneralControl_Unloaded;
    }

    public void Initialize(
        GlobalSettingsManager settingsManager,
        NotificationService notificationService,
        Action<bool> setMergeAutomaticallyAfterConversion,
        Func<string> getOutputFolder,
        Action<Window> chooseOutputFolder,
        Action<Window> openOutputFolder,
        INotifyPropertyChanged outputFolderChangeSource)
    {
        _settingsManager = settingsManager ?? throw new ArgumentNullException(nameof(settingsManager));
        _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
        _setMergeAutomaticallyAfterConversion = setMergeAutomaticallyAfterConversion ?? throw new ArgumentNullException(nameof(setMergeAutomaticallyAfterConversion));
        _getOutputFolder = getOutputFolder ?? throw new ArgumentNullException(nameof(getOutputFolder));
        _chooseOutputFolder = chooseOutputFolder ?? throw new ArgumentNullException(nameof(chooseOutputFolder));
        _openOutputFolder = openOutputFolder ?? throw new ArgumentNullException(nameof(openOutputFolder));
        _outputFolderChangeSource = outputFolderChangeSource ?? throw new ArgumentNullException(nameof(outputFolderChangeSource));
        _outputFolderChangeSource.PropertyChanged += OutputFolderChangeSource_PropertyChanged;

        UpdateOutputFolderDisplay();
        MergeAutomaticallyCheckBox.IsChecked = Settings.MergeAutomaticallyAfterConversion;
        KeepComputerAwakeCheckBox.IsChecked = Settings.KeepComputerAwakeDuringLongOperations;
        SelectFocusProfile();
        SelectEventProfile();
        SelectSoundLibrary();
        SoundVolumeSlider.Value = Settings.SoundVolumePercent;
        UpdateSoundVolumeText();
        _isLoading = false;
    }

    private AppSettings Settings =>
        _settingsManager?.Current ?? throw new InvalidOperationException("The general settings control is not initialized.");

    private void ResetWarnings_Click(object sender, RoutedEventArgs e)
    {
        WarningSuppressionService.ResetAll(Settings);
        _settingsManager!.SaveImmediate();
    }

    private void OpenOutputFolder_Click(object sender, RoutedEventArgs e)
    {
        var owner = Window.GetWindow(this)
            ?? throw new InvalidOperationException("The general settings control is not hosted in a window.");
        _openOutputFolder!(owner);
    }

    private void ChooseOutputFolder_Click(object sender, RoutedEventArgs e)
    {
        var owner = Window.GetWindow(this)
            ?? throw new InvalidOperationException("The general settings control is not hosted in a window.");
        _chooseOutputFolder!(owner);
        UpdateOutputFolderDisplay();
    }

    private void OutputFolderChangeSource_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.PropertyName) || string.Equals(e.PropertyName, "OutputFolder", StringComparison.Ordinal))
            UpdateOutputFolderDisplay();
    }

    private void UpdateOutputFolderDisplay()
    {
        if (OutputFolderPathTextBlock is null)
            return;

        var value = _getOutputFolder?.Invoke() ?? string.Empty;
        OutputFolderPathTextBlock.Text = value;
    }

    private void MergeAutomaticallyCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_isLoading)
            return;

        _setMergeAutomaticallyAfterConversion!(MergeAutomaticallyCheckBox.IsChecked == true);
        _settingsManager!.SaveImmediate();
    }

    private void KeepComputerAwakeCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_isLoading)
            return;

        Settings.KeepComputerAwakeDuringLongOperations = KeepComputerAwakeCheckBox.IsChecked == true;
        _settingsManager!.SaveImmediate();
    }

    private void FocusProfileComboBox_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading)
            return;

        Settings.FocusProfile = GetSelectedFocusProfile().ToString();
        _settingsManager!.SaveImmediate();
    }

    private void EventProfileComboBox_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading)
            return;

        Settings.EventProfile = GetSelectedEventProfile().ToString();
        _settingsManager!.SaveImmediate();
    }

    private void SoundLibraryComboBox_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading)
            return;

        Settings.SoundLibrary = GetSelectedSoundLibrary().ToString();
        _settingsManager!.SaveImmediate();
        _notificationService!.PlaySoundPreview();
    }

    private void SoundVolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isLoading)
            return;

        Settings.SoundVolumePercent = SoundSettingsService.NormalizeVolumePercent((int)Math.Round(e.NewValue));
        _settingsManager!.SaveDebounced();
        UpdateSoundVolumeText();
    }

    private void PlayTestSound_Click(object sender, RoutedEventArgs e) => _notificationService!.PlaySoundPreview();

    private void RunFocusTest_Click(object sender, RoutedEventArgs e)
    {
        if (_isRunningFocusTest || _ownerWindow is null)
            return;

        var profile = FocusSettingsService.NormalizeProfile(Settings.FocusProfile);
        if (profile != FocusProfile.Off)
            _ = RunFocusTestAsync(_ownerWindow, profile, _focusTestCancellation?.Token ?? CancellationToken.None);
    }

    private async Task RunFocusTestAsync(Window owner, FocusProfile profile, CancellationToken cancellationToken)
    {
        _isRunningFocusTest = true;
        try
        {
            EnsureTemporaryTaskbarVisibilityForFocusTest(owner);
            owner.WindowState = WindowState.Minimized;
            await Task.Delay(900, cancellationToken);
            FlashTaskbar(owner, count: 5);

            if (profile == FocusProfile.Foreground)
            {
                await Task.Delay(1200, cancellationToken);
                BringWindowToForeground(owner, useTemporaryTopmost: true);
                RestoreTemporaryTaskbarVisibilityAfterFocusTest(owner);
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _isRunningFocusTest = false;
        }
    }

    private void SettingsGeneralControl_Loaded(object sender, RoutedEventArgs e)
    {
        var owner = Window.GetWindow(this);
        if (ReferenceEquals(_ownerWindow, owner))
            return;

        DetachOwnerWindow();
        _ownerWindow = owner;
        if (_ownerWindow is not null)
            _ownerWindow.StateChanged += OwnerWindow_StateChanged;
        _focusTestCancellation = new CancellationTokenSource();
    }

    private void SettingsGeneralControl_Unloaded(object sender, RoutedEventArgs e)
    {
        DetachOwnerWindow();
        if (_outputFolderChangeSource is not null)
            _outputFolderChangeSource.PropertyChanged -= OutputFolderChangeSource_PropertyChanged;
    }

    private void DetachOwnerWindow()
    {
        _focusTestCancellation?.Cancel();
        _focusTestCancellation?.Dispose();
        _focusTestCancellation = null;

        if (_ownerWindow is not null)
        {
            _ownerWindow.StateChanged -= OwnerWindow_StateChanged;
            if (_restoreShowInTaskbarAfterFocusTest)
                _ownerWindow.ShowInTaskbar = false;
        }

        _restoreShowInTaskbarAfterFocusTest = false;
        _ownerWindow = null;
    }

    private void OwnerWindow_StateChanged(object? sender, EventArgs e)
    {
        if (_ownerWindow is { WindowState: not WindowState.Minimized } owner)
            RestoreTemporaryTaskbarVisibilityAfterFocusTest(owner);
    }

    private void EnsureTemporaryTaskbarVisibilityForFocusTest(Window owner)
    {
        if (owner.ShowInTaskbar)
            return;

        owner.ShowInTaskbar = true;
        _restoreShowInTaskbarAfterFocusTest = true;
    }

    private void RestoreTemporaryTaskbarVisibilityAfterFocusTest(Window owner)
    {
        if (!_restoreShowInTaskbarAfterFocusTest || owner.WindowState == WindowState.Minimized)
            return;

        owner.ShowInTaskbar = false;
        _restoreShowInTaskbarAfterFocusTest = false;
    }

    private void SelectFocusProfile()
    {
        var selected = FocusSettingsService.NormalizeProfile(Settings.FocusProfile);
        FocusProfileComboBox.SelectedItem = FocusProfileComboBox.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(item => Enum.TryParse<FocusProfile>(item.Tag?.ToString(), out var value) && value == selected);
        FocusProfileComboBox.SelectedIndex = FocusProfileComboBox.SelectedItem is null ? 1 : FocusProfileComboBox.SelectedIndex;
    }

    private void SelectEventProfile()
    {
        var selected = EventProfileSettingsService.NormalizeProfile(Settings.EventProfile);
        EventProfileComboBox.SelectedItem = EventProfileComboBox.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(item => Enum.TryParse<EventProfile>(item.Tag?.ToString(), out var value) && value == selected);
        EventProfileComboBox.SelectedIndex = EventProfileComboBox.SelectedItem is null ? 2 : EventProfileComboBox.SelectedIndex;
    }

    private void SelectSoundLibrary()
    {
        var selected = SoundSettingsService.NormalizeLibrary(Settings.SoundLibrary);
        SoundLibraryComboBox.SelectedItem = SoundLibraryComboBox.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(item => Enum.TryParse<SoundLibrary>(item.Tag?.ToString(), out var value) && value == selected);
        SoundLibraryComboBox.SelectedIndex = SoundLibraryComboBox.SelectedItem is null ? 0 : SoundLibraryComboBox.SelectedIndex;
    }

    private FocusProfile GetSelectedFocusProfile() =>
        FocusProfileComboBox.SelectedItem is ComboBoxItem item && Enum.TryParse<FocusProfile>(item.Tag?.ToString(), out var profile)
            ? profile
            : FocusSettingsService.DefaultProfile;

    private EventProfile GetSelectedEventProfile() =>
        EventProfileComboBox.SelectedItem is ComboBoxItem item && Enum.TryParse<EventProfile>(item.Tag?.ToString(), out var profile)
            ? profile
            : EventProfileSettingsService.DefaultProfile;

    private SoundLibrary GetSelectedSoundLibrary() =>
        SoundLibraryComboBox.SelectedItem is ComboBoxItem item && Enum.TryParse<SoundLibrary>(item.Tag?.ToString(), out var library)
            ? library
            : SoundSettingsService.DefaultLibrary;

    private void UpdateSoundVolumeText() => SoundVolumeTextBlock.Text = $"{Settings.SoundVolumePercent} %";

    private static void BringWindowToForeground(Window window, bool useTemporaryTopmost)
    {
        if (!window.IsVisible)
            window.Show();
        if (window.WindowState == WindowState.Minimized)
            window.WindowState = WindowState.Normal;

        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
            return;

        if (useTemporaryTopmost)
        {
            var wasTopmost = window.Topmost;
            window.Topmost = true;
            window.Activate();
            SetForegroundWindow(handle);
            window.Topmost = wasTopmost;
            return;
        }

        window.Activate();
        SetForegroundWindow(handle);
    }

    private static void FlashTaskbar(Window window, int count)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (count <= 0 || handle == IntPtr.Zero)
            return;

        var info = new FlashWindowInfo
        {
            Size = (uint)Marshal.SizeOf<FlashWindowInfo>(),
            WindowHandle = handle,
            Flags = FlashTray,
            Count = (uint)count
        };
        FlashWindowEx(ref info);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FlashWindowInfo
    {
        public uint Size;
        public IntPtr WindowHandle;
        public uint Flags;
        public uint Count;
        public uint TimeoutMilliseconds;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FlashWindowEx(ref FlashWindowInfo info);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr windowHandle);
}
