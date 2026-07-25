using BookStitch.Dialog;
using BookStitch.Models;
using BookStitch.Services;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;

namespace BookStitch;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;
    private readonly SettingsService _settingsService;
    private readonly NotificationService _notificationService;
    private readonly Func<int, ProjectCleanupResult> _deleteOldProjects;
    private readonly Action<bool> _setMetadataPanelExpanded;
    private readonly Action<bool> _setPipelineStateDebugVisible;
    private readonly Func<Task> _previewDiscSourceAnalysis;
    private readonly Func<Task> _startDeveloperAudioDiscTest;
    private readonly DeveloperDiscTestService _developerDiscTestService = new();
    private readonly DiscDriveService _developerDiscDriveService = new();
    private readonly DiscDriveConfigurationService _discDriveConfigurationService = new();
    private readonly Mp3DiscImportService _developerMp3DiscImportService = new();
    private readonly IDiscWaitDialogService _developerDiscWaitDialogService;
    private CancellationTokenSource? _developerDiscTestCancellation;
    private CancellationTokenSource? _delayedFocusTestCancellation;
    private bool _metadataPanelExpandedPreview;
    private bool _isLoading;
    private bool _restoreShowInTaskbarAfterFocusTest;
    private readonly bool _openDeveloperTabOnLoad;
    private bool _isRunningFocusTest;
    private string? _selectedDriveRoundRoot;

    public SettingsWindow(
        AppSettings settings,
        SettingsService settingsService,
        NotificationService notificationService,
        Func<int, ProjectCleanupResult> deleteOldProjects,
        bool metadataPanelExpanded,
        Action<bool> setMetadataPanelExpanded,
        Action<bool> setPipelineStateDebugVisible,
        Func<Task> previewDiscSourceAnalysis,
        Func<Task> startDeveloperAudioDiscTest,
        bool openDeveloperTabOnLoad = false)
    {
        _settings = settings;
        _settingsService = settingsService;
        _notificationService = notificationService;
        _deleteOldProjects = deleteOldProjects;
        _metadataPanelExpandedPreview = metadataPanelExpanded;
        _setMetadataPanelExpanded = setMetadataPanelExpanded;
        _setPipelineStateDebugVisible = setPipelineStateDebugVisible;
        _previewDiscSourceAnalysis = previewDiscSourceAnalysis;
        _startDeveloperAudioDiscTest = startDeveloperAudioDiscTest;
        _openDeveloperTabOnLoad = openDeveloperTabOnLoad;
        _developerDiscWaitDialogService = new SwitchableDiscWaitDialogService(() => _settings.UseBoxedDiscWaitDialog);
        _isLoading = true;

        NormalizeSettings();
        InitializeComponent();
        LoadControlsFromSettings();
        if (_openDeveloperTabOnLoad)
            SelectDeveloperTab();
        StateChanged += SettingsWindow_StateChanged;
        Closed += (_, _) => CancelCurrentDeveloperDiscTest();

        _isLoading = false;
    }

    private void LoadControlsFromSettings()
    {
        if (FindName("MergeAutomaticallyCheckBox") is CheckBox mergeAutomaticallyCheckBox)
            mergeAutomaticallyCheckBox.IsChecked = _settings.MergeAutomaticallyAfterConversion;

        if (FindName("OverwriteWithoutAskingCheckBox") is CheckBox overwriteWithoutAskingCheckBox)
            overwriteWithoutAskingCheckBox.IsChecked = _settings.OverwriteFinalOutputWithoutAsking;

        if (FindName("KeepComputerAwakeCheckBox") is CheckBox keepComputerAwakeCheckBox)
            keepComputerAwakeCheckBox.IsChecked = _settings.KeepComputerAwakeDuringLongOperations;

        if (FindName("ExperimentalDriveRoundEnabledCheckBox") is CheckBox experimentalDriveRoundEnabledCheckBox)
            experimentalDriveRoundEnabledCheckBox.IsChecked = _settings.ExperimentalDriveRoundEnabled;

        RefreshDiscDriveRoundList(saveSettings: false);

        if (FindName("OutputExtensionComboBox") is ComboBox outputExtensionComboBox)
            SelectComboBoxItemByContent(outputExtensionComboBox, _settings.DefaultOutputExtension);

        if (FindName("FileNameTemplateComboBox") is ComboBox fileNameTemplateComboBox)
            fileNameTemplateComboBox.Text = _settings.DefaultFileNameTemplate;

        if (FindName("KeepAlbumLinkedToTitleCheckBox") is CheckBox keepAlbumLinkedToTitleCheckBox)
            keepAlbumLinkedToTitleCheckBox.IsChecked = _settings.KeepAlbumLinkedToTitle;

        if (FindName("UseLeadingZerosInChapterSuggestionsCheckBox") is CheckBox useLeadingZerosCheckBox)
            useLeadingZerosCheckBox.IsChecked = _settings.UseLeadingZerosInChapterSuggestions;

        if (FindName("MetadataAnimationMillisecondsTextBox") is TextBox animationTextBox)
            animationTextBox.Text = _settings.MetadataPanelAnimationMilliseconds.ToString(CultureInfo.InvariantCulture);

        if (FindName("ForceShowFfmpegSetupButtonCheckBox") is CheckBox forceShowFfmpegCheckBox)
            forceShowFfmpegCheckBox.IsChecked = _settings.ForceShowFfmpegSetupButton;

        if (FindName("ShowDeveloperTabCheckBox") is CheckBox showDeveloperTabCheckBox)
            showDeveloperTabCheckBox.IsChecked = _settings.ShowDeveloperTab;

        if (FindName("ShowPipelineStateDebugCheckBox") is CheckBox showPipelineStateDebugCheckBox)
            showPipelineStateDebugCheckBox.IsChecked = _settings.ShowPipelineStateDebug;

        SelectDiscWaitDialogVariantComboBoxItem();

        ApplyDeveloperTabVisibility();

        UpdateMetadataPanelPreviewButtonText();

        SelectAudioDiscWorkingFormatComboBoxItem();
        SelectFocusProfileComboBoxItem();
        SelectFocusProfileComboBoxItem("DevFocusProfileComboBox");
        SelectSoundProfileComboBoxItem();
        SelectSoundProfileComboBoxItem("DevSoundProfileComboBox");
        SelectSoundLibraryComboBoxItem();
        SelectSoundLibraryComboBoxItem("DevSoundLibraryComboBox");

        if (FindName("SoundVolumeSlider") is Slider soundVolumeSlider)
            soundVolumeSlider.Value = _settings.SoundVolumePercent;
        if (FindName("DevSoundVolumeSlider") is Slider devSoundVolumeSlider)
            devSoundVolumeSlider.Value = _settings.SoundVolumePercent;

        UpdateSoundVolumeText();

        if (FindName("ProjectRetentionDaysComboBox") is ComboBox projectRetentionDaysComboBox)
            projectRetentionDaysComboBox.Text = _settings.ProjectRetentionDays.ToString(CultureInfo.InvariantCulture);

        UpdateProjectRetentionHint();
        SelectOutputFolderLayoutComboBoxItem();
        UpdateOutputFolderPreview();
    }

    private void NormalizeSettings()
    {
        _settings.ProjectRetentionDays = ProjectIndexService.NormalizeRetentionDays(_settings.ProjectRetentionDays);
        _settings.DeleteProjectsOlderThanDays = _settings.ProjectRetentionDays;

        _settings.ShowCompletedProjects = true;
        _settings.ShowIncompleteProjects = false;
        _settings.DiscDriveOrder ??= [];
        _settings.OutputFolderLayout = OutputFolderLayoutService.NormalizeLayout(_settings.OutputFolderLayout);
        _settings.AudioDiscWorkingFormat = AudioDiscSettingsService.NormalizeWorkingFormat(_settings.AudioDiscWorkingFormat).ToString();
        _settings.SoundProfile = SoundSettingsService.NormalizeProfile(_settings.SoundProfile).ToString();
        _settings.FocusProfile = FocusSettingsService.NormalizeProfile(_settings.FocusProfile).ToString();
        _settings.SoundLibrary = SoundSettingsService.NormalizeLibrary(_settings.SoundLibrary).ToString();
        _settings.SoundVolumePercent = SoundSettingsService.NormalizeVolumePercent(_settings.SoundVolumePercent);
        _settings.MetadataPanelAnimationMilliseconds = Math.Clamp(_settings.MetadataPanelAnimationMilliseconds, 0, 2000);
        if (_settings.ShowPipelineStateDebug || _settings.ForceShowFfmpegSetupButton)
            _settings.ShowDeveloperTab = true;
        _settingsService.Save(_settings);
    }

    private void MergeAutomaticallyCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_isLoading)
            return;

        if (sender is not CheckBox mergeAutomaticallyCheckBox)
            return;

        _settings.MergeAutomaticallyAfterConversion = mergeAutomaticallyCheckBox.IsChecked == true;
        _settingsService.Save(_settings);
    }


    private void OverwriteWithoutAskingCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_isLoading)
            return;

        if (sender is not CheckBox overwriteWithoutAskingCheckBox)
            return;

        _settings.OverwriteFinalOutputWithoutAsking = overwriteWithoutAskingCheckBox.IsChecked == true;
        _settingsService.Save(_settings);
    }

    private void KeepComputerAwakeCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_isLoading)
            return;

        if (sender is not CheckBox keepComputerAwakeCheckBox)
            return;

        _settings.KeepComputerAwakeDuringLongOperations = keepComputerAwakeCheckBox.IsChecked == true;
        _settingsService.Save(_settings);
    }

    private void ExperimentalDriveRoundEnabledCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_isLoading || sender is not CheckBox checkBox)
            return;

        _settings.ExperimentalDriveRoundEnabled = checkBox.IsChecked == true;
        _settingsService.Save(_settings);
        RefreshDiscDriveRoundList(saveSettings: true, keepSelectedRoot: GetSelectedDiscDriveRoundRoot());
    }

    private void DriveRoundRefresh_Click(object sender, RoutedEventArgs e)
    {
        RefreshDiscDriveRoundList(saveSettings: true, keepSelectedRoot: GetSelectedDiscDriveRoundRoot());
    }

    private void DriveRoundMoveLeft_Click(object sender, RoutedEventArgs e)
    {
        MoveSelectedDriveRoundSlot(-1);
    }

    private void DriveRoundMoveRight_Click(object sender, RoutedEventArgs e)
    {
        MoveSelectedDriveRoundSlot(1);
    }

    private void MoveSelectedDriveRoundSlot(int direction)
    {
        var root = GetSelectedDiscDriveRoundRoot();
        if (string.IsNullOrWhiteSpace(root))
            return;

        _discDriveConfigurationService.MoveDrive(_settings, root, direction);
        _settingsService.Save(_settings);
        RefreshDiscDriveRoundList(saveSettings: false, keepSelectedRoot: root);
    }

    private void DriveRoundSlot_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string root } || string.IsNullOrWhiteSpace(root))
            return;

        var normalizedRoot = DiscDriveConfigurationService.NormalizeRootPath(root);
        var wasAlreadySelected = string.Equals(
            _selectedDriveRoundRoot,
            normalizedRoot,
            StringComparison.OrdinalIgnoreCase);
        _selectedDriveRoundRoot = normalizedRoot;

        if (!_settings.ExperimentalDriveRoundEnabled || !wasAlreadySelected)
        {
            RefreshDiscDriveRoundList(saveSettings: false, keepSelectedRoot: _selectedDriveRoundRoot);
            return;
        }

        var item = _settings.DiscDriveOrder?.FirstOrDefault(entry =>
            string.Equals(
                DiscDriveConfigurationService.NormalizeRootPath(entry.DriveRoot),
                _selectedDriveRoundRoot,
                StringComparison.OrdinalIgnoreCase));
        if (item is null)
            return;

        _discDriveConfigurationService.SetEnabled(_settings, _selectedDriveRoundRoot, !item.IsEnabled);
        _settingsService.Save(_settings);
        RefreshDiscDriveRoundList(saveSettings: false, keepSelectedRoot: _selectedDriveRoundRoot);
    }

    private void RefreshDiscDriveRoundList(bool saveSettings, string? keepSelectedRoot = null)
    {
        if (FindName("DiscDriveSlotPanel") is not UniformGrid slotPanel)
            return;

        var selectedRoot = DiscDriveConfigurationService.NormalizeRootPath(keepSelectedRoot ?? GetSelectedDiscDriveRoundRoot());
        var drives = _developerDiscDriveService.GetCdDrives();
        var items = _discDriveConfigurationService.Synchronize(_settings, drives);
        if (saveSettings)
            _settingsService.Save(_settings);

        if (!items.Any(item => string.Equals(
                DiscDriveConfigurationService.NormalizeRootPath(item.Configuration.DriveRoot),
                selectedRoot,
                StringComparison.OrdinalIgnoreCase)))
        {
            selectedRoot = string.Empty;
        }

        _selectedDriveRoundRoot = selectedRoot;
        slotPanel.Children.Clear();

        for (var index = 0; index < DiscDriveConfigurationService.MaximumActiveDrives; index++)
        {
            if (index < items.Count)
                slotPanel.Children.Add(CreateDiscDriveSlotButton(items[index], index, selectedRoot));
            else
                slotPanel.Children.Add(CreateEmptyDiscDriveSlotButton(index));
        }

        UpdateDriveRoundHint(items);
        UpdateDriveRoundControlAvailability(items);
    }

    private Button CreateDiscDriveSlotButton(
        DiscDriveConfigurationItem item,
        int index,
        string selectedRoot)
    {
        var root = DiscDriveConfigurationService.NormalizeRootPath(item.Configuration.DriveRoot);
        var driveLetter = BuildDiscDriveSlotLabel(root);
        var isActiveAndConnected = item.Configuration.IsEnabled && item.IsConnected;
        var isSelected = !string.IsNullOrWhiteSpace(selectedRoot) &&
                         string.Equals(root, selectedRoot, StringComparison.OrdinalIgnoreCase);
        var button = new Button
        {
            Content = item.IsConnected ? driveLetter : $"{driveLetter}\nfehlt",
            Tag = root,
            Width = 90,
            Height = 36,
            Margin = new Thickness(index == 0 ? 0 : 8, 0, 0, 0),
            Padding = new Thickness(8, 0, 8, 0),
            FontWeight = FontWeights.SemiBold,
            FontSize = 14,
            Opacity = isActiveAndConnected ? 1d : 0.58d,
            ToolTip = BuildDiscDriveSlotToolTip(item, isSelected),
            IsEnabled = true,
            BorderThickness = new Thickness(1)
        };

        if (item.Configuration.IsEnabled)
        {
            button.SetResourceReference(FrameworkElement.StyleProperty, "PrimaryButtonStyle");
            button.SetResourceReference(Control.BackgroundProperty, isSelected ? "AccentHoverBrush" : "AccentBrush");
            button.SetResourceReference(Control.BorderBrushProperty, isSelected ? "AccentHoverBrush" : "AccentBrush");
        }
        else
        {
            button.SetResourceReference(FrameworkElement.StyleProperty, "SecondaryButtonStyle");
            button.SetResourceReference(Control.BorderBrushProperty, item.IsConnected ? "AccentBrush" : "InputBorderBrush");
        }

        button.Click += DriveRoundSlot_Click;
        return button;
    }

    private Button CreateEmptyDiscDriveSlotButton(int index)
    {
        var button = new Button
        {
            Content = "leer",
            Width = 90,
            Height = 36,
            Margin = new Thickness(index == 0 ? 0 : 8, 0, 0, 0),
            Padding = new Thickness(8, 0, 8, 0),
            Opacity = 0.34d,
            IsEnabled = false,
            ToolTip = "Freier Laufwerksplatz"
        };
        button.SetResourceReference(FrameworkElement.StyleProperty, "SecondaryButtonStyle");
        return button;
    }

    private string? GetSelectedDiscDriveRoundRoot() => _selectedDriveRoundRoot;

    private static string BuildDiscDriveSlotLabel(string root)
    {
        if (string.IsNullOrWhiteSpace(root))
            return "?";

        var trimmed = root.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.IsNullOrWhiteSpace(trimmed) ? "?" : trimmed;
    }

    private static string BuildDiscDriveSlotToolTip(DiscDriveConfigurationItem item, bool isSelected)
    {
        var root = DiscDriveConfigurationService.NormalizeRootPath(item.Configuration.DriveRoot);
        var displayName = string.IsNullOrWhiteSpace(item.Configuration.DisplayName)
            ? root
            : item.Configuration.DisplayName.Trim();
        var connection = item.IsConnected ? "verbunden" : "nicht verbunden";
        var active = item.Configuration.IsEnabled ? "aktiv" : "deaktiviert";
        var clickHint = isSelected
            ? "Erneut klicken: aktiv/inaktiv umschalten"
            : "Klicken: zum Verschieben auswählen";
        return $"{displayName}\n{connection} • {active}\n{clickHint}";
    }

    private void UpdateDriveRoundHint(IReadOnlyList<DiscDriveConfigurationItem> items)
    {
        if (FindName("DiscDriveRoundHintTextBlock") is not TextBlock hint)
            return;

        if (items.Count == 0)
        {
            hint.Text = "Keine CD-/DVD-Laufwerke erkannt.";
            return;
        }

        var activeConnected = items.Count(item => item.Configuration.IsEnabled && item.IsConnected);
        var disconnected = items.Count(item => !item.IsConnected);
        var extraLimit = activeConnected > DiscDriveConfigurationService.MaximumActiveDrives
            ? $" BookStitch verwendet nur die ersten {DiscDriveConfigurationService.MaximumActiveDrives} aktiven Laufwerke."
            : string.Empty;
        var disconnectedText = disconnected > 0
            ? $" {disconnected} gespeicherte(s) Laufwerk(e) ist/sind aktuell nicht verbunden."
            : string.Empty;
        hint.Text = $"{activeConnected} aktive verbundene Laufwerk(e). Ein Klick wählt ein Laufwerk zum Verschieben aus, erneuter Klick schaltet aktiv/inaktiv.{disconnectedText}{extraLimit}".Trim();
    }

    private void UpdateDriveRoundControlAvailability(IReadOnlyList<DiscDriveConfigurationItem>? items = null)
    {
        var enabled = _settings.ExperimentalDriveRoundEnabled;
        var selectedRoot = GetSelectedDiscDriveRoundRoot();
        var hasSelection = !string.IsNullOrWhiteSpace(selectedRoot);
        var orderedItems = items ?? _settings.DiscDriveOrder
            .OrderBy(item => item.Order)
            .Select(item => new DiscDriveConfigurationItem(item, true, null))
            .ToList();
        var selectedIndex = hasSelection
            ? orderedItems.ToList().FindIndex(item => string.Equals(
                DiscDriveConfigurationService.NormalizeRootPath(item.Configuration.DriveRoot),
                selectedRoot,
                StringComparison.OrdinalIgnoreCase))
            : -1;

        if (FindName("DiscDriveSlotPanel") is UniformGrid slotPanel)
            slotPanel.Opacity = enabled ? 1d : 0.62d;
        if (FindName("DriveRoundRefreshButton") is Button refreshButton)
            refreshButton.IsEnabled = true;
        if (FindName("DriveRoundLeftButton") is Button leftButton)
            leftButton.IsEnabled = enabled && selectedIndex > 0;
        if (FindName("DriveRoundRightButton") is Button rightButton)
            rightButton.IsEnabled = enabled && selectedIndex >= 0 && selectedIndex < orderedItems.Count - 1;
    }


    public void SelectDeveloperTab()
    {
        _settings.ShowDeveloperTab = true;
        ApplyDeveloperTabVisibility();

        if (FindName("DeveloperTabItem") is TabItem developerTabItem)
            developerTabItem.IsSelected = true;
    }

    private void ApplyDeveloperTabVisibility()
    {
        if (FindName("DeveloperTabItem") is not TabItem developerTabItem)
            return;

        developerTabItem.Visibility = _settings.ShowDeveloperTab ? Visibility.Visible : Visibility.Collapsed;
        if (!_settings.ShowDeveloperTab && developerTabItem.IsSelected && FindName("SettingsTabControl") is TabControl tabControl)
            tabControl.SelectedIndex = 0;
    }

    private void OutputExtensionComboBox_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading || sender is not ComboBox comboBox)
            return;

        var value = GetComboBoxText(comboBox).Trim();
        _settings.DefaultOutputExtension = value is ".m4a" or ".m4b" ? value : ".m4a";
        _settingsService.Save(_settings);
        UpdateOutputFolderPreview();
    }

    private void FileNameTemplateComboBox_Changed(object sender, SelectionChangedEventArgs e)
    {
        SaveFileNameTemplate(sender as ComboBox);
    }

    private void FileNameTemplateComboBox_LostFocus(object sender, RoutedEventArgs e)
    {
        SaveFileNameTemplate(sender as ComboBox);
    }

    private void SaveFileNameTemplate(ComboBox? comboBox)
    {
        if (_isLoading || comboBox is null)
            return;

        var value = GetComboBoxText(comboBox).Trim();
        if (string.IsNullOrWhiteSpace(value))
            value = "{Autor} - {Titel}";

        _settings.DefaultFileNameTemplate = value;
        _settingsService.Save(_settings);
        UpdateOutputFolderPreview();
    }

    private void KeepAlbumLinkedToTitleCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_isLoading || sender is not CheckBox checkBox)
            return;

        _settings.KeepAlbumLinkedToTitle = checkBox.IsChecked == true;
        _settingsService.Save(_settings);
    }

    private void UseLeadingZerosInChapterSuggestionsCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_isLoading || sender is not CheckBox checkBox)
            return;

        _settings.UseLeadingZerosInChapterSuggestions = checkBox.IsChecked == true;
        _settingsService.Save(_settings);
    }

    private void AudioDiscWorkingFormatComboBox_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading || sender is not ComboBox comboBox)
            return;

        _settings.AudioDiscWorkingFormat = GetSelectedAudioDiscWorkingFormat(comboBox).ToString();
        _settingsService.Save(_settings);
    }

    private void SelectAudioDiscWorkingFormatComboBoxItem()
    {
        if (FindName("AudioDiscWorkingFormatComboBox") is not ComboBox comboBox)
            return;

        var selectedFormat = AudioDiscSettingsService.NormalizeWorkingFormat(_settings.AudioDiscWorkingFormat);
        foreach (var item in comboBox.Items.OfType<ComboBoxItem>())
        {
            if (Enum.TryParse<AudioDiscWorkingFormat>(item.Tag?.ToString(), out var format)
                && format == selectedFormat)
            {
                comboBox.SelectedItem = item;
                return;
            }
        }

        comboBox.SelectedIndex = 0;
    }

    private static AudioDiscWorkingFormat GetSelectedAudioDiscWorkingFormat(ComboBox comboBox)
    {
        if (comboBox.SelectedItem is ComboBoxItem item
            && Enum.TryParse<AudioDiscWorkingFormat>(item.Tag?.ToString(), out var format))
        {
            return format;
        }

        return AudioDiscSettingsService.DefaultWorkingFormat;
    }

    private void FocusProfileComboBox_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading || sender is not ComboBox comboBox)
            return;

        _settings.FocusProfile = GetSelectedFocusProfile(comboBox).ToString();
        _settingsService.Save(_settings);
    }

    private void SelectFocusProfileComboBoxItem() => SelectFocusProfileComboBoxItem("FocusProfileComboBox");

    private void SelectFocusProfileComboBoxItem(string comboBoxName)
    {
        if (FindName(comboBoxName) is not ComboBox comboBox)
            return;

        var selectedProfile = FocusSettingsService.NormalizeProfile(_settings.FocusProfile);
        foreach (var item in comboBox.Items.OfType<ComboBoxItem>())
        {
            if (Enum.TryParse<FocusProfile>(item.Tag?.ToString(), out var profile) && profile == selectedProfile)
            {
                comboBox.SelectedItem = item;
                return;
            }
        }

        comboBox.SelectedIndex = 1;
    }

    private static FocusProfile GetSelectedFocusProfile(ComboBox comboBox)
    {
        if (comboBox.SelectedItem is ComboBoxItem item
            && Enum.TryParse<FocusProfile>(item.Tag?.ToString(), out var profile))
        {
            return profile;
        }

        return FocusSettingsService.DefaultProfile;
    }

    private void SoundProfileComboBox_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading || sender is not ComboBox comboBox)
            return;

        _settings.SoundProfile = GetSelectedSoundProfile(comboBox).ToString();
        _settingsService.Save(_settings);
    }

    private void SoundLibraryComboBox_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading || sender is not ComboBox comboBox)
            return;

        _settings.SoundLibrary = GetSelectedSoundLibrary(comboBox).ToString();
        _settingsService.Save(_settings);
        _notificationService.PlaySoundPreview();
    }

    private void SoundVolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isLoading)
            return;

        _settings.SoundVolumePercent = SoundSettingsService.NormalizeVolumePercent((int)Math.Round(e.NewValue));
        _settingsService.Save(_settings);
        UpdateSoundVolumeText();
    }

    private void PlayTestSound_Click(object sender, RoutedEventArgs e)
    {
        _notificationService.PlaySoundPreview();
    }

    private void RunFocusTest_Click(object sender, RoutedEventArgs e)
    {
        if (_isRunningFocusTest)
            return;

        var profile = FocusSettingsService.NormalizeProfile(_settings.FocusProfile);
        if (profile == FocusProfile.Off)
            return;

        _ = RunFocusTestAsync(profile);
    }

    private async Task RunFocusTestAsync(FocusProfile profile)
    {
        _isRunningFocusTest = true;

        try
        {
            EnsureTemporaryTaskbarVisibilityForFocusTest();
            WindowState = WindowState.Minimized;
            await Task.Delay(900);

            FlashTaskbar(this, count: 5);

            if (profile != FocusProfile.Foreground)
                return;

            await Task.Delay(1200);
            BringWindowToForeground(this, useTemporaryTopmost: true);
            RestoreTemporaryTaskbarVisibilityAfterFocusTest();
        }
        finally
        {
            _isRunningFocusTest = false;
        }
    }

    private void EnsureTemporaryTaskbarVisibilityForFocusTest()
    {
        if (ShowInTaskbar)
            return;

        ShowInTaskbar = true;
        _restoreShowInTaskbarAfterFocusTest = true;
    }

    private void SettingsWindow_StateChanged(object? sender, EventArgs e)
    {
        if (WindowState != WindowState.Minimized)
            RestoreTemporaryTaskbarVisibilityAfterFocusTest();
    }

    private void RestoreTemporaryTaskbarVisibilityAfterFocusTest()
    {
        if (!_restoreShowInTaskbarAfterFocusTest || WindowState == WindowState.Minimized)
            return;

        ShowInTaskbar = false;
        _restoreShowInTaskbarAfterFocusTest = false;
    }

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
        if (count <= 0)
            return;

        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
            return;

        var info = new FlashWindowInfo
        {
            Size = (uint)Marshal.SizeOf<FlashWindowInfo>(),
            WindowHandle = handle,
            Flags = FlashTray,
            Count = (uint)count,
            TimeoutMilliseconds = 0
        };

        FlashWindowEx(ref info);
    }

    private void SelectSoundProfileComboBoxItem() => SelectSoundProfileComboBoxItem("SoundProfileComboBox");

    private void SelectSoundProfileComboBoxItem(string comboBoxName)
    {
        if (FindName(comboBoxName) is not ComboBox comboBox)
            return;

        var selectedProfile = SoundSettingsService.NormalizeProfile(_settings.SoundProfile);
        foreach (var item in comboBox.Items.OfType<ComboBoxItem>())
        {
            if (Enum.TryParse<SoundProfile>(item.Tag?.ToString(), out var profile) && profile == selectedProfile)
            {
                comboBox.SelectedItem = item;
                return;
            }
        }

        comboBox.SelectedIndex = 2;
    }

    private void SelectSoundLibraryComboBoxItem() => SelectSoundLibraryComboBoxItem("SoundLibraryComboBox");

    private void SelectSoundLibraryComboBoxItem(string comboBoxName)
    {
        if (FindName(comboBoxName) is not ComboBox comboBox)
            return;

        var selectedLibrary = SoundSettingsService.NormalizeLibrary(_settings.SoundLibrary);
        foreach (var item in comboBox.Items.OfType<ComboBoxItem>())
        {
            if (Enum.TryParse<SoundLibrary>(item.Tag?.ToString(), out var library) && library == selectedLibrary)
            {
                comboBox.SelectedItem = item;
                return;
            }
        }

        comboBox.SelectedIndex = 0;
    }

    private static SoundLibrary GetSelectedSoundLibrary(ComboBox comboBox)
    {
        if (comboBox.SelectedItem is ComboBoxItem item
            && Enum.TryParse<SoundLibrary>(item.Tag?.ToString(), out var library))
        {
            return library;
        }

        return SoundSettingsService.DefaultLibrary;
    }

    private static SoundProfile GetSelectedSoundProfile(ComboBox comboBox)
    {
        if (comboBox.SelectedItem is ComboBoxItem item
            && Enum.TryParse<SoundProfile>(item.Tag?.ToString(), out var profile))
        {
            return profile;
        }

        return SoundSettingsService.DefaultProfile;
    }

    private void UpdateSoundVolumeText()
    {
        if (FindName("SoundVolumeTextBlock") is TextBlock textBlock)
            textBlock.Text = $"{_settings.SoundVolumePercent} %";
        if (FindName("DevSoundVolumeTextBlock") is TextBlock devTextBlock)
            devTextBlock.Text = $"{_settings.SoundVolumePercent} %";
    }

    private void ProjectRetentionDaysComboBox_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading)
            return;

        if (sender is not ComboBox comboBox)
            return;

        SaveProjectRetentionDays(GetComboBoxText(comboBox), updateText: false);
    }

    private void ProjectRetentionDaysComboBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not ComboBox comboBox)
            return;

        SaveProjectRetentionDays(GetComboBoxText(comboBox), updateText: true);
    }

    private void OutputFolderLayoutComboBox_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading)
            return;

        if (sender is not ComboBox comboBox)
            return;

        var layout = GetSelectedOutputFolderLayout(comboBox);
        _settings.OutputFolderLayout = OutputFolderLayoutService.NormalizeLayout(layout);
        _settingsService.Save(_settings);
        UpdateOutputFolderPreview();
    }

    private void DeleteOldProjectsNow_Click(object sender, RoutedEventArgs e)
    {
        SaveProjectRetentionDaysFromControl();

        var days = _settings.ProjectRetentionDays;
        var result = _deleteOldProjects(days);

        var message = new StringBuilder();
        message.AppendLine(result.DeletedCount == 0
            ? "Es wurden keine alten Projekte gelöscht."
            : result.DeletedCount == 1
                ? "1 altes Projekt wurde gelöscht."
                : $"{result.DeletedCount} alte Projekte wurden gelöscht.");

        if (result.HasFailures)
        {
            message.AppendLine();
            message.AppendLine("Nicht gelöscht werden konnten:");

            foreach (var failure in result.Failures.Take(8))
                message.AppendLine("• " + failure);

            if (result.Failures.Count > 8)
                message.AppendLine($"• ... und {result.Failures.Count - 8} weitere");
        }

        AppDialogService.Show(
            this,
            "Projekte löschen",
            result.HasFailures ? "Projektbereinigung teilweise fehlgeschlagen" : "Projektbereinigung abgeschlossen",
            message.ToString().TrimEnd(),
            result.HasFailures ? AppDialogKind.Warning : AppDialogKind.Information,
            null,
            new[] { new AppDialogButton("OK", AppDialogResult.Ok, IsPrimary: true, IsDefault: true) });
    }

    private void SaveProjectRetentionDaysFromControl()
    {
        if (FindName("ProjectRetentionDaysComboBox") is ComboBox comboBox)
            SaveProjectRetentionDays(GetComboBoxText(comboBox), updateText: true);
    }

    private void SaveProjectRetentionDays(string? text, bool updateText)
    {
        if (!TryReadInt(text, out var value))
            value = ProjectIndexService.DefaultRetentionDays;

        _settings.ProjectRetentionDays = ProjectIndexService.NormalizeRetentionDays(value);
        _settings.DeleteProjectsOlderThanDays = _settings.ProjectRetentionDays;
        _settingsService.Save(_settings);

        if (updateText && FindName("ProjectRetentionDaysComboBox") is ComboBox comboBox)
            comboBox.Text = _settings.ProjectRetentionDays.ToString(CultureInfo.InvariantCulture);

        UpdateProjectRetentionHint();
    }

    private void UpdateProjectRetentionHint()
    {
        if (FindName("ProjectRetentionHintTextBlock") is not TextBlock textBlock)
            return;

        var days = ProjectIndexService.NormalizeRetentionDays(_settings.ProjectRetentionDays);
        textBlock.Text = days == 0
            ? "Alle Projekte werden beim Schließen automatisch gelöscht."
            : $"Projekte älter als {days} Tage werden beim Schließen automatisch gelöscht.";
    }

    private void SelectOutputFolderLayoutComboBoxItem()
    {
        if (FindName("OutputFolderLayoutComboBox") is not ComboBox comboBox)
            return;

        foreach (var item in comboBox.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(item.Tag?.ToString(), _settings.OutputFolderLayout, StringComparison.OrdinalIgnoreCase))
            {
                comboBox.SelectedItem = item;
                return;
            }
        }

        comboBox.SelectedIndex = 3;
    }

    private static string GetSelectedOutputFolderLayout(ComboBox comboBox)
    {
        if (comboBox.SelectedItem is ComboBoxItem item)
            return item.Tag?.ToString() ?? OutputFolderLayoutService.DefaultLayout;

        return OutputFolderLayoutService.DefaultLayout;
    }

    private static bool TryReadInt(string? text, out int value)
    {
        return int.TryParse((text ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static void SelectComboBoxItemByContent(ComboBox comboBox, string value)
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

    private static string GetComboBoxText(ComboBox comboBox)
    {
        if (comboBox.SelectedItem is ComboBoxItem selectedItem)
            return selectedItem.Content?.ToString() ?? comboBox.Text;

        return comboBox.Text;
    }


    private void UpdateOutputFolderPreview()
    {
        if (FindName("OutputFolderPreviewTextBlock") is not TextBlock previewTextBlock)
            return;

        var baseOutputFolder = string.IsNullOrWhiteSpace(_settings.OutputFolder)
            ? "Ausgabeordner"
            : _settings.OutputFolder.Trim();

        var author = "Cornelia Funke";
        var title = "Tintenherz";
        var album = "Tintenherz";
        var narrator = "Rainer Strecker";
        var series = "Tintenwelt";
        var template = string.IsNullOrWhiteSpace(_settings.DefaultFileNameTemplate)
            ? "{Autor} - {Titel}"
            : _settings.DefaultFileNameTemplate;
        var extension = _settings.DefaultOutputExtension is ".m4a" or ".m4b"
            ? _settings.DefaultOutputExtension
            : ".m4a";
        var fileName = FileNameTemplateService.BuildOutputFileName(
            title,
            author,
            narrator,
            template,
            extension,
            album,
            series);

        var outputPath = new OutputFolderLayoutService().BuildOutputPath(
            baseOutputFolder,
            author,
            title,
            fileName,
            _settings.OutputFolderLayout,
            album,
            series);

        var outputFolder = Path.GetDirectoryName(outputPath) ?? string.Empty;
        var displayFolder = string.IsNullOrWhiteSpace(outputFolder)
            ? baseOutputFolder
            : outputFolder;
        var separator = string.IsNullOrWhiteSpace(displayFolder) || displayFolder.EndsWith(Path.DirectorySeparatorChar)
            ? string.Empty
            : Path.DirectorySeparatorChar.ToString();

        previewTextBlock.Inlines.Clear();
        previewTextBlock.Inlines.Add(new Run(displayFolder + separator));
        previewTextBlock.Inlines.Add(new Run(fileName)
        {
            Foreground = (Brush)FindResource("AccentSoftBrush"),
            FontWeight = FontWeights.SemiBold
        });
    }

    private const uint FlashTray = 0x00000002;

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

    private void MetadataAnimationMillisecondsTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        SaveMetadataAnimationMilliseconds();
    }

    private void MetadataAnimationMillisecondsTextBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != System.Windows.Input.Key.Enter)
            return;

        SaveMetadataAnimationMilliseconds();
        e.Handled = true;
    }

    private void SaveMetadataAnimationMilliseconds()
    {
        if (_isLoading || FindName("MetadataAnimationMillisecondsTextBox") is not TextBox textBox)
            return;

        if (!TryReadInt(textBox.Text, out var value))
            value = 550;

        _settings.MetadataPanelAnimationMilliseconds = Math.Clamp(value, 0, 2000);
        textBox.Text = _settings.MetadataPanelAnimationMilliseconds.ToString(CultureInfo.InvariantCulture);
        _settingsService.Save(_settings);
    }

    private void ToggleMetadataPanelPreview_Click(object sender, RoutedEventArgs e)
    {
        SaveMetadataAnimationMilliseconds();
        _metadataPanelExpandedPreview = !_metadataPanelExpandedPreview;
        _setMetadataPanelExpanded(_metadataPanelExpandedPreview);
        UpdateMetadataPanelPreviewButtonText();
    }

    private void UpdateMetadataPanelPreviewButtonText()
    {
        if (FindName("ToggleMetadataPanelPreviewButton") is Button button)
            button.Content = "Tagbalken-Test";
    }

    private void ForceShowFfmpegSetupButtonCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_isLoading || sender is not CheckBox checkBox)
            return;

        _settings.ForceShowFfmpegSetupButton = checkBox.IsChecked == true;
        _settingsService.Save(_settings);
    }

    private void DeleteAllProjects_Click(object sender, RoutedEventArgs e)
    {
        var confirmed = AppDialogService.Confirm(
            this,
            "Alle Projekte endgültig löschen?",
            "Alle BookStitch-Projektordner werden einschließlich Originaldateien, Konvertierungen und Ausgaben gelöscht.",
            ["Globale Einstellungen, Logs und der BookStitch-Hauptordner bleiben erhalten.", "Dieser Vorgang kann nicht rückgängig gemacht werden."],
            "Alle Projekte löschen");

        if (!confirmed)
            return;

        var result = _deleteOldProjects(0);
        ShowProjectCleanupResult(result, "Alle Projekte löschen");
    }

    private void ShowProjectCleanupResult(ProjectCleanupResult result, string title)
    {
        var message = new StringBuilder();
        message.AppendLine(result.DeletedCount == 0
            ? "Es wurden keine Projekte gelöscht."
            : result.DeletedCount == 1
                ? "1 Projekt wurde gelöscht."
                : $"{result.DeletedCount} Projekte wurden gelöscht.");

        if (result.HasFailures)
        {
            message.AppendLine();
            message.AppendLine("Nicht gelöscht werden konnten:");
            foreach (var failure in result.Failures.Take(8))
                message.AppendLine("• " + failure);
        }

        AppDialogService.Show(
            this,
            title,
            result.HasFailures ? "Projektbereinigung teilweise fehlgeschlagen" : "Projektbereinigung abgeschlossen",
            message.ToString().TrimEnd(),
            result.HasFailures ? AppDialogKind.Warning : AppDialogKind.Information,
            null,
            [new AppDialogButton("OK", AppDialogResult.Ok, IsPrimary: true, IsDefault: true)]);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => CloseWindow();
    private void ShowDeveloperTabCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_isLoading || sender is not CheckBox checkBox)
            return;

        _settings.ShowDeveloperTab = checkBox.IsChecked == true;
        ApplyDeveloperTabVisibility();
        _settingsService.Save(_settings);
        _setPipelineStateDebugVisible(_settings.ShowDeveloperTab && _settings.ShowPipelineStateDebug);
    }

    private void DiscWaitDialogVariantComboBox_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading || sender is not ComboBox comboBox || comboBox.SelectedItem is not ComboBoxItem item)
            return;

        _settings.UseBoxedDiscWaitDialog = string.Equals(item.Tag?.ToString(), "Boxed", StringComparison.Ordinal);
        _settingsService.Save(_settings);
    }

    private void SelectDiscWaitDialogVariantComboBoxItem()
    {
        if (FindName("DiscWaitDialogVariantComboBox") is not ComboBox comboBox)
            return;

        var wanted = _settings.UseBoxedDiscWaitDialog ? "Boxed" : "Legacy";
        comboBox.SelectedItem = comboBox.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(item => string.Equals(item.Tag?.ToString(), wanted, StringComparison.Ordinal));
        comboBox.SelectedIndex = comboBox.SelectedItem is null ? 0 : comboBox.SelectedIndex;
    }

    private void ShowPipelineStateDebugCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_isLoading || sender is not CheckBox checkBox)
            return;

        _settings.ShowPipelineStateDebug = checkBox.IsChecked == true;
        _settingsService.Save(_settings);
        _setPipelineStateDebugVisible(_settings.ShowDeveloperTab && _settings.ShowPipelineStateDebug);
    }

    private void SettingsWindow_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
            return;

        e.Handled = true;
        CloseWindow();
    }

    private async void PreviewDiscSourceAnalysis_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || !button.IsEnabled)
            return;

        button.IsEnabled = false;
        var previousContent = button.Content;
        button.Content = "Vorschau läuft …";

        try
        {
            await _previewDiscSourceAnalysis();
        }
        finally
        {
            button.Content = previousContent;
            button.IsEnabled = true;
        }
    }

    private void MessagePreview_Click(object sender, RoutedEventArgs e)
    {
        var kind = sender is Button { Tag: string tag } && Enum.TryParse<AppDialogKind>(tag, out var parsed)
            ? parsed
            : AppDialogKind.Information;

        AppDialogService.Show(
            this,
            "BookStitch – Dialogvorschau",
            kind switch
            {
                AppDialogKind.Warning => "Beispielwarnung",
                AppDialogKind.Error => "Beispielfehler",
                AppDialogKind.Question => "Beispielfrage",
                _ => "Beispielinformation"
            },
            "Dieser Dialog dient ausschließlich der visuellen und mechanischen Prüfung. Es werden keine Projektdaten verändert.",
            kind,
            ["Erste optionale Detailzeile", "Zweite optionale Detailzeile"],
            kind == AppDialogKind.Question
                ? [
                    new AppDialogButton("Ja", AppDialogResult.Yes, IsPrimary: true, IsDefault: true),
                    new AppDialogButton("Nein", AppDialogResult.No),
                    new AppDialogButton("Abbrechen", AppDialogResult.Cancel, IsCancel: true)
                  ]
                : [new AppDialogButton("OK", AppDialogResult.Ok, IsPrimary: true, IsDefault: true)]);
    }

    private void InputPreview_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new AppInputDialog(
            "BookStitch – Dialogvorschau",
            "Beispielwert eingeben",
            "Prüfe Eingabefeld, Fehlermeldung, Enter, Escape und die Fenstermechanik.",
            12,
            1,
            99)
        {
            Owner = this
        };
        dialog.ShowDialog();
    }

    private void DiscSourcePreview_Click(object sender, RoutedEventArgs e)
    {
        var drives = _developerDiscDriveService.GetCdDriveShells();
        var dialog = new DiscSourceSelectionDialog(drives, string.Empty, _settings.LastSelectedOpticalDrive)
        {
            Owner = this
        };
        dialog.ShowDialog();
    }

    private void AddDiscsPreview_Click(object sender, RoutedEventArgs e)
    {
        var drives = _developerDiscDriveService.GetCdDriveShells();
        var dialog = new AddMoreDiscsDialog(1, 2, 2, 2, 99, drives, _settings.LastSelectedOpticalDrive, string.Empty)
        {
            Owner = this
        };
        dialog.ShowDialog();
    }

    private void ResumeProjectPreview_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ResumeProjectDialog(
            _settingsService.ProjectRootFolder,
            new ProjectIndexService(),
            new ProjectResumePlanService(),
            _settings.ProjectRetentionDays)
        {
            Owner = this
        };
        dialog.ShowDialog();
    }

    private void ProjectSetupPreview_Click(object sender, RoutedEventArgs e)
    {
        var sourceKind = sender is Button { Tag: string tag } && Enum.TryParse<ProjectSetupSourceKind>(tag, out var parsed)
            ? parsed
            : ProjectSetupSourceKind.Folder;

        var sourceLabel = sourceKind switch
        {
            ProjectSetupSourceKind.AudioDisc => "Audio-CD in Laufwerk D:",
            ProjectSetupSourceKind.Mp3Disc => "MP3-CD in Laufwerk D:",
            _ => "C:\\Beispiel\\Hörbuch"
        };
        var windowTitle = sourceKind switch
        {
            ProjectSetupSourceKind.AudioDisc => "Audio-CD-Projekt vorbereiten",
            ProjectSetupSourceKind.Mp3Disc => "MP3-CD-Projekt vorbereiten",
            _ => "Lokales Projekt vorbereiten"
        };

        var request = new ProjectSetupDialogRequest(
            sourceKind,
            windowTitle,
            sourceLabel,
            "Diese Vorschau verändert keine Projektdaten. Prüfe Größe, Buttons, Eingabefelder, Ziehen und Schließen.",
            sourceKind == ProjectSetupSourceKind.Folder ? 1 : 2,
            1,
            99,
            ["AAC Stereo 128 kbps", "AAC Mono 64 kbps"],
            "AAC Stereo 128 kbps",
            _settings.SelectedParallelJobs,
            _settings.DefaultOutputExtension,
            _settings.OutputFolder ?? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            "Beispielhörbuch",
            "Beispielhörbuch",
            "Beispielautor",
            "Beispielsprecher",
            _settings.DefaultGenre,
            _settings.DefaultFileNameTemplate,
            string.Empty,
            string.Empty,
            Path.Combine(_settingsService.ProjectRootFolder, "Covers"),
            _settings.MergeAutomaticallyAfterConversion,
            _settings.KeepAlbumLinkedToTitle,
            SourceFolder: sourceLabel,
            MaxSourceBitrateKbps: 320,
            LastCoverFolder: _settings.LastCoverFolder ?? string.Empty);

        var dialog = new DiscProjectSetupDialog(request) { Owner = this };
        dialog.ShowDialog();
    }

    private void NotificationPreview_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tag } ||
            !Enum.TryParse<NotificationEvent>(tag, out var notificationEvent))
        {
            return;
        }

        _notificationService.Notify(notificationEvent);
    }

    private async void DelayedFocusPreview_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tag } button ||
            !Enum.TryParse<NotificationEvent>(tag, out var notificationEvent))
        {
            return;
        }

        _delayedFocusTestCancellation?.Cancel();
        _delayedFocusTestCancellation?.Dispose();
        _delayedFocusTestCancellation = new CancellationTokenSource();
        var cancellationToken = _delayedFocusTestCancellation.Token;

        button.IsEnabled = false;
        try
        {
            for (var remaining = 4; remaining > 0; remaining--)
            {
                if (FindName("DelayedFocusStatusTextBlock") is TextBlock statusTextBlock)
                    statusTextBlock.Text = $"{button.Content}: Auslösung in {remaining} s";
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            }

            if (FindName("DelayedFocusStatusTextBlock") is TextBlock completedTextBlock)
                completedTextBlock.Text = $"Ausgelöst: {button.Content}";
            _notificationService.Notify(notificationEvent);
        }
        catch (OperationCanceledException)
        {
            if (FindName("DelayedFocusStatusTextBlock") is TextBlock canceledTextBlock)
                canceledTextBlock.Text = "Verzögerter Fokus-Test abgebrochen";
        }
        finally
        {
            button.IsEnabled = true;
        }
    }

    private async void SimulatedDiscWaitPreview_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tag }
            || !Enum.TryParse<DeveloperDiscSimulationScenario>(tag, out var scenario))
        {
            return;
        }

        CancelCurrentDeveloperDiscTest();
        _developerDiscTestCancellation = new CancellationTokenSource();
        var cancellationToken = _developerDiscTestCancellation.Token;
        var checkNumber = 0;

        SetDeveloperDiscTestStatus($"Dialogsimulation gestartet: {GetSimulationDisplayName(scenario)}", string.Empty);
        try
        {
            var completed = await _developerDiscWaitDialogService.WaitForDiscAsync(
                this,
                new DiscWaitDialogRequest(
                    DiscNumber: 2,
                    TotalDiscs: 3,
                    MediaName: "Test-CD",
                    InitialInstruction: "Entwicklersimulation wird vorbereitet...",
                    HintText: "Diese Vorschau greift nicht auf ein Laufwerk zu und verändert keine Projektdaten."),
                _ => Task.FromResult(_developerDiscTestService.CreateSimulationResult(scenario, ++checkNumber)),
                status => SetDeveloperDiscTestStatus(status, null),
                progress => SetDeveloperDiscTestStatus(null, progress),
                cancellationToken);

            SetDeveloperDiscTestStatus(
                completed == DiscWaitDialogOutcome.Ready
                    ? $"Dialogsimulation abgeschlossen: {GetSimulationDisplayName(scenario)}"
                    : $"Dialogsimulation später fortsetzbar beendet: {GetSimulationDisplayName(scenario)}",
                string.Empty);
        }
        catch (OperationCanceledException)
        {
            SetDeveloperDiscTestStatus("Dialogsimulation abgebrochen.", string.Empty);
        }
    }

    private async void HybridDiscTestPreview_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tag })
            return;

        var selectedDrive = SelectOpticalDrive();
        if (string.IsNullOrWhiteSpace(selectedDrive))
            return;

        _settings.LastSelectedOpticalDrive = selectedDrive;
        _settingsService.Save(_settings);

        CancelCurrentDeveloperDiscTest();
        _developerDiscTestCancellation = new CancellationTokenSource();
        var cancellationToken = _developerDiscTestCancellation.Token;

        try
        {
            var isAudio = tag.StartsWith("Audio", StringComparison.Ordinal);
            var duplicateTest = tag.EndsWith("Duplicate", StringComparison.Ordinal);
            var mediaName = isAudio ? "Audio-CD" : "MP3-CD";

            HashSet<string> knownIdentities = [];
            if (duplicateTest)
            {
                var identity = isAudio
                    ? ReadCurrentAudioDiscIdentity(selectedDrive)
                    : ReadCurrentMp3DiscIdentity(selectedDrive);
                if (string.IsNullOrWhiteSpace(identity))
                    return;

                knownIdentities.Add(identity);
            }

            var ejected = _developerDiscDriveService.TryEjectDisc(selectedDrive);
            SetDeveloperDiscTestStatus(
                ejected
                    ? $"{mediaName}-Hardwaretest: Laufwerk wurde ausgeworfen. Bitte Disc einlegen."
                    : $"{mediaName}-Hardwaretest: Bitte Laufwerk öffnen und Disc einlegen.",
                duplicateTest
                    ? "Für den Duplikattest dieselbe Disc erneut einlegen."
                    : "Es wird nur erkannt und geprüft; Import und Ripping bleiben deaktiviert.");

            var completed = isAudio
                ? await RunAudioHybridTestAsync(selectedDrive, knownIdentities, cancellationToken)
                : await RunMp3HybridTestAsync(selectedDrive, knownIdentities, cancellationToken);

            SetDeveloperDiscTestStatus(
                completed == DiscWaitDialogOutcome.Ready
                    ? $"{mediaName}-Hardwaretest erfolgreich. Es wurden keine Tracks verarbeitet."
                    : $"{mediaName}-Hardwaretest wurde für später beendet.",
                string.Empty);
        }
        catch (OperationCanceledException)
        {
            SetDeveloperDiscTestStatus("Hybridtest abgebrochen.", string.Empty);
        }
        catch (Exception ex)
        {
            SetDeveloperDiscTestStatus("Hybridtest fehlgeschlagen.", string.Empty);
            AppDialogService.Show(
                this,
                "BookStitch – Hybridtest",
                "Hardwaretest fehlgeschlagen",
                ex.Message,
                AppDialogKind.Error,
                buttons: [new AppDialogButton("OK", AppDialogResult.Ok, IsPrimary: true, IsDefault: true)]);
        }
    }

    private Task<DiscWaitDialogOutcome> RunMp3HybridTestAsync(
        string driveRoot,
        ISet<string> knownSignatures,
        CancellationToken cancellationToken)
    {
        var pollingService = new Mp3DiscPollingService(_developerMp3DiscImportService, _developerDiscDriveService);
        return _developerDiscWaitDialogService.WaitForDiscAsync(
            this,
            new DiscWaitDialogRequest(
                DiscNumber: 2,
                TotalDiscs: 3,
                MediaName: "MP3-CD",
                InitialInstruction: "Bitte CD 2 von 3 einlegen.\n\nBookStitch erkennt die eingelegte Disc automatisch und setzt anschließend fort.",
                HintText: "Nach erfolgreicher Erkennung endet der Test ohne Import, Konvertierung oder Projektänderung.",
                DriveDisplayName: CreateDiscDriveDisplayName(driveRoot),
                NotifyDisplayState: NotifyDeveloperDiscTestPollingState),
            token => Task.Run(
                () => pollingService.CheckDiscSourceForNextImport(driveRoot, 2, 3, knownSignatures),
                token),
            status => SetDeveloperDiscTestStatus(status, null),
            progress => SetDeveloperDiscTestStatus(null, progress),
            cancellationToken);
    }

    private Task<DiscWaitDialogOutcome> RunAudioHybridTestAsync(
        string driveRoot,
        ISet<string> knownIdentities,
        CancellationToken cancellationToken)
    {
        var pollingService = new AudioDiscPollingService(new AudioDiscReaderService(), _developerDiscDriveService);
        return _developerDiscWaitDialogService.WaitForDiscAsync(
            this,
            new DiscWaitDialogRequest(
                DiscNumber: 2,
                TotalDiscs: 3,
                MediaName: "Audio-CD",
                InitialInstruction: "Bitte Audio-CD 2 von 3 einlegen.\n\nBookStitch erkennt die eingelegte Disc automatisch und setzt anschließend fort.",
                HintText: "Nach erfolgreicher Erkennung endet der Test ohne Ripping, Konvertierung oder Projektänderung.",
                DriveDisplayName: CreateDiscDriveDisplayName(driveRoot),
                NotifyDisplayState: NotifyDeveloperDiscTestPollingState),
            token => Task.Run(
                () => pollingService.CheckNextDisc(driveRoot, 2, 3, knownIdentities).PollingResult,
                token),
            status => SetDeveloperDiscTestStatus(status, null),
            progress => SetDeveloperDiscTestStatus(null, progress),
            cancellationToken);
    }

    private string? ReadCurrentMp3DiscIdentity(string driveRoot)
    {
        var analysis = _developerMp3DiscImportService.AnalyzeSource(driveRoot);
        if (!analysis.IsSupportedDataDisc)
        {
            ShowDiscTestInformation("Für den Duplikattest muss zuerst eine unterstützte MP3-CD im ausgewählten Laufwerk liegen.");
            return null;
        }

        return _developerMp3DiscImportService.CreateDiscSignature(driveRoot, analysis);
    }

    private string? ReadCurrentAudioDiscIdentity(string driveRoot)
    {
        var mediaKind = _developerDiscDriveService.GetMediaKindForPath(driveRoot);
        if (AudioDiscPollingService.IsClearlyNotAudioDisc(mediaKind))
        {
            ShowDiscTestInformation("Für den Audio-CD-Duplikattest muss zuerst eine lesbare Audio-CD im ausgewählten Laufwerk liegen.");
            return null;
        }

        var readResult = new AudioDiscReaderService().ReadDisc(driveRoot);
        if (!readResult.IsAudioDisc || readResult.Disc is null)
        {
            ShowDiscTestInformation("Für den Duplikattest muss zuerst eine lesbare Audio-CD im ausgewählten Laufwerk liegen.");
            return null;
        }

        return readResult.Disc.DiscIdentity;
    }

    private void NotifyDeveloperDiscTestPollingState(DiscPollingDisplayState state)
    {
        if (state is DiscPollingDisplayState.Unsupported or DiscPollingDisplayState.Duplicate)
            _notificationService.Notify(NotificationEvent.Warning);
    }

    private static string CreateDiscDriveDisplayName(string driveRoot)
    {
        var root = Path.GetPathRoot(driveRoot)?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar, Path.VolumeSeparatorChar);
        return string.IsNullOrWhiteSpace(root) ? string.Empty : $"CD-Laufwerk {root}";
    }

    private string? SelectOpticalDrive()
    {
        var drives = _developerDiscDriveService.GetCdDriveShells();
        if (drives.Count == 0)
        {
            ShowDiscTestInformation("Windows hat kein optisches Laufwerk gemeldet.");
            return null;
        }

        var dialog = new DiscSourceSelectionDialog(drives, string.Empty, _settings.LastSelectedOpticalDrive)
        {
            Owner = this
        };
        return dialog.ShowDialog() == true ? dialog.SelectedPath : null;
    }

    private void ShowDiscTestInformation(string message)
    {
        AppDialogService.Show(
            this,
            "BookStitch – CD-Test",
            "Test kann nicht gestartet werden",
            message,
            AppDialogKind.Information,
            buttons: [new AppDialogButton("OK", AppDialogResult.Ok, IsPrimary: true, IsDefault: true)]);
    }

    private void SetDeveloperDiscTestStatus(string? status, string? progress)
    {
        if (status is not null && FindName("DiscTestStatusTextBlock") is TextBlock statusTextBlock)
            statusTextBlock.Text = status;

        if (progress is not null && FindName("DiscTestProgressTextBlock") is TextBlock progressTextBlock)
            progressTextBlock.Text = progress;
    }

    private void CancelCurrentDeveloperDiscTest()
    {
        _developerDiscTestCancellation?.Cancel();
        _developerDiscTestCancellation?.Dispose();
        _developerDiscTestCancellation = null;
    }

    private static string GetSimulationDisplayName(DeveloperDiscSimulationScenario scenario) => scenario switch
    {
        DeveloperDiscSimulationScenario.EmptyDrive => "Laufwerk leer",
        DeveloperDiscSimulationScenario.UnsupportedDisc => "falsche oder nicht unterstützte Disc",
        DeveloperDiscSimulationScenario.DuplicateEjected => "Duplikat erkannt und ausgeworfen",
        DeveloperDiscSimulationScenario.DuplicateManualEject => "Duplikat, manuelles Auswerfen nötig",
        DeveloperDiscSimulationScenario.SlowThenReady => "Laufwerk langsam, danach erkannt",
        DeveloperDiscSimulationScenario.Ready => "richtige Disc erkannt",
        _ => scenario.ToString()
    };

    protected override void OnClosed(EventArgs e)
    {
        _delayedFocusTestCancellation?.Cancel();
        _delayedFocusTestCancellation?.Dispose();
        base.OnClosed(e);
    }

    private void CloseWindow()
    {
        CancelCurrentDeveloperDiscTest();
        SaveMetadataAnimationMilliseconds();
        SaveProjectRetentionDaysFromControl();
        Close();
    }


    private async void StartDeveloperAudioDiscTest_Click(object sender, RoutedEventArgs e)
    {
        Hide();
        try { await _startDeveloperAudioDiscTest(); }
        finally { Close(); }
    }
}
