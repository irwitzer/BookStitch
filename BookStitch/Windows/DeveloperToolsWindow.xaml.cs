using BookStitch.Dialog;
using BookStitch.Models;
using BookStitch.Services;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace BookStitch;

public partial class DeveloperToolsWindow : Window
{
    private readonly GlobalSettingsManager _settingsManager;
    private readonly AppSettings _settings;
    private readonly string _projectRootFolder;
    private readonly ProjectIndexService _projectIndexService;
    private readonly DebugSettings _debugSettings;
    private readonly DebugSettingsService _debugSettingsService;
    private readonly BookStitchEnvironmentContext _environmentContext;
    private readonly ProjectEnvironmentSelectionService _projectEnvironmentSelectionService;
    private readonly Action _requestApplicationRestart;
    private readonly DebugWindowPlacementService _debugWindowPlacementService;
    private readonly NotificationService _notificationService;
    private readonly Action<bool> _setMetadataPanelExpanded;
    private readonly Action<bool> _setForceShowFfmpegSetupButton;
    private readonly Action<bool> _setPerformanceDiagnosticsEnabled;
    private readonly Action<int> _setTrackGridRowHeightLevel;
    private readonly Action<string> _setBackgroundPreset;
    private readonly Action<string> _setTextColorPreset;
    private readonly Action _refreshGenreOptions;
    private readonly Func<Task> _previewDiscSourceAnalysis;
    private readonly Func<string> _getPipelineStateText;
    private readonly Func<string> _getMainStatusText;
    private readonly Func<string> _getProgressStatusText;
    private readonly Func<ChapterDebugSnapshot> _getChapterDebugSnapshot;
    private readonly Func<MetadataDebugSnapshot> _getMetadataDebugSnapshot;
    private readonly DeveloperDiscTestService _developerDiscTestService = new();
    private readonly DiscDriveService _developerDiscDriveService = new();
    private readonly Mp3DiscImportService _developerMp3DiscImportService = new();
    private readonly IDiscWaitDialogService _developerDiscWaitDialogService;
    private readonly DispatcherTimer _liveStateTimer;
    private readonly WindowMetricsPresentationService _windowMetricsPresentationService = new();
    private CancellationTokenSource? _developerDiscTestCancellation;
    private CancellationTokenSource? _delayedFocusTestCancellation;
    private bool _metadataPanelExpandedPreview;
    private readonly List<BookStitch.Presentation.DeveloperImagePreviewRow> _imageRows = [];
    private bool _isLoading;

    private readonly Action<int, bool>? _startProgressPreview;
    private readonly Action? _stopProgressPreview;

    public DeveloperToolsWindow(
        GlobalSettingsManager settingsManager,
        string projectRootFolder,
        DebugSettings debugSettings,
        DebugSettingsService debugSettingsService,
        BookStitchEnvironmentContext environmentContext,
        ProjectEnvironmentSelectionService projectEnvironmentSelectionService,
        Action requestApplicationRestart,
        NotificationService notificationService,
        bool metadataPanelExpanded,
        Action<bool> setMetadataPanelExpanded,
        Action<bool> setForceShowFfmpegSetupButton,
        Action<bool> setPerformanceDiagnosticsEnabled,
        Action<int> setTrackGridRowHeightLevel,
        Action<string> setBackgroundPreset,
        Action<string> setTextColorPreset,
        Action refreshGenreOptions,
        Func<Task> previewDiscSourceAnalysis,
        Func<string> getPipelineStateText,
        Func<string> getMainStatusText,
        Func<string> getProgressStatusText,
        Func<ChapterDebugSnapshot> getChapterDebugSnapshot,
        Func<MetadataDebugSnapshot> getMetadataDebugSnapshot,
        ProjectIndexService? projectIndexService = null,
        Action<int, bool>? startProgressPreview = null,
        Action? stopProgressPreview = null)
    {
        _startProgressPreview = startProgressPreview;
        _stopProgressPreview = stopProgressPreview;
        _settingsManager = settingsManager ?? throw new ArgumentNullException(nameof(settingsManager));
        _settings = _settingsManager.Current;
        _projectRootFolder = projectRootFolder ?? throw new ArgumentNullException(nameof(projectRootFolder));
        _projectIndexService = projectIndexService ?? new ProjectIndexService();
        _debugSettings = debugSettings ?? throw new ArgumentNullException(nameof(debugSettings));
        _debugSettingsService = debugSettingsService ?? throw new ArgumentNullException(nameof(debugSettingsService));
        _environmentContext = environmentContext ?? throw new ArgumentNullException(nameof(environmentContext));
        _projectEnvironmentSelectionService = projectEnvironmentSelectionService ?? throw new ArgumentNullException(nameof(projectEnvironmentSelectionService));
        _requestApplicationRestart = requestApplicationRestart ?? throw new ArgumentNullException(nameof(requestApplicationRestart));
        _debugWindowPlacementService = new DebugWindowPlacementService(_debugSettings, _debugSettingsService);
        _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
        _metadataPanelExpandedPreview = metadataPanelExpanded;
        _setMetadataPanelExpanded = setMetadataPanelExpanded ?? throw new ArgumentNullException(nameof(setMetadataPanelExpanded));
        _setForceShowFfmpegSetupButton = setForceShowFfmpegSetupButton ?? throw new ArgumentNullException(nameof(setForceShowFfmpegSetupButton));
        _setPerformanceDiagnosticsEnabled = setPerformanceDiagnosticsEnabled ?? throw new ArgumentNullException(nameof(setPerformanceDiagnosticsEnabled));
        _setTrackGridRowHeightLevel = setTrackGridRowHeightLevel ?? throw new ArgumentNullException(nameof(setTrackGridRowHeightLevel));
        _setBackgroundPreset = setBackgroundPreset ?? throw new ArgumentNullException(nameof(setBackgroundPreset));
        _setTextColorPreset = setTextColorPreset ?? throw new ArgumentNullException(nameof(setTextColorPreset));
        _refreshGenreOptions = refreshGenreOptions ?? throw new ArgumentNullException(nameof(refreshGenreOptions));
        _previewDiscSourceAnalysis = previewDiscSourceAnalysis ?? throw new ArgumentNullException(nameof(previewDiscSourceAnalysis));
        _getPipelineStateText = getPipelineStateText ?? throw new ArgumentNullException(nameof(getPipelineStateText));
        _getMainStatusText = getMainStatusText ?? throw new ArgumentNullException(nameof(getMainStatusText));
        _getProgressStatusText = getProgressStatusText ?? throw new ArgumentNullException(nameof(getProgressStatusText));
        _getChapterDebugSnapshot = getChapterDebugSnapshot ?? throw new ArgumentNullException(nameof(getChapterDebugSnapshot));
        _getMetadataDebugSnapshot = getMetadataDebugSnapshot ?? throw new ArgumentNullException(nameof(getMetadataDebugSnapshot));
        _developerDiscWaitDialogService = new BoxedDiscWaitDialogService();
        _liveStateTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        _liveStateTimer.Tick += (_, _) => RefreshLiveState();

        _isLoading = true;
        InitializeComponent();
        ProjectSetupPageNavigationBridge.StateChanged += ProjectSetupPageNavigationBridge_StateChanged;
        ApplyAppearanceResources();
        _debugWindowPlacementService.Attach(this, DebugWindowPlacementTarget.DeveloperTools);
        ConfigureControls();
        WeakEventManager<DeveloperAppearancePreview, EventArgs>.AddHandler(DeveloperToolsCommands.AppearancePreview,
            nameof(DeveloperAppearancePreview.Changed), PreviewAppearanceChanged);
        RestoreLastTab();
        RefreshLiveState();
        _isLoading = false;

        Loaded += (_, _) => _liveStateTimer.Start();
        _settingsManager.GenreOptionsChanged += GenreOptionsChanged;
        Closed += (_, _) => Cleanup();
    }

