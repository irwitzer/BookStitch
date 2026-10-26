using BookStitch.Models;
using BookStitch.Controls;
using BookStitch.Services;
using Microsoft.Win32;
using System.IO;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace BookStitch.Dialog;

public partial class DiscProjectSetupDialog : Window, INotifyPropertyChanged
{
    public DiscCountInputState DiscCountState { get; private set; } = null!;
    public TextBox DiscCountTextBox => DiscCountPanel.Input;
    private readonly int _minimumDiscs;
    private readonly int _maximumDiscs;
    private readonly IReadOnlyList<string> _exportPresets;
    private readonly int? _maxSourceBitrateKbps;
    private readonly ProjectSetupDialogRequest _request;
    private readonly ProjectSetupModeProfile _modeProfile;
    private readonly Func<Window, ProjectSetupDialogGlobalSettings>? _openAdvancedSettings;
    private readonly Action<bool>? _setAutoMerge;
    private readonly Action<string>? _setOutputExtension;
    private readonly Action<string>? _setOutputFolder;
    private readonly Action<string>? _setFileNameTemplate;
    private readonly Action<string>? _setLastCoverFolder;
    private readonly Func<int> _getCoverWarningThresholdPixels;
    private readonly Action<int> _setCoverWarningThresholdPixels;
    private readonly Action<int>? _discCountChanged;
    private readonly GlobalSettingsManager? _settingsManager;
    private readonly Func<bool> _shouldWarnDiscCount;
    private readonly Action<bool>? _setAlwaysWarnDiscCount;
    private readonly ProjectMetadataState _metadataState;
    private ProjectMetadata _observedCanonicalMetadata = null!;
    private readonly MetadataPreferences _metadataPreferences;
    private readonly Action<MetadataPreferences> _metadataPreferencesChanged;
    private readonly Func<IReadOnlyList<SourceMetadataSnapshot>> _sourceSnapshotsProvider;
    private readonly Func<IReadOnlyList<MetadataFieldDecisionDiagnostic>> _metadataFieldDecisionsProvider;
    private readonly Action<ProjectMetadata> _saveMetadata;
    private readonly Action<Window>? _openMetadataDialog;
    private readonly Func<FinalAudioTagData>? _compatibilityPreviewProvider;
    private readonly Func<bool>? _compatibilityDescriptionEnabledProvider;
    private readonly Action<bool>? _compatibilityDescriptionEnabledChanged;
    private readonly MetadataNormalizationService _metadataNormalization = new();
    private readonly ConceptCandidateSessionService _candidateSession;
    private readonly SourceMetadataAlternativeService _candidateService = new();
    private readonly ConceptMetadataFieldComposition _metadataFields;
    private readonly ConceptSeriesFieldState _seriesFieldState;
    private readonly ConceptMetadataFieldViewModel _descriptionField;
    private readonly ConceptDescriptionFieldState _descriptionFieldState;
    private readonly ConceptMetadataFieldViewModel _isbnField;
    private readonly ConceptSpecialMetadataFieldState _abridgedStatusFieldState;
    private readonly ConceptSpecialMetadataFieldState _workTypeFieldState;
    private readonly ConceptSpecialMetadataFieldState _yearFieldState;
    private readonly ConceptMinimalPresentationScope _minimalPresentationScope = new();
    private IReadOnlyList<string> _genres;
    private readonly Func<AppearanceMode> _getAppearanceMode;
    private readonly Func<int> _getProjectSetupPageAnimationMilliseconds;
    private bool _metadataRefreshQueued;
    private bool _isInitializing;
    private string _lastAcceptedPreset = "";
    private string _outputFolder = "";
    private readonly CoverImageService _coverImageService = new();
    private readonly CoverPickerService _coverPickerService;
    private readonly string _coverWorkFolder;
    private string _selectedCoverSourcePath = "";
    private string _processedCoverPath = "";
    private bool _isCoverDialogOpen;
    private bool _setupDraftExitApproved;
    private int _currentSetupPage;
    private bool _isSetupPageAnimating;

    internal int CurrentSetupPage => _currentSetupPage;
    internal bool IsSetupPageAnimating => _isSetupPageAnimating;

    public DiscProjectSetupResult Result { get; private set; } = new(
        1, "", "Auto", ".m4a", "", "", "", "", "", "Audiobook", "{Autor} - {Titel}", "", "", false);

    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action<string>? AcceptedPresetChanged;

    public string CompatibilityFieldLabel => LocalizationText.Get("Metadata.Search.CompatibilityField");

    public string CompatibilityFieldText =>
        _compatibilityPreviewProvider is null
            ? string.Empty
            : CompatibilityDescriptionRenderer.Render(_compatibilityPreviewProvider());

    public bool IsCompatibilityDescriptionEnabled
    {
        get => _compatibilityDescriptionEnabledProvider?.Invoke() ?? false;
        set
        {
            if (value == IsCompatibilityDescriptionEnabled)
                return;

            _compatibilityDescriptionEnabledChanged?.Invoke(value);
            OnPropertyChanged(nameof(IsCompatibilityDescriptionEnabled));
        }
    }

