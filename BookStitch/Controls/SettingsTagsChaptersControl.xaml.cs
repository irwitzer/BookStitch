using BookStitch.Models;
using BookStitch.Presentation;
using BookStitch.Services;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace BookStitch.Controls;

public enum SettingsTagsChaptersSection
{
    Metadata,
    Chapters
}

public partial class SettingsTagsChaptersControl : UserControl
{
    private readonly ChapterSettingsPreviewService _chapterSettingsPreviewService = new();
    private readonly ChapterSettingsEditorService _chapterSettingsEditorService = new();
    private GlobalSettingsManager? _settingsManager;
    private Func<ChapterPreviewData?>? _chapterPreviewProjectDataProvider;
    private Action? _chapterSettingsChanged;
    private CoverPickerService? _coverPickerService;
    private bool _coverPickerSubscribed;
    private bool _isLoading = true;

    public SettingsTagsChaptersControl()
    {
        InitializeComponent();
        Loaded += SettingsTagsChaptersControl_Loaded;
        Unloaded += SettingsTagsChaptersControl_Unloaded;
    }

    public void Initialize(
        GlobalSettingsManager settingsManager,
        Func<ChapterPreviewData?> chapterPreviewProjectDataProvider,
        Action? chapterSettingsChanged,
        CoverPickerService? coverPickerService)
    {
        _settingsManager = settingsManager ?? throw new ArgumentNullException(nameof(settingsManager));
        _chapterPreviewProjectDataProvider = chapterPreviewProjectDataProvider ?? throw new ArgumentNullException(nameof(chapterPreviewProjectDataProvider));
        _chapterSettingsChanged = chapterSettingsChanged;
        _coverPickerService = coverPickerService;

        ConfigureChapterControls();
        ConfigureCoverWarningControls();
        SettingsComboBoxSelection.SelectByContent(OutputExtensionComboBox, Settings.DefaultOutputExtension);
        FileNameTemplateComboBox.Text = Settings.DefaultFileNameTemplate;
        SelectOutputFolderLayout();
        OverwriteWithoutAskingCheckBox.IsChecked = Settings.OverwriteFinalOutputWithoutAsking;
        SelectCoverWarningThreshold();
        SelectCoverTransparency();
        RememberLastCoverPickerFolderCheckBox.IsChecked =
            _coverPickerService?.RememberLastCoverPickerFolder ?? Settings.RememberLastCoverPickerFolder;
        SelectChapterControls();
        UpdateChapterCustomControlsVisibility();
        UpdateOutputFolderPreview();
        UpdateChapterPreview();
        _isLoading = false;
        RefreshGenreSettings();
    }

    public void CommitPendingEdits()
    {
        if (_settingsManager is not null)
            SaveFileNameTemplate();
    }

    public void ShowSection(SettingsTagsChaptersSection section)
    {
        var wasLoading = _isLoading;
        _isLoading = true;
        try { SelectCoverWarningThreshold(); SelectCoverTransparency(); }
        finally { _isLoading = wasLoading; }
        MetadataSettingsSection.Visibility = section == SettingsTagsChaptersSection.Metadata
            ? Visibility.Visible
            : Visibility.Collapsed;
        ChaptersSettingsSection.Visibility = section == SettingsTagsChaptersSection.Chapters
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private AppSettings Settings =>
        _settingsManager?.Current ?? throw new InvalidOperationException("The tags and chapters settings control is not initialized.");

    private void OutputExtensionComboBox_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading)
            return;