    private void ConfigureControls()
    {
        MetadataAnimationMillisecondsTextBox.Text = AnimationTimingSettingsService
            .GetMetadataPanelMilliseconds(_debugSettings)
            .ToString(CultureInfo.InvariantCulture);
        ProjectSetupPageAnimationMillisecondsTextBox.Text = AnimationTimingSettingsService
            .GetProjectSetupPageMilliseconds(_debugSettings)
            .ToString(CultureInfo.InvariantCulture);
        ForceShowFfmpegSetupButtonCheckBox.IsChecked = _debugSettings.ForceShowFfmpegSetupButton;
        PerformanceDiagnosticsEnabledCheckBox.IsChecked = _debugSettings.PerformanceDiagnosticsEnabled;
        PrivateGenreListCheckBox.IsChecked = _settings.UsePrivateGenreList;

        SelectEventProfileComboBoxItem();
        SelectSoundLibraryComboBoxItem();
        SelectFocusProfileComboBoxItem();
        DevSoundVolumeSlider.Value = _settings.SoundVolumePercent;
        UpdateSoundVolumeText();
        UpdateMetadataPanelPreviewButtonText();
        ConfigureAppearanceControls();
        ConfigureProjectEnvironmentControls();
        UpdateProjectSetupPagePreviewButtons();
    }

    private void ConfigureProjectEnvironmentControls()
    {
        ActiveProjectEnvironmentTextBlock.Text = _environmentContext.DisplayName;

        var hasRepository = !string.IsNullOrWhiteSpace(_environmentContext.RepositoryRoot);
        ProjectEnvironmentComboBox.IsEnabled = hasRepository;
        ProjectEnvironmentAvailabilityTextBlock.Text = hasRepository
            ? LocalizationText.Get("Developer.EnvironmentNextStart")
            : LocalizationText.Get("Developer.EnvironmentRepositoryOnly");

        var selectedEnvironment = hasRepository
            ? _projectEnvironmentSelectionService.Load(_environmentContext.RepositoryRoot!)
            : BookStitchEnvironment.Production;
        SelectProjectEnvironment(selectedEnvironment);
    }

    private void SelectProjectEnvironment(BookStitchEnvironment environment)
    {
        foreach (var item in ProjectEnvironmentComboBox.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(item.Tag?.ToString(), environment.ToString(), StringComparison.Ordinal))
            {
                ProjectEnvironmentComboBox.SelectedItem = item;
                return;
            }
        }