    public DiscProjectSetupDialog(
        ProjectSetupDialogRequest request,
        AppearanceMode appearanceMode,
        Func<Window, ProjectSetupDialogGlobalSettings>? openAdvancedSettings = null,
        Action<bool>? setAutoMerge = null,
        Action<string>? setOutputExtension = null,
        Action<string>? setOutputFolder = null,
        Action<string>? setFileNameTemplate = null,
        Action<string>? setLastCoverFolder = null,
        Func<int>? getCoverWarningThresholdPixels = null,
        CoverPickerService? coverPickerService = null,
        Action<int>? discCountChanged = null,
        ProjectMetadataState? metadataState = null,
        MetadataPreferences? metadataPreferences = null,
        Action<MetadataPreferences>? metadataPreferencesChanged = null,
        Func<IReadOnlyList<SourceMetadataSnapshot>>? sourceSnapshotsProvider = null,
        Func<IReadOnlyList<MetadataFieldDecisionDiagnostic>>? metadataFieldDecisionsProvider = null,
        Action<ProjectMetadata>? saveMetadata = null,
        Action<Window>? openMetadataDialog = null,
        Func<FinalAudioTagData>? compatibilityPreviewProvider = null,
        Func<bool>? compatibilityDescriptionEnabledProvider = null,
        Action<bool>? compatibilityDescriptionEnabledChanged = null,
        Func<AppearanceMode>? getAppearanceMode = null,
        Func<int>? getProjectSetupPageAnimationMilliseconds = null,
        Func<bool>? shouldWarnDiscCount = null,
        Action<bool>? setAlwaysWarnDiscCount = null,
        Action<int>? setCoverWarningThresholdPixels = null,
        GlobalSettingsManager? settingsManager = null)
    {
        InitializeComponent();

        _request = request;
        _settingsManager=settingsManager;
        _setCoverWarningThresholdPixels = setCoverWarningThresholdPixels ?? (_ => { });
        _shouldWarnDiscCount = shouldWarnDiscCount ?? (() => true);
        _setAlwaysWarnDiscCount = setAlwaysWarnDiscCount;
        _modeProfile = ProjectSetupModeProfile.FromSourceKind(request.SourceKind);
        _openAdvancedSettings = openAdvancedSettings;
        _setAutoMerge = setAutoMerge;
        _setOutputExtension = setOutputExtension;
        _setOutputFolder = setOutputFolder;
        _setFileNameTemplate = setFileNameTemplate;
        _setLastCoverFolder = setLastCoverFolder;
        _getCoverWarningThresholdPixels = getCoverWarningThresholdPixels
            ?? (() => CoverWarningSettingsService.DefaultThresholdPixels);
        _coverPickerService = coverPickerService ?? new CoverPickerService(
            new AppSettings { LastCoverFolder = request.LastCoverFolder },
            () => { },
            () => null);
        _discCountChanged = discCountChanged;
        _getAppearanceMode = getAppearanceMode ?? (() => appearanceMode);
        _getProjectSetupPageAnimationMilliseconds = getProjectSetupPageAnimationMilliseconds
            ?? (() => AnimationTimingSettingsService.ProjectSetupPageDefaultMilliseconds);
        _metadataState = metadataState ?? CreateMetadataState(request);
        _metadataPreferences = metadataPreferences ?? new MetadataPreferences();
        MetadataPreferencesService.Normalize(_metadataPreferences);
        _metadataPreferencesChanged = metadataPreferencesChanged ?? (_ => { });
        _sourceSnapshotsProvider = sourceSnapshotsProvider ?? (() => []);
        _metadataFieldDecisionsProvider = metadataFieldDecisionsProvider ?? (() => []);
        _saveMetadata = saveMetadata ?? (_ => { });
        _openMetadataDialog = openMetadataDialog;
        _compatibilityPreviewProvider = compatibilityPreviewProvider;
        _compatibilityDescriptionEnabledProvider = compatibilityDescriptionEnabledProvider;
        _compatibilityDescriptionEnabledChanged = compatibilityDescriptionEnabledChanged;
        _genres = _settingsManager is not null ? GenreListService.GetGenres(_settingsManager.Current)
            : request.GenreOptions ?? GenreListService.GetGenres(request.UsePrivateGenreList);
        _candidateSession = new ConceptCandidateSessionService(
            _metadataNormalization,
            new ScalarCandidatePresentationService(_metadataNormalization));
        _metadataFields = new ConceptMetadataFieldComposition(
            () => _metadataState.CanonicalMetadata,
            _metadataPreferences,
            _metadataNormalization,
            metadata => _saveMetadata(metadata),
            preferences => _metadataPreferencesChanged(preferences),
            candidateRefreshRequested: _ => QueueMetadataFieldRefresh(),
            candidateValueCommitted: (field, value) =>
            {
                _candidateSession.RetainUserValue(field, value);
                QueueMetadataFieldRefresh();
            },
            titleAlbumBindingChanged: OnTitleAlbumBindingChanged);
        _seriesFieldState = new ConceptSeriesFieldState(
            _metadataState,
            _metadataPreferences,
            _metadataNormalization,
            _saveMetadata,
            _metadataPreferencesChanged,
            QueueMetadataFieldRefresh);
        _descriptionField = new ConceptMetadataFieldViewModel(
            () => _metadataState.CanonicalMetadata,
            _metadataPreferences,
            _metadataNormalization,
            ProjectMetadataField.Description,
            ConceptMetadataGroupCatalog.Description.Descriptor,
            _saveMetadata,
            _metadataPreferencesChanged,
            isMultiline: true);
        _descriptionFieldState = new ConceptDescriptionFieldState(
            _metadataState,
            _descriptionField,
            _saveMetadata);
        _isbnField = new ConceptMetadataFieldViewModel(
            () => _metadataState.CanonicalMetadata,
            _metadataPreferences,
            _metadataNormalization,
            ProjectMetadataField.ISBN,
            ConceptMetadataFieldCatalog.Get(ProjectMetadataField.ISBN),
            _saveMetadata,
            _metadataPreferencesChanged,
            candidateRefreshRequested: _ => QueueMetadataFieldRefresh(),
            candidateValueCommitted: (field, value) =>
            {
                _candidateSession.RetainUserValue(field, value);
                QueueMetadataFieldRefresh();
            });
        _abridgedStatusFieldState = CreateSpecialFieldState(ProjectMetadataField.AbridgedStatus);
        _workTypeFieldState = CreateSpecialFieldState(ProjectMetadataField.AudioWorkType);
        _yearFieldState = CreateSpecialFieldState(ProjectMetadataField.Year);
        ConfigureEmbeddedMetadataFields();
        _metadataState.PropertyChanged += MetadataState_PropertyChanged;
        _observedCanonicalMetadata = _metadataState.CanonicalMetadata;
        _observedCanonicalMetadata.PropertyChanged += CanonicalMetadata_PropertyChanged;
        Activated += DiscProjectSetupDialog_Activated;
        Closing += DiscProjectSetupDialog_Closing;
        Closed += DiscProjectSetupDialog_Closed;
        WeakEventManager<DeveloperAppearancePreview, EventArgs>.AddHandler(DeveloperToolsCommands.AppearancePreview,
            nameof(DeveloperAppearancePreview.Changed), PreviewAppearanceChanged);
        _isInitializing = true;
        _minimumDiscs = Math.Clamp(request.MinimumDiscs, 1, 99);
        _maximumDiscs = Math.Clamp(request.MaximumDiscs, _minimumDiscs, 99);
        _exportPresets = request.ExportPresets;
        _maxSourceBitrateKbps = request.MaxSourceBitrateKbps;
        _coverWorkFolder = request.CoverWorkFolder;
        Title = request.WindowTitle;
        MetadataButton.IsEnabled = _openMetadataDialog is not null;

        ApplySourceModeProfile(_modeProfile);
        ProjectFolderTextBlock.Text = BuildFolderDisplayName(request.SourceFolder);

        DiscCountState = new DiscCountInputState(request.DefaultDiscCount, _minimumDiscs, _maximumDiscs);
        DiscCountPanel.DataContext = DiscCountState;
        DiscCountState.ValueChanged += value =>
        {
            if (!_isInitializing) _discCountChanged?.Invoke(value);
        };

        PresetComboBox.ItemsSource = request.ExportPresets;
        if (request.IsDiscResume && !request.ExportPresets.Contains(request.SelectedExportPreset))
            PresetComboBox.ItemsSource = request.ExportPresets.Append(request.SelectedExportPreset).ToArray();
        if (request.IsDiscResume || request.ExportPresets.Contains(request.SelectedExportPreset))
            PresetComboBox.SelectedItem = request.SelectedExportPreset;
        else if (request.ExportPresets.Count > 0)
            PresetComboBox.SelectedIndex = 0;

        _lastAcceptedPreset = PresetComboBox.SelectedItem?.ToString() ?? request.SelectedExportPreset ?? "";
        if (request.IsDiscResume)
        {
            PresetComboBox.IsEnabled = false;
            DiscCountPanel.IsEnabled = false;
        }
        if (request.IsDiscResume || request.IsContinuation)
        {
            StartProjectButton.Content = LocalizationText.Get("MainWindow.Action.Continue");
            StartProjectButton.MinWidth = 150;
            StartProjectButton.Width = double.NaN;
            System.Windows.Automation.AutomationProperties.SetName(StartProjectButton, LocalizationText.Get("MainWindow.Action.Continue"));
        }
        ParallelJobsTextBox.Text = string.IsNullOrWhiteSpace(request.ParallelJobs) ? "Auto" : request.ParallelJobs;

        SelectOutputExtension(string.IsNullOrWhiteSpace(request.OutputExtension) ? ".m4a" : request.OutputExtension);
        SetOutputFolder(request.OutputFolder ?? "", persistGlobally: false);
        _selectedCoverSourcePath = _metadataState.CoverSourcePath;
        _processedCoverPath = !string.IsNullOrWhiteSpace(_metadataState.ProcessedCoverPath) && File.Exists(_metadataState.ProcessedCoverPath)
            ? _metadataState.ProcessedCoverPath
            : "";
        SetCoverPreview(string.IsNullOrWhiteSpace(_metadataState.CoverPreviewSource) ? _selectedCoverSourcePath : _metadataState.CoverPreviewSource);

        RefreshEmbeddedMetadataFields();
        FileNameTemplateTextBox.Text = string.IsNullOrWhiteSpace(request.FileNameTemplate) ? "{Autor} - {Titel}" : request.FileNameTemplate;
        AutoMergeCheckBox.IsChecked = request.AutoMergeAfterConversion;
        if (_settingsManager is not null)
        {
            _settingsManager.DriveCircleChanged += GlobalSettingsManager_DriveCircleChanged;
            _settingsManager.GenreOptionsChanged += GlobalSettingsManager_GenreOptionsChanged;
        }
        DriveCircleRow.Visibility = request.SourceKind == ProjectSetupSourceKind.Folder ? Visibility.Collapsed : Visibility.Visible;
        DriveCircleCheckBox.IsChecked = _settingsManager?.Current.ExperimentalDriveRoundEnabled == true;
        ConfigureTabOrder(_modeProfile);

        _isInitializing = false;
        UpdatePreview();

        Loaded += (_, _) =>
        {
            ProjectSetupPageNavigationBridge.Register(this);
            UpdateSetupPageNavigationButtons();
            UpdateOutputFolderBadge();

            if (_modeProfile.ShowsProjectFolder)
            {
                SetupTitleField.FocusEditorAndSelectAll();
            }
            else
            {
                DiscCountTextBox.Focus();
                DiscCountTextBox.SelectAll();
            }
        };
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Handled || Keyboard.Modifiers != ModifierKeys.None)
            return;

