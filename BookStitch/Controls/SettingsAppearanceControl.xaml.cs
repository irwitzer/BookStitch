using BookStitch.Models;
using BookStitch.Services;
using System.Windows;
using System.Windows.Controls;

namespace BookStitch.Controls;

public partial class SettingsAppearanceControl : UserControl
{
    private GlobalSettingsManager? _settingsManager;
    private Action<AppSettings>? _applyAppearanceSettings;
    private bool _isLoading = true;

    public SettingsAppearanceControl()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            System.ComponentModel.PropertyChangedEventManager.AddHandler(
                DeveloperToolsCommands.AccessState, DeveloperAccessChanged, string.Empty);
            if (_settingsManager is not null) RefreshBackgroundPresets();
        };
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible && _settingsManager is not null) RefreshBackgroundPresets();
        };
        Unloaded += (_, _) => System.ComponentModel.PropertyChangedEventManager.RemoveHandler(
            DeveloperToolsCommands.AccessState, DeveloperAccessChanged, string.Empty);
    }

    public void Initialize(
        GlobalSettingsManager settingsManager,
        Action<AppSettings> applyAppearanceSettings)
    {
        _settingsManager = settingsManager ?? throw new ArgumentNullException(nameof(settingsManager));
        _applyAppearanceSettings = applyAppearanceSettings ?? throw new ArgumentNullException(nameof(applyAppearanceSettings));

        AppearanceSettingsService.NormalizeRegular(Settings, DeveloperToolsCommands.AccessState.IsEnabled);
        ApplyAppearanceResources(Settings);
        RefreshBackgroundPresets();
        SelectControls();
        _isLoading = false;
    }

    private void DeveloperAccessChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (Dispatcher.CheckAccess() && IsVisible && _settingsManager is not null && e.PropertyName == nameof(DeveloperToolsAccessState.IsEnabled))
            RefreshBackgroundPresets();
    }

    private void RefreshBackgroundPresets()
    {
        var wasLoading = _isLoading;
        _isLoading = true;
        try
        {
            AppearanceSettingsService.NormalizeRegular(Settings, DeveloperToolsCommands.AccessState.IsEnabled);
            SettingsComboBoxSelection.Populate(BackgroundPresetComboBox,
                AppearanceSettingsService.BackgroundPresets
                    .Where(item => !BackgroundPresetCatalog.IsDeveloperOnly(item.Preset) ||
                                   DeveloperToolsCommands.AccessState.IsEnabled)
                    .Select(item => (item.Preset.ToString(), item.DisplayName)).ToArray());
            SettingsComboBoxSelection.SelectByTag(BackgroundPresetComboBox, Settings.BackgroundPreset);
            PopulateTextColorPresets();
            SettingsComboBoxSelection.SelectByTag(TextColorPresetComboBox, Settings.TextColorPreset);
        }
        finally { _isLoading = wasLoading; }
    }

    private AppSettings Settings =>
        _settingsManager?.Current ?? throw new InvalidOperationException("The appearance settings control is not initialized.");

    private void SelectControls()
    {
        SettingsComboBoxSelection.SelectByTag(BackgroundPresetComboBox, Settings.BackgroundPreset);
        PopulateTextColorPresets();
        SettingsComboBoxSelection.SelectByTag(TextColorPresetComboBox, Settings.TextColorPreset);
    }

    private void BackgroundPresetComboBox_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading)
            return;

        Settings.BackgroundPreset = SettingsComboBoxSelection.GetSelectedTag(
            BackgroundPresetComboBox,
            AppearanceSettingsService.DefaultBackgroundPreset);
        AppearanceSettingsService.NormalizeRegular(Settings, DeveloperToolsCommands.AccessState.IsEnabled);

        _isLoading = true;
        try
        {
            PopulateTextColorPresets();
            SettingsComboBoxSelection.SelectByTag(TextColorPresetComboBox, Settings.TextColorPreset);
        }
        finally
        {
            _isLoading = false;
        }

        SaveAndApply();
    }

    private void TextColorPresetComboBox_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading)
            return;

        Settings.TextColorPreset = SettingsComboBoxSelection.GetSelectedTag(
            TextColorPresetComboBox,
            AppearanceSettingsService.DefaultTextColorPreset);
        SaveAndApply();
    }

    private void PopulateTextColorPresets()
    {
        var backgroundPreset = SettingsComboBoxSelection.GetSelectedTag(
            BackgroundPresetComboBox,
            Settings.BackgroundPreset);
        SettingsComboBoxSelection.Populate(
            TextColorPresetComboBox,
            AppearanceSettingsService.GetCompatibleTextColorPresets(backgroundPreset)
                .Select(item => (item.Preset.ToString(), item.DisplayName))
                .ToArray());
    }

    private void SaveAndApply()
    {
        AppearanceSettingsService.NormalizeRegular(Settings, DeveloperToolsCommands.AccessState.IsEnabled);
        _settingsManager!.SaveImmediate();
        ApplyAppearanceResources(Settings);
        _applyAppearanceSettings!(Settings);
    }

    private void ApplyAppearanceResources(AppSettings settings)
    {
        var resources = Application.Current?.Resources ?? Resources;
        AppearanceSettingsService.ApplyEffectiveToResources(resources, settings);
    }
}