        ProjectEnvironmentComboBox.SelectedIndex = 0;
    }

    private void ProjectEnvironmentComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading || string.IsNullOrWhiteSpace(_environmentContext.RepositoryRoot))
            return;

        if (ProjectEnvironmentComboBox.SelectedItem is not ComboBoxItem { Tag: string tag } ||
            !Enum.TryParse<BookStitchEnvironment>(tag, ignoreCase: false, out var selectedEnvironment))
        {
            return;
        }

        try
        {
            _projectEnvironmentSelectionService.Save(_environmentContext.RepositoryRoot, selectedEnvironment);
        }
        catch (Exception ex)
        {
            var persistedEnvironment = _projectEnvironmentSelectionService.Load(_environmentContext.RepositoryRoot);
            _isLoading = true;
            try
            {
                SelectProjectEnvironment(persistedEnvironment);
            }
            finally
            {
                _isLoading = false;
            }

            AppDialogService.Show(
                this,
                LocalizationText.Get("Developer.EnvironmentSaveFailed"),
                LocalizationText.Get("Developer.EnvironmentUnchanged"),
                LocalizationText.Get("Developer.EnvironmentKept"),
                AppDialogKind.Error,
                [ex.Message],
                [new AppDialogButton("OK", AppDialogResult.Ok, IsPrimary: true, IsDefault: true)]);
        }
    }

    private void RestartBookStitchButton_Click(object sender, RoutedEventArgs e)
    {
        _requestApplicationRestart();
    }

    private void RestoreLastTab()
    {
        DeveloperNavigationList.SelectedItem = DeveloperNavigationList.Items.OfType<ListBoxItem>()
            .FirstOrDefault(item => string.Equals(item.Tag?.ToString(), _debugSettings.LastTab, StringComparison.Ordinal))
            ?? DeveloperNavigationList.Items.OfType<ListBoxItem>().First(item => Equals(item.Tag, "Status"));
        ShowSelectedSection();
    }

    private void DeveloperNavigationList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.Source != DeveloperNavigationList || DeveloperNavigationList.SelectedItem is not ListBoxItem item)
            return;
        ShowSelectedSection();
        if (_isLoading)
            return;
        _debugSettings.LastTab = item.Tag?.ToString() ?? "Status";
        _debugSettingsService.Save(_debugSettings);
    }

    private void ShowSelectedSection()
    {
        if (DeveloperContentHost is null)
            return;
        var selected = (DeveloperNavigationList.SelectedItem as ListBoxItem)?.Tag?.ToString();
        foreach (var section in DeveloperContentHost.Children.OfType<FrameworkElement>())
            section.Visibility = string.Equals(section.Tag?.ToString(), selected, StringComparison.Ordinal)
                ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RefreshLiveState()
    {
        UpdateProjectSetupPagePreviewButtons();
        PipelineStateTextBlock.Text = NormalizeLiveText(_getPipelineStateText());
        MainStatusTextBlock.Text = NormalizeLiveText(_getMainStatusText());
        ProgressStatusTextBlock.Text = NormalizeLiveText(_getProgressStatusText());
        RefreshWindowMetrics();
        ChapterDebugTextBlock.Text = FormatChapterDebug(_getChapterDebugSnapshot());
        MetadataDebugTextBox.Text = FormatMetadataDebug(_getMetadataDebugSnapshot());
    }

    private void RefreshWindowMetrics()
    {
        // Application.Windows is dispatcher-bound; secondary UI dispatchers can only inspect their own windows.
        var windows = Application.Current?.Dispatcher.CheckAccess() == true
            ? Application.Current.Windows.OfType<Window>()
            : new[] { this };
        var mainWindow = Application.Current?.Dispatcher.CheckAccess() == true
            ? Application.Current.MainWindow : null;
        var candidates = windows
            .Where(window => window.Dispatcher.CheckAccess())
            .Where(window => window.GetType().Assembly == typeof(DeveloperToolsWindow).Assembly)
            .Select(window => new WindowMetricsCandidate(
                string.IsNullOrWhiteSpace(window.Title) ? window.GetType().Name : window.Title,
                window.ActualWidth, window.ActualHeight,
                window.MinWidth, window.MinHeight,
                window.MaxWidth, window.MaxHeight,
                ReferenceEquals(window, mainWindow),
                window is DeveloperToolsWindow,
                window.IsActive,
                window.GetType().Name))
            .ToList();
        var presentation = _windowMetricsPresentationService.Update(candidates);
        MainWindowMetricsTextBlock.Text = presentation.MainWindowText;
        ActiveWindowMetricsTextBlock.Text = presentation.SecondaryWindowText;
    }

    private static string FormatMetadataDebug(MetadataDebugSnapshot snapshot)
    {
        return string.Join(
            Environment.NewLine + Environment.NewLine,
            snapshot.Fields.Select(FormatMetadataField));
    }

    private static string FormatMetadataField(MetadataFieldDecisionDiagnostic field)
    {
        var winner = field.Winner;
        var runner = field.RunnerUp;
        return string.Join(Environment.NewLine,
            FormatMetadataFieldName(field.Field),
            $"Kanonischer Wert: {DisplayOrDash(field.CanonicalValue)}",
            $"Origin: {field.Origin}",
            $"Entscheidung: {field.DecisionReason}",
            $"Winner Evidence: {winner?.EvidenceType.ToString() ?? "–"}",
            $"Winner Support: {FormatSupport(winner)}",
            $"Runner-up Wert: {DisplayOrDash(runner?.Value)}",
            $"Runner-up Evidence: {runner?.EvidenceType.ToString() ?? "–"}",
            $"Runner-up Support: {FormatSupport(runner)}",
            $"Verwerfungsgrund: {runner?.RejectedReason ?? "–"}");
    }

    private static string FormatSupport(MetadataDecisionCandidateDiagnostic? candidate) =>
        candidate?.SupportCount is { } support && candidate.EvidenceCount is { } total
            ? $"{support} / {total} = {FormatRatio(candidate.SupportRatio)}"
            : "–";

    private static string DisplayOrDash(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "–" : value;

    private static string FormatMetadataFieldName(ProjectMetadataField field) => field switch
    {
        ProjectMetadataField.Authors => "Author",
        ProjectMetadataField.Narrators => "Narrator / Interpret",
        _ => field.ToString()
    };

    private static string FormatChapterDebug(ChapterDebugSnapshot snapshot)
    {
        var resolved = snapshot.Resolved;
        var winner = snapshot.Winner;
        if (resolved is null || winner is null)
        {
            return $"Aktiver Modus: {snapshot.ActiveMode}\n\n" +
                   LocalizationText.Get("Developer.NoResolveResult");
        }

        var runner = snapshot.RunnerUp;
        var summary = snapshot.SourceSummary;
        return string.Join(Environment.NewLine,
            $"Aktiver Modus: {snapshot.ActiveMode}",
            $"Resolved Text: {resolved.Text}",
            string.Empty,
            "Winner",
            $"Source: {winner.Source}",
            $"Quality: {winner.Quality}",
            $"Metadata Origin: {winner.MetadataOrigin}",
            $"DecisionReason: {resolved.DecisionReason}",
            $"EvidenceStrength: {FormatRatio(winner.EvidenceStrength)}",
            string.Empty,
            "Runner-up",
            $"Source: {runner?.Source.ToString() ?? "–"}",
            $"Quality: {runner?.Quality.ToString() ?? "–"}",
            $"Metadata Origin: {runner?.MetadataOrigin.ToString() ?? "–"}",
            $"Text: {runner?.Text ?? "–"}",
            string.Empty,
            "Textstufen",
            $"RawText: {winner.RawText ?? "–"}",
            $"NormalizedText: {winner.NormalizedText}",
            $"AnalysisText: {winner.AnalysisText}",
            $"OutputBaseText: {winner.OutputBaseText}",
            string.Empty,
            "Scope",
            $"ScopeId: {winner.ScopeId}",
            $"ValidMeaningfulCount: {summary?.ValidMeaningfulCount.ToString(CultureInfo.InvariantCulture) ?? "–"}",
            $"DominantCount: {summary?.DominantCount.ToString(CultureInfo.InvariantCulture) ?? "–"}",
            $"Dominante Gruppe: {FormatDominantGroup(snapshot)}",
            $"GroupSupportRatio: {FormatRatio(winner.GroupSupportRatio)}",
            $"DeviationRatio: {FormatRatio(winner.DeviationRatio)}",
            "Individual-Schwelle: 40 %",
            "Fuzzy-Dominanzschwelle: 60 %",
            "Fuzzy-Cap: 10 %",
            $"EvidenceStrength: {FormatRatio(winner.EvidenceStrength)}");
    }

    private static string FormatDominantGroup(ChapterDebugSnapshot snapshot)
    {
        var valid = snapshot.SourceSummary?.ValidMeaningfulCount ?? 0;
        if (valid == 0 || snapshot.DominantGroupCount == 0)
            return "–";

        return $"\"{snapshot.DominantGroupText}\" · {snapshot.DominantGroupCount} / {valid} = " +
               FormatRatio((double)snapshot.DominantGroupCount / valid);
    }

    private static string FormatRatio(double? value) => value is null
        ? "–"
        : value.Value.ToString("P1", CultureInfo.CurrentCulture);

    private static string NormalizeLiveText(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "–" : value.Trim();

    private void ConfigureAppearanceControls()
    {
        ConfigureAppearancePresetControls();
        ConfigureComboBox(
            TrackGridRowHeightComboBox,
            TrackGridRowHeightService.Options.Select(item => (
                item.Level.ToString(CultureInfo.InvariantCulture),
                $"{item.DisplayName} ({item.RowHeight:0} DIP)")));
        SelectComboBoxItemByTag(
            TrackGridRowHeightComboBox,
            _debugSettings.TrackGridRowHeightLevel.ToString(CultureInfo.InvariantCulture));
        DeveloperToolsPlacementCheckBox.IsChecked = _debugWindowPlacementService.IsRememberEnabled(DebugWindowPlacementTarget.DeveloperTools);
        GlobalSettingsPlacementCheckBox.IsChecked = _debugWindowPlacementService.IsRememberEnabled(DebugWindowPlacementTarget.GlobalSettings);
        ProjectSetupPlacementCheckBox.IsChecked = _debugWindowPlacementService.IsRememberEnabled(DebugWindowPlacementTarget.ProjectSetup);
        MetadataDialogPlacementCheckBox.IsChecked = _debugWindowPlacementService.IsRememberEnabled(DebugWindowPlacementTarget.MetadataDialog);
        ResumeProjectPlacementCheckBox.IsChecked = _debugWindowPlacementService.IsRememberEnabled(DebugWindowPlacementTarget.ResumeProject);
    }

    private void ConfigureAppearancePresetControls()
    {
        ConfigureComboBox(TrackOrderVariantComboBox, new[] {
            ("Png", LocalizationText.Get("Developer.Images.ClassicPng")),
            ("Vector", LocalizationText.Get("Developer.Images.ClassicVectors")) });
        SelectComboBoxItemByTag(TrackOrderVariantComboBox, DeveloperToolsCommands.AppearancePreview.TrackOrderVectors ? "Vector" : "Png");
        var effective = AppearanceSettingsService.GetEffectiveSettings(_settings);
        foreach (var combo in new[] { BackgroundPresetComboBox, ImagesBackgroundComboBox })
        {
            ConfigureComboBox(combo, AppearanceSettingsService.BackgroundPresets
                .Where(item => DeveloperToolsCommands.AccessState.IsEnabled || !BackgroundPresetCatalog.IsDeveloperOnly(item.Preset))
                .Select(item => (item.Preset.ToString(), item.DisplayName)));
            SelectComboBoxItemByTag(combo, effective.BackgroundPreset);
        }
        foreach (var combo in new[] { TextColorPresetComboBox, ImagesTextColorComboBox })
        {
            ConfigureComboBox(combo, AppearanceSettingsService.GetCompatibleTextColorPresets(effective.BackgroundPreset)
                .Select(item => (item.Preset.ToString(), item.DisplayName)));
            SelectComboBoxItemByTag(combo, effective.TextColorPreset);
        }
        if (_imageRows.Count == 0)
        {
            _imageRows.AddRange(AppearanceAssetCatalog.PreviewRoles.Select(role =>
                new BookStitch.Presentation.DeveloperImagePreviewRow(role, DeveloperToolsCommands.AppearancePreview)));
            ImagePreviewRolesItemsControl.ItemsSource = _imageRows;
        }
        foreach (var row in _imageRows) row.Refresh(AppearanceSettingsService.GetEffectiveMode(_settings));
    }

    private void TrackOrderVariantComboBox_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading) return;
        DeveloperToolsCommands.AppearancePreview.SetTrackOrderVectors(
            (TrackOrderVariantComboBox.SelectedItem as ComboBoxItem)?.Tag as string == "Vector");
    }

    private void PreviewAppearanceChanged(object? sender, EventArgs e)
    {
        if (Dispatcher.CheckAccess() && IsLoaded) SynchronizeAppearanceControls();
    }

    internal void SynchronizeAppearanceControls()
    {
        var wasLoading = _isLoading;
        _isLoading = true;
        try
        {
            ConfigureAppearancePresetControls();
        }
        finally
        {
            _isLoading = wasLoading;
        }
    }

    private void BackgroundPresetComboBox_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading)
            return;

        _setBackgroundPreset(GetSelectedTag(
            (ComboBox)sender,
            AppearanceSettingsService.DefaultBackgroundPreset));
    }

    private void TextColorPresetComboBox_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading)
            return;

        _setTextColorPreset(GetSelectedTag(
            (ComboBox)sender,
            AppearanceSettingsService.DefaultTextColorPreset));
    }

    private void TrackGridRowHeightComboBox_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading)
            return;

        var selected = GetSelectedIntTag(
            TrackGridRowHeightComboBox,
            DebugSettings.DefaultTrackGridRowHeightLevel);
        _setTrackGridRowHeightLevel(TrackGridRowHeightService.NormalizeLevel(selected));
    }

    private void DialogPlacementRememberCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_isLoading ||
            sender is not CheckBox { Tag: string targetText } checkBox ||
            !Enum.TryParse<DebugWindowPlacementTarget>(targetText, out var target))
        {
            return;
        }

        _debugWindowPlacementService.SetRememberEnabled(target, checkBox.IsChecked == true);
    }

    private void ConfigureTextColorPresetComboBox()
    {
        var backgroundPreset = GetSelectedTag(BackgroundPresetComboBox, _settings.BackgroundPreset);
        ConfigureComboBox(
            TextColorPresetComboBox,
            AppearanceSettingsService.GetCompatibleTextColorPresets(backgroundPreset)
                .Select(item => (item.Preset.ToString(), item.DisplayName)));
    }

    private void ApplyAppearanceResources()
    {
        var resources = Application.Current?.Resources ?? Resources;
        AppearanceSettingsService.ApplyEffectiveToResources(resources, _settings);
    }

    private static void ConfigureComboBox(ComboBox comboBox, IEnumerable<(string Tag, string DisplayName)> items)
    {
        comboBox.Items.Clear();
        foreach (var (tag, displayName) in items)
        {
            comboBox.Items.Add(new ComboBoxItem
            {
                Tag = tag,
                Content = displayName
            });
        }
    }

    private static void SelectComboBoxItemByTag(ComboBox comboBox, string? tag)
    {
        comboBox.SelectedItem = comboBox.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(item => string.Equals(item.Tag?.ToString(), tag, StringComparison.OrdinalIgnoreCase));
        if (comboBox.SelectedItem is null && comboBox.Items.Count > 0)
            comboBox.SelectedIndex = 0;
    }

    private static string GetSelectedTag(ComboBox comboBox, string fallback) =>
        comboBox.SelectedItem is ComboBoxItem item && !string.IsNullOrWhiteSpace(item.Tag?.ToString())
            ? item.Tag!.ToString()!
            : fallback;

    private static int GetSelectedIntTag(ComboBox comboBox, int fallback) =>
        comboBox.SelectedItem is ComboBoxItem item &&
        int.TryParse(item.Tag?.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : fallback;

    private void PerformanceDiagnosticsEnabledCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_isLoading || sender is not CheckBox checkBox)
            return;

        _debugSettings.PerformanceDiagnosticsEnabled = checkBox.IsChecked == true;
        _debugSettingsService.Save(_debugSettings);
        _setPerformanceDiagnosticsEnabled(_debugSettings.PerformanceDiagnosticsEnabled);
    }

    private void ForceShowFfmpegSetupButtonCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_isLoading || sender is not CheckBox checkBox)
            return;

        _debugSettings.ForceShowFfmpegSetupButton = checkBox.IsChecked == true;
        _debugSettingsService.Save(_debugSettings);
        _setForceShowFfmpegSetupButton(_debugSettings.ForceShowFfmpegSetupButton);
    }

    private void GenreOptionsChanged(object? sender, EventArgs e)
    {
        var wasLoading = _isLoading;
        _isLoading = true;
        try { PrivateGenreListCheckBox.IsChecked = _settings.UsePrivateGenreList; }
        finally { _isLoading = wasLoading; }
    }

    private void PrivateGenreListCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_isLoading || sender is not CheckBox checkBox)
            return;

        _settingsManager.SetPrivateGenres(checkBox.IsChecked == true);
    }

    private void MetadataAnimationMillisecondsTextBox_LostFocus(object sender, RoutedEventArgs e) =>
        SaveMetadataAnimationMilliseconds();

    private void MetadataAnimationMillisecondsTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;

        SaveMetadataAnimationMilliseconds();
        e.Handled = true;
    }

    private void SaveMetadataAnimationMilliseconds()
    {
        if (_isLoading)
            return;

        if (!int.TryParse(MetadataAnimationMillisecondsTextBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            value = AnimationTimingSettingsService.MetadataPanelDefaultMilliseconds;

        _debugSettings.MetadataPanelAnimationMillisecondsOverride =
            AnimationTimingSettingsService.ToMetadataPanelOverride(value);
        MetadataAnimationMillisecondsTextBox.Text = AnimationTimingSettingsService
            .GetMetadataPanelMilliseconds(_debugSettings)
            .ToString(CultureInfo.InvariantCulture);
        _debugSettingsService.Save(_debugSettings);
    }

    private void ProjectSetupPageAnimationMillisecondsTextBox_LostFocus(object sender, RoutedEventArgs e) =>
        SaveProjectSetupPageAnimationMilliseconds();

    private void ProjectSetupPageAnimationMillisecondsTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;
        SaveProjectSetupPageAnimationMilliseconds();
        e.Handled = true;
    }

    private void SaveProjectSetupPageAnimationMilliseconds()
    {
        if (_isLoading)
            return;
        if (!int.TryParse(ProjectSetupPageAnimationMillisecondsTextBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            value = AnimationTimingSettingsService.ProjectSetupPageDefaultMilliseconds;
        _debugSettings.ProjectSetupPageAnimationMillisecondsOverride =
            AnimationTimingSettingsService.ToProjectSetupPageOverride(value);
        ProjectSetupPageAnimationMillisecondsTextBox.Text = AnimationTimingSettingsService
            .GetProjectSetupPageMilliseconds(_debugSettings)
            .ToString(CultureInfo.InvariantCulture);
        _debugSettingsService.Save(_debugSettings);
    }

    private void ProjectSetupPageNavigationBridge_StateChanged(object? sender, EventArgs e) =>
        UpdateProjectSetupPagePreviewButtons();

    private void ProjectSetupPagePreviousButton_Click(object sender, RoutedEventArgs e)
    {
        SaveProjectSetupPageAnimationMilliseconds();
        ProjectSetupPageNavigationBridge.TryNavigate(-1);
        UpdateProjectSetupPagePreviewButtons();
    }

    private void ProjectSetupPageNextButton_Click(object sender, RoutedEventArgs e)
    {
        SaveProjectSetupPageAnimationMilliseconds();
        ProjectSetupPageNavigationBridge.TryNavigate(1);
        UpdateProjectSetupPagePreviewButtons();
    }

    private void UpdateProjectSetupPagePreviewButtons()
    {
        if (ProjectSetupPagePreviousButton is null || ProjectSetupPageNextButton is null)
            return;
        ProjectSetupPagePreviousButton.IsEnabled = ProjectSetupPageNavigationBridge.CanNavigate(-1);
        ProjectSetupPageNextButton.IsEnabled = ProjectSetupPageNavigationBridge.CanNavigate(1);
    }

    private void ToggleMetadataPanelPreview_Click(object sender, RoutedEventArgs e)
    {
        SaveMetadataAnimationMilliseconds();
        _metadataPanelExpandedPreview = !_metadataPanelExpandedPreview;
        _setMetadataPanelExpanded(_metadataPanelExpandedPreview);
        UpdateMetadataPanelPreviewButtonText();
    }

    private void UpdateMetadataPanelPreviewButtonText() =>
        ToggleMetadataPanelPreviewButton.Content = LocalizationText.Get("Developer.TestTagBar");

    private void EventProfileComboBox_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading || sender is not ComboBox comboBox)
            return;

        _settings.EventProfile = GetSelectedEventProfile(comboBox).ToString();
        _settingsManager.SaveImmediate();
    }

    private void FocusProfileComboBox_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading || sender is not ComboBox comboBox)
            return;

        _settings.FocusProfile = GetSelectedFocusProfile(comboBox).ToString();
        _settingsManager.SaveImmediate();
    }

    private void SoundLibraryComboBox_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading || sender is not ComboBox comboBox)
            return;

        _settings.SoundLibrary = GetSelectedSoundLibrary(comboBox).ToString();
        _settingsManager.SaveImmediate();
        _notificationService.PlaySoundPreview();
    }

    private void SoundVolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isLoading)
            return;

        _settings.SoundVolumePercent = SoundSettingsService.NormalizeVolumePercent((int)Math.Round(e.NewValue));
        _settingsManager.SaveDebounced();
        UpdateSoundVolumeText();
    }

    private void SelectEventProfileComboBoxItem() =>
        SelectProfile(DevEventProfileComboBox, EventProfileSettingsService.NormalizeProfile(_settings.EventProfile).ToString(), fallbackIndex: 2);

    private void SelectSoundLibraryComboBoxItem() =>
        SelectProfile(DevSoundLibraryComboBox, SoundSettingsService.NormalizeLibrary(_settings.SoundLibrary).ToString(), fallbackIndex: 0);

    private void SelectFocusProfileComboBoxItem() =>
        SelectProfile(DevFocusProfileComboBox, FocusSettingsService.NormalizeProfile(_settings.FocusProfile).ToString(), fallbackIndex: 1);

    private static void SelectProfile(ComboBox comboBox, string tag, int fallbackIndex)
    {
        comboBox.SelectedItem = comboBox.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(item => string.Equals(item.Tag?.ToString(), tag, StringComparison.OrdinalIgnoreCase));
        if (comboBox.SelectedItem is null)
            comboBox.SelectedIndex = fallbackIndex;
    }

    private static EventProfile GetSelectedEventProfile(ComboBox comboBox) =>
        comboBox.SelectedItem is ComboBoxItem item && Enum.TryParse<EventProfile>(item.Tag?.ToString(), out var profile)
            ? profile
            : EventProfileSettingsService.DefaultProfile;

    private static SoundLibrary GetSelectedSoundLibrary(ComboBox comboBox) =>
        comboBox.SelectedItem is ComboBoxItem item && Enum.TryParse<SoundLibrary>(item.Tag?.ToString(), out var library)
            ? library
            : SoundSettingsService.DefaultLibrary;

    private static FocusProfile GetSelectedFocusProfile(ComboBox comboBox) =>
        comboBox.SelectedItem is ComboBoxItem item && Enum.TryParse<FocusProfile>(item.Tag?.ToString(), out var profile)
            ? profile
            : FocusSettingsService.DefaultProfile;

    private void UpdateSoundVolumeText() =>
        DevSoundVolumeTextBlock.Text = $"{_settings.SoundVolumePercent} %";

    private async void PreviewDiscSourceAnalysis_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || !button.IsEnabled)
            return;

        button.IsEnabled = false;
        var previousContent = button.Content;
        button.Content = LocalizationText.Get("Developer.PreviewRunning");

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
            LocalizationText.Get("Developer.PreviewTitle"),
            kind switch
            {
                AppDialogKind.Warning => LocalizationText.Get("Developer.SampleWarning"),
                AppDialogKind.Error => LocalizationText.Get("Developer.SampleError"),
                AppDialogKind.Question => LocalizationText.Get("Developer.SampleQuestion"),
                _ => LocalizationText.Get("Developer.SampleInformation")
            },
            LocalizationText.Get("Developer.PreviewPurpose"),
            kind,
            [LocalizationText.Get("Developer.DetailFirst"), LocalizationText.Get("Developer.DetailSecond")],
            kind == AppDialogKind.Question
                ? [
                    new AppDialogButton(LocalizationText.Get("Common.Yes"), AppDialogResult.Yes, IsPrimary: true, IsDefault: true),
                    new AppDialogButton(LocalizationText.Get("Common.No"), AppDialogResult.No),
                    new AppDialogButton(LocalizationText.Get("Common.Cancel"), AppDialogResult.Cancel, IsCancel: true)
                  ]
                : [new AppDialogButton("OK", AppDialogResult.Ok, IsPrimary: true, IsDefault: true)]);
    }

    private void DiscSourcePreview_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new DiscSourceSelectionDialog([], string.Empty, _settings.LastSelectedOpticalDrive, _settings.DiscDriveOrder, _developerDiscDriveService)
        {
            Owner = this
        };
        ModalDialogService.ShowDialog(dialog, this);
    }

    private void AddDiscsPreview_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new AddMoreDiscsDialog(1, 2, 2, 2, 99, [], _settings.LastSelectedOpticalDrive, string.Empty, _settings.DiscDriveOrder, _developerDiscDriveService)
        {
            Owner = this
        };
        ModalDialogService.ShowDialog(dialog, this);
    }

    private void ResumeProjectPreview_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ResumeProjectDialog(
            _projectRootFolder,
            _projectIndexService,
            new ProjectResumePlanService(),
            _settings.ProjectRetentionDays,
            AppearanceSettingsService.GetEffectiveMode(_settings),
            selectedProject: null,
            projectRetentionMode: _settings.ProjectRetentionMode, settingsManager: _settingsManager)
        {
            Owner = this
        };
        _debugWindowPlacementService.Attach(dialog, DebugWindowPlacementTarget.ResumeProject);
        ModalDialogService.ShowDialog(dialog, this);
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
            ProjectSetupSourceKind.AudioDisc => LocalizationText.Get("MainWindow.Setup.AudioPrepare"),
            ProjectSetupSourceKind.Mp3Disc => LocalizationText.Get("MainWindow.Setup.Mp3Prepare"),
            _ => LocalizationText.Get("Developer.SetUpLocalProject")
        };

        var request = new ProjectSetupDialogRequest(
            sourceKind,
            windowTitle,
            sourceLabel,
            LocalizationText.Get("Developer.CheckSetup"),
            sourceKind == ProjectSetupSourceKind.Folder ? 1 : 2,
            1,
            99,
            ["AAC Stereo 128 kbps", "AAC Mono 64 kbps"],
            "AAC Stereo 128 kbps",
            _settings.SelectedParallelJobs,
            _settings.DefaultOutputExtension,
            _settings.OutputFolder ?? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            LocalizationText.Get("Developer.SampleBook"),
            LocalizationText.Get("Developer.SampleBook"),
            "Beispielautor",
            "Beispielsprecher",
            _settings.DefaultGenre,
            _settings.DefaultFileNameTemplate,
            string.Empty,
            string.Empty,
            Path.Combine(_projectRootFolder, "Covers"),
            _settings.MergeAutomaticallyAfterConversion,
            UsePrivateGenreList: _settings.UsePrivateGenreList,
            GenreOptions: GenreListService.GetGenres(_settings),
            SourceFolder: sourceLabel,
            MaxSourceBitrateKbps: 320,
            LastCoverFolder: _settings.LastCoverFolder ?? string.Empty);

        var dialog = new DiscProjectSetupDialog(
            request,
            AppearanceSettingsService.GetEffectiveMode(_settings))
        {
            Owner = this
        };
        _debugWindowPlacementService.Attach(dialog, DebugWindowPlacementTarget.ProjectSetup);
        ModalDialogService.ShowDialog(dialog, this);
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
                    statusTextBlock.Text = LocalizationText.Format("Developer.Countdown", button.Content, remaining);
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            }

            if (FindName("DelayedFocusStatusTextBlock") is TextBlock completedTextBlock)
                completedTextBlock.Text = LocalizationText.Format("Developer.Triggered", button.Content);
            _notificationService.Notify(notificationEvent);
        }
        catch (OperationCanceledException)
        {
            if (FindName("DelayedFocusStatusTextBlock") is TextBlock canceledTextBlock)
                canceledTextBlock.Text = LocalizationText.Get("Developer.DelayedFocusCanceled");
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

        SetDeveloperDiscTestStatus(LocalizationText.Format("Developer.SimulationStarted", GetSimulationDisplayName(scenario)), string.Empty);
        try
        {
            var completed = await _developerDiscWaitDialogService.WaitForDiscAsync(
                this,
                new DiscWaitDialogRequest(
                    DiscNumber: 2,
                    TotalDiscs: 3,
                    MediaName: LocalizationText.Get("Disc.Media.Mp3"),
                    InitialInstruction: LocalizationText.Get("Developer.SimulationPreparing"),
                    HintText: LocalizationText.Get("Developer.SimulationNoDrive")),
                (_, _) => Task.FromResult(_developerDiscTestService.CreateSimulationResult(scenario, ++checkNumber)),
                status => SetDeveloperDiscTestStatus(status, null),
                progress => SetDeveloperDiscTestStatus(null, progress),
                cancellationToken);

            SetDeveloperDiscTestStatus(
                completed == DiscWaitDialogOutcome.Ready
                    ? LocalizationText.Format("Developer.SimulationCompleted", GetSimulationDisplayName(scenario))
                    : LocalizationText.Format("Developer.SimulationPaused", GetSimulationDisplayName(scenario)),
                string.Empty);
        }
        catch (OperationCanceledException)
        {
            SetDeveloperDiscTestStatus(LocalizationText.Get("Developer.SimulationCanceled"), string.Empty);
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
        _settingsManager.SaveImmediate();

        CancelCurrentDeveloperDiscTest();
        _developerDiscTestCancellation = new CancellationTokenSource();
        var cancellationToken = _developerDiscTestCancellation.Token;

        try
        {
            var isAudio = tag.StartsWith("Audio", StringComparison.Ordinal);
            var duplicateTest = tag.EndsWith("Duplicate", StringComparison.Ordinal);
            var mediaName = isAudio ? LocalizationText.Get("Disc.Media.Audio") : LocalizationText.Get("Disc.Media.Mp3");

            HashSet<string> knownIdentities = [];
            if (duplicateTest)
            {
                var identity = isAudio
                    ? await ReadCurrentAudioDiscIdentityAsync(selectedDrive, cancellationToken)
                    : await ReadCurrentMp3DiscIdentityAsync(selectedDrive, cancellationToken);
                if (string.IsNullOrWhiteSpace(identity))
                    return;

                knownIdentities.Add(identity);
            }

            var ejected = await _developerDiscDriveService.TryEjectDiscAsync(selectedDrive, cancellationToken);
            SetDeveloperDiscTestStatus(
                ejected
                    ? LocalizationText.Format("Developer.HardwareEjected", mediaName)
                    : LocalizationText.Format("Developer.HardwareInsert", mediaName),
                duplicateTest
                    ? LocalizationText.Get("Developer.DuplicateInstruction")
                    : LocalizationText.Get("Developer.DetectionOnly"));

            var completed = isAudio
                ? await RunAudioHybridTestAsync(selectedDrive, knownIdentities, cancellationToken)
                : await RunMp3HybridTestAsync(selectedDrive, knownIdentities, cancellationToken);

            SetDeveloperDiscTestStatus(
                completed == DiscWaitDialogOutcome.Ready
                    ? LocalizationText.Format("Developer.HardwareCompleted", mediaName)
                    : LocalizationText.Format("Developer.HardwarePaused", mediaName),
                string.Empty);
        }
        catch (OperationCanceledException)
        {
            SetDeveloperDiscTestStatus(LocalizationText.Get("Developer.HybridCanceled"), string.Empty);
        }
        catch (Exception ex)
        {
            SetDeveloperDiscTestStatus(LocalizationText.Get("Developer.HybridFailed"), string.Empty);
            AppDialogService.Show(
                this,
                "BookStitch – " + LocalizationText.Get("Developer.HybridHardwareTest"),
                LocalizationText.Get("Developer.HybridFailed"),
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
                MediaName: LocalizationText.Get("Disc.Media.Mp3"),
                InitialInstruction: LocalizationText.Format("Disc.Polling.Mp3.SourceNotReady.Dialog", 2, 3),
                HintText: LocalizationText.Get("Developer.AfterDetection"),
                DriveDisplayName: CreateDiscDriveDisplayName(driveRoot),
                NotifyDisplayState: NotifyDeveloperDiscTestPollingState),
            (checkRequest, token) => pollingService.CheckDiscSourceForNextImportAsync(
                driveRoot, 2, 3, knownSignatures, cancellationToken: token, acceptDuplicate: checkRequest.AcceptNextDuplicate),
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
                MediaName: LocalizationText.Get("Disc.Media.Audio"),
                InitialInstruction: LocalizationText.Format("Disc.Polling.Audio.SourceNotReady.Dialog", 2, 3),
                HintText: LocalizationText.Get("Developer.AfterAudioDetection"),
                DriveDisplayName: CreateDiscDriveDisplayName(driveRoot),
                NotifyDisplayState: NotifyDeveloperDiscTestPollingState),
            async (checkRequest, token) => (await pollingService.CheckNextDiscAsync(
                driveRoot, 2, 3, knownIdentities, token, checkRequest.AcceptNextDuplicate)).PollingResult,
            status => SetDeveloperDiscTestStatus(status, null),
            progress => SetDeveloperDiscTestStatus(null, progress),
            cancellationToken);
    }

    private async Task<string?> ReadCurrentMp3DiscIdentityAsync(string driveRoot, CancellationToken cancellationToken)
    {
        var result = await Task.Run(() =>
        {
            var analysis = _developerMp3DiscImportService.AnalyzeSource(driveRoot);
            return (analysis, signature: analysis.IsSupportedDataDisc
                ? _developerMp3DiscImportService.CreateDiscSignature(driveRoot, analysis)
                : null);
        }, cancellationToken);
        if (!result.analysis.IsSupportedDataDisc)
        {
            ShowDiscTestInformation(LocalizationText.Get("Developer.DuplicateMp3Required"));
            return null;
        }

        return result.signature;
    }

    private async Task<string?> ReadCurrentAudioDiscIdentityAsync(string driveRoot, CancellationToken cancellationToken)
    {
        var mediaKind = await _developerDiscDriveService.GetMediaKindForPathAsync(driveRoot, cancellationToken);
        if (AudioDiscPollingService.IsClearlyNotAudioDisc(mediaKind))
        {
            ShowDiscTestInformation(LocalizationText.Get("Developer.DuplicateAudioRequired"));
            return null;
        }

        var readResult = await Task.Run(() => new AudioDiscReaderService().ReadDisc(driveRoot), cancellationToken);
        if (!readResult.IsAudioDisc || readResult.Disc is null)
        {
            ShowDiscTestInformation(LocalizationText.Get("Developer.DuplicateReadableAudioRequired"));
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
        return string.IsNullOrWhiteSpace(root) ? string.Empty : LocalizationText.Format("Developer.DriveName", root);
    }

    private string? SelectOpticalDrive()
    {
        var dialog = new DiscSourceSelectionDialog([], string.Empty, _settings.LastSelectedOpticalDrive, _settings.DiscDriveOrder, _developerDiscDriveService)
        {
            Owner = this
        };
        return ModalDialogService.ShowDialog(dialog, this) == true ? dialog.SelectedPath : null;
    }

    private void ShowDiscTestInformation(string message)
    {
        AppDialogService.Show(
            this,
            "BookStitch",
            LocalizationText.Get("Developer.TestUnavailable"),
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
        DeveloperDiscSimulationScenario.EmptyDrive => LocalizationText.Get("Developer.EmptyDrive"),
        DeveloperDiscSimulationScenario.UnsupportedDisc => LocalizationText.Get("Developer.UnsupportedDiscDescription"),
        DeveloperDiscSimulationScenario.DuplicateEjected => LocalizationText.Get("Developer.DuplicateDetected"),
        DeveloperDiscSimulationScenario.DuplicateManualEject => LocalizationText.Get("Developer.DuplicateManual"),
        DeveloperDiscSimulationScenario.SlowThenReady => LocalizationText.Get("Developer.SlowDriveDescription"),
        DeveloperDiscSimulationScenario.Ready => LocalizationText.Get("Developer.CorrectDisc"),
        _ => scenario.ToString()
    };

    private void DeveloperToolsWindow_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
            return;

        e.Handled = true;
        Close();
    }
    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void TestProgressArtwork_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(ProgressPreviewSecondsTextBox.Text, NumberStyles.Integer,
                CultureInfo.CurrentCulture, out var seconds) || seconds is < 1 or > 3600)
        {
            ProgressPreviewSecondsTextBox.Text = "3";
            ProgressPreviewSecondsTextBox.Focus();
            ProgressPreviewSecondsTextBox.SelectAll();
            return;
        }
        _startProgressPreview?.Invoke(seconds, ProgressPreviewKindComboBox.SelectedIndex == 1);
    }

    private void Cleanup()
    {
        _settingsManager.GenreOptionsChanged -= GenreOptionsChanged;
        WeakEventManager<DeveloperAppearancePreview, EventArgs>.RemoveHandler(DeveloperToolsCommands.AppearancePreview,
            nameof(DeveloperAppearancePreview.Changed), PreviewAppearanceChanged);
        _stopProgressPreview?.Invoke();
        _settingsManager.Flush();
        ProjectSetupPageNavigationBridge.StateChanged -= ProjectSetupPageNavigationBridge_StateChanged;
        _liveStateTimer.Stop();
        CancelCurrentDeveloperDiscTest();
        _delayedFocusTestCancellation?.Cancel();
        _delayedFocusTestCancellation?.Dispose();
        _delayedFocusTestCancellation = null;
    }
}