        var direction = e.Key switch
        {
            Key.Left => -1,
            Key.Right => 1,
            _ => 0
        };
        if (direction == 0 || ShouldPreserveArrowKeyForFocusedEditor(e.OriginalSource))
            return;

        if (NavigateSetupPage(direction))
            e.Handled = true;
    }

    private static bool ShouldPreserveArrowKeyForFocusedEditor(object? source)
    {
        for (var current = source as DependencyObject; current is not null; current = GetInputAncestor(current))
        {
            if (current is ConceptMetadataFieldControl or TextBoxBase or PasswordBox or ComboBox or Selector or Slider or ScrollBar)
                return true;
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

    private void PreviousSetupPageButton_Click(object sender, RoutedEventArgs e) => NavigateSetupPage(-1);

    private void NextSetupPageButton_Click(object sender, RoutedEventArgs e) => NavigateSetupPage(1);

    internal bool CanNavigateSetupPage(int direction)
    {
        var target = _currentSetupPage + Math.Sign(direction);
        return !_isSetupPageAnimating && target is >= 0 and <= 1;
    }

    internal bool NavigateSetupPage(int direction)
    {
        if (!CanNavigateSetupPage(direction))
            return false;

        var targetPage = _currentSetupPage + Math.Sign(direction);
        var outgoing = _currentSetupPage == 0 ? SetupPage0 : SetupPage1;
        var incoming = targetPage == 0 ? SetupPage0 : SetupPage1;
        var outgoingTransform = _currentSetupPage == 0 ? SetupPage0Transform : SetupPage1Transform;
        var incomingTransform = targetPage == 0 ? SetupPage0Transform : SetupPage1Transform;
        var distance = Math.Max(1, SetupPageHost.ActualWidth);
        var directionSign = targetPage > _currentSetupPage ? 1d : -1d;
        var durationMs = AnimationTimingSettingsService.Normalize(
            _getProjectSetupPageAnimationMilliseconds());

        _isSetupPageAnimating = true;
        ProjectSetupPageNavigationBridge.NotifyNavigationStateChanged();
        outgoing.IsHitTestVisible = false;
        incoming.Visibility = Visibility.Visible;
        incoming.IsHitTestVisible = false;
        incomingTransform.X = directionSign * distance;

        if (durationMs == 0)
        {
            CompleteSetupPageNavigation(targetPage, outgoing, incoming, outgoingTransform, incomingTransform);
            return true;
        }

        var duration = TimeSpan.FromMilliseconds(durationMs);
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        var outgoingAnimation = new DoubleAnimation(0, -directionSign * distance, duration) { EasingFunction = easing };
        var incomingAnimation = new DoubleAnimation(directionSign * distance, 0, duration) { EasingFunction = easing };
        incomingAnimation.Completed += (_, _) =>
            CompleteSetupPageNavigation(targetPage, outgoing, incoming, outgoingTransform, incomingTransform);
        outgoingTransform.BeginAnimation(TranslateTransform.XProperty, outgoingAnimation, HandoffBehavior.SnapshotAndReplace);
        incomingTransform.BeginAnimation(TranslateTransform.XProperty, incomingAnimation, HandoffBehavior.SnapshotAndReplace);
        UpdateSetupPageNavigationButtons();
        return true;
    }

    private void CompleteSetupPageNavigation(
        int targetPage,
        FrameworkElement outgoing,
        FrameworkElement incoming,
        TranslateTransform outgoingTransform,
        TranslateTransform incomingTransform)
    {
        outgoingTransform.BeginAnimation(TranslateTransform.XProperty, null);
        incomingTransform.BeginAnimation(TranslateTransform.XProperty, null);
        outgoingTransform.X = 0;
        incomingTransform.X = 0;
        outgoing.Visibility = Visibility.Collapsed;
        outgoing.IsHitTestVisible = false;
        incoming.Visibility = Visibility.Visible;
        incoming.IsHitTestVisible = true;
        _currentSetupPage = targetPage;
        _isSetupPageAnimating = false;
        UpdateSetupPageNavigationButtons();
        ProjectSetupPageNavigationBridge.NotifyNavigationStateChanged();
    }

    private void UpdateSetupPageNavigationButtons()
    {
        PreviousSetupPageButton.IsEnabled = CanNavigateSetupPage(-1);
        NextSetupPageButton.IsEnabled = CanNavigateSetupPage(1);
    }

    private void OnTitleAlbumBindingChanged()
    {
        QueueMetadataFieldRefresh();
        if (_metadataFields.TitleAlbumBinding.IsActive)
            return;

        _ = Dispatcher.BeginInvoke(
            DispatcherPriority.Background,
            new Action(() =>
            {
                if (!_metadataFields.TitleAlbumBinding.IsActive &&
                    _metadataFields.Album.CanEditValues &&
                    _metadataFields.Album.Values.Count == 0)
                {
                    SetupAlbumField.TryOpenMinimalEditor();
                }
            }));
    }

    private void ApplySourceModeProfile(ProjectSetupModeProfile profile)
    {
        DiscCountLabel.Visibility = profile.ShowsDiscCount ? Visibility.Visible : Visibility.Collapsed;
        DiscCountPanel.Visibility = profile.ShowsDiscCount ? Visibility.Visible : Visibility.Collapsed;
        ProjectFolderLabel.Visibility = profile.ShowsProjectFolder ? Visibility.Visible : Visibility.Collapsed;
        ProjectFolderButton.Visibility = profile.ShowsProjectFolder ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ConfigureTabOrder(ProjectSetupModeProfile profile)
    {
        var index = 0;
        if (profile.ShowsDiscCount)
            DiscCountTextBox.TabIndex = index++;

        ParallelJobsTextBox.TabIndex = index++;
        OutputExtensionComboBox.TabIndex = index++;
        PresetComboBox.TabIndex = index++;
        AutoMergeCheckBox.TabIndex = index++;
        DriveCircleCheckBox.TabIndex = index++;
        OutputFolderStateChipButton.TabIndex = index++;
        OpenOutputFolderButton.TabIndex = index++;
        FileNameTemplateTextBox.TabIndex = index++;
        SetupTitleField.TabIndex = index++;
        SetupAlbumField.TabIndex = index++;
        SetupAuthorField.TabIndex = index++;
        SetupNarratorField.TabIndex = index++;
        SetupGenreField.TabIndex = index++;
        SetupSeriesField.TabIndex = index++;
        SetupAbridgedStatusField.TabIndex = index++;
        SetupWorkTypeField.TabIndex = index++;
        SetupYearField.TabIndex = index++;
        SetupIsbnField.TabIndex = index++;
        SetupDescriptionField.TabIndex = index++;
        StartProjectButton.TabIndex = index++;
        SortTracksButton.TabIndex = index;
    }


    private static string BuildFolderDisplayName(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
            return LocalizationText.Get("Disc.Setup.SourceFolder");

        var trimmed = folderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return Path.GetFileName(trimmed) is { Length: > 0 } name ? name : folderPath;
    }

    private void SelectOutputExtension(string extension)
    {
        var normalized = extension.Equals(".m4b", StringComparison.OrdinalIgnoreCase)
            ? ".m4b"
            : ".m4a";

        foreach (var item in OutputExtensionComboBox.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(item.Content?.ToString(), normalized, StringComparison.OrdinalIgnoreCase))
            {
                OutputExtensionComboBox.SelectedItem = item;
                return;
            }
        }

        OutputExtensionComboBox.SelectedIndex = 0;
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        if (!CaptureResult(validateForStart: true))
            return;
        if (!TryResolveSetupDraftsBeforeLeaving(SetupDraftLeaveIntent.Exit))
            return;
        if (_request.ConfirmDiscCountOnStart && _modeProfile.ShowsDiscCount && DiscCountState.RequiresStartConfirmation && _shouldWarnDiscCount())
        {
            var confirmation = new DiscCountConfirmationDialog(DiscCountState);
            if (ModalDialogService.ShowDialog(confirmation, this) != true || confirmation.Result != AppDialogResult.Ok)
                return;
            if (!CaptureResult(validateForStart: true))
                return;
            _setAlwaysWarnDiscCount?.Invoke(confirmation.AlwaysWarn);
        }

        _setupDraftExitApproved = true;
        DialogResult = true;
        Close();
    }

    private bool CaptureResult(bool validateForStart)
    {
        var discCount = 1;
        if (_modeProfile.ShowsDiscCount &&
            !DiscCountState.TryGetValue(out discCount))
        {
            if (validateForStart)
            {
                ShowError(LocalizationText.Format("Disc.Setup.DiscCountRange", _minimumDiscs, _maximumDiscs));
                DiscCountTextBox.Focus();
                DiscCountTextBox.SelectAll();
                return false;
            }

            discCount = Math.Clamp(_request.DefaultDiscCount, _minimumDiscs, _maximumDiscs);
        }

        var preset = _request.IsDiscResume ? _request.SelectedExportPreset : PresetComboBox.SelectedItem?.ToString() ?? _request.SelectedExportPreset;
        if (validateForStart && string.IsNullOrWhiteSpace(preset))
        {
            ShowError(LocalizationText.Get("Disc.Setup.SelectPreset"));
            PresetComboBox.Focus();
            return false;
        }

        var parallelJobs = ParallelJobsTextBox.Text.Trim();
        if (!IsValidParallelJobs(parallelJobs))
        {
            if (validateForStart)
            {
                ShowError(LocalizationText.Get("Disc.Setup.ParallelJobsRange"));
                ParallelJobsTextBox.Focus();
                ParallelJobsTextBox.SelectAll();
                return false;
            }

            parallelJobs = _request.ParallelJobs;
        }

        var outputFolder = _outputFolder.Trim();

        AlignManagedCoverWithCommittedTitle();

        Result = new DiscProjectSetupResult(
            _request.IsDiscResume ? _request.DefaultDiscCount : discCount,
            preset,
            NormalizeParallelJobs(parallelJobs),
            GetSelectedOutputExtension(),
            outputFolder,
            _metadataState.BookTitle.Trim(),
            _metadataState.Album.Trim(),
            _metadataState.Author.Trim(),
            _metadataState.Narrator.Trim(),
            _metadataState.Genre.Trim(),
            GetComboBoxText(FileNameTemplateTextBox).Trim(),
            _selectedCoverSourcePath,
            _processedCoverPath,
            AutoMergeCheckBox.IsChecked == true);

        return true;
    }

    private static bool IsValidParallelJobs(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return true;

        if (value.Equals("Auto", StringComparison.OrdinalIgnoreCase))
            return true;

        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var jobs) &&
               jobs >= 1 &&
               jobs <= 40;
    }

    private static string NormalizeParallelJobs(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "Auto";

        if (value.Equals("Auto", StringComparison.OrdinalIgnoreCase))
            return "Auto";

        return int.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture)
            .ToString(CultureInfo.InvariantCulture);
    }

    private string GetSelectedOutputExtension()
    {
        if (OutputExtensionComboBox.SelectedItem is ComboBoxItem item &&
            string.Equals(item.Content?.ToString(), ".m4b", StringComparison.OrdinalIgnoreCase))
        {
            return ".m4b";
        }

        return ".m4a";
    }

    private void ChooseOutputFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = LocalizationText.Get("Disc.Setup.SelectOutputFolder"),
            Multiselect = false
        };

        dialog.InitialDirectory = !string.IsNullOrWhiteSpace(_outputFolder) &&
                                  Directory.Exists(_outputFolder)
            ? _outputFolder
            : Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

        if (dialog.ShowDialog(this) != true)
            return;

        SetOutputFolder(dialog.FolderName, persistGlobally: true);
        ErrorText.Visibility = Visibility.Collapsed;
    }

    private void SetOutputFolder(string? outputFolder, bool persistGlobally)
    {
        _outputFolder = outputFolder?.Trim() ?? "";
        OpenOutputFolderButton.IsEnabled =
            !string.IsNullOrWhiteSpace(_outputFolder) && Directory.Exists(_outputFolder);
        UpdateOutputFolderBadge();

        if (persistGlobally && !string.IsNullOrWhiteSpace(_outputFolder))
            _setOutputFolder?.Invoke(_outputFolder);
    }

    private void OpenOutputFolder_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_outputFolder) || !Directory.Exists(_outputFolder))
        {
            AppDialogService.Warning(
                this,
                LocalizationText.Get("Disc.Setup.FolderNotFound.Title"),
                LocalizationText.Format("Disc.Setup.FolderNotFound.Message", _outputFolder));
            return;
        }

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = _outputFolder,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            AppDialogService.Warning(
                this,
                LocalizationText.Get("Disc.Setup.ExplorerFailed.Title"),
                LocalizationText.Format("Disc.Setup.ExplorerFailed.Message", ex.Message));
        }
    }

    private void OutputFolderStateChip_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateOutputFolderBadge();
    }

    private void UpdateOutputFolderBadge()
    {
        var availableWidth = OutputFolderBadgeTextBlock.ActualWidth;
        if (availableWidth <= 0)
            availableWidth = Math.Max(0, OutputFolderStateChip.ActualWidth - 18);

        OutputFolderBadgeTextBlock.Text = OutputFolderBadgeDisplayService.BuildText(
            _outputFolder,
            availableWidth,
            MeasureOutputFolderBadgeText);
    }

    private double MeasureOutputFolderBadgeText(string text)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        var dpi = VisualTreeHelper.GetDpi(OutputFolderBadgeTextBlock).PixelsPerDip;
        var formattedText = new FormattedText(
            text,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            new Typeface(
                OutputFolderBadgeTextBlock.FontFamily,
                OutputFolderBadgeTextBlock.FontStyle,
                OutputFolderBadgeTextBlock.FontWeight,
                OutputFolderBadgeTextBlock.FontStretch),
            OutputFolderBadgeTextBlock.FontSize,
            OutputFolderBadgeTextBlock.Foreground,
            dpi);

        return formattedText.WidthIncludingTrailingWhitespace;
    }

    private void ConfigureEmbeddedMetadataFields()
    {
        SetupTitleField.DataContext = _metadataFields.Title;
        ConceptMetadataFieldBindingAccessory.Configure(
            SetupAlbumField,
            _metadataFields.Album,
            _metadataFields.TitleAlbumBinding);
        SetupAuthorField.DataContext = _metadataFields.Authors;
        SetupNarratorField.DataContext = _metadataFields.Narrators;
        SetupGenreField.DataContext = _metadataFields.Genres;
        SetupSeriesField.DataContext = _seriesFieldState;
        SetupAbridgedStatusField.DataContext = _abridgedStatusFieldState;
        SetupWorkTypeField.DataContext = _workTypeFieldState;
        SetupYearField.DataContext = _yearFieldState;
        SetupIsbnField.DataContext = _isbnField;
        SetupDescriptionField.DataContext = _descriptionFieldState;
        SetupCompatibilityField.DataContext = this;

        foreach (var field in new[]
        {
            SetupTitleField,
            SetupAlbumField,
            SetupAuthorField,
            SetupNarratorField,
            SetupGenreField
        })
        {
            field.MinimalPresentationScope = _minimalPresentationScope;
        }
    }

    private ConceptSpecialMetadataFieldState CreateSpecialFieldState(ProjectMetadataField field) =>
        new(
            _metadataState,
            _metadataPreferences,
            field,
            _saveMetadata,
            _metadataPreferencesChanged,
            _candidateSession);

    private void RefreshEmbeddedMetadataFields()
    {
        _metadataFields.Refresh();
        _isbnField.Refresh();
        _seriesFieldState.RefreshCanonical();
        _descriptionFieldState.RefreshCanonical();
        _abridgedStatusFieldState.RefreshCanonical();
        _workTypeFieldState.RefreshCanonical();
        _yearFieldState.RefreshCanonical();
        OnPropertyChanged(nameof(CompatibilityFieldText));
        OnPropertyChanged(nameof(IsCompatibilityDescriptionEnabled));
        RefreshEmbeddedMetadataCandidates();
        UpdatePreview();
    }

    private void RefreshEmbeddedMetadataCandidates()
    {
        var snapshots = _sourceSnapshotsProvider();
        var decisions = _metadataFieldDecisionsProvider();
        var bindings = new[] { _metadataFields.TitleAlbumBinding };

        foreach (var field in _metadataFields.Fields.Append(_isbnField))
        {
            field.SetCandidates(ConceptMetadataFieldCandidateComposer.Build(
                field,
                _metadataState,
                snapshots,
                decisions,
                _genres,
                _candidateSession,
                _candidateService,
                bindings));
        }
        var allCandidates = _candidateService.Build(snapshots);
        _seriesFieldState.SetCandidates(_seriesFieldState.BuildCandidates(allCandidates));
        _abridgedStatusFieldState.SetCandidates(_abridgedStatusFieldState.BuildCandidates(allCandidates));
        _workTypeFieldState.SetCandidates(_workTypeFieldState.BuildCandidates(allCandidates));
    }

    private void QueueMetadataFieldRefresh()
    {
        if (_metadataRefreshQueued || Dispatcher.HasShutdownStarted)
            return;

        _metadataRefreshQueued = true;
        _ = Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Background,
            new Action(() =>
            {
                _metadataRefreshQueued = false;
                RefreshEmbeddedMetadataFields();
            }));
    }

    private void MetadataState_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ProjectMetadataState.CanonicalMetadata) &&
            !ReferenceEquals(_observedCanonicalMetadata, _metadataState.CanonicalMetadata))
        {
            _observedCanonicalMetadata.PropertyChanged -= CanonicalMetadata_PropertyChanged;
            _observedCanonicalMetadata = _metadataState.CanonicalMetadata;
            _observedCanonicalMetadata.PropertyChanged += CanonicalMetadata_PropertyChanged;
            QueueMetadataFieldRefresh();
        }

        if (e.PropertyName is nameof(ProjectMetadataState.CoverSourcePath) or
            nameof(ProjectMetadataState.ProcessedCoverPath) or
            nameof(ProjectMetadataState.CoverPreviewSource))
        {
            _selectedCoverSourcePath = _metadataState.CoverSourcePath;
            _processedCoverPath = _metadataState.ProcessedCoverPath;
            SetCoverPreview(_metadataState.CoverPreviewSource);
        }

        if (e.PropertyName is nameof(ProjectMetadataState.BookTitle) or
            nameof(ProjectMetadataState.Album) or
            nameof(ProjectMetadataState.Author) or
            nameof(ProjectMetadataState.Narrator) or
            nameof(ProjectMetadataState.Genre) or
            nameof(ProjectMetadataState.Series))
        {
            QueueMetadataFieldRefresh();
        }
    }

    private void CanonicalMetadata_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ProjectMetadataField.Description) or
            nameof(ProjectMetadataField.Series) or
            nameof(ProjectMetadataField.AbridgedStatus) or
            nameof(ProjectMetadataField.AudioWorkType) or
            nameof(ProjectMetadataField.Year) or
            nameof(ProjectMetadataField.ISBN))
        {
            QueueMetadataFieldRefresh();
        }
    }

    private void DiscProjectSetupDialog_Activated(object? sender, EventArgs e)
    {
        if (!_isInitializing)
            QueueMetadataFieldRefresh();
    }

    private void OnPropertyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private enum SetupDraftLeaveIntent
    {
        Exit,
        Metadata
    }

    internal static ConceptMetadataFieldViewModel[] GetActiveSetupDrafts(IEnumerable<ConceptMetadataFieldViewModel> fields) =>
        fields.Where(field => field.HasDraft).ToArray();

    internal static void DiscardSetupDrafts(IEnumerable<ConceptMetadataFieldViewModel> fields)
    {
        foreach (var field in fields.Where(field => field.HasDraft))
            field.DiscardDraft();
    }

    private sealed record SetupDraftParticipant(string Label, Func<bool> HasDraft, Action DiscardDraft);

    private SetupDraftParticipant[] GetActiveSetupDraftParticipants()
    {
        var participants = _metadataFields.Fields
            .Select(field => new SetupDraftParticipant(field.Label, () => field.HasDraft, field.DiscardDraft))
            .ToList();
        participants.Add(new SetupDraftParticipant(_isbnField.Label, () => _isbnField.HasDraft, _isbnField.DiscardDraft));
        participants.Add(new SetupDraftParticipant(_abridgedStatusFieldState.Label, () => _abridgedStatusFieldState.HasDraft, _abridgedStatusFieldState.DiscardDraft));
        participants.Add(new SetupDraftParticipant(_workTypeFieldState.Label, () => _workTypeFieldState.HasDraft, _workTypeFieldState.DiscardDraft));
        participants.Add(new SetupDraftParticipant(_yearFieldState.Label, () => _yearFieldState.HasDraft, _yearFieldState.DiscardDraft));
        participants.Add(new SetupDraftParticipant(_seriesFieldState.SeriesLabel, () => _seriesFieldState.HasSeriesNameDraft, _seriesFieldState.DiscardDraft));
        participants.Add(new SetupDraftParticipant(_seriesFieldState.SeriesPartLabel, () => _seriesFieldState.HasSeriesPartDraft, _seriesFieldState.DiscardDraft));
        participants.Add(new SetupDraftParticipant(_descriptionFieldState.DescriptionLabel, () => _descriptionFieldState.HasDraft, _descriptionFieldState.DiscardDraft));
        return participants.Where(item => item.HasDraft()).ToArray();
    }

    private bool TryResolveSetupDraftsBeforeLeaving(SetupDraftLeaveIntent intent)
    {
        var activeDrafts = GetActiveSetupDraftParticipants();
        if (activeDrafts.Length == 0)
            return true;

        if (!_metadataPreferences.WarnBeforeDiscardingConceptDrafts)
        {
            foreach (var draft in activeDrafts)
                draft.DiscardDraft();
            RefreshEmbeddedMetadataFields();
            return true;
        }

        var continueText = intent == SetupDraftLeaveIntent.Metadata
            ? LocalizationText.Get("Disc.Setup.DiscardAndOpenMetadata")
            : LocalizationText.Get("Disc.Setup.CloseAnyway");
        var result = AppDialogService.Show(
            this,
            "BookStitch",
            LocalizationText.Get("Disc.Setup.UnconfirmedInput"),
            ConceptDraftWarningPresentation.BuildMessage(activeDrafts.Select(draft => draft.Label)),
            AppDialogKind.Warning,
            buttons:
            [
                new AppDialogButton(LocalizationText.Get("Disc.Setup.Back"), AppDialogResult.Cancel, IsDefault: true, IsCancel: true),
                new AppDialogButton(continueText, AppDialogResult.Yes, IsDanger: true)
            ]);
        if (result != AppDialogResult.Yes)
            return false;

        foreach (var draft in activeDrafts)
            draft.DiscardDraft();
        RefreshEmbeddedMetadataFields();
        return true;
    }

    private void DiscProjectSetupDialog_Closing(object? sender, CancelEventArgs e)
    {
        if (_setupDraftExitApproved)
            return;

        if (!TryResolveSetupDraftsBeforeLeaving(SetupDraftLeaveIntent.Exit))
        {
            e.Cancel = true;
            return;
        }

        CaptureResult(validateForStart: false);
        _setupDraftExitApproved = true;
    }

    private void PreviewAppearanceChanged(object? sender, EventArgs e)
    {
        if (Dispatcher.CheckAccess() && IsLoaded && string.IsNullOrWhiteSpace(_metadataState.ProcessedCoverPath) && !File.Exists(_selectedCoverSourcePath))
            SetDefaultCoverPreview();
    }

    private void DiscProjectSetupDialog_Closed(object? sender, EventArgs e)
    {
        WeakEventManager<DeveloperAppearancePreview, EventArgs>.RemoveHandler(DeveloperToolsCommands.AppearancePreview,
            nameof(DeveloperAppearancePreview.Changed), PreviewAppearanceChanged);
        if (_settingsManager is not null)
        {
            _settingsManager.DriveCircleChanged -= GlobalSettingsManager_DriveCircleChanged;
            _settingsManager.GenreOptionsChanged -= GlobalSettingsManager_GenreOptionsChanged;
        }
        ProjectSetupPageNavigationBridge.Unregister(this);
        _metadataState.PropertyChanged -= MetadataState_PropertyChanged;
        _observedCanonicalMetadata.PropertyChanged -= CanonicalMetadata_PropertyChanged;
        Activated -= DiscProjectSetupDialog_Activated;
        Closing -= DiscProjectSetupDialog_Closing;
    }

    private void CoverBorder_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        ChooseCover();
    }

    private void CoverBorder_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender is Border { ContextMenu: { } contextMenu } &&
            contextMenu.Items.OfType<MenuItem>().FirstOrDefault() is { } removeCoverItem)
        {
            removeCoverItem.IsEnabled = !string.IsNullOrWhiteSpace(_processedCoverPath);
        }
    }

    private void RemoveCover_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_processedCoverPath))
            return;

        try
        {
            EmbeddedCoverService.RecordUserRemoval(_metadataState.CanonicalMetadata, _selectedCoverSourcePath);
        }
        catch (Exception ex)
        {
            AppDialogService.Error(this, LocalizationText.Get("Disc.Setup.CoverFailed.Title"),
                LocalizationText.Get("Disc.Setup.CoverFailed.Message"), new[] { ex.Message });
            return;
        }
        _selectedCoverSourcePath = "";
        _processedCoverPath = "";
        _metadataState.ApplyCoverMetadata("", "", MetadataOrigin.User);
        SetDefaultCoverPreview();
    }

    private void ChooseCover_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        ChooseCover();
    }

    private void ChooseCover()
    {
        if (_isCoverDialogOpen)
            return;

        _isCoverDialogOpen = true;
        try
        {
            var selectedFile = _coverPickerService.ChooseCoverFile(
                this,
                new CoverPickerContext(
                    _request.SourceKind,
                    _request.SourceFolder,
                    _selectedCoverSourcePath,
                    _request.LastCoverFolder));
            if (string.IsNullOrWhiteSpace(selectedFile))
                return;

            var selectedFolder = Path.GetDirectoryName(selectedFile);
            if (!string.IsNullOrWhiteSpace(selectedFolder) && Directory.Exists(selectedFolder))
                _setLastCoverFolder?.Invoke(selectedFolder);

            ProcessSelectedCover(selectedFile);
        }
        finally
        {
            Dispatcher.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.ApplicationIdle,
                new System.Action(() => _isCoverDialogOpen = false));
        }
    }

    private void ProcessSelectedCover(string sourcePath)
    {
        try
        {
            var result = _coverImageService.CreateProcessedCover(
                sourcePath,
                _coverWorkFolder,
                _metadataState.BookTitle.Trim(),
                _getCoverWarningThresholdPixels());
            EmbeddedCoverService.RecordUserSelection(_metadataState.CanonicalMetadata, result.SourcePath);
            _selectedCoverSourcePath = result.SourcePath;
            _processedCoverPath = result.ProcessedJpegPath;
            _metadataState.ApplyCoverMetadata(result.SourcePath, result.ProcessedJpegPath, MetadataOrigin.User);
            SetCoverPreview(_metadataState.CoverPreviewSource);

            if (!string.IsNullOrWhiteSpace(result.Warning))
            {
                AppDialogService.CoverWarning(
                    this,
                    LocalizationText.Get("Disc.Setup.CoverAccepted"),
                    result.Warning,
                    _getCoverWarningThresholdPixels(),
                    _setCoverWarningThresholdPixels);
            }
        }
        catch (Exception ex)
        {
            AppDialogService.Error(
                this,
                LocalizationText.Get("Disc.Setup.CoverFailed.Title"),
                LocalizationText.Get("Disc.Setup.CoverFailed.Message"),
                new[] { ex.Message });
        }
    }

    private void SetCoverPreview(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            SetDefaultCoverPreview();
            return;
        }

        try
        {
            var image = new BitmapImage();
            if (File.Exists(filePath)) CoverInputSafety.ValidateFile(filePath);
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = File.Exists(filePath)
                ? new Uri(filePath, UriKind.Absolute)
                : new Uri(
                    CoverPreviewPresentationService.GetPlaceholderSource(_getAppearanceMode()),
                    UriKind.Relative);
            image.EndInit();
            image.Freeze();

            CoverPreviewImage.Source = image;
            CoverHintBorder.Visibility = File.Exists(filePath)
                ? Visibility.Collapsed
                : Visibility.Visible;
        }
        catch
        {
            SetDefaultCoverPreview();
        }
    }

    private void SetDefaultCoverPreview()
    {
        CoverPreviewImage.Source = TryFindResource(AppearanceSettingsService.CoverPlaceholderImageSourceKey) as ImageSource ??
            LoadCoverPlaceholderFallback();
        CoverHintBorder.Visibility = Visibility.Visible;
    }

    private ImageSource LoadCoverPlaceholderFallback()
    {
        var image = new BitmapImage(
            new Uri(
                CoverPreviewPresentationService.GetPlaceholderSource(_getAppearanceMode()),
                UriKind.Relative));
        image.Freeze();
        return image;
    }

    private void MetadataButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryResolveSetupDraftsBeforeLeaving(SetupDraftLeaveIntent.Metadata))
            return;

        _openMetadataDialog?.Invoke(this);
        RefreshEmbeddedMetadataFields();
        e.Handled = true;
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        if (_openAdvancedSettings is not null)
        {
            var settings = _openAdvancedSettings(this);
            AutoMergeCheckBox.IsChecked = settings.AutoMergeAfterConversion;
            DriveCircleCheckBox.IsChecked = _settingsManager?.Current.ExperimentalDriveRoundEnabled == true;
            SelectOutputExtension(settings.OutputExtension);
            FileNameTemplateTextBox.Text = settings.FileNameTemplate;
            SetOutputFolder(settings.OutputFolder, persistGlobally: false);
            UpdatePreview();
            if (string.IsNullOrWhiteSpace(_metadataState.ProcessedCoverPath))
                SetDefaultCoverPreview();
        }
        Activate();
        Focus();
    }

    private void ProjectFolderButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_request.SourceFolder) || !Directory.Exists(_request.SourceFolder))
            return;

        if(ProjectFolderOpenService.IsManagedFolder(_request.SourceFolder))
        {
            ProjectFolderOpenService.Open(this,_request.SourceFolder,_settingsManager);
            return;
        }

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = _request.SourceFolder,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            AppDialogService.Warning(this, LocalizationText.Get("Disc.Setup.SourceFolderOpenFailed"), ex.Message);
        }
    }


    private static ProjectMetadataState CreateMetadataState(ProjectSetupDialogRequest request)
    {
        var metadata = new ProjectMetadata();
        SetSourceScalar(metadata, ProjectMetadataField.Title, request.BookTitle);
        SetSourceScalar(metadata, ProjectMetadataField.Album, request.Album);
        SetSourceList(metadata, ProjectMetadataField.Authors, request.Author);
        SetSourceList(metadata, ProjectMetadataField.Narrators, request.Narrator);
        SetSourceList(
            metadata,
            ProjectMetadataField.Genres,
            GenreListService.IsSelectableGenre(request.Genre)
                ? request.Genre
                : GenreListService.GetDefaultGenre(request.UsePrivateGenreList));

        var coverSourcePath = AppearanceAssetCatalog.IsCoverPlaceholderPath(request.CoverSourcePath)
            ? ""
            : request.CoverSourcePath ?? "";
        var previewSource = AppearanceAssetCatalog.IsCoverPlaceholderPath(request.CoverPreviewSource)
            ? ""
            : request.CoverPreviewSource ?? "";
        var state = new ProjectMetadataState
        {
            CoverSourcePath = coverSourcePath,
            ProcessedCoverPath = !string.IsNullOrWhiteSpace(previewSource) && File.Exists(previewSource)
                ? previewSource
                : "",
            CoverPreviewSource = previewSource
        };
        state.ApplyCanonicalMetadata(metadata);
        return state;
    }

    private static void SetSourceScalar(ProjectMetadata metadata, ProjectMetadataField field, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            metadata.SetValue(field, value, MetadataOrigin.SourceAggregation);
    }

    private static void SetSourceList(ProjectMetadata metadata, ProjectMetadataField field, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            metadata.SetValue<IReadOnlyList<string>>(
                field,
                [value.Trim()],
                MetadataOrigin.SourceAggregation);
        }
    }

    private void AlignManagedCoverWithCommittedTitle()
    {
        if (string.IsNullOrWhiteSpace(_processedCoverPath) || !File.Exists(_processedCoverPath))
            return;

        var alignedPath = _coverImageService.EnsureManagedCoverForTitle(_processedCoverPath, _metadataState.BookTitle);
        if (string.Equals(alignedPath, _processedCoverPath, StringComparison.OrdinalIgnoreCase))
            return;

        _processedCoverPath = alignedPath;
        var origin = _metadataState.CanonicalMetadata.Cover.Origin;
        _metadataState.ApplyCoverMetadata(_selectedCoverSourcePath, alignedPath, origin);
        SetCoverPreview(_metadataState.CoverPreviewSource);
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }

    private void SortTracksButton_Click(object sender, RoutedEventArgs e)
    {
        CaptureResult(validateForStart: false);
        if (!TryResolveSetupDraftsBeforeLeaving(SetupDraftLeaveIntent.Exit))
            return;

        _setupDraftExitApproved = true;
        DialogResult = false;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryResolveSetupDraftsBeforeLeaving(SetupDraftLeaveIntent.Exit))
            return;

        CaptureResult(validateForStart: false);
        _setupDraftExitApproved = true;
        DialogResult = false;
        Close();
    }

    private void ParallelJobsTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        e.Handled = !e.Text.All(ch => char.IsDigit(ch) || char.IsLetter(ch));
    }

    private void ParallelJobsTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Up)
        {
            StepParallelJobs(+1);
            e.Handled = true;
        }
        else if (e.Key == Key.Down)
        {
            StepParallelJobs(-1);
            e.Handled = true;
        }
    }

    private void ParallelJobsTextBox_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        StepParallelJobs(e.Delta > 0 ? +1 : -1);
        e.Handled = true;
    }

    private void GlobalSettingsManager_GenreOptionsChanged(object? sender, EventArgs e)
    {
        _genres = GenreListService.GetGenres(_settingsManager!.Current);
        RefreshEmbeddedMetadataCandidates();
    }

    private void GlobalSettingsManager_DriveCircleChanged(object? sender, EventArgs e)
        => DriveCircleCheckBox.IsChecked = _settingsManager!.DriveCircleEnabled;

    private void DriveCircleCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_isInitializing || _settingsManager is null) return;
        _settingsManager.DriveCircleEnabled = DriveCircleCheckBox.IsChecked == true;
    }

    private void AutoMergeCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_isInitializing)
            return;

        _setAutoMerge?.Invoke(AutoMergeCheckBox.IsChecked == true);
    }

    private void IncreaseParallelJobs_Click(object sender, RoutedEventArgs e)
    {
        StepParallelJobs(+1);
    }

    private void DecreaseParallelJobs_Click(object sender, RoutedEventArgs e)
    {
        StepParallelJobs(-1);
    }

    private static string GetProposedText(TextBox textBox, string input)
    {
        var text = textBox.Text ?? string.Empty;
        var selectionStart = Math.Clamp(textBox.SelectionStart, 0, text.Length);
        var selectionLength = Math.Clamp(textBox.SelectionLength, 0, text.Length - selectionStart);
        return text.Remove(selectionStart, selectionLength).Insert(selectionStart, input);
    }

    private void StepParallelJobs(int delta)
    {
        var text = ParallelJobsTextBox.Text.Trim();

        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            value = delta > 0 ? 0 : 2;

        value = Math.Clamp(value + delta, 1, 40);
        ParallelJobsTextBox.Text = value.ToString(CultureInfo.InvariantCulture);
        ParallelJobsTextBox.SelectAll();
    }

    private void MetadataComboBox_KeyUp(object sender, KeyEventArgs e)
    {
        SyncFileNameTemplateIfNeeded(sender);
        UpdatePreview();
    }

    private void MetadataComboBox_LostFocus(object sender, RoutedEventArgs e)
    {
        SyncFileNameTemplateIfNeeded(sender);
        UpdatePreview();
    }

    private void MetadataComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        SyncFileNameTemplateIfNeeded(sender);
        UpdatePreview();
    }

    private void SyncFileNameTemplateIfNeeded(object sender)
    {
        if (_isInitializing || !ReferenceEquals(sender, FileNameTemplateTextBox))
            return;

        var template = GetComboBoxText(FileNameTemplateTextBox).Trim();
        if (!string.IsNullOrWhiteSpace(template))
            _setFileNameTemplate?.Invoke(template);
    }

    private void PresetComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing)
            return;

        ErrorText.Visibility = Visibility.Collapsed;

        var selectedPreset = PresetComboBox.SelectedItem?.ToString();
        if (string.IsNullOrWhiteSpace(selectedPreset))
            return;

        var confirmedPreset = ConfirmPresetBitrateIfNeeded(selectedPreset);
        if (!string.Equals(confirmedPreset, selectedPreset, StringComparison.OrdinalIgnoreCase))
        {
            _isInitializing = true;
            PresetComboBox.SelectedItem = confirmedPreset;
            _isInitializing = false;
        }

        _lastAcceptedPreset = confirmedPreset;
        AcceptedPresetChanged?.Invoke(confirmedPreset);
    }

    private string ConfirmPresetBitrateIfNeeded(string requestedPreset)
    {
        if (_maxSourceBitrateKbps is not > 0)
            return requestedPreset;

        var requested = ExportPreset.Parse(requestedPreset);
        if (requested.BitrateKbps <= _maxSourceBitrateKbps.Value)
            return requestedPreset;

        var recommendedPreset = FindBestPresetForSourceBitrate(_maxSourceBitrateKbps.Value, requested.Channels);
        var recommended = ExportPreset.Parse(recommendedPreset);

        var result = AppDialogService.Show(
            this,
            LocalizationText.Get("Disc.Setup.PresetWarning.Title"),
            LocalizationText.Get("Disc.Setup.PresetWarning.Heading"),
            LocalizationText.Format("Disc.Setup.PresetWarning.Message", _maxSourceBitrateKbps.Value, requested.BitrateKbps),
            AppDialogKind.Warning,
            details: new[]
            {
                LocalizationText.Format("Disc.Setup.PresetWarning.Recommended", recommended.DisplayName),
                LocalizationText.Format("Disc.Setup.PresetWarning.Selected", requested.DisplayName)
            },
            buttons: new[]
            {
                new AppDialogButton(LocalizationText.Format("Disc.Setup.PresetWarning.UseRecommended", recommended.DisplayName), AppDialogResult.Yes, IsPrimary: true, IsDefault: true),
                new AppDialogButton(LocalizationText.Get("Disc.Setup.PresetWarning.UseAnyway"), AppDialogResult.No),
                new AppDialogButton(LocalizationText.Get("Common.Cancel"), AppDialogResult.Cancel, IsCancel: true)
            });

        return result switch
        {
            AppDialogResult.Yes => recommendedPreset,
            AppDialogResult.No => requestedPreset,
            _ => string.IsNullOrWhiteSpace(_lastAcceptedPreset) ? requestedPreset : _lastAcceptedPreset
        };
    }

    private string FindBestPresetForSourceBitrate(int maxSourceBitrateKbps, int preferredChannels)
    {
        var parsedPresets = _exportPresets
            .Select(ExportPreset.Parse)
            .OrderBy(preset => preset.BitrateKbps)
            .ThenBy(preset => preset.Channels)
            .ToList();

        var sameChannels = parsedPresets
            .Where(preset => preset.Channels == preferredChannels && preset.BitrateKbps <= maxSourceBitrateKbps)
            .OrderByDescending(preset => preset.BitrateKbps)
            .FirstOrDefault();

        if (sameChannels is not null)
            return sameChannels.DisplayName;

        var anyChannels = parsedPresets
            .Where(preset => preset.BitrateKbps <= maxSourceBitrateKbps)
            .OrderByDescending(preset => preset.BitrateKbps)
            .FirstOrDefault();

        return (anyChannels ?? parsedPresets.First()).DisplayName;
    }

    private void OutputExtensionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isInitializing)
            _setOutputExtension?.Invoke(GetSelectedOutputExtension());

        UpdatePreview();
    }

    private void UpdatePreview()
    {
        if (_isInitializing || PreviewTextBlock is null)
            return;

        PreviewTextBlock.Text = FileNameTemplateService.BuildOutputFileName(
            _metadataState.BookTitle,
            _metadataState.Author,
            _metadataState.Narrator,
            string.IsNullOrWhiteSpace(GetComboBoxText(FileNameTemplateTextBox)) ? "{Autor} - {Titel}" : GetComboBoxText(FileNameTemplateTextBox),
            GetSelectedOutputExtension(),
            _metadataState.Album);
    }

    private static string GetComboBoxText(ComboBox comboBox)
    {
        if (comboBox.SelectedItem is ComboBoxItem item)
            return item.Content?.ToString() ?? "";

        if (comboBox.SelectedItem is string selectedText)
            return selectedText;

        return comboBox.Text ?? "";
    }
}
