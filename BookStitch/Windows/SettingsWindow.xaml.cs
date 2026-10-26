using BookStitch.Controls;
using BookStitch.Models;
using BookStitch.Services;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace BookStitch;

public partial class SettingsWindow : Window
{
    private const string DefaultSettingsSection = "General";
    private const string LegacyTagsChaptersSection = "TagsChapters";

    private readonly GlobalSettingsManager _settingsManager;
    private readonly AppSettings _settings;
    private readonly string? _initialTabKey;
    private bool _isLoading = true;
    private bool _isSynchronizingNavigation;

    private IEnumerable<ListBoxItem> SettingsSections => SettingsNavigationList.Items.OfType<ListBoxItem>()
        .Concat(SettingsUtilityNavigationList.Items.OfType<ListBoxItem>());

    private ListBoxItem? SelectedSettingsSection =>
        SettingsNavigationList.SelectedItem as ListBoxItem ?? SettingsUtilityNavigationList.SelectedItem as ListBoxItem;

    private void SelectNavigationItem(ListBoxItem? item)
    {
        if (item?.Parent is ListBox list)
            list.SelectedItem = item;
    }

    public SettingsWindow(
        GlobalSettingsManager settingsManager,
        NotificationService notificationService,
        Func<int, ProjectCleanupResult> deleteOldProjects,
        Action<bool> setMergeAutomaticallyAfterConversion,
        Action<AppSettings> applyAppearanceSettings,
        Func<string> getOutputFolder,
        Action<Window> chooseOutputFolder,
        Action<Window> openOutputFolder,
        INotifyPropertyChanged outputFolderChangeSource,
        Action requestApplicationRestart,
        IReadOnlyList<TrackInfo>? chapterPreviewProjectTracks = null,
        string? chapterPreviewProjectBookTitle = null,
        string? chapterPreviewProjectAlbum = null,
        IReadOnlyDictionary<Guid, string>? chapterPreviewResolvedAutoContents = null,
        Func<ChapterPreviewData?>? chapterPreviewProjectDataProvider = null,
        Action? chapterSettingsChanged = null,
        string? initialTabKey = null,
        CoverPickerService? coverPickerService = null,
        string? projectRootFolder = null,
        ProjectIndexService? projectIndexService = null,
        ProjectResumePlanService? projectResumePlanService = null,
        Action<Window, bool>? checkProjectIntegrity = null,Func<Window,string,bool,Task<ProjectOperationOutcome>>? rebuildProject=null)
    {
        _settingsManager = settingsManager ?? throw new ArgumentNullException(nameof(settingsManager));
        _settings = _settingsManager.Current;
        _initialTabKey = initialTabKey;

        var projectTracks = chapterPreviewProjectTracks?.ToList() ?? [];
        var initialChapterPreviewData = projectTracks.Count == 0
            ? null
            : new ChapterPreviewData(
                chapterPreviewProjectBookTitle ?? string.Empty,
                chapterPreviewProjectAlbum ?? string.Empty,
                projectTracks,
                chapterPreviewResolvedAutoContents);
        var previewProvider = chapterPreviewProjectDataProvider ?? (() => initialChapterPreviewData);

        InitializeComponent();

        GeneralSettingsControl.Initialize(
            _settingsManager,
            notificationService,
            setMergeAutomaticallyAfterConversion,
            getOutputFolder,
            chooseOutputFolder,
            openOutputFolder,
            outputFolderChangeSource);
        ConversionSettingsControl.Initialize(_settingsManager);
        LanguageSettingsControl.Initialize(_settingsManager, requestApplicationRestart);
        ProjectManagementSettingsControl.Initialize(
            _settingsManager,
            deleteOldProjects,
            projectRootFolder ?? _settings.WorkingFolder ?? string.Empty,
            projectIndexService ?? new ProjectIndexService(),
            projectResumePlanService ?? new ProjectResumePlanService());
        TagsChaptersSettingsControl.Initialize(
            _settingsManager,
            previewProvider,
            chapterSettingsChanged,
            coverPickerService);
        GeneralSettingsControl.AppearanceSettingsControl.Initialize(_settingsManager, applyAppearanceSettings);
        ProjectManagementSettingsControl.ConfigureIntegrity(checkProjectIntegrity,rebuildProject);
        DriveSettingsControl.Initialize(_settingsManager);
        InfoSettingsControl.Initialize();

        if (!string.IsNullOrWhiteSpace(_initialTabKey))
            SelectSettingsSection(_initialTabKey);
        else
            RestoreLastSettingsSection();

        _isLoading = false;
    }

    private void RestoreLastSettingsSection()
    {
        var sectionKey = string.IsNullOrWhiteSpace(_settings.LastSettingsTab)
            ? DefaultSettingsSection
            : _settings.LastSettingsTab;
        SelectSettingsSection(sectionKey);
    }

    private void SelectSettingsSection(string sectionKey)
    {
        var normalizedKey = NormalizeSettingsSectionKey(sectionKey);
        var selected = FindVisibleSettingsSection(normalizedKey) ?? FindVisibleSettingsSection(DefaultSettingsSection);
        SelectNavigationItem(selected ?? SettingsSections.FirstOrDefault());
        ShowSelectedSettingsSection();
    }