        var value = SettingsComboBoxSelection.GetText(OutputExtensionComboBox).Trim();
        Settings.DefaultOutputExtension = value is ".m4a" or ".m4b" ? value : ".m4a";
        _settingsManager!.SaveImmediate();
        UpdateOutputFolderPreview();
    }

    private void FileNameTemplateComboBox_Changed(object sender, SelectionChangedEventArgs e) => SaveFileNameTemplate();

    private void FileNameTemplateComboBox_LostFocus(object sender, RoutedEventArgs e) => SaveFileNameTemplate();

    private void SaveFileNameTemplate()
    {
        if (_isLoading)
            return;

        var value = SettingsComboBoxSelection.GetText(FileNameTemplateComboBox).Trim();
        if (string.IsNullOrWhiteSpace(value))
            value = "{Autor} - {Titel}";
        if (!string.Equals(Settings.DefaultFileNameTemplate, value, StringComparison.Ordinal))
        {
            Settings.DefaultFileNameTemplate = value;
            _settingsManager!.SaveImmediate();
        }
        UpdateOutputFolderPreview();
    }

    private void OutputFolderLayoutComboBox_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading)
            return;

        Settings.OutputFolderLayout = OutputFolderLayoutService.NormalizeLayout(
            SettingsComboBoxSelection.GetSelectedTag(OutputFolderLayoutComboBox, OutputFolderLayoutService.DefaultLayout));
        _settingsManager!.SaveImmediate();
        UpdateOutputFolderPreview();
    }

    private void OverwriteWithoutAskingCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_isLoading)
            return;

        Settings.OverwriteFinalOutputWithoutAsking = OverwriteWithoutAskingCheckBox.IsChecked == true;
        _settingsManager!.SaveImmediate();
    }

    private void ConfigureChapterControls()
    {
        ChapterSettingsHeadingTextBlock.Text = ChapterText("ChapterSettingsHeading");
        ChapterModeLabelTextBlock.Text = ChapterText("Mode");
        ChapterNumberingStyleLabelTextBlock.Text = ChapterText("NumberingStyle");
        ChapterPreviewDataSourceLabelTextBlock.Text = ChapterText("PreviewDataSource");
        ChapterCustomHeadingTextBlock.Text = ChapterText("CustomRules");

        SettingsComboBoxSelection.Populate(
            ChapterModeComboBox,
            ChapterSettingsEditorPresentation.Modes
                .Select(option => (option.Value, ChapterSettingsEditorPresentation.GetText(option.ResourceKey)))
                .ToArray());
        SettingsComboBoxSelection.Populate(
            ChapterNumberingStyleComboBox,
            ChapterSettingsEditorPresentation.NumberingStyles
                .Select(option => (option.Value, ChapterSettingsEditorPresentation.GetText(option.ResourceKey)))
                .ToArray());
        SettingsComboBoxSelection.Populate(
            ChapterPreviewDataSourceComboBox,
            (ChapterSettingsService.PreviewDataSourceSample, ChapterText("PreviewDataSample")),
            (ChapterSettingsService.PreviewDataSourceCurrentProject, ChapterText("PreviewDataCurrentProject")));

        var slots = ChapterSettingsEditorPresentation.Slots
            .Select(option => (option.Value, ChapterSettingsEditorPresentation.GetText(option.ResourceKey)))
            .ToArray();
        SettingsComboBoxSelection.Populate(ChapterSlot1ComboBox, slots);
        SettingsComboBoxSelection.Populate(ChapterSlot2ComboBox, slots);
        SettingsComboBoxSelection.Populate(ChapterSlot3ComboBox, slots);

        var separators = ChapterSettingsEditorPresentation.Separators
            .Select(option => (option.Value, ChapterSettingsEditorPresentation.GetText(option.ResourceKey)))
            .ToArray();
        SettingsComboBoxSelection.Populate(ChapterSeparator1ComboBox, separators);
        SettingsComboBoxSelection.Populate(ChapterSeparator2ComboBox, separators);
    }

    private void ChapterSettingsComboBox_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_isLoading)
            SaveChapterSettingsFromControls();
    }

    private void SaveChapterSettingsFromControls()
    {
        var current = _chapterSettingsEditorService.Read(Settings);
        _chapterSettingsEditorService.Apply(
            Settings,
            new ChapterSettingsEditorState(
                SettingsComboBoxSelection.GetSelectedTag(ChapterModeComboBox, current.Mode),
                SettingsComboBoxSelection.GetSelectedTag(ChapterNumberingStyleComboBox, current.NumberingStyle),
                SettingsComboBoxSelection.GetSelectedTag(ChapterPreviewDataSourceComboBox, current.PreviewDataSource),
                SettingsComboBoxSelection.GetSelectedTag(ChapterSlot1ComboBox, current.Slot1),
                SettingsComboBoxSelection.GetSelectedTag(ChapterSeparator1ComboBox, current.Separator1),
                SettingsComboBoxSelection.GetSelectedTag(ChapterSlot2ComboBox, current.Slot2),
                SettingsComboBoxSelection.GetSelectedTag(ChapterSeparator2ComboBox, current.Separator2),
                SettingsComboBoxSelection.GetSelectedTag(ChapterSlot3ComboBox, current.Slot3)));

        _isLoading = true;
        try
        {
            SelectChapterControls();
        }
        finally
        {
            _isLoading = false;
        }

        var persisted = _settingsManager!.SaveImmediate();
        UpdateChapterCustomControlsVisibility();
        UpdateChapterSlotOptionAvailability();
        if (persisted)
            _chapterSettingsChanged?.Invoke();
        UpdateChapterPreview();
    }

    private void SelectChapterControls()
    {
        var chapterSettings = ChapterSettingsService.Normalize(Settings.ChapterSettings, legacyUseLeadingZeros: false);
        Settings.ChapterSettings = chapterSettings;
        SettingsComboBoxSelection.SelectByTag(ChapterModeComboBox, chapterSettings.Mode);
        SettingsComboBoxSelection.SelectByTag(ChapterNumberingStyleComboBox, chapterSettings.NumberingStyle);
        if (_chapterPreviewProjectDataProvider!() is null &&
            string.Equals(chapterSettings.PreviewDataSource, ChapterSettingsService.PreviewDataSourceCurrentProject, StringComparison.Ordinal))
        {
            chapterSettings.PreviewDataSource = ChapterSettingsService.PreviewDataSourceSample;
        }
        SettingsComboBoxSelection.SelectByTag(ChapterPreviewDataSourceComboBox, chapterSettings.PreviewDataSource);
        SettingsComboBoxSelection.SelectByTag(ChapterSlot1ComboBox, chapterSettings.Slot1);
        SettingsComboBoxSelection.SelectByTag(ChapterSeparator1ComboBox, chapterSettings.Separator1);
        SettingsComboBoxSelection.SelectByTag(ChapterSlot2ComboBox, chapterSettings.Slot2);
        SettingsComboBoxSelection.SelectByTag(ChapterSeparator2ComboBox, chapterSettings.Separator2);
        SettingsComboBoxSelection.SelectByTag(ChapterSlot3ComboBox, chapterSettings.Slot3);
        UpdateChapterSlotOptionAvailability();
    }

    private void UpdateChapterCustomControlsVisibility()
    {
        var chapterSettings = ChapterSettingsService.Normalize(Settings.ChapterSettings, legacyUseLeadingZeros: false);
        Settings.ChapterSettings = chapterSettings;
        var mode = SettingsComboBoxSelection.GetSelectedTag(ChapterModeComboBox, chapterSettings.Mode);
        ChapterCustomSettingsPanel.Visibility = string.Equals(mode, ChapterSettingsService.ModeCustom, StringComparison.Ordinal)
            ? Visibility.Visible
            : Visibility.Collapsed;

        foreach (var item in ChapterPreviewDataSourceComboBox.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(item.Tag?.ToString(), ChapterSettingsService.PreviewDataSourceCurrentProject, StringComparison.Ordinal))
                item.IsEnabled = _chapterPreviewProjectDataProvider!() is not null;
        }
    }

    private void UpdateChapterSlotOptionAvailability()
    {
        var numberingStyle = SettingsComboBoxSelection.GetSelectedTag(
            ChapterNumberingStyleComboBox,
            Settings.ChapterSettings?.NumberingStyle ?? ChapterNumberingStyle.Plain.ToString());
        foreach (var comboBox in new[] { ChapterSlot1ComboBox, ChapterSlot2ComboBox, ChapterSlot3ComboBox })
        {
            foreach (var item in comboBox.Items.OfType<ComboBoxItem>())
            {
                var slotValue = item.Tag?.ToString() ?? string.Empty;
                item.IsEnabled = ChapterSettingsEditorPresentation.IsSlotOptionEnabled(slotValue, numberingStyle);
                var helpText = ChapterSettingsEditorPresentation.GetSlotOptionUnavailableHelpText(slotValue, numberingStyle);
                item.ToolTip = helpText;
                ToolTipService.SetShowOnDisabled(item, helpText is not null);
                AutomationProperties.SetHelpText(item, helpText ?? string.Empty);
            }
        }
    }

    private void UpdateChapterPreview()
    {
        ChapterPreviewTextBlock.Text = _chapterSettingsPreviewService.CreatePreview(
            Settings.ChapterSettings,
            legacyUseLeadingZeros: false,
            _chapterPreviewProjectDataProvider!(),
            maxItems: 5);
    }

    private void SelectCoverTransparency()
    {
        var mode = CoverTransparencySettingsService.NormalizeMode(Settings.CoverTransparencyMode);
        CoverTransparencyComboBox.SelectedItem = CoverTransparencyComboBox.Items.OfType<ComboBoxItem>()
            .First(item => string.Equals(item.Tag as string, mode, StringComparison.Ordinal));
    }

    private void CoverTransparencyComboBox_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading || _settingsManager is null) return;
        var selected = CoverTransparencyComboBox.SelectedItem as ComboBoxItem;
        Settings.CoverTransparencyMode = CoverTransparencySettingsService.NormalizeMode(selected?.Tag as string);
        _settingsManager.SaveImmediate();
    }

    private void ConfigureCoverWarningControls()
    {
        CoverWarningThresholdHeadingTextBlock.Text =
            LocalizationText.Get(CoverWarningSettingsPresentation.HeadingResourceKey);
        CoverWarningThresholdComboBox.Items.Clear();
        foreach (var option in CoverWarningSettingsPresentation.Options)
        {
            CoverWarningThresholdComboBox.Items.Add(new ComboBoxItem
            {
                Tag = option.ThresholdPixels,
                Content = LocalizationText.Get(option.ResourceKey)
            });
        }
    }

    private void CoverWarningThresholdComboBox_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading)
            return;

        var selected = CoverWarningThresholdComboBox.SelectedItem as ComboBoxItem;
        var threshold = int.TryParse(selected?.Tag?.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : CoverWarningSettingsService.DefaultThresholdPixels;
        Settings.CoverWarningThresholdPixels = CoverWarningSettingsService.NormalizeThresholdPixels(threshold);
        _settingsManager!.SaveImmediate();
    }

    private void SelectCoverWarningThreshold()
    {
        var threshold = CoverWarningSettingsService.NormalizeThresholdPixels(Settings.CoverWarningThresholdPixels);
        CoverWarningThresholdComboBox.SelectedItem = CoverWarningThresholdComboBox.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(item => int.TryParse(item.Tag?.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value == threshold);
        if (CoverWarningThresholdComboBox.SelectedItem is null)
            CoverWarningThresholdComboBox.SelectedIndex = 1;
    }

    private void RememberLastCoverPickerFolderCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_isLoading)
            return;

        if (_coverPickerService is not null)
            _coverPickerService.RememberLastCoverPickerFolder = RememberLastCoverPickerFolderCheckBox.IsChecked == true;
        else
        {
            Settings.RememberLastCoverPickerFolder = RememberLastCoverPickerFolderCheckBox.IsChecked == true;
            _settingsManager!.SaveImmediate();
        }
    }

    private void SettingsTagsChaptersControl_Loaded(object sender, RoutedEventArgs e)
    {
        SubscribeGenreSettings();
        if (_coverPickerService is null || _coverPickerSubscribed)
            return;

        _coverPickerService.PropertyChanged += CoverPickerService_PropertyChanged;
        _coverPickerSubscribed = true;
        _isLoading = true;
        RememberLastCoverPickerFolderCheckBox.IsChecked = _coverPickerService.RememberLastCoverPickerFolder;
        _isLoading = false;
        RefreshGenreSettings();
    }

    private void SettingsTagsChaptersControl_Unloaded(object sender, RoutedEventArgs e)
    {
        UnsubscribeGenreSettings();
        if (_coverPickerService is null || !_coverPickerSubscribed)
            return;

        _coverPickerService.PropertyChanged -= CoverPickerService_PropertyChanged;
        _coverPickerSubscribed = false;
    }

    private void CoverPickerService_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(CoverPickerService.RememberLastCoverPickerFolder) && !string.IsNullOrEmpty(e.PropertyName))
            return;

        Dispatcher.InvokeAsync(() =>
        {
            _isLoading = true;
            RememberLastCoverPickerFolderCheckBox.IsChecked = _coverPickerService?.RememberLastCoverPickerFolder == true;
            _isLoading = false;
        });
    }

    private void SelectOutputFolderLayout()
    {
        var selected = OutputFolderLayoutComboBox.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(item => string.Equals(item.Tag?.ToString(), Settings.OutputFolderLayout, StringComparison.OrdinalIgnoreCase));
        OutputFolderLayoutComboBox.SelectedItem = selected;
        if (selected is null)
            OutputFolderLayoutComboBox.SelectedIndex = 3;
    }

    private void UpdateOutputFolderPreview()
    {
        var baseOutputFolder = string.IsNullOrWhiteSpace(Settings.OutputFolder)
            ? LocalizationText.Get("Settings.Preview.OutputFolder")
            : Settings.OutputFolder.Trim();
        var author = LocalizationText.Get("Settings.Preview.Author");
        var title = LocalizationText.Get("Settings.Preview.Title");
        var album = LocalizationText.Get("Settings.Preview.Album");
        var narrator = LocalizationText.Get("Settings.Preview.Narrator");
        var series = LocalizationText.Get("Settings.Preview.Series");
        var template = string.IsNullOrWhiteSpace(Settings.DefaultFileNameTemplate)
            ? "{Autor} - {Titel}"
            : Settings.DefaultFileNameTemplate;
        var extension = Settings.DefaultOutputExtension is ".m4a" or ".m4b"
            ? Settings.DefaultOutputExtension
            : ".m4a";
        var fileName = FileNameTemplateService.BuildOutputFileName(title, author, narrator, template, extension, album, series);
        var outputPath = new OutputFolderLayoutService().BuildOutputPath(
            baseOutputFolder,
            author,
            title,
            fileName,
            Settings.OutputFolderLayout,
            album,
            series);
        var outputFolder = Path.GetDirectoryName(outputPath) ?? string.Empty;
        var displayFolder = string.IsNullOrWhiteSpace(outputFolder) ? baseOutputFolder : outputFolder;
        var separator = string.IsNullOrWhiteSpace(displayFolder) || displayFolder.EndsWith(Path.DirectorySeparatorChar)
            ? string.Empty
            : Path.DirectorySeparatorChar.ToString();

        OutputFolderPreviewTextBlock.Inlines.Clear();
        OutputFolderPreviewTextBlock.Inlines.Add(new Run(displayFolder + separator));
        OutputFolderPreviewTextBlock.Inlines.Add(new Run(fileName)
        {
            Foreground = (Brush)FindResource("AccentForegroundBrush"),
            FontWeight = FontWeights.SemiBold
        });
    }

    private static string ChapterText(string key) =>
        LocalizationText.Get(key);
}