    private static string NormalizeSettingsSectionKey(string? sectionKey) =>
        string.Equals(sectionKey, LegacyTagsChaptersSection, StringComparison.Ordinal)
            ? "Metadata"
            : sectionKey ?? string.Empty;

    private ListBoxItem? FindVisibleSettingsSection(string sectionKey) =>
        SettingsSections
            .FirstOrDefault(item =>
                item.Visibility == Visibility.Visible &&
                string.Equals(item.Tag?.ToString(), sectionKey, StringComparison.Ordinal));

    private void SettingsNavigationList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isSynchronizingNavigation || e.OriginalSource is not ListBox navigation ||
            (navigation != SettingsNavigationList && navigation != SettingsUtilityNavigationList) ||
            navigation.SelectedItem is not ListBoxItem)
            return;

        _isSynchronizingNavigation = true;
        try
        {
            (navigation == SettingsNavigationList ? SettingsUtilityNavigationList : SettingsNavigationList).SelectedItem = null;
        }
        finally { _isSynchronizingNavigation = false; }

        ShowSelectedSettingsSection();

        if (_isLoading || SelectedSettingsSection is not ListBoxItem { Tag: string sectionKey } ||
            string.Equals(_settings.LastSettingsTab, sectionKey, StringComparison.Ordinal))
        {
            return;
        }

        _settings.LastSettingsTab = sectionKey;
        _settingsManager.SaveImmediate();
    }

    private void ShowSelectedSettingsSection()
    {
        var sectionKey = SelectedSettingsSection?.Tag?.ToString() ?? DefaultSettingsSection;

        ConversionSettingsControl.Visibility = sectionKey == "Conversion" ? Visibility.Visible : Visibility.Collapsed;
        GeneralSettingsControl.Visibility = sectionKey == "General" ? Visibility.Visible : Visibility.Collapsed;
        LanguageSettingsControl.Visibility = sectionKey == "Language" ? Visibility.Visible : Visibility.Collapsed;
        ProjectManagementSettingsControl.Visibility = sectionKey == "ProjectManagement" ? Visibility.Visible : Visibility.Collapsed;
        DriveSettingsControl.Visibility = sectionKey == "Drive" ? Visibility.Visible : Visibility.Collapsed;
        InfoSettingsControl.Visibility = sectionKey == "Info" ? Visibility.Visible : Visibility.Collapsed;
        FaqSettingsControl.Visibility = sectionKey == "FAQ" ? Visibility.Visible : Visibility.Collapsed;

        var showTagsChapters = sectionKey is "Metadata" or "Chapters";
        TagsChaptersSettingsControl.Visibility = showTagsChapters ? Visibility.Visible : Visibility.Collapsed;
        if (showTagsChapters)
        {
            TagsChaptersSettingsControl.ShowSection(
                sectionKey == "Chapters"
                    ? SettingsTagsChaptersSection.Chapters
                    : SettingsTagsChaptersSection.Metadata);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void SettingsWindow_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close();
            return;
        }

        if (e.Handled || Keyboard.Modifiers != ModifierKeys.None)
            return;

        var direction = e.Key switch
        {
            Key.Left => -1,
            Key.Right => 1,
            _ => 0
        };
        if (direction == 0 || ShouldPreserveArrowKeyForFocusedInput(e.OriginalSource))
            return;

        if (NavigateVisibleSettingsSection(direction))
            e.Handled = true;
    }

    private bool NavigateVisibleSettingsSection(int direction)
    {
        var visibleSections = SettingsSections
            .Where(item => item.Visibility == Visibility.Visible)
            .ToArray();
        if (visibleSections.Length < 2)
            return false;

        var currentIndex = Array.IndexOf(visibleSections, SelectedSettingsSection);
        var nextIndex = currentIndex < 0
            ? direction > 0 ? 0 : visibleSections.Length - 1
            : (currentIndex + Math.Sign(direction) + visibleSections.Length) % visibleSections.Length;

        SelectNavigationItem(visibleSections[nextIndex]);
        visibleSections[nextIndex].Focus();
        return true;
    }

    private bool ShouldPreserveArrowKeyForFocusedInput(object? source)
    {
        var current = source as DependencyObject ?? Keyboard.FocusedElement as DependencyObject;
        while (current is not null)
        {
            if (ReferenceEquals(current, SettingsNavigationList) || ReferenceEquals(current, SettingsUtilityNavigationList))
                return false;

            if (current is TextBoxBase or PasswordBox or ComboBox or Slider or ScrollBar or Selector)
                return true;

            current = GetInputAncestor(current);
        }

        return false;
    }

    private static DependencyObject? GetInputAncestor(DependencyObject current)
    {
        if (current is Visual or System.Windows.Media.Media3D.Visual3D)
        {
            var visualParent = VisualTreeHelper.GetParent(current);
            if (visualParent is not null)
                return visualParent;
        }

        return current is FrameworkContentElement contentElement
            ? contentElement.Parent
            : LogicalTreeHelper.GetParent(current);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        ProjectManagementSettingsControl.CommitPendingEdits();
        TagsChaptersSettingsControl.CommitPendingEdits();
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        _settingsManager.Flush();
        base.OnClosed(e);
    }
}
