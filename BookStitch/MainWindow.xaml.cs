using BookStitch.Controls;
using BookStitch.Dialog;
using BookStitch.Models;
using BookStitch.Models.TrackOrdering;
using BookStitch.Presentation;
using BookStitch.Services;
using BookStitch.Services.TrackOrdering;
using Microsoft.Win32;
using System.Diagnostics;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Animation;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using WinForms = System.Windows.Forms;

namespace BookStitch;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private readonly record struct VisibleTrackGridRow(TrackInfo Track, TrackEmbeddedChapterDisplayInfo? Chapter)
    {
        public static VisibleTrackGridRow ForTrack(TrackInfo track) => new(track, null);

        public static VisibleTrackGridRow ForChapter(TrackInfo track, TrackEmbeddedChapterDisplayInfo chapter) => new(track, chapter);
    }

    private static readonly TimeSpan ProjectSnapshotInterval = TimeSpan.FromSeconds(30);

    private enum TrackAnalysisCancellationScope
    {
        None,
        LocalFolderImport,
        LocalFolderExtension,
        ResumeLoad
    }

    private readonly MainWindowServiceComposition _services;
    private readonly MainWindowRuntimeComposition _runtimeServices;
    private readonly TrackStateUpdateQueueService _trackStateUpdateQueueService;
    private readonly DispatcherTimer _chapterMetadataRefreshTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(250)
    };
    private readonly DispatcherTimer _projectSetupDiscCountRefreshTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(120)
    };
    private readonly Stopwatch _startupPerformanceStopwatch = Stopwatch.StartNew();
    private string _startupPerformanceDetails = string.Empty;
    private readonly Presentation.MainWindowPresentationState _presentationState = new();
    private readonly ChapterSettingsEditorService _chapterSettingsEditorService = new();
    private bool _isRefreshingMainChapterSettingsEditor;

    private AppSettings _settings = new();
    private CoverPickerService _coverPickerService = null!;
    private DebugSettings _debugSettings = new();
    private DebugWindowPlacementService? _debugWindowPlacementService;
    private DeveloperToolsWindow? _developerToolsWindow;
    private FfmpegToolStatus _ffmpegStatus = new();
    private bool _isLoadingSettings;
    private bool _suppressPresetBitrateWarning;
    private bool _isApplyingExportPresetSelectionToUi;
    private Func<Task>? _pausedProjectExtensionContinuation;
    private Action? _pausedProjectExtensionStopAction;
    private PresetReconversionRollbackContext? _presetReconversionRollbackContext;
    private ProjectExtensionRollbackSnapshot? _projectExtensionRollbackSnapshot;
    private TimeSpan _audioDiscElapsedBeforeCurrentRun;
    private TimeSpan _audioDiscCurrentRunElapsed;
    private int? _audioDiscElapsedDiscNumber;
    private string _audioDiscElapsedProjectFolder = string.Empty;
    private CancellationTokenSource? _exportCancellation;
    private string? _lockedDiscResumePreset;
    private readonly DiscResumePreviewState _resumeDiscPreview = new();
    private bool _resumeDiscSourceSelectionRequired;
    private string? _resumeDiscStartDriveRoot;
    private bool _isDiscStopSaveInProgress;
    private CancellationTokenSource? _discImportCancellation;
    private CancellationTokenSource? _trackAnalysisCancellation;
    private TrackAnalysisCancellationScope _trackAnalysisCancellationScope;
    private Guid _workflowStatusOperationId;
    private Mp3DiscProjectManifest? _activeMp3DiscManifest;
    private readonly Mp3DiscChapterSourceCatalog _mp3DiscChapterSourceCatalog = new();
    private Mp3DiscPendingChapterDisc? _transientMp3DiscChapterPreview;
    private ChapterAutoEngineResult? _currentAutoChapterResult;
    private ActiveChapterResult? _currentActiveChapterResult;
    private IReadOnlyList<MetadataFieldDecisionDiagnostic> _latestMetadataFieldDecisions = [];
    private string _latestMetadataDiagnosisProjectFolder = string.Empty;
    private bool _isShutdownInProgress;
    private bool _allowWindowClose;
    private bool _windowCloseCleanupCompleted;
    private PerformanceOperationScope _shutdownPerformanceScope;
    private bool _isApplyingTrackGridColumnLayout;
    private bool _trackGridContextMenuForHeader;
    private readonly HashSet<Grid> _embeddedChapterRowGrids = [];
    private string _embeddedChapterColumnLayoutSignature = string.Empty;
    private bool _isCoverDialogOpen;
    private string _trackGridSortKey = string.Empty;
    private ListSortDirection _trackGridSortDirection = ListSortDirection.Descending;
    private TrackInfo? _lastFileWarningJumpTrack;
    private TrackInfo? _lastChapterWarningJumpTrack;
    private TrackEmbeddedChapterDisplayInfo? _activeEmbeddedChapterRow;
    private bool _isUpdatingTrackSelectionFromChapterNavigation;
    private static readonly TimeSpan TrackGridAutoScrollInterval = TimeSpan.FromMilliseconds(16);
    private static readonly TimeSpan TrackGridAutoScrollMaxElapsed = TimeSpan.FromMilliseconds(50);
    private readonly DispatcherTimer _trackGridAutoScrollTimer = new(DispatcherPriority.Render) { Interval = TrackGridAutoScrollInterval };
    private readonly Stopwatch _trackGridAutoScrollClock = new();
    private bool _isTrackGridAutoScrollActive;
    private Point _trackGridAutoScrollAnchor;
    private Point _trackGridAutoScrollCurrent;
    private TimeSpan _trackGridAutoScrollLastTimestamp;
    private Cursor? _trackGridCursorBeforeAutoScroll;

    private static string NoProjectStatusText => LocalizationText.Get("MainWindow.General.ReadyForProject");

    private string _selectedFolder = string.Empty;
    private string _selectedSourceDisplayOverride = "";
    private string _selectedSourceOpenPathOverride = "";
    private string _statusText = NoProjectStatusText;
    private string _currentFolderPath = "";
    private string _currentProjectWorkFolder = "";
    private readonly SessionProjectLifecycle _sessionProject = new();
    private readonly Guid _processingRunId = Guid.NewGuid();
    private DeferredProjectIdentity? _activeLocalPreparation;
    private bool _isWorkspaceReplacementInProgress;
    private int _workspaceReplacementVersion;
    private bool _isProjectCloseInProgress;
    private bool _isResumeLoading;
    private ProjectMaintenanceCoordinator? _maintenanceCoordinator;
    private int _openSettingsContexts;
    private int _openMetadataContexts;
    private bool _mainWindowReady;
    private bool _isProjectBrowserOpen;
    private long _displayedProjectCatalogGeneration = -1;
    private string _pendingDiscProjectSourceFolder = "";
    private DiscProjectSetupResult? _pendingMp3DiscSetupResult;
    private DiscSourceQuickIdentity? _pendingMp3DiscQuickIdentity;
    private string _pendingMp3DiscStructureSignature = "";
    private AudioDiscInfo? _pendingAudioDisc;
    private int _pendingAudioDiscTotalDiscs = 1;
    private int? _previewEffectiveTotalDiscs;
    private bool _isAudioDiscProjectAwaitingRip;
    private AudioDiscProjectManifest? _activeAudioDiscManifest;
    private LoadedProjectStateSnapshot _loadedProjectState = LoadedProjectStateSnapshot.None;
    private int _mp3DiscBackgroundPresetPreparationActive;
    private ProjectReviewStateSnapshot _projectReviewState =
        new(ProjectPipelineState.Preparing, false, false);
    private string _manualMergeReviewPreparedPreset = "";
    private bool _manualMergeReviewNeedsReconversion;
    private bool _manualMergeReviewHasPreparedTracks;

    private string _outputFolder = "";
    private readonly ProjectMetadataState _projectMetadata = new();
    private readonly ProjectMetadataSession _projectMetadataSession = new();
    private MetadataDialog? _developmentMetadataDialog;
    private readonly ChapterGridColumnLayoutState _chapterGridColumnLayoutState = new();
    private bool _suppressProjectMetadataStateNotifications;
    private bool _isWorkspaceTransitioning;
    private bool _integritySweepRunning;
    private string _lastObservedProjectMetadataBookTitle = "";
    private string _replaceableGenreSeed = "";

    private bool _isMetadataPanelExpanded;
    private bool _isMetadataEditingAvailable;
    private string _outputExtension = ".m4a";
    private string _fileNameTemplate = "{Autor} - {Titel}";
    private string _selectedExportPreset = ExportPreset.DefaultDisplayName;
    private string _parallelJobsInput = "Auto";
    private double _exportProgressPercent;
    private string _exportProgressForeground = "#16858A";
    private bool _isProgressIndeterminate;
    private static string DiscAnalysisReadingText => LocalizationText.Get("MainWindow.DiscAnalysis.Reading");
    private static string DiscAnalysisSlowText => LocalizationText.Get("MainWindow.DiscAnalysis.Slow");

    private bool _isDiscSourceAnalysisActive;
    private string _discSourceAnalysisText = DiscAnalysisReadingText;
    private string _exportProgressText = LocalizationText.Format("MainWindow.Progress.Idle", 0);
    private ProjectIndexItem? _selectedResumeProject;

    public BulkObservableCollection<TrackInfo> Tracks { get; } = [];
    public ObservableCollection<ProjectIndexItem> ResumeProjects { get; } = [];

    public IReadOnlyList<string> ExportPresets { get; } = ExportPreset.AvailableDisplayNames;

    public bool IsBusy
    {
        get => _presentationState.IsBusy;
        set
        {
            var change = MainWindowSelectionStateService.CreateBooleanChange(
                _presentationState.IsBusy,
                value);

            if (!change.HasChanged)
                return;

            _presentationState.IsBusy = change.Value;
            if (change.Value)
                StopTrackGridAutoScroll();

            OnPropertyChanged();
            NotifyExportUiStateChanged();
            RefreshSystemSleepPrevention();
        }
    }

    public bool IsExporting
    {
        get => _presentationState.IsExporting;
        set
        {
            var change = MainWindowSelectionStateService.CreateBooleanChange(
                _presentationState.IsExporting,
                value);

            if (!change.HasChanged)
                return;

            _presentationState.IsExporting = change.Value;
            OnPropertyChanged();
            NotifyExportUiStateChanged();
            RefreshSystemSleepPrevention();
        }
    }

    public bool IsDiscImporting
    {
        get => _presentationState.IsDiscImporting;
        set
        {
            var change = MainWindowSelectionStateService.CreateBooleanChange(
                _presentationState.IsDiscImporting,
                value);

            if (!change.HasChanged)
                return;

            _presentationState.IsDiscImporting = change.Value;
            OnPropertyChanged();
            NotifyExportUiStateChanged();
            RefreshSystemSleepPrevention();
        }
    }

    private ExportUiStateSnapshot ExportUiState =>
        _services.Status.ExportUiStateService.Create(
            ExportUiStateInputService.Create(
                new ExportUiStateSource(
                    ProjectReviewState.Phase,
                    new PipelineExecutionState(
                        IsBusy || _isDiscStopSaveInProgress || _isProjectCloseInProgress || _isWorkspaceTransitioning,
                        _presentationState.IsPipelinePaused,
                        _presentationState.IsPipelineFailed,
                        _presentationState.IsProjectExtensionRun),
                    _manualMergeReviewNeedsReconversion,
                    _manualMergeReviewHasPreparedTracks,
                    _ffmpegStatus.FfmpegAvailable,
                    LoadedProjectState.ToPipelineProjectState(),
                    Tracks.Count,
                    EffectiveOutputFolder,
                    ExportProgressPercent,
                    SelectedResumeProject,
                    _trackAnalysisCancellation is { IsCancellationRequested: false } &&
                    _trackAnalysisCancellationScope != TrackAnalysisCancellationScope.None,
                    _presetReconversionRollbackContext is not null,
                    _resumeDiscPreview.DiscNumber is not null && !_resumeDiscSourceSelectionRequired &&
                    !IsDiscImporting && !IsExporting,
                    _presentationState.PauseRequested, _presentationState.UiTransition,
                    IsDiscImporting, IsExporting, _resumeDiscPreview.ProcessingStarted)));

    public bool CanSelectFolder => ExportUiState.CanSelectFolder;
    public bool CanStartNewProject =>
        CanSelectFolder && _ffmpegStatus.FfmpegAvailable;
    public bool CanConfigureFfmpeg => ExportUiState.CanConfigureFfmpeg;
    public PrimaryButtonVisualState FolderDiscActionVisualState =>
        MainActionButtonVisualStateService.ResolveProjectAction(LoadedProjectState.HasLoadedProject, CanStartNewProject);

    public PrimaryButtonVisualState ProjectsActionVisualState =>
        MainActionButtonVisualStateService.ResolveProjectAction(LoadedProjectState.HasLoadedProject, CanRefreshResumeProjects);

    public PrimaryButtonVisualState MetadataActionVisualState =>
        MainActionButtonVisualStateService.ResolveMetadataAction(CanChangeBookMetadata);

    public string FolderButtonGlyph => UiSymbolResources.Folder;
    public string DiscButtonGlyph => UiSymbolResources.AudioDisc;
    private MainWindowSourcePresentation SourcePresentation =>
        MainWindowSourcePresentationService.Create(
            Tracks.Count,
            SelectedFolder,
            _selectedSourceDisplayOverride,
            _selectedSourceOpenPathOverride,
            Tracks.Count > 0 && (LoadedProjectState.IsAudioDisc || LoadedProjectState.IsMp3Disc));

    public bool HasProjectTracks => Tracks.Count > 0;
    public Visibility FfmpegSetupButtonVisibility =>
        !_ffmpegStatus.FfmpegAvailable || _debugSettings.ForceShowFfmpegSetupButton
            ? Visibility.Visible
            : Visibility.Collapsed;

    public double TrackGridRowHeight =>
        TrackGridRowHeightService.GetRowHeight(_debugSettings.TrackGridRowHeightLevel);
    public bool CanEditTrackOrder => ExportUiState.CanEditTrackOrder;
    public bool CanStartExport =>
        _presentationState.UiTransition is null && !_presentationState.PauseRequested &&
        !_isDiscStopSaveInProgress && (ExportUiState.CanStartExport ||
        AudioDiscExportAvailabilityService.CanStart(
            _isAudioDiscProjectAwaitingRip,
            IsBusy,
            IsExporting,
            IsDiscImporting,
            _activeAudioDiscManifest is not null,
            _ffmpegStatus.FfmpegAvailable));
    public string ExportButtonText => ExportUiState.ExportButtonText;
    public Visibility ExportButtonVisibility => ExportUiState.ExportButtonVisibility;
    public string SecondaryButtonText => ExportUiState.SecondaryButtonText;
    public Visibility SecondaryButtonVisibility => ExportUiState.SecondaryButtonVisibility;
    private bool IsNewProjectPreparation => !string.IsNullOrWhiteSpace(_sessionProject.ProjectFolder) &&
        TrackPathService.PathEquals(_sessionProject.ProjectFolder, _currentProjectWorkFolder) &&
        ProjectReviewState.Phase is ProjectPipelineState.AcquiringSources or ProjectPipelineState.Converting;
    public bool CanDeletePausedNewProject => _presentationState.UiTransition is null && !_isDiscStopSaveInProgress && !_isProjectCloseInProgress &&
        _presentationState.IsPipelinePaused && !IsBusy && IsNewProjectPreparation &&
        _sessionProject.ProjectType == ProjectManifestTypes.FolderProject &&
        !_presentationState.IsProjectExtensionRun && _presetReconversionRollbackContext is null;

    public bool CanCancelExport => !_isDiscStopSaveInProgress && ExportUiState.CanCancelExport;
    public bool CanStopAndSaveDiscProject => _presentationState.UiTransition is null && _presentationState.IsPipelinePaused &&
        !IsBusy && !_isDiscStopSaveInProgress &&
        (LoadedProjectState.IsAudioDisc || LoadedProjectState.IsMp3Disc) &&
        DiscProjectStopSaveService.CanOfferImportSave(ProjectReviewState.Phase,
            LoadedProjectState.NeedsDiscImport || _isAudioDiscProjectAwaitingRip ||
            (_sessionProject.Owns(_currentProjectWorkFolder) && _sessionProject.StartAccepted) ||
            _activeMp3DiscManifest?.IsStoppedForResume == true || _activeAudioDiscManifest?.IsStoppedForResume == true,
            _presentationState.IsProjectExtensionRun, _presetReconversionRollbackContext is not null) &&
        new DiscProjectStopSaveService().CanSave(_currentProjectWorkFolder);
    public Visibility StopAndSaveDiscProjectVisibility => CanStopAndSaveDiscProject ||
        _presentationState.UiTransition?.Target == PipelineUiTransitionTarget.DiscStopSave ? Visibility.Visible : Visibility.Collapsed;
    public string StopAndSaveDiscProjectButtonText => _presentationState.UiTransition?.Target == PipelineUiTransitionTarget.DiscStopSave
        ? _presentationState.UiTransition.ButtonText : LocalizationText.Get("MainWindow.Action.Save");
    public Visibility DeleteNewProjectVisibility => CanDeletePausedNewProject ||
        _presentationState.UiTransition?.Target == PipelineUiTransitionTarget.DeleteNewProject ? Visibility.Visible : Visibility.Collapsed;
    public string DeleteNewProjectButtonText => _presentationState.UiTransition?.Target == PipelineUiTransitionTarget.DeleteNewProject
        ? _presentationState.UiTransition.ButtonText : LocalizationText.Get("Project.Close.Delete");
    public bool CanAddProjectSources => ExportUiState.CanAddProjectSources;
    public Visibility AddProjectSourcesVisibility => ExportUiState.AddProjectSourcesVisibility;
    public string AddProjectSourcesButtonText => ExportUiState.AddProjectSourcesButtonText;
    public bool CanChooseOutputFolder => ExportUiState.CanChooseOutputFolder;
    public bool CanOpenSelectedFolder =>
        !string.IsNullOrWhiteSpace(SelectedSourceOpenPath) && Directory.Exists(SelectedSourceOpenPath);
    public bool CanOpenOutputFolder =>
        !string.IsNullOrWhiteSpace(OutputFolder) && Directory.Exists(OutputFolder);

    private string EffectiveOutputFolder =>
        OutputFolderFallbackService.Resolve(OutputFolder);

    private OutputFolderPrimaryAction EffectiveOutputFolderPrimaryAction =>
        OutputFolderPrimaryActionService.Resolve(
            _settings.OutputFolderPrimaryAction,
            CanOpenOutputFolder);

    public bool IsOutputFolderOpenPrimaryAction =>
        EffectiveOutputFolderPrimaryAction == OutputFolderPrimaryAction.Open;

    public bool IsOutputFolderChangePrimaryAction =>
        EffectiveOutputFolderPrimaryAction == OutputFolderPrimaryAction.Change;

    public bool CanChangeExportOptions => ExportUiState.CanChangeExportOptions;
    public bool CanChangeExportPreset => string.IsNullOrWhiteSpace(_lockedDiscResumePreset) && ExportUiState.CanChangeExportPreset;

    public bool DriveCircleEnabled
    {
        get => _settings.ExperimentalDriveRoundEnabled;
        set
        {
            _services.Foundation.GlobalSettingsManager.DriveCircleEnabled = value;
        }
    }
    public Visibility DriveCircleVisibility => LoadedProjectState.IsMp3Disc || LoadedProjectState.IsAudioDisc ||
        _pendingAudioDisc is not null || !string.IsNullOrWhiteSpace(_pendingDiscProjectSourceFolder)
            ? Visibility.Visible : Visibility.Collapsed;

    public bool MergeAutomaticallyAfterConversion
    {
        get => _settings.MergeAutomaticallyAfterConversion;
        set
        {
            var change = MainWindowSelectionStateService.CreateBooleanChange(
                _settings.MergeAutomaticallyAfterConversion,
                value);

            if (!change.HasChanged)
                return;

            _settings.MergeAutomaticallyAfterConversion = change.Value;
            OnPropertyChanged();
            SaveSettingsIfReady();
        }
    }

    public bool CanOpenSettings => ExportUiState.CanOpenSettings;
    public bool CanChangeBookMetadata =>
        _isMetadataEditingAvailable && ExportUiState.CanChangeBookMetadata;
    public bool CanRemoveCover => CoverPreviewPresentationService.CanRemove(
        _projectMetadata.ProcessedCoverPath,
        CanChangeBookMetadata);
    public bool CanRefreshResumeProjects => ExportUiState.CanRefreshResumeProjects;
    public bool CanInspectSelectedResumeProject => ExportUiState.CanInspectSelectedResumeProject;
    public Visibility ExportProgressVisibility => _isResumeLoading ? Visibility.Visible : ExportUiState.ExportProgressVisibility;

    public string ResumeProjectSummary =>
        ResumeProjectSummaryService.Create(ResumeProjects.Count);

    public ProjectIndexItem? SelectedResumeProject
    {
        get => _selectedResumeProject;
        set
        {
            var change = MainWindowSelectionStateService.CreateReferenceChange(
                _selectedResumeProject,
                value);

            if (!change.HasChanged)
                return;

            _selectedResumeProject = change.Value;
            OnPropertyChanged();
            NotifyExportUiStateChanged();
        }
    }

    public string SelectedFolder
    {
        get => _selectedFolder;
        set
        {
            var change = MainWindowSelectionStateService.CreateOrdinalStringChange(
                _selectedFolder,
                value ?? string.Empty);

            if (!change.HasChanged)
                return;

            _selectedFolder = change.Value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedSourceDisplayText));
            OnPropertyChanged(nameof(SelectedSourceOpenPath));
            OnPropertyChanged(nameof(CanOpenSelectedFolder));
        }
    }

    public string SelectedSourceDisplayText => SourcePresentation.DisplayText;

    public string SelectedSourceOpenPath => SourcePresentation.OpenPath;

    private LoadedProjectStateSnapshot LoadedProjectState => _loadedProjectState;

    private ProjectReviewStateSnapshot ProjectReviewState => _projectReviewState;

    private void ApplyLoadedProjectState(LoadedProjectStateSnapshot state)
    {
        _loadedProjectState = state;
        OnPropertyChanged(nameof(FolderDiscActionVisualState));
        OnPropertyChanged(nameof(ProjectsActionVisualState));
    }

    private void ApplyProjectReviewState(ProjectReviewStateSnapshot state)
    {
        _projectReviewState = state;
        NotifyExportUiStateChanged();
        RefreshSystemSleepPrevention();
    }

    private CurrentProjectTypePresentation CurrentProjectTypePresentation =>
        CurrentProjectTypePresentationService.Create(
            LoadedProjectState,
            _activeAudioDiscManifest is not null,
            _activeMp3DiscManifest is not null,
            !string.IsNullOrWhiteSpace(_currentProjectWorkFolder));

    public string CurrentProjectTypeGlyph => CurrentProjectTypePresentation.Glyph;

    public Visibility CurrentProjectTypeGlyphVisibility => CurrentProjectTypePresentation.IsVisible
        ? Visibility.Visible
        : Visibility.Collapsed;

    public string StatusText
    {
        get => _presentationState.ProjectStatus(_statusText);
        set
        {
            var change = MainWindowSelectionStateService.CreateOrdinalStringChange(
                _statusText,
                (value ?? string.Empty));

            if (!change.HasChanged)
                return;

            _statusText = change.Value;
            OnPropertyChanged();
        }
    }

    public string OutputFolder
    {
        get => _outputFolder;
        set
        {
            var change = MainWindowSelectionStateService.CreateOrdinalStringChange(
                _outputFolder,
                (value?.Trim() ?? string.Empty));

            if (!change.HasChanged)
                return;

            _outputFolder = change.Value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanStartExport));
            OnPropertyChanged(nameof(CanOpenOutputFolder));
            OnPropertyChanged(nameof(IsOutputFolderOpenPrimaryAction));
            OnPropertyChanged(nameof(IsOutputFolderChangePrimaryAction));
            UpdateOutputFolderBadge();

            _settings.OutputFolder = _outputFolder;
            SaveSettingsIfReady();
        }
    }

    private void ProjectMetadata_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_suppressProjectMetadataStateNotifications)
            return;

        switch (e.PropertyName)
        {
            case nameof(ProjectMetadataState.CanonicalMetadata):
                SynchronizeActiveMetadataBindings();
                break;

            case nameof(ProjectMetadataState.BookTitle):
            {
                var previousTitle = _lastObservedProjectMetadataBookTitle;
                _lastObservedProjectMetadataBookTitle = _projectMetadata.BookTitle;
                OnPropertyChanged(nameof(BookTitle));
                OnPropertyChanged(nameof(MetadataSummaryTitle));
                OnPropertyChanged(nameof(MetadataSummaryDetails));
                OnPropertyChanged(nameof(OutputFileNamePreview));
                OnExportPreviewChanged();
                QueueChapterMetadataRefreshIfNeeded();
                RefreshActiveAudioDiscPreviewMetadata();
                RefreshActiveMp3DiscPreviewMetadata(previousTitle);
                AlignManagedCoverWithBookTitle();
                break;
            }

            case nameof(ProjectMetadataState.Album):
                OnPropertyChanged(nameof(Album));
                OnPropertyChanged(nameof(MetadataSummaryDetails));
                QueueChapterMetadataRefreshIfNeeded();
                break;

            case nameof(ProjectMetadataState.Author):
                OnPropertyChanged(nameof(Author));
                OnPropertyChanged(nameof(MetadataSummaryDetails));
                OnPropertyChanged(nameof(OutputFileNamePreview));
                OnExportPreviewChanged();
                RefreshActiveAudioDiscPreviewMetadata();
                break;

            case nameof(ProjectMetadataState.Narrator):
                OnPropertyChanged(nameof(Narrator));
                OnPropertyChanged(nameof(MetadataSummaryDetails));
                OnPropertyChanged(nameof(OutputFileNamePreview));
                OnExportPreviewChanged();
                break;

            case nameof(ProjectMetadataState.Genre):
                OnPropertyChanged(nameof(Genre));
                OnPropertyChanged(nameof(MetadataSummaryDetails));
                if (GenreListService.IsSelectableGenre(_projectMetadata.Genre))
                {
                    _settings.DefaultGenre = _projectMetadata.Genre;
                    SaveSettingsIfReady();
                }
                break;

            case nameof(ProjectMetadataState.CoverPreviewSource):
                OnPropertyChanged(nameof(CoverPreviewSource));
                OnPropertyChanged(nameof(CoverHintVisibility));
                break;

            case nameof(ProjectMetadataState.ProcessedCoverPath):
                OnPropertyChanged(nameof(CoverHintVisibility));
                OnPropertyChanged(nameof(CanRemoveCover));
                break;
        }
    }

    public string BookTitle
    {
        get => _projectMetadata.BookTitle;
        set
        {
            var change = MainWindowSelectionStateService.CreateOrdinalStringChange(
                _projectMetadata.BookTitle,
                value);
            if (!change.HasChanged)
                return;

            var previousTitle = _projectMetadata.BookTitle;
            _suppressProjectMetadataStateNotifications = true;
            _projectMetadata.BookTitle = change.Value;
            _lastObservedProjectMetadataBookTitle = change.Value;
            _suppressProjectMetadataStateNotifications = false;
            OnPropertyChanged();
            OnPropertyChanged(nameof(MetadataSummaryTitle));
            OnPropertyChanged(nameof(MetadataSummaryDetails));
            OnPropertyChanged(nameof(OutputFileNamePreview));

            OnExportPreviewChanged();
            QueueChapterMetadataRefreshIfNeeded();
            RefreshActiveAudioDiscPreviewMetadata();
            RefreshActiveMp3DiscPreviewMetadata(previousTitle);
            AlignManagedCoverWithBookTitle();
        }
    }

    private void AlignManagedCoverWithBookTitle()
    {
        if (_presentationState.IsProjectExtensionRun || _presetReconversionRollbackContext is not null) return;
        var currentPath = _projectMetadata.ProcessedCoverPath;
        if (string.IsNullOrWhiteSpace(currentPath) || !File.Exists(currentPath))
            return;

        try
        {
            var alignedPath = _services.Media.CoverImageService.EnsureManagedCoverForTitle(currentPath, _projectMetadata.BookTitle);
            if (string.Equals(alignedPath, currentPath, StringComparison.OrdinalIgnoreCase))
                return;

            var origin = _projectMetadata.CanonicalMetadata.Cover.Origin;
            _projectMetadata.ApplyCoverMetadata(_projectMetadata.CoverSourcePath, alignedPath, origin);
            CoverPreviewSource = alignedPath;
            TryFlushCanonicalMetadataSession();
        }
        catch (Exception ex)
        {
            _runtimeServices.DiagnosticLogService.WriteError("Verwalteter Cover-Dateiname konnte nicht an den Titel angeglichen werden", ex);
        }
    }

    public string Author
    {
        get => _projectMetadata.Author;
        set
        {
            var change = MainWindowSelectionStateService.CreateOrdinalStringChange(
                _projectMetadata.Author,
                value);
            if (!change.HasChanged)
                return;

            _suppressProjectMetadataStateNotifications = true;
            _projectMetadata.Author = change.Value;
            _suppressProjectMetadataStateNotifications = false;
            OnPropertyChanged();
            OnPropertyChanged(nameof(MetadataSummaryDetails));
            OnPropertyChanged(nameof(OutputFileNamePreview));
            OnExportPreviewChanged();
            RefreshActiveAudioDiscPreviewMetadata();
        }
    }

    public string Narrator
    {
        get => _projectMetadata.Narrator;
        set
        {
            var change = MainWindowSelectionStateService.CreateOrdinalStringChange(
                _projectMetadata.Narrator,
                value);
            if (!change.HasChanged)
                return;

            _suppressProjectMetadataStateNotifications = true;
            _projectMetadata.Narrator = change.Value;
            _suppressProjectMetadataStateNotifications = false;
            OnPropertyChanged();
            OnPropertyChanged(nameof(MetadataSummaryDetails));
            OnPropertyChanged(nameof(OutputFileNamePreview));
            OnExportPreviewChanged();
        }
    }


    public string Album
    {
        get => _projectMetadata.Album;
        set
        {
            var change = MainWindowSelectionStateService.CreateOrdinalStringChange(
                _projectMetadata.Album,
                value);
            if (!change.HasChanged)
                return;

            _suppressProjectMetadataStateNotifications = true;
            _projectMetadata.Album = change.Value;
            _suppressProjectMetadataStateNotifications = false;
            OnPropertyChanged();
            OnPropertyChanged(nameof(MetadataSummaryDetails));
            QueueChapterMetadataRefreshIfNeeded();

            OnPropertyChanged(nameof(OutputFileNamePreview));
            OnExportPreviewChanged();
        }
    }

    public string Series
    {
        get
        {
            var series = _projectMetadata.CanonicalMetadata?
                .GetValue<IReadOnlyList<SeriesEntry>>(ProjectMetadataField.Series)
                .Value ?? [];

            return series
                .FirstOrDefault(item => !string.IsNullOrWhiteSpace(item.Name))
                ?.Name
                ?.Trim() ?? string.Empty;
        }
    }

    private MetadataSummaryPresentation MetadataSummaryPresentation =>
        MetadataSummaryPresentationService.Create(
            BookTitle,
            Author,
            Narrator,
            Album,
            Genre);

    public string MetadataSummaryTitle =>
        MetadataSummaryPresentation.Title;

    public string MetadataSummaryDetails =>
        MetadataSummaryPresentation.Details;

    private MetadataPanelTogglePresentation MetadataPanelTogglePresentation =>
        MetadataPanelTogglePresentationService.Create(_isMetadataPanelExpanded);

    public string MetadataToggleGlyph =>
        MetadataPanelTogglePresentation.Glyph;

    public string Genre
    {
        get => _projectMetadata.Genre;
        set
        {
            var normalized = NormalizeComboBoxValue(value);
            var decision = GenreSelectionDecisionService.Create(
                _projectMetadata.Genre,
                normalized,
                GenreListService.IsSeparator(normalized));

            if (!decision.ShouldApply)
                return;

            _suppressProjectMetadataStateNotifications = true;
            _projectMetadata.Genre = decision.Value;
            _suppressProjectMetadataStateNotifications = false;
            OnPropertyChanged();
            OnPropertyChanged(nameof(MetadataSummaryDetails));

            if (GenreListService.IsSelectableGenre(_projectMetadata.Genre))
            {
                _settings.DefaultGenre = _projectMetadata.Genre;
                SaveSettingsIfReady();
            }
        }
    }

    public string OutputExtension
    {
        get => _outputExtension;
        set
        {
            var change = ExportSettingsNormalizationService.CreateOutputExtensionChange(
                _outputExtension,
                NormalizeComboBoxValue(value));

            if (!change.HasChanged)
                return;

            _outputExtension = change.Value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(OutputFileNamePreview));
            OnExportPreviewChanged();

            _settings.DefaultOutputExtension = _outputExtension;
            SaveSettingsIfReady();
        }
    }

    public string FileNameTemplate
    {
        get => _fileNameTemplate;
        set
        {
            var change = ExportSettingsNormalizationService.CreateFileNameTemplateChange(
                _fileNameTemplate,
                value);

            if (!change.HasChanged)
                return;

            _fileNameTemplate = change.Value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(OutputFileNamePreview));
            OnExportPreviewChanged();

            _settings.DefaultFileNameTemplate = _fileNameTemplate;
            SaveSettingsIfReady();
        }
    }

    public string OutputFolderLayout
    {
        get => _settings.OutputFolderLayout;
        set
        {
            var normalized = OutputFolderLayoutService.NormalizeLayout(value);
            if (string.Equals(_settings.OutputFolderLayout, normalized, StringComparison.Ordinal))
                return;
            _settings.OutputFolderLayout = normalized;
            OnPropertyChanged();
            OnExportPreviewChanged();
            SaveSettingsIfReady();
        }
    }

    public string SelectedExportPreset
    {
        get => _selectedExportPreset;
        set
        {
            if (!string.IsNullOrWhiteSpace(_lockedDiscResumePreset) &&
                !string.Equals(NormalizeComboBoxValue(value), _lockedDiscResumePreset, StringComparison.OrdinalIgnoreCase))
                return;
            var change = ExportSettingsNormalizationService.CreatePresetChange(
                _selectedExportPreset,
                NormalizeComboBoxValue(value),
                ExportPresets,
                ExportPreset.DefaultDisplayName);

            if (!change.HasChanged)
                return;

            _selectedExportPreset = change.Value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ExportPreviewCodecSummary));

            RecalculateProcessingActionsForCurrentPreset();
            UpdateManualMergeReviewPresetState();
            if (!ProjectReviewState.IsWaitingForManualMergeReview)
                UpdateFinalStatus(Tracks.Count);

            _settings.SelectedExportPreset = _selectedExportPreset;
            SaveSettingsIfReady();
        }
    }

    public string ParallelJobsInput
    {
        get => _parallelJobsInput;
        set => SetParallelJobsInput(value, showMessage: !_isLoadingSettings);
    }

    public string CoverPreviewSource
    {
        get => CoverPreviewPresentationService.NormalizeSource(
            _projectMetadata.CoverPreviewSource,
            AppearanceSettingsService.GetEffectiveMode(_settings),
            AppearanceSettingsService.GetCoverPlaceholderOverride());
        set
        {
            var stateValue = string.IsNullOrWhiteSpace(value) || AppearanceAssetCatalog.IsCoverPlaceholderPath(value)
                ? string.Empty
                : value;
            var change = MainWindowSelectionStateService.CreateOrdinalStringChange(
                _projectMetadata.CoverPreviewSource,
                stateValue);

            if (!change.HasChanged)
                return;

            _suppressProjectMetadataStateNotifications = true;
            _projectMetadata.CoverPreviewSource = change.Value;
            _suppressProjectMetadataStateNotifications = false;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CoverHintVisibility));
        }
    }

    public Visibility CoverHintVisibility =>
        CoverPreviewPresentationService.IsHintVisible(
            _projectMetadata.ProcessedCoverPath,
            CanChangeBookMetadata)
            ? Visibility.Visible
            : Visibility.Collapsed;

    public double ExportProgressPercent
    {
        get => _exportProgressPercent;
        set
        {
            var change = ProgressStateChangeService.CreatePercentChange(
                _exportProgressPercent,
                value);

            if (!change.HasChanged)
                return;

            _exportProgressPercent = change.Value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ExportProgressVisibility));
        }
    }

    public string ExportProgressForeground
    {
        get => _exportProgressForeground;
        private set
        {
            var change = ProgressStateChangeService.CreateForegroundChange(
                _exportProgressForeground,
                value);

            if (!change.HasChanged)
                return;

            _exportProgressForeground = change.Value;
            OnPropertyChanged();
        }
    }

    public bool IsProgressIndeterminate
    {
        get => _isResumeLoading || _isProgressIndeterminate;
        set
        {
            var change = ProgressStateChangeService.CreateIndeterminateChange(
                _isProgressIndeterminate,
                value);

            if (!change.HasChanged)
                return;

            _isProgressIndeterminate = change.Value;
            OnPropertyChanged();
        }
    }

    public bool IsDiscSourceAnalysisActive
    {
        get => _isDiscSourceAnalysisActive;
        private set
        {
            var change = ProgressStateChangeService.CreateAnalysisActiveChange(
                _isDiscSourceAnalysisActive,
                value);

            if (!change.HasChanged)
                return;

            _isDiscSourceAnalysisActive = change.Value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DiscSourceAnalysisVisibility));
            OnPropertyChanged(nameof(StandardProgressVisibility));
        }
    }

    public string DiscSourceAnalysisText
    {
        get => _discSourceAnalysisText;
        private set
        {
            var change = ProgressStateChangeService.CreateTextChange(
                _discSourceAnalysisText,
                value);

            if (!change.HasChanged)
                return;

            _discSourceAnalysisText = change.Value;
            OnPropertyChanged();
        }
    }

    public Visibility DiscSourceAnalysisVisibility =>
        IsDiscSourceAnalysisActive
            ? Visibility.Visible
            : Visibility.Collapsed;

    public Visibility StandardProgressVisibility =>
        IsDiscSourceAnalysisActive
            ? Visibility.Collapsed
            : Visibility.Visible;

    private DriveCircleStatusAnimation? _driveCircleStatusAnimation;

    private IDisposable? BeginDriveCircleStatusAnimation(CancellationToken token)
    {
        if (!_settings.ExperimentalDriveRoundEnabled || token.IsCancellationRequested) return null;
        _driveCircleStatusAnimation?.Dispose();
        _driveCircleStatusAnimation = new(Dispatcher, () => OnPropertyChanged(nameof(ExportProgressText)), token);
        OnPropertyChanged(nameof(ExportProgressText));
        return _driveCircleStatusAnimation;
    }

    public string ExportProgressText
    {
        get => _driveCircleStatusAnimation is { IsActive: true } animation ? animation.Text : _exportProgressText;
        set
        {
            var change = ProgressStateChangeService.CreateTextChange(
                _exportProgressText,
                value);

            if (!change.HasChanged)
                return;

            _exportProgressText = change.Value;
            OnPropertyChanged();
        }
    }

    public string ExportPreviewCodecSummary =>
        "Export-Preset:";

    public string OutputFileNamePreview => FileNameTemplateService.BuildOutputFileName(
        BookTitle,
        Author,
        Narrator,
        FileNameTemplate,
        OutputExtension,
        Album,
        Series);

    public string ExportPreviewFileName =>
        ExportPreviewFileNamePresentationService.Create(
            Tracks.Count,
            BuildOutputRelativePathPreview());

    private ExportPreviewCountPresentation ExportPreviewCountPresentation =>
        ExportPreviewCountPresentationService.Create(
            Tracks.Count,
            ExportPreviewMetricsService.CountPlannedChapters(Tracks));

    public string ExportPreviewTrackCount =>
        ExportPreviewCountPresentation.TrackSummary;

    public string ExportPreviewTotalDuration =>
        ExportDurationPresentationService.Create(
            Tracks.Count,
            ExportPreviewMetricsService.GetKnownDurations(
                Tracks.Select(GetPreciseDuration)));

    public string ExportPreviewActionSummary =>
        LocalizationText.Format("MainWindow.Summary.Action", BuildProcessingActionSummary());

    public string ExportPreviewChapterSummary =>
        ExportPreviewCountPresentation.ChapterSummary;

    public string ExportPreviewIssueSummary => _services.Tracks.TrackIssueSummaryService
        .Create(Tracks)
        .ToDisplayText();

    private TrackWarningSummaryPresentation TrackWarningSummaryPresentation
    {
        get
        {
            var metrics = TrackWarningMetricsService.Create(
                Tracks.Select(track => new TrackWarningMetric(
                    track.DisplayFileWarning,
                    track.DisplayChapterWarning)));

            return TrackWarningSummaryPresentationService.Create(
                metrics.FileWarningCount,
                metrics.ChapterWarningCount);
        }
    }

    public string TrackFileWarningSummary =>
        TrackWarningSummaryPresentation.FileWarningSummary;

    public string TrackChapterWarningSummary =>
        TrackWarningSummaryPresentation.ChapterWarningSummary;

    public Visibility TrackFileWarningSummaryVisibility =>
        TrackWarningSummaryPresentation.HasFileWarnings
            ? Visibility.Visible
            : Visibility.Collapsed;

    public Visibility TrackChapterWarningSummaryVisibility =>
        TrackWarningSummaryPresentation.HasChapterWarnings
            ? Visibility.Visible
            : Visibility.Collapsed;

    public Visibility TrackWarningSummarySeparatorVisibility =>
        TrackWarningSummaryPresentation.ShowSeparator
            ? Visibility.Visible
            : Visibility.Collapsed;

    public Visibility TrackWarningSummaryVisibility =>
        TrackWarningSummaryPresentation.IsVisible
            ? Visibility.Visible
            : Visibility.Collapsed;

    public string EnvironmentBrandText =>
        _services.Foundation.EnvironmentContext.BrandText;

    public MainWindow()
    {
        var environmentContext = Application.Current is App app
            ? app.ActiveEnvironmentContext
            : new BookStitchEnvironmentResolver().Resolve();
        _services = MainWindowServiceComposition.Create(
            environmentContext,
            () => _ffmpegStatus.FfmpegPath ?? string.Empty,
            () => _debugSettings.UseBoxedDiscWaitDialog);
        _services.Foundation.ProjectIndexService.SetActiveProjectFolderProvider(() => _currentProjectWorkFolder);
        _services.Discs.DiscDriveService.SetPerformanceTraceProvider(
            () => (Application.Current as App)?.PerformanceTraceService);

        InitializeComponent();
        Title = _services.Foundation.EnvironmentContext.WindowTitle;

        ConfigureTrackGridColumnLayoutHandlers();
        _trackGridAutoScrollTimer.Tick += TrackGridAutoScrollTimer_Tick;
        _chapterMetadataRefreshTimer.Tick += ChapterMetadataRefreshTimer_Tick;
        _projectSetupDiscCountRefreshTimer.Tick += ProjectSetupDiscCountRefreshTimer_Tick;
        _projectMetadata.PropertyChanged += ProjectMetadata_PropertyChanged;

        _trackStateUpdateQueueService = new TrackStateUpdateQueueService(
            () => new TrackStateUpdateContext(
                Tracks,
                _currentProjectWorkFolder,
                SelectedExportPreset,
                IsBusy || IsExporting || IsDiscImporting,
                IsDiscImporting || _isDiscStopSaveInProgress,
                ProjectReviewState.Phase),
            RefreshAfterTrackStateUpdate);

        Tracks.CollectionChanged += Tracks_CollectionChanged;
        Closing += MainWindow_Closing;
        Closed += MainWindow_Closed;

        var startupPhaseTimer = Stopwatch.StartNew();
        LoadSettings();
        DeveloperToolsCommands.AccessState.Initialize(
            _settings, _services.Foundation.EnvironmentContext.Environment,
            _services.Foundation.GlobalSettingsManager.SaveImmediate);
        DeveloperToolsCommands.AccessState.PropertyChanged += DeveloperAccessState_PropertyChanged;
        DeveloperToolsCommands.AppearancePreview.Changed += DeveloperAppearancePreview_Changed;
        DeveloperAccessState_PropertyChanged(this, new PropertyChangedEventArgs(nameof(DeveloperToolsAccessState.IsEnabled)));
        ConfigureMainChapterSettingsEditor();
        RefreshMainChapterSettingsEditorFromSharedState();
        _coverPickerService = new CoverPickerService(
            _settings,
            SaveSettingsIfReady,
            () => GetWorkingRootFolder());
        EnsureCanonicalMetadataSessionForCurrentState();
        var loadSettingsElapsed = startupPhaseTimer.Elapsed;

        startupPhaseTimer.Restart();
        _runtimeServices = MainWindowRuntimeComposition.Create(
            GetProjectFolderStructure().ProjectRootFolder,
            () => _settings,
            () => this,
            _services.Foundation.EnvironmentContext);
        _services.Foundation.GlobalSettingsManager.SaveFailed += GlobalSettingsManager_SaveFailed;
        _services.Foundation.GlobalSettingsManager.DriveCircleChanged += GlobalSettingsManager_DriveCircleChanged;
        _services.Foundation.GlobalSettingsManager.GenreOptionsChanged += GlobalSettingsManager_GenreOptionsChanged;
        var runtimeCompositionElapsed = startupPhaseTimer.Elapsed;

        _runtimeServices.DiagnosticLogService.WriteApplicationEvent(
            "MAIN WINDOW",
            "Das Hauptfenster wurde initialisiert.");

        startupPhaseTimer.Restart();
        DetectFfmpegTools();
        var ffmpegDetectionElapsed = startupPhaseTimer.Elapsed;

        startupPhaseTimer.Restart();
        // Catalog preload starts only after the main window is visible.
        var resumeRefreshElapsed = TimeSpan.Zero;
        startupPhaseTimer.Stop();

        DataContext = this;
        SetMetadataPanelExpanded(false, animate: false);
        SetMetadataEditingAvailable(false);
        _trackStateUpdateQueueService.Start();

        _startupPerformanceDetails =
            $"LoadSettings={FormatPerformanceMilliseconds(loadSettingsElapsed)} ms; " +
            $"RuntimeComposition={FormatPerformanceMilliseconds(runtimeCompositionElapsed)} ms; " +
            $"FfmpegDetection={FormatPerformanceMilliseconds(ffmpegDetectionElapsed)} ms; " +
            $"ResumeRefresh={FormatPerformanceMilliseconds(resumeRefreshElapsed)} ms; " +
            $"ConstructorReady={FormatPerformanceMilliseconds(_startupPerformanceStopwatch.Elapsed)} ms";

        var catalog = _services.Foundation.ProjectIndexService.Catalog;
        _maintenanceCoordinator = new ProjectMaintenanceCoordinator(
            (request, token) =>
            {
                var snapshot = catalog.RefreshAsync(request.WorkingRoot, request.RetentionDays,
                    force: catalog.Snapshot?.CreatedUtc.ToLocalTime().Date != request.NowUtc.ToLocalTime().Date)
                    .WaitAsync(token).GetAwaiter().GetResult();
                token.ThrowIfCancellationRequested();
                return _services.Foundation.ProjectIndexService.DeleteProjectsOlderThan(
                    request.WorkingRoot, request.NowUtc, request.RetentionDays, token, snapshot.ProjectIndexItems);
            },
            (request, token) => new CoverCleanupService().DeleteOldUnreferencedCoverFiles(
                request.WorkingRoot, request.CoversFolder, request.NowUtc, request.RetentionDays, token));
        _services.Foundation.ProjectIndexService.BeforeManualDeletion = () =>
            _maintenanceCoordinator.StopAsync().GetAwaiter().GetResult();
        _maintenanceCoordinator.LogFailure = message =>
            _runtimeServices.DiagnosticLogService.WriteApplicationEvent("AUTOMATIC MAINTENANCE", message);
        if (Application.Current is App diagnosticsApp)
        {
            catalog.StartOperation = name => diagnosticsApp.StartPerformanceOperation(name);
            catalog.StartPhase = diagnosticsApp.StartPerformancePhase;
            ProjectCoverThumbnailCache.Shared.StartOperation = name => diagnosticsApp.StartPerformanceOperation(name);
            _maintenanceCoordinator.StartOperation = name => diagnosticsApp.StartPerformanceOperation(name);
            _maintenanceCoordinator.StartPhase = diagnosticsApp.StartPerformancePhase;
        }
        catalog.SnapshotChanged += ProjectCatalog_SnapshotChanged;
        Loaded += MainWindow_Loaded;
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        Loaded -= MainWindow_Loaded;
        Dispatcher.BeginInvoke(new Action(() => InitializeEmptyProjectSweepState()), DispatcherPriority.Background);
        _mainWindowReady = true;
        Dispatcher.BeginInvoke(new Action(() => _ = RefreshProjectCatalogAsync(showStatus: false)), DispatcherPriority.Background);

        if (Application.Current is App app)
            app.CompletePerformanceStartup();

        var startupElapsed = _startupPerformanceStopwatch.Elapsed;
        _startupPerformanceStopwatch.Stop();

        _runtimeServices.DiagnosticLogService.WritePerformanceEvent(
            "MAIN WINDOW STARTUP",
            startupElapsed,
            $"{_startupPerformanceDetails}; Tracks={Tracks.Count}; ResumeProjects={ResumeProjects.Count}");

    }

    private static string FormatPerformanceMilliseconds(TimeSpan elapsed) =>
        elapsed.TotalMilliseconds.ToString("0.0", CultureInfo.InvariantCulture);
    private void RefreshTrackPresentationViews()
    {
        TracksGrid?.Items.Refresh();
        _developmentMetadataDialog?.RefreshChapterPresentationView();
    }

    private void RefreshAfterTrackStateUpdate()
    {
        RefreshTrackPresentationViews();

        var action = TrackStateRefreshDecisionService.Create(
            IsBusy,
            IsExporting,
            IsDiscImporting);

        switch (action)
        {
            case TrackStateRefreshAction.RefreshExportPreview:
                OnExportPreviewChanged();
                break;

            case TrackStateRefreshAction.UpdateFinalStatus:
                UpdateFinalStatus(Tracks.Count);
                break;

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(action),
                    action,
                    "Unbekannte Aktualisierungsaktion.");
        }
    }

    private void SetMetadataEditingAvailable(bool available)
    {
        var change = MainWindowSelectionStateService.CreateBooleanChange(
            _isMetadataEditingAvailable,
            available);

        if (!change.HasChanged)
            return;

        _isMetadataEditingAvailable = change.Value;
        OnPropertyChanged(nameof(CanChangeBookMetadata));
        OnPropertyChanged(nameof(CanRemoveCover));
        OnPropertyChanged(nameof(CoverHintVisibility));
    }

    private void Tracks_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(HasProjectTracks));
        OnPropertyChanged(nameof(SelectedSourceDisplayText));
        OnPropertyChanged(nameof(SelectedSourceOpenPath));
        OnPropertyChanged(nameof(CanOpenSelectedFolder));
        NotifyExportUiStateChanged();
        OnExportPreviewChanged();
        _trackStateUpdateQueueService.RequestRefresh();
    }

    private void LoadSettings()
    {
        _isLoadingSettings = true;

        _settings = _services.Foundation.GlobalSettingsManager.Current;
        _debugSettings = _services.Foundation.DebugSettingsService.Load();
        _debugWindowPlacementService = new DebugWindowPlacementService(
            _debugSettings,
            _services.Foundation.DebugSettingsService);

        var selection = _services.Foundation.SettingsSelectionService.Create(
            _settings,
            ExportPresets);
        ApplyAppearanceResources(_settings);

        InitializeGenreStateForEmptySession();
        OutputExtension = selection.OutputExtension;
        FileNameTemplate = selection.FileNameTemplate;
        SelectedExportPreset = selection.SelectedExportPreset;
        SetParallelJobsInput(selection.SelectedParallelJobs, showMessage: false);

        var outputFolderDecision =
            SettingsFolderLoadDecisionService.CreateOutputFolderDecision(
                _settings.OutputFolder,
                Directory.Exists(_settings.OutputFolder));

        if (outputFolderDecision.ShouldApply)
            OutputFolder = outputFolderDecision.Value;

        var selectedFolderDecision =
            SettingsFolderLoadDecisionService.CreateSelectedFolderDecision(
                _settings.LastSelectedFolder);

        if (selectedFolderDecision.ShouldApply)
            SelectedFolder = selectedFolderDecision.Value;

        ApplyTrackGridColumnLayout();
        _isLoadingSettings = false;
    }

    private void ApplyAppearanceResources(AppSettings settings)
    {
        var resources = Application.Current?.Resources ?? Resources;
        AppearanceSettingsService.ApplyEffectiveToResources(resources, settings);
        RefreshCoverPlaceholderForAppearance(settings);
    }

    private void SetBackgroundPreset(string preset) => DeveloperToolsCommands.AppearancePreview.SetBackground(preset);

    private void SetTextColorPreset(string preset) => DeveloperToolsCommands.AppearancePreview.SetTextColor(preset);

    private void DeveloperAppearancePreview_Changed(object? sender, EventArgs e)
    {
        ApplyAppearanceResources(_settings);
        _developerToolsWindow?.SynchronizeAppearanceControls();
    }

    private void ApplyRegularAppearanceSettings(AppSettings settings)
    {
        ApplyAppearanceResources(settings);
        DeveloperToolsCommands.AppearancePreview.NotifyRegularSettingsChanged();
    }

    private void RefreshCoverPlaceholderForAppearance(AppSettings settings)
    {
        _ = settings;
        if (!string.IsNullOrWhiteSpace(_projectMetadata.ProcessedCoverPath) ||
            (!string.IsNullOrWhiteSpace(_projectMetadata.CoverPreviewSource) &&
             !AppearanceAssetCatalog.IsCoverPlaceholderPath(_projectMetadata.CoverPreviewSource)))
        {
            return;
        }

        OnPropertyChanged(nameof(CoverPreviewSource));
        OnPropertyChanged(nameof(CoverHintVisibility));
    }

    private void DetectFfmpegTools()
    {
        _ffmpegStatus = _services.Media.FfmpegService.DetectTools(_settings);
        ApplyFfmpegStatusToSettingsAndUi();
    }

    private void ApplyFfmpegStatusToSettingsAndUi()
    {
        var decision = FfmpegSettingsApplicationService.Create(
            _settings.FfmpegPath,
            _ffmpegStatus.FfmpegPath,
            _ffmpegStatus.FfmpegAvailable,
            _settings.FfprobePath,
            _ffmpegStatus.FfprobePath,
            _ffmpegStatus.FfprobeAvailable,
            StatusText);

        _settings.FfmpegPath = decision.FfmpegPath;
        _settings.FfprobePath = decision.FfprobePath;

        SaveSettingsIfReady();

        // Die globale Werkzeugerkennung steuert Button und Sperrprofil.
        // Sie überschreibt nicht den fachlichen Projektstatus im Hauptfenster.
        if (decision.ShouldClearToolStatus)
            StatusText = string.Empty;

        OnPropertyChanged(nameof(CanConfigureFfmpeg));
        OnPropertyChanged(nameof(FfmpegSetupButtonVisibility));
        OnPropertyChanged(nameof(CanStartExport));
        OnPropertyChanged(nameof(CanStartNewProject));
    }

    private void SaveSettingsIfReady()
    {
        if (_isLoadingSettings)
            return;

        _services.Foundation.GlobalSettingsManager.SaveImmediate();
    }

    private void ApplyMergeAutomaticallyFromSettingsWindow(bool enabled)
    {
        if (_settings.MergeAutomaticallyAfterConversion == enabled)
            return;

        _settings.MergeAutomaticallyAfterConversion = enabled;
        OnPropertyChanged(nameof(MergeAutomaticallyAfterConversion));
        OnPropertyChanged(nameof(DriveCircleEnabled));
    }

    private void GlobalSettingsManager_DriveCircleChanged(object? sender, EventArgs e)
        => OnPropertyChanged(nameof(DriveCircleEnabled));

    private void GlobalSettingsManager_SaveFailed(object? sender, GlobalSettingsSaveFailedEventArgs e)
    {
        _runtimeServices.DiagnosticLogService.WriteApplicationEvent(
            "GLOBAL SETTINGS SAVE FAILED",
            e.Exception.ToString());
        StatusText = LocalizationText.Get("MainWindow.Settings.SaveFailed");
    }

    private void MainWindowCaption_CloseRequested(object sender, RoutedEventArgs e) => Close();

    private async void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        _ = _maintenanceCoordinator?.StopAsync();
        PipelineUiTransition? transitionOwner = null;
        try
        {
            if (_isDiscStopSaveInProgress || _isProjectCloseInProgress || (_isShutdownInProgress && !_allowWindowClose) ||
                (_presentationState.UiTransition is not null && !_allowWindowClose))
            {
                e.Cancel = true;
                return;
            }
            if (_isWorkspaceReplacementInProgress)
            {
                e.Cancel = true;
                return;
            }
            if (!_allowWindowClose && _sessionProject.IsUnstartedAudio(_currentProjectWorkFolder))
            {
                e.Cancel = true;
                if (TryDiscardAudioPreparation())
                {
                    _allowWindowClose = true;
                    _ = Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(Close));
                }
                else CancelApplicationRestartRequest();
                return;
            }
            if (!TryCheckpointMetadataBeforeClose())
            {
                e.Cancel = true;
                return;
            }
            if (_allowWindowClose)
                return;

            if (!TryCheckpointEditableProject())
            {
                e.Cancel = true;
                return;
            }

            var activity = _services.Pipeline.ApplicationShutdownService.GetActiveActivity(
                IsDiscImporting,
                IsDiscImporting && _activeAudioDiscManifest is not null,
                IsExporting,
                IsBusy,
                _presentationState.IsPipelinePaused,
                _activeAudioDiscManifest is not null,
                _activeMp3DiscManifest is not null);

            if (activity == ApplicationActivity.None)
            {
                if (!string.IsNullOrWhiteSpace(_currentProjectWorkFolder))
                {
                    e.Cancel = true;
                    if (await CloseProjectFromWindowAsync())
                    {
                        _allowWindowClose = true;
                        _ = Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(Close));
                    }
                    else CancelApplicationRestartRequest();
                }
                return;
            }

            e.Cancel = true;

            if (_isShutdownInProgress)
                return;

            var prompt = _services.Pipeline.ApplicationShutdownService.CreatePrompt(activity);
            if (_sessionProject.ProjectType == ProjectManifestTypes.FolderProject &&
                _sessionProject.DeleteOnWindowClose(_currentProjectWorkFolder,
                    ProjectReviewState.Phase is ProjectPipelineState.AcquiringSources or ProjectPipelineState.Converting))
                prompt = prompt with
                {
                    Heading = LocalizationText.Get("Project.Close.DeleteHeading"),
                    Message = LocalizationText.Get("Project.Close.DeleteMessage")
                };
            var discardAttempt = _presentationState.IsProjectExtensionRun || _presetReconversionRollbackContext is not null ||
                ProjectReviewState.Phase == ProjectPipelineState.Merging;
            if (discardAttempt && !_presentationState.IsPipelinePaused)
            {
                await RequestPipelinePauseAsync();
                await WaitForProjectProducersAsync();
            }
            if (discardAttempt)
                prompt = prompt with { Heading = LocalizationText.Get("Project.Close.DiscardHeading"),
                    Message = LocalizationText.Get("Project.Close.DiscardMessage") };
            var result = AppDialogService.Show(
                this,
                title: LocalizationText.Get("Shutdown.Title"),
                heading: prompt.Heading,
                message: prompt.Message,
                kind: AppDialogKind.Warning,
                buttons: new[]
                {
                    new AppDialogButton(LocalizationText.Get(discardAttempt ? "Project.Close.Back" : "Shutdown.KeepWorking"), AppDialogResult.Cancel, IsDefault: true, IsCancel: true),
                    new AppDialogButton(LocalizationText.Get(discardAttempt ? "Project.Close.DiscardAndClose" : "Shutdown.AbortAndClose"), AppDialogResult.Yes, IsDanger: true)
                });

            if (result != AppDialogResult.Yes)
            {
                CancelApplicationRestartRequest();
                return;
            }

            EnsureShutdownPerformanceStarted();
            _isShutdownInProgress = true;
            transitionOwner = BeginPipelineUiTransition(discardAttempt ? PipelineUiTransitionKind.Discarding : PipelineUiTransitionKind.Stopping, PipelineUiTransitionTarget.Primary);
            await Dispatcher.Yield(DispatcherPriority.Background);
            StatusText = prompt.ProgressText;
            ExportProgressText = LocalizationText.Get("Shutdown.ControlledStop");
            NotifyExportUiStateChanged();

            if (!string.IsNullOrWhiteSpace(_currentProjectWorkFolder))
            {
                if (await CloseProjectFromWindowAsync(confirmIntent: false, cancelActiveWork: true))
                {
                    _allowWindowClose = true;
                    _ = Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(Close));
                }
                else
                {
                    _isShutdownInProgress = false;
                    CancelApplicationRestartRequest();
                    NotifyExportUiStateChanged();
                }
                return;
            }

            TrySaveActiveProjectSnapshot();
            RequestActiveOperationCancellation();

            var shutdownWaitPerformancePhase = StartShutdownPerformancePhase("shutdown.wait-for-idle");
            var shutdownWaitTimer = Stopwatch.StartNew();
            bool stoppedCleanly;
            try
            {
                stoppedCleanly = await _services.Pipeline.ApplicationShutdownService.WaitForIdleAsync(
                    () => IsDiscImporting || IsExporting || IsBusy,
                    TimeSpan.FromSeconds(30));
                if (stoppedCleanly) await WaitForProjectProducersAsync();
                shutdownWaitPerformancePhase.Complete();
            }
            catch (OperationCanceledException ex)
            {
                shutdownWaitPerformancePhase.Cancel();
                _shutdownPerformanceScope.Cancel();
                _shutdownPerformanceScope = default;
                if (Application.Current is App app)
                    app.TryFinalizeDisabledPerformanceSession();
                _runtimeServices.DiagnosticLogService.WriteError("Shutdown wait interrupted", ex);
                stoppedCleanly = false;
            }
            catch (Exception ex)
            {
                shutdownWaitPerformancePhase.Fail(ex);
                _shutdownPerformanceScope.Fail(ex);
                _shutdownPerformanceScope = default;
                if (Application.Current is App app)
                    app.TryFinalizeDisabledPerformanceSession();
                _runtimeServices.DiagnosticLogService.WriteError("Shutdown wait interrupted", ex);
                stoppedCleanly = false;
            }
            finally
            {
                shutdownWaitTimer.Stop();
            }

            _runtimeServices.DiagnosticLogService.WritePerformanceEvent(
                "ACTIVE WORK SHUTDOWN WAIT",
                shutdownWaitTimer.Elapsed,
                $"Activity={activity}; StoppedCleanly={stoppedCleanly}");

            if (!TryCheckpointMetadataBeforeClose())
            {
                e.Cancel = true;
                return;
            }
            TrySaveActiveProjectSnapshot();

            if (stoppedCleanly)
            {
                CleanupCurrentProjectPartFiles();
            }
            else
            {
                StatusText = LocalizationText.Get("Shutdown.CleanupNextStart");
                _isShutdownInProgress = false;
                CancelApplicationRestartRequest();
                NotifyExportUiStateChanged();
                return;
            }

            _allowWindowClose = true;
            _ = Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(Close));
        }
        catch (Exception ex)
        {
            _runtimeServices.DiagnosticLogService.WriteError("Window closing failed", ex);
            if (_projectMetadataSession.IsAttached && _projectMetadataSession.HasUnpersistedChanges)
            {
                e.Cancel = true;
                _allowWindowClose = false;
                _isShutdownInProgress = false;
                CancelApplicationRestartRequest();
                AppDialogService.Error(this, LocalizationText.Get("MainWindow.Metadata.SaveFailed.Title"),
                    LocalizationText.Get("MainWindow.Metadata.SaveFailed.Message"), details: new[] { ex.Message });
                return;
            }
            e.Cancel = true;
            _allowWindowClose = false;
            _isShutdownInProgress = false;
            CancelApplicationRestartRequest();
            NotifyExportUiStateChanged();
        }
        finally
        {
            EndPipelineUiTransition(transitionOwner);
        }
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        DeveloperToolsCommands.AccessState.PropertyChanged -= DeveloperAccessState_PropertyChanged;
        DeveloperToolsCommands.AppearancePreview.Changed -= DeveloperAppearancePreview_Changed;
        RunCloseCleanupStep(() => { CompleteWindowCloseCleanup(); });
        RunCloseCleanupStep(() => { ConfirmApplicationRestartAfterCloseCleanup(); });
    }

    private void RequestApplicationRestart()
    {
        if (Application.Current is not App app)
            return;

        app.ApplicationRestartService.RequestRestart();
        Close();
    }

    private static void CancelApplicationRestartRequest()
    {
        if (Application.Current is App app)
            app.ApplicationRestartService.CancelRestart();
    }

    private static void ConfirmApplicationRestartAfterCloseCleanup()
    {
        if (Application.Current is App app)
            app.ApplicationRestartService.ConfirmCloseCompleted();
    }

    private void EnsureShutdownPerformanceStarted()
    {
        if (_shutdownPerformanceScope.IsActive)
            return;

        if (Application.Current is App app)
            _shutdownPerformanceScope = app.StartPerformanceOperation("app.shutdown");
    }

    private PerformancePhaseScope StartShutdownPerformancePhase(string phaseName)
    {
        if (!_shutdownPerformanceScope.IsActive || Application.Current is not App app)
            return default;

        return app.StartPerformancePhase(_shutdownPerformanceScope.OperationId, phaseName);
    }

    private static void RunPerformanceStep(
        App? app,
        Guid operationId,
        string phaseName,
        Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        RunPerformanceStep(
            app,
            operationId,
            phaseName,
            () =>
            {
                action();
                return true;
            });
    }

    private static T RunPerformanceStep<T>(
        App? app,
        Guid operationId,
        string phaseName,
        Func<T> action)
    {
        ArgumentNullException.ThrowIfNull(action);

        var phase = app is null || operationId == Guid.Empty
            ? default
            : app.StartPerformancePhase(operationId, phaseName);

        try
        {
            var result = action();
            phase.Complete();
            return result;
        }
        catch (Exception ex)
        {
            phase.Fail(ex);
            throw;
        }
    }

    private void CompleteWindowCloseCleanup()
    {
        _driveCircleStatusAnimation?.Dispose();
        if (_windowCloseCleanupCompleted)
            return;

        _windowCloseCleanupCompleted = true;
        EnsureShutdownPerformanceStarted();
        var cleanupPerformancePhase = StartShutdownPerformancePhase("shutdown.cleanup");

        try
        {
            var totalTimer = Stopwatch.StartNew();

            var timerDisposeTimer = Stopwatch.StartNew();
            RunCloseCleanupStep(() => { _resumeDiscPreview.Clear(); _transientMp3DiscChapterPreview = null; });
            RunCloseCleanupStep(() => { StopTrackGridAutoScroll(); });
            RunCloseCleanupStep(() => { _chapterMetadataRefreshTimer.Stop(); });
            if (_developerToolsWindow is not null)
            {
                _developerToolsWindow.Close();
                _developerToolsWindow = null;
            }
            timerDisposeTimer.Stop();

            RunCloseCleanupStep(() => { _maintenanceCoordinator?.StopAsync().GetAwaiter().GetResult(); });
            RunCloseCleanupStep(() => { _maintenanceCoordinator?.Dispose(); });

            var queueDisposeTimer = Stopwatch.StartNew();
            _services.Foundation.ProjectIndexService.Catalog.SnapshotChanged -= ProjectCatalog_SnapshotChanged;
            RunCloseCleanupStep(() => { _services.Foundation.ProjectIndexService.Catalog.Dispose(); });
            RunCloseCleanupStep(() => { _trackStateUpdateQueueService.Dispose(); });
            queueDisposeTimer.Stop();

            RunCloseCleanupStep(() => { _services.Foundation.GlobalSettingsManager.Dispose(); });
            _services.Foundation.GlobalSettingsManager.SaveFailed -= GlobalSettingsManager_SaveFailed;
            _services.Foundation.GlobalSettingsManager.DriveCircleChanged -= GlobalSettingsManager_DriveCircleChanged;
            _services.Foundation.GlobalSettingsManager.GenreOptionsChanged -= GlobalSettingsManager_GenreOptionsChanged;

            var notificationDisposeTimer = Stopwatch.StartNew();
            RunCloseCleanupStep(() => { _runtimeServices.NotificationService.Dispose(); });
            notificationDisposeTimer.Stop();

            var sleepPreventionDisposeTimer = Stopwatch.StartNew();
            RunCloseCleanupStep(() => { _services.Foundation.SystemSleepPreventionService.Dispose(); });
            sleepPreventionDisposeTimer.Stop();

            totalTimer.Stop();

            _runtimeServices.DiagnosticLogService.WritePerformanceEvent(
                "WINDOW CLOSE CLEANUP",
                totalTimer.Elapsed,
                $"TimerStop={FormatPerformanceMilliseconds(timerDisposeTimer.Elapsed)} ms; " +
                $"TrackQueueDispose={FormatPerformanceMilliseconds(queueDisposeTimer.Elapsed)} ms; " +
                $"NotificationDispose={FormatPerformanceMilliseconds(notificationDisposeTimer.Elapsed)} ms; " +
                $"SleepPreventionDispose={FormatPerformanceMilliseconds(sleepPreventionDisposeTimer.Elapsed)} ms; " +
                $"Tracks={Tracks.Count}; ResumeProjects={ResumeProjects.Count}");

            cleanupPerformancePhase.Complete();
            _shutdownPerformanceScope.Complete();
        }
        catch (Exception ex)
        {
            cleanupPerformancePhase.Fail(ex);
            _shutdownPerformanceScope.Fail(ex);
            _runtimeServices.DiagnosticLogService.WriteError("Window close cleanup failed", ex);
        }
        finally
        {
            _shutdownPerformanceScope = default;
            if (Application.Current is App app)
                app.TryFinalizeDisabledPerformanceSession();
        }
    }
    private void RunCloseCleanupStep(Action action)
    {
        try { action(); }
        catch (Exception ex)
        {
            _runtimeServices.DiagnosticLogService.WriteError("Window close cleanup step failed", ex);
        }
    }

    internal void PrepareForConfirmedSessionEnding(bool logoff)
    {
        if (_allowWindowClose) return;
        // Confirmation is the only entry. Do not checkpoint a large manifest or wait
        // for producers here; newer file revisions conservatively invalidate the order.
        _isShutdownInProgress = true;
        _allowWindowClose = true;
        RunCloseCleanupStep(() =>
        {
            if (_presentationState.IsProjectExtensionRun || _presetReconversionRollbackContext is not null ||
                ProjectReviewState.Phase == ProjectPipelineState.Merging) return;
            var phase = ProjectReviewState.Phase;
            var active = IsBusy || IsDiscImporting || IsExporting || _presentationState.IsPipelinePaused;
            if (_isResumeLoading || !_sessionProject.CanDeferCompletion(_currentProjectWorkFolder, phase, active)) return;
            DeferredProjectIdentity? identity = null;
            if (LoadedProjectState.IsAudioDisc && _activeAudioDiscManifest is { } audio)
                identity = new(audio.ProjectFolder, ProjectManifestTypes.AudioCdProject, audio.ProjectId);
            else if (LoadedProjectState.IsMp3Disc && _activeMp3DiscManifest is { } mp3)
                identity = new(mp3.ProjectFolder, ProjectManifestTypes.Mp3DiscProject, mp3.ProjectId);
            else if (!_presentationState.IsProjectExtensionRun && _presetReconversionRollbackContext is null)
                identity = _activeLocalPreparation;
            if (identity is null || !TrackPathService.PathEquals(identity.Folder, _currentProjectWorkFolder)) return;
            new DeferredProjectCompletionService(GetWorkingRootFolder(),
                _services.Foundation.EnvironmentContext.Environment.ToString())
                .RecordConfirmed(identity, _processingRunId, logoff);
        });
        RequestActiveOperationCancellation();
    }

    private void RequestActiveOperationCancellation()
    {
        if (_discImportCancellation is { IsCancellationRequested: false })
            RunCloseCleanupStep(() => _discImportCancellation.Cancel());

        if (_exportCancellation is { IsCancellationRequested: false })
            RunCloseCleanupStep(() => _exportCancellation.Cancel());

        if (_trackAnalysisCancellation is { IsCancellationRequested: false })
            RunCloseCleanupStep(() => _trackAnalysisCancellation.Cancel());
    }

    private void RefreshSystemSleepPrevention()
    {
        var shouldKeepAwake = _services.Foundation.SystemSleepPreventionActivityService.ShouldKeepAwake(
            new SystemSleepPreventionActivityInput(
                _settings.KeepComputerAwakeDuringLongOperations,
                ProjectReviewState.Phase,
                IsBusy,
                IsExporting,
                IsDiscImporting));

        _services.Foundation.SystemSleepPreventionService.SetActive(shouldKeepAwake);
    }

    private async Task RequestPipelinePauseAsync()
    {
        if (_presentationState.PauseRequested || _presentationState.IsPipelinePaused) return;
        var discCancellation = _discImportCancellation;
        var exportCancellation = _exportCancellation;
        var plan = _services.Pipeline.PipelinePauseRequestService.Create(
            new PipelinePauseRequestInput(
                ProjectReviewState.Phase,
                IsDiscImporting,
                IsExporting,
                discCancellation is { IsCancellationRequested: false },
                exportCancellation is { IsCancellationRequested: false }));

        if (!plan.ShouldPause)
            return;

        var pauseRequestTransition =
            _services.Pipeline.PipelinePauseTransitionService.CreatePauseRequest(
                _presentationState.IsPipelinePaused,
                _presentationState.IsPipelineFailed,
                IsBusy,
                IsExporting,
                IsDiscImporting);
        var transitionOwner = BeginPipelineUiTransition(PipelineUiTransitionKind.Pausing, PipelineUiTransitionTarget.Primary);
        ApplyPipelineTransition(pauseRequestTransition);
        StatusText = plan.StatusText;
        ExportProgressText = plan.ProgressText;

        try
        {
            // Let binding/layout/render work run before cancellation callbacks or synchronous cleanup.
            await Dispatcher.Yield(DispatcherPriority.Background);
            if (plan.CancelDiscImport && ReferenceEquals(discCancellation, _discImportCancellation))
                await discCancellation!.CancelAsync();
            if (plan.CancelExport && ReferenceEquals(exportCancellation, _exportCancellation))
                await exportCancellation!.CancelAsync();
            NotifyExportUiStateChanged();
        }
        catch (Exception ex)
        {
            _runtimeServices.DiagnosticLogService.WriteError("Pause request failed", ex);
            _presentationState.PauseRequested = false;
            StatusText = LocalizationText.Get("Pipeline.Transition.PauseFailed");
            EndPipelineUiTransition(transitionOwner);
        }
    }

    private bool TryCheckpointMetadataBeforeClose()
    {
        if (!_projectMetadataSession.IsAttached ||
            TryFlushCanonicalMetadataBeforeExtensionSnapshot(_projectMetadataSession.ProjectFolder!)) return true;
        _allowWindowClose = false;
        _isShutdownInProgress = false;
        CancelApplicationRestartRequest();
        NotifyExportUiStateChanged();
        return false;
    }

    private void StopAutomaticMaintenance() =>
        _maintenanceCoordinator?.StopAsync().GetAwaiter().GetResult();

    private bool TryCheckpointEditableProject()
    {
        // An export can still be in the previous Review/Completed phase during analysis.
        // Its manifest owner must drain before the UI snapshot writes another revision.
        if (_exportCancellation is not null || _discImportCancellation is not null ||
            _trackAnalysisCancellation is not null) return true;
        if (ProjectReviewState.Phase is not (ProjectPipelineState.ReviewBeforeMerge or ProjectPipelineState.Completed)
            || string.IsNullOrWhiteSpace(_currentProjectWorkFolder)) return true;
        try
        {
            StopAutomaticMaintenance();
            FlushPendingChapterMetadataRefresh();
            FlushCanonicalMetadataSessionForProject(_currentProjectWorkFolder);
            var projectType = ResolveCurrentProjectType(ProjectManifestTypes.FolderProject);
            _services.Workflows.ProjectSnapshotService.SaveEditableProjectSnapshot(
                _currentProjectWorkFolder, projectType, CreateProjectSnapshotFromUi(), Tracks.ToList(), BuildFinalOutputPath());
            _services.Foundation.ProjectIndexService.Catalog.MarkDirty();
            return true;
        }
        catch (Exception ex)
        {
            _runtimeServices.DiagnosticLogService.WriteError("Editable project checkpoint failed", ex);
            AppDialogService.Error(this, LocalizationText.Get("MainWindow.Project.SaveFailed.Title"),
                LocalizationText.Get("MainWindow.Project.SaveFailed.Message"), details: new[] { ex.Message });
            CancelApplicationRestartRequest();
            return false;
        }
    }

    private void TrySaveActiveProjectSnapshot()
    {
        if (_isDiscStopSaveInProgress) return;
        if (new DiscProjectStopSaveService().HasPendingCommit(_currentProjectWorkFolder)) return;
        if (_projectMetadataSession.IsAttached &&
            !TryFlushCanonicalMetadataBeforeExtensionSnapshot(_projectMetadataSession.ProjectFolder!)) return;

        if (_activeMp3DiscManifest is not null)
            TrySaveCurrentMp3DiscProjectSnapshot(_activeMp3DiscManifest);

        if (_activeAudioDiscManifest is not null)
            TrySaveCurrentAudioDiscProjectSnapshot(_activeAudioDiscManifest, force: true);
    }

    private void TryFlushCanonicalMetadataSession()
    {
        if (!_projectMetadataSession.IsAttached)
            return;

        try
        {
            StopAutomaticMaintenance();
            var saved = _projectMetadataSession.Flush();
            if (saved) _services.Foundation.ProjectIndexService.Catalog.MarkDirty();
        }
        catch (Exception ex)
        {
            _runtimeServices.DiagnosticLogService.WriteError(
                "Canonical ProjectMetadata flush failed.",
                ex);
        }
    }

    private void SynchronizeActiveMetadataBindings()
    {
        var snapshot = MetadataFieldBindingPreferencesService.CreateSnapshot(_settings.MetadataPreferences);
        if (!new ProjectMetadataFieldBindingSynchronizationService().SynchronizeActiveTargets(
                _projectMetadata.CanonicalMetadata,
                snapshot))
        {
            return;
        }

        TryFlushCanonicalMetadataSession();
    }

    private void CleanupCurrentProjectPartFiles()
    {
        if (string.IsNullOrWhiteSpace(_currentProjectWorkFolder) ||
            !Directory.Exists(_currentProjectWorkFolder))
        {
            return;
        }

        try
        {
            _services.Foundation.ConvertedFileCleanupService.DeletePartFiles(_currentProjectWorkFolder);
        }
        catch (Exception ex)
        {
            _runtimeServices.DiagnosticLogService.WriteError("Project part-file cleanup deferred until next start or export", ex);
        }
    }

    private static string GetDesktopFolder()
    {
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        if (!string.IsNullOrWhiteSpace(desktop) && Directory.Exists(desktop))
            return desktop;

        return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    private static string GetExistingFolderOrDefault(string? folderPath, string defaultFolder)
    {
        if (!string.IsNullOrWhiteSpace(folderPath) && Directory.Exists(folderPath))
            return folderPath;

        return defaultFolder;
    }


    private async void OpenResumeProjects_Click(object sender, RoutedEventArgs e)
    {
        if (IsBusy)
            return;

        try
        {
            _isProjectBrowserOpen = true;
            CheckAutomaticMaintenance();
            StopAutomaticMaintenance();
            var dialog = new ResumeProjectDialog(
                GetWorkingRootFolder(),
                _services.Foundation.ProjectIndexService,
                _services.Foundation.ProjectResumePlanService,
                _settings.ProjectRetentionDays,
                AppearanceSettingsService.GetEffectiveMode(_settings),
                SelectedResumeProject,
                _settings.ProjectRetentionMode,
                _services.Foundation.GlobalSettingsManager, RunProjectRebuild)
            {
                Owner = this
            };
            AttachDebugWindowPlacement(dialog, DebugWindowPlacementTarget.ResumeProject);

            var result = ModalDialogService.ShowDialog(dialog, this);

            if (dialog.InitialProjectLoadElapsed > TimeSpan.Zero)
            {
                _runtimeServices.DiagnosticLogService.WritePerformanceEvent(
                    "PROJECT DIALOG INITIAL LOAD",
                    dialog.InitialProjectLoadElapsed,
                    $"Scan={FormatPerformanceMilliseconds(dialog.InitialProjectScanElapsed)} ms; " +
                    $"InitialPreview={FormatPerformanceMilliseconds(dialog.InitialSelectionPreviewElapsed)} ms; " +
                    $"Projects={dialog.InitialProjectCount}");
            }

            if (dialog.SelectedProject is not null)
                SelectedResumeProject = dialog.SelectedProject;

            if (result == true && dialog.SelectedResumePlan is not null)
                await LoadResumeProjectIntoWorkspaceAsync(dialog.SelectedResumePlan);
        }
        catch (Exception ex)
        {
            AppDialogService.Error(this,
                LocalizationText.Get("Settings.Projects.LoadFailed.Title"),
                LocalizationText.Format("Settings.Projects.LoadFailed.Message", ex.Message));
        }
        finally
        {
            _isProjectBrowserOpen = false;
            CheckAutomaticMaintenance();
        }
    }

    private void SetPipelineState(ProjectPipelineState state)
    {
        if (state is ProjectPipelineState.AcquiringSources or ProjectPipelineState.Converting or
            ProjectPipelineState.ReviewBeforeMerge or ProjectPipelineState.Merging or ProjectPipelineState.Completed)
            SetOriginalAudioColumnsVisible(false);
        if (ProjectReviewState.Phase == state)
        {
            RefreshSystemSleepPrevention();
            return;
        }

        _projectReviewState = ProjectReviewState.WithPhase(state);
        if (state is ProjectPipelineState.ReviewBeforeMerge or ProjectPipelineState.Completed)
            _sessionProject.Clear();
        if (state is ProjectPipelineState.Preparing or ProjectPipelineState.ReviewBeforeMerge or ProjectPipelineState.Merging or ProjectPipelineState.Completed)
        {
            var stableTransition =
                _services.Pipeline.PipelinePauseTransitionService.CreateStableStateActivation(
                    _presentationState.IsPipelineFailed,
                    IsBusy,
                    IsExporting,
                    IsDiscImporting);
            ApplyPipelineTransition(stableTransition);
        }
        PersistDiscPipelineState(state);
        if (!string.IsNullOrWhiteSpace(_currentProjectWorkFolder))
            _services.Foundation.ProjectIndexService.Catalog.MarkDirty();
        NotifyExportUiStateChanged();
        RefreshSystemSleepPrevention();
    }

    private void PersistDiscPipelineState(ProjectPipelineState state)
    {
        try
        {
            if (_activeAudioDiscManifest is not null)
            {
                _activeAudioDiscManifest.PipelineState = state.ToManifestValue();
                TrySaveCurrentAudioDiscProjectSnapshot(_activeAudioDiscManifest, force: true);
                return;
            }

            var mp3Manifest = _activeMp3DiscManifest;
            if (mp3Manifest is null && LoadedProjectState.IsMp3Disc &&
                !string.IsNullOrWhiteSpace(_currentProjectWorkFolder))
            {
                mp3Manifest = _services.Foundation.Mp3DiscProjectService.TryLoad(_currentProjectWorkFolder);
            }

            if (mp3Manifest is not null)
            {
                mp3Manifest.PipelineState = state.ToManifestValue();
                StopAutomaticMaintenance();
                _services.Foundation.Mp3DiscProjectService.Save(mp3Manifest);
                _services.Foundation.ProjectIndexService.Catalog.MarkDirty();
            }
        }
        catch
        {
            // Der zentrale UI-Zustand darf nicht an einer zusätzlichen Manifest-Synchronisierung scheitern.
        }
    }

    private async Task<bool> EnsureResumeDiscPreviewAsync()
    {
        if (!_resumeDiscSourceSelectionRequired)
            return true;
        if (_trackAnalysisCancellation is not null)
            return false;
        var selected = ShowDiscSourceSelectionDialog();
        if (string.IsNullOrWhiteSpace(selected))
            return false;

        using var previewCancellation = new CancellationTokenSource();
        _trackAnalysisCancellation = previewCancellation;
        var token = previewCancellation.Token;
        var wasBusy = IsBusy;
        IsBusy = true;
        try
        {
            if (_activeAudioDiscManifest is { } audioManifest)
            {
                var next = _services.Foundation.AudioDiscProjectService.GetNextRequiredDiscNumber(audioManifest);
                if (next is null) return true;
                var prepared = await WaitForNextAudioDiscAsync(audioManifest, next.Value, token,
                    startWithSelectedDrive: true, previewOnly: true, selectedSource: selected);
                if (prepared is null) return false;
                token.ThrowIfCancellationRequested();
                _resumeDiscPreview.Set(prepared);
                Tracks.ReplaceAll(DiscResumePreviewState.AppendAudioTracks(Tracks,
                    _services.Foundation.AudioDiscProjectService.CreateTrackPreview(audioManifest, prepared)));
                selected = prepared.SourceDriveRoot;
            }
            else
            {
                var manifest = TryLoadCurrentMp3DiscProjectManifest(resetStateWhenFolderMissing: false);
                if (manifest is null) return false;
                var next = _services.Foundation.Mp3DiscProjectService.BuildResumePlan(manifest).NextMissingDiscNumber;
                if (next is null) return true;
                var workflow = _services.Workflows.Mp3DiscImportWorkflowService;
                var signatures = workflow.BuildCompletedDiscSignatureSet(manifest);
                var outcome = await WaitForNextDiscAsync(selected, next.Value, manifest.TotalDiscs,
                    signatures, token, acceptedSource => selected = acceptedSource, startWithSelectedDrive: true, previewOnly: true);
                if (outcome is not (DiscWaitDialogOutcome.Ready or DiscWaitDialogOutcome.ReadyWithDuplicateOverride))
                    return false;
                var precheck = await Task.Run(() => workflow.AnalyzeDiscForImport(selected, signatures, token), token);
                if (!precheck.Analysis.IsSupportedDataDisc ||
                    (precheck.IsAlreadyImported && outcome != DiscWaitDialogOutcome.ReadyWithDuplicateOverride))
                    return false;
                var sources = workflow.ReuseExistingSourceIds(manifest.ProjectFolder, next.Value,
                    precheck.PlannedSources, manifest);
                var prepared = new Mp3DiscPendingChapterDisc(next.Value, precheck.Signature, sources);
                var previewTracks = await _services.Media.FolderScanner.ScanAsync(selected, TrackNumberPreference.FileName,
                    cancellationToken: token);
                token.ThrowIfCancellationRequested();
                _services.Discs.Mp3DiscTrackReconciliationService.NormalizeProjectDiscNumber(previewTracks, next.Value);
                _services.Discs.Mp3DiscTrackReconciliationService.ApplyPlannedSourceIds(previewTracks, selected, next.Value, sources);
                _resumeDiscPreview.Set(prepared, outcome == DiscWaitDialogOutcome.ReadyWithDuplicateOverride);
                _transientMp3DiscChapterPreview = prepared;
                AppendDiscPreviewTracks(previewTracks, selected, next.Value, clearExistingTracks: false);
            }

            _resumeDiscStartDriveRoot = selected;
            SelectedFolder = selected;
            _resumeDiscSourceSelectionRequired = false;
            UpdateIndexes();
            RefreshTrackPresentationViews();
            AutoFitTrackColumnsAfterRender();
            NotifyExportUiStateChanged();
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception ex)
        {
            _runtimeServices.DiagnosticLogService.WriteError("DiscResumePreview", ex);
            EnterPipelinePause(LocalizationText.Get("MainWindow.Project.Paused"));
            return false;
        }
        finally
        {
            _trackAnalysisCancellation = null;
            IsBusy = wasBusy;
            NotifyExportUiStateChanged();
        }
    }

    private void DiscardChangedResumeDiscPreview(int discNumber)
    {
        if (_resumeDiscPreview.DiscNumber != discNumber) return;
        Tracks.ReplaceAll(Tracks.Where(track => track.DiscNumber != discNumber).ToList());
        _resumeDiscPreview.Clear();
        _resumeDiscStartDriveRoot = null;
        _transientMp3DiscChapterPreview = null;
        PersistTrackListState();
    }

    private void ResetLoadedResumeProjectState()
    {
        if (!_isDiscStopSaveInProgress && !_isProjectCloseInProgress && !_isShutdownInProgress)
            _presentationState.ClearUiTransition();
        _lockedDiscResumePreset = null;
        _resumeDiscSourceSelectionRequired = false;
        _resumeDiscStartDriveRoot = null;
        _resumeDiscPreview.Clear();
        _mp3DiscChapterSourceCatalog.Clear();
        _transientMp3DiscChapterPreview = null;
        ApplyLoadedProjectState(LoadedProjectStateSnapshot.None);
        _activeAudioDiscManifest = null;
        _pendingAudioDisc = null;
        _isAudioDiscProjectAwaitingRip = false;
        SetPipelineState(ProjectPipelineState.Preparing);
        ResetManualMergeReviewState();
        NotifyExportUiStateChanged();
    }

    private void ResetManualMergeReviewState()
    {
        var hadManualMergeReviewState = ProjectReviewState.IsWaitingForManualMergeReview ||
                                        _manualMergeReviewNeedsReconversion ||
                                        _manualMergeReviewHasPreparedTracks ||
                                        !string.IsNullOrWhiteSpace(_manualMergeReviewPreparedPreset);

        _projectReviewState = ProjectReviewState.WithManualMergeReview(false);
        _manualMergeReviewPreparedPreset = "";
        _manualMergeReviewNeedsReconversion = false;
        _manualMergeReviewHasPreparedTracks = false;
        _projectReviewState = ProjectReviewState.WithCompletion(false);
        ClearPresetReconversionPauseContext();

        if (!hadManualMergeReviewState)
            return;

        SetTrackActionDisplayOverride("");
        NotifyExportUiStateChanged();
    }

    private void UpdateManualMergeReviewPresetState(bool forceRefresh = false, bool updateStatusText = true)
    {
        if (!ProjectReviewState.IsReviewBeforeMerge && !ProjectReviewState.IsCompleted)
            return;

        var includedTracks = Tracks.Where(track => !track.IsExcluded).ToList();
        var preparation = _services.Workflows.TrackPreparedStateRefreshService.GetPreparationProgress(
            includedTracks,
            SelectedExportPreset);
        var hasCompletePreparedPreset = preparation.TotalCount > 0 && preparation.PendingCount == 0;
        var needsReconversion = !hasCompletePreparedPreset;
        var hasPreparedTracks = preparation.PreparedCount > 0;

        IsProgressIndeterminate = false;
        ExportProgressPercent = preparation.Percent;

        var reconversionStateChanged = _manualMergeReviewNeedsReconversion != needsReconversion;
        var preparedStateChanged = _manualMergeReviewHasPreparedTracks != hasPreparedTracks;
        if (reconversionStateChanged || preparedStateChanged || forceRefresh)
        {
            _manualMergeReviewNeedsReconversion = needsReconversion;
            _manualMergeReviewHasPreparedTracks = hasPreparedTracks;
            SetTrackActionDisplayOverride(needsReconversion
                ? LocalizationText.Get("MainWindow.Action.Reconvert")
                : LocalizationText.Get("MainWindow.Action.Merge"));
            NotifyExportUiStateChanged();
        }

        if (!updateStatusText)
            return;

        if (needsReconversion && (LoadedProjectState.IsLocal || LoadedProjectState.IsMp3Disc))
        {
            var preset = ExportPreset.Parse(SelectedExportPreset);
            var total = includedTracks.Count;
            var totalChapters = ExportPreviewMetricsService.CountPlannedChapters(includedTracks);
            var projectKind = LoadedProjectState.IsMp3Disc
                ? WorkflowProjectKind.Mp3Disc
                : WorkflowProjectKind.Folder;
            var totalDiscs = LoadedProjectState.IsMp3Disc
                ? Math.Max(1, includedTracks.Select(track => track.DiscNumber ?? 1).DefaultIfEmpty(1).Max())
                : 0;

            ApplyWorkflowStatusViewState(new WorkflowStatusFormatter().Format(new WorkflowStatusSnapshot
            {
                ProjectId = _currentProjectWorkFolder,
                ProjectKind = projectKind,
                ProjectState = ProjectReviewState.Phase,
                IsPresetChangePending = true,
                TotalSourceItems = total,
                TotalChapters = totalChapters,
                SourceProgress = LoadedProjectState.IsMp3Disc
                    ? new SourceAcquisitionProgress(
                        SourceAcquisitionKind.Copying,
                        total,
                        total,
                        total,
                        total,
                        CurrentDisc: totalDiscs,
                        TotalDiscs: totalDiscs,
                        Percent: 100,
                        CurrentSourceFinished: true,
                        AllSourcesFinished: true)
                    : null,
                ConversionProgress = new ConversionActivityProgress(
                    preparation.PreparedCount,
                    total,
                    preparation.Percent,
                    Array.Empty<int>(),
                    preset.BitrateKbps,
                    preset.Channels == 1,
                    IsLive: false)
            }));
        }
        else if (needsReconversion)
        {
            UpdateFinalStatus(Tracks.Count);
            ExportProgressText = LocalizationText.Format("MainWindow.Progress.Prepared", preparation.Percent, preparation.PreparedCount, preparation.TotalCount);
        }
        else
        {
            StatusText = LocalizationText.Get("Workflow.Review.PreparedStatus");
            ExportProgressText = LocalizationText.Get("Workflow.Review.AudioProgress");
        }
    }

    private void InitializeResumedManualMergeReviewPresetBaseline(
        ProjectResumePlan plan,
        ProjectResumeLoadResult loadResult)
    {
        _manualMergeReviewPreparedPreset = string.Empty;

        if (loadResult.PipelineState is not (ProjectPipelineState.ReviewBeforeMerge or ProjectPipelineState.Completed))
            return;

        var preparedPreset = ResolveResumedPreparedPreset(plan);
        if (string.IsNullOrWhiteSpace(preparedPreset))
            return;

        if (string.IsNullOrWhiteSpace(plan.SelectedPreset) &&
            !string.Equals(SelectedExportPreset, preparedPreset, StringComparison.OrdinalIgnoreCase))
        {
            SetSelectedExportPresetSilently(preparedPreset);
        }

        _manualMergeReviewPreparedPreset = preparedPreset;
    }

    private static string ResolveResumedPreparedPreset(ProjectResumePlan plan)
    {
        if (!string.IsNullOrWhiteSpace(plan.SelectedPreset))
            return plan.SelectedPreset.Trim();

        var preparedPresets = plan.Tracks
            .Select(track => track.Preset?.Trim() ?? string.Empty)
            .Where(preset => !string.IsNullOrWhiteSpace(preset))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return preparedPresets.Length == 1
            ? preparedPresets[0]
            : string.Empty;
    }

    private void SetTrackActionDisplayOverride(string value)
    {
        foreach (var track in Tracks)
            track.DisplayProcessingActionOverride = track.IsExcluded
                ? LocalizationText.Get("StatusExcluded")
                : value;

        RefreshTrackPresentationViews();
    }

    private bool TryCheckpointWorkspaceReplacement(int expectedVersion)
    {
        if (expectedVersion != _workspaceReplacementVersion || _isWorkspaceReplacementInProgress ||
            _isProjectCloseInProgress || _isDiscStopSaveInProgress || _isShutdownInProgress || IsBusy) return false;
        if (_sessionProject.IsUnstartedAudio(_currentProjectWorkFolder) && !TryDiscardAudioPreparation()) return false;
        if (!TryCheckpointEditableProject()) return false;
        _workspaceReplacementVersion++;
        return true;
    }

    private bool TryDiscardAudioPreparation()
    {
        if (_isWorkspaceReplacementInProgress || _isProjectCloseInProgress || _isDiscStopSaveInProgress ||
            IsBusy || IsDiscImporting || IsExporting || _discImportCancellation is not null ||
            _exportCancellation is not null || _trackAnalysisCancellation is not null) return false;
        var folder = _currentProjectWorkFolder;
        _isWorkspaceReplacementInProgress = true;
        try
        {
            if (_isShutdownInProgress || _allowWindowClose || IsBusy || IsDiscImporting || IsExporting ||
                _discImportCancellation is not null || _exportCancellation is not null || _trackAnalysisCancellation is not null)
                throw new IOException(LocalizationText.Get("Project.Preparation.Unsafe"));
            _sessionProject.ValidateAudioDiscard(GetWorkingRootFolder(),
                GetProjectFolderStructure().AudioDiscProjectsFolder, _currentProjectWorkFolder);
            if (!TrackPathService.PathEquals(folder, _currentProjectWorkFolder)) return false;
            // No producer or asynchronous gap exists in this never-started workspace.
            // Temporarily release only the active-folder guard; restore it on any failure.
            _currentProjectWorkFolder = string.Empty;
            var deletion = _services.Foundation.ProjectIndexService.DeleteProject(GetWorkingRootFolder(), folder);
            if (!deletion.Deleted) throw new IOException(deletion.ErrorMessage);
            ClearCurrentProjectWorkspace();
            _workspaceReplacementVersion++;
            RefreshResumeProjects(showStatus: false);
            return true;
        }
        catch (Exception error)
        {
            _currentProjectWorkFolder = folder;
            _runtimeServices.DiagnosticLogService.WriteError("Unstarted audio preparation cleanup failed", error);
            AppDialogService.Error(this, LocalizationText.Get("MainWindow.Project.DeleteFailed.Title"), LocalizationText.Get("Common.OperationFailed"), details: new[] { error.Message });
            return false;
        }
        finally { _isWorkspaceReplacementInProgress = false; }
    }

    private async Task LoadResumeProjectIntoWorkspaceAsync(ProjectResumePlan plan)
    {
        var replacementVersion = _workspaceReplacementVersion;
        if (!TryCheckpointWorkspaceReplacement(replacementVersion)) return;
        var resumeLoadTimer = Stopwatch.StartNew();
        var prepareElapsed = TimeSpan.Zero;
        var ffprobeElapsed = TimeSpan.Zero;
        var resumeRefreshElapsed = TimeSpan.Zero;
        var loadSucceeded = false;
        var previousStatusText = _statusText;
        var previousProgressText = ExportProgressText;
        var loadingText = LocalizationText.Get("MainWindow.Project.ResumeLoading");
        var performanceApp = Application.Current as App;
        PerformanceOperationScope resumePerformanceScope = default;
        if (performanceApp is not null)
        {
            resumePerformanceScope = performanceApp.StartPerformanceOperation(
                "project.resume",
                PerformanceProjectType.Resume,
                new PerformanceWorkloadSnapshot
                {
                    ProjectType = PerformanceProjectType.Resume,
                    TrackCount = plan.Tracks.Count,
                    FileCount = plan.Tracks.Count,
                    DiscCount = plan.TotalDiscs > 0 ? plan.TotalDiscs : null
                });
        }

        void MeasureResumePhase(string name, Action action)
        {
            var phase = performanceApp is null ? default(PerformancePhaseScope) :
                performanceApp.StartPerformancePhase(resumePerformanceScope.OperationId, name);
            try { action(); phase.Complete(); }
            catch (OperationCanceledException) { phase.Cancel(); throw; }
            catch (Exception error) { phase.Fail(error); throw; }
        }

        IsBusy = true;
        _isResumeLoading = true;
        StatusText = loadingText;
        ExportProgressText = loadingText;
        IsProgressIndeterminate = true;
        OnPropertyChanged(nameof(ExportProgressVisibility));

        try
        {
            // Paint feedback before beginning the actual workspace load.
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
            var phaseTimer = Stopwatch.StartNew();
            var preparePerformancePhase = performanceApp is null
                ? default(PerformancePhaseScope)
                : performanceApp.StartPerformancePhase(resumePerformanceScope.OperationId, "resume.prepare");
            ProjectResumeLoadResult loadResult;
            try
            {
                loadResult = await Task.Run(() => _services.Workflows.ProjectResumeLoadWorkflowService.Prepare(plan));
                prepareElapsed = phaseTimer.Elapsed;
                if (!loadResult.Success)
                {
                    var failure = new InvalidOperationException("Resume preparation returned an unsuccessful result.");
                    preparePerformancePhase.Fail(failure);
                    resumePerformanceScope.Fail(failure);
                    AppDialogService.Error(
                        this,
                        LocalizationText.Get("MainWindow.Resume.AudioLoadFailed.Title"),
                        loadResult.ErrorMessage);
                    return;
                }

                preparePerformancePhase.Complete();
            }
            catch (OperationCanceledException)
            {
                prepareElapsed = phaseTimer.Elapsed;
                preparePerformancePhase.Cancel();
                throw;
            }
            catch (Exception ex)
            {
                prepareElapsed = phaseTimer.Elapsed;
                preparePerformancePhase.Fail(ex);
                throw;
            }

            MeasureResumePhase("resume.source-metadata-backfill",
                () => BackfillMissingSourceMetadataSnapshots(plan.ProjectFolder));

            BeginWorkspaceTransition();
            try
            {
                MeasureResumePhase("resume.workspace-state", () =>
                {
                    _resumeDiscPreview.Clear();
                    _transientMp3DiscChapterPreview = null;
                    _sessionProject.Clear();
                    _currentProjectWorkFolder = plan.ProjectFolder;
                    _pendingDiscProjectSourceFolder = string.Empty;
                    _pendingAudioDisc = null;
                    ApplyLoadedProjectState(
                        LoadedProjectStateSnapshot.Create(
                            loadResult.NeedsMp3DiscImport,
                            loadResult.IsMp3DiscProject,
                            loadResult.IsAudioDiscProject,
                            !loadResult.IsMp3DiscProject && !loadResult.IsAudioDiscProject));
                    _activeAudioDiscManifest = loadResult.AudioDiscManifest;
                    _isAudioDiscProjectAwaitingRip = loadResult.IsAudioDiscProjectAwaitingRip;
                    ApplyProjectReviewState(
                        new ProjectReviewStateSnapshot(
                            loadResult.PipelineState,
                            loadResult.IsWaitingForManualMergeReview,
                            loadResult.IsCompletedProject));
                    SetMetadataEditingAvailable(true);
                    _currentFolderPath = loadResult.CurrentFolderPath;
                    SelectedFolder = loadResult.SelectedFolder;
                    SetSelectedSourceDisplayOverride(
                        SelectedResumeProject?.ListDisplayName ?? plan.DisplayName,
                        loadResult.SelectedFolder);
                    if (loadResult.IsAudioDiscProject)
                        SetAudioDiscSourceDisplayOverride(loadResult.AudioDiscManifest);

                    if (!string.IsNullOrWhiteSpace(plan.OutputExtension))
                        OutputExtension = plan.OutputExtension;

                    if (!string.IsNullOrWhiteSpace(plan.FileNameTemplate))
                        FileNameTemplate = plan.FileNameTemplate;

                    if (!string.IsNullOrWhiteSpace(plan.SelectedPreset))
                    {
                        _lockedDiscResumePreset = null;
                        SetSelectedExportPresetSilently(plan.SelectedPreset);
                    }
                    _lockedDiscResumePreset = plan.IsStoppedDiscProject ? plan.SelectedPreset : null;
                    _resumeDiscSourceSelectionRequired = loadResult.IsAudioDiscProjectAwaitingRip || loadResult.NeedsMp3DiscImport;
                    _resumeDiscStartDriveRoot = null;

                    InitializeResumedManualMergeReviewPresetBaseline(plan, loadResult);

                    // ParallelJobsInput is a global user setting and intentionally survives project resume.

                });

                MeasureResumePhase("resume.metadata", () =>
                {
                    _projectMetadataSession.Discard();
                    _projectMetadata.Clear("");
                    if (loadResult.CanonicalMetadata is not null)
                    {
                        var canonicalMetadata = _projectMetadataSession.OpenExisting(plan.ProjectFolder);
                        _projectMetadata.ApplyCanonicalMetadata(canonicalMetadata);
                    }
                    else
                    {
                        // Defensive fallback for externally constructed/stale resume plans.
                        // Normal ProjectResumePlanService plans already own a canonical metadata store.
                        ApplyLoadedTitleAndAlbum(plan.BookTitle, plan.Album);
                        Author = plan.Author;
                        Narrator = plan.Narrator;

                        if (!string.IsNullOrWhiteSpace(plan.Genre))
                            Genre = plan.Genre;

                        var fallbackMetadata = _projectMetadata.EnsureCanonicalMetadata(MetadataOrigin.None);
                        _projectMetadataSession.BeginNew(fallbackMetadata);
                        _projectMetadataSession.AttachNewProject(plan.ProjectFolder);
                    }

                    ApplySourceMetadataAggregation(
                        plan.ProjectFolder,
                        ProjectMetadataInitializationState.ProjectMetadataInitialized,
                        !loadResult.IsMp3DiscProject && !loadResult.IsAudioDiscProject
                            ? CreateLocalFolderAggregationOptions(loadResult.SelectedFolder)
                            : loadResult.IsMp3DiscProject
                                ? CreateMp3DiscProjectAggregationOptions()
                                : CreateAudioDiscProjectAggregationOptions());

                });
                MeasureResumePhase("resume.cover", () => LoadResumeCover(plan));
                MeasureResumePhase("resume.track-replace", () => Tracks.ReplaceAll(loadResult.Tracks));
                SetOriginalAudioColumnsVisible(false, force: true);
            }
            finally
            {
                EndWorkspaceTransition();
            }

            _chapterGridColumnLayoutState.MarkAutoFitPending();
            MeasureResumePhase("resume.update-indexes.initial", UpdateIndexes);

            if (_ffmpegStatus.IsComplete)
            {
                phaseTimer.Restart();
                var ffprobePerformancePhase = performanceApp is null
                    ? default(PerformancePhaseScope)
                    : performanceApp.StartPerformancePhase(resumePerformanceScope.OperationId, "resume.ffprobe");
                try
                {
                    var ffprobePerformanceContext = performanceApp?.CreatePerformanceExternalProcessContext(
                        resumePerformanceScope.OperationId,
                        ffprobePerformancePhase.PhaseId,
                        "resume.ffprobe");
                    using var resumeAnalysisCancellation = new CancellationTokenSource();
                    _trackAnalysisCancellation = resumeAnalysisCancellation;
                    _trackAnalysisCancellationScope = TrackAnalysisCancellationScope.ResumeLoad;
                    NotifyExportUiStateChanged();
                    bool resumeProbeCompleted;
                    try
                    {
                        resumeProbeCompleted = await EnrichResumeTracksWithFfprobeAsync(
                            ffprobePerformanceContext,
                            resumeAnalysisCancellation.Token);
                    }
                    finally
                    {
                        _trackAnalysisCancellation = null;
                        _trackAnalysisCancellationScope = TrackAnalysisCancellationScope.None;
                        NotifyExportUiStateChanged();
                    }

                    if (!resumeProbeCompleted)
                    {
                        ffprobeElapsed = phaseTimer.Elapsed;
                        ffprobePerformancePhase.Cancel();
                        ResetWorkspaceAfterCanceledTrackAnalysis(TrackAnalysisCancellationScope.ResumeLoad);
                        return;
                    }

                    ffprobeElapsed = phaseTimer.Elapsed;
                    ffprobePerformancePhase.Complete();
                }
                catch (OperationCanceledException)
                {
                    ffprobeElapsed = phaseTimer.Elapsed;
                    ffprobePerformancePhase.Cancel();
                    ResetWorkspaceAfterCanceledTrackAnalysis(TrackAnalysisCancellationScope.ResumeLoad);
                    return;
                }
                catch (Exception ex)
                {
                    ffprobeElapsed = phaseTimer.Elapsed;
                    ffprobePerformancePhase.Fail(ex);
                    throw;
                }
            }

            MeasureResumePhase("resume.processing-actions",
                () => RecalculateProcessingActionsForCurrentPreset(requestTrackStateRefresh: false));
            MeasureResumePhase("resume.update-indexes.final", UpdateIndexes);
            MeasureResumePhase("resume.final-ui", () =>
            {
                _isResumeLoading = false;
                IsProgressIndeterminate = false;
                OnPropertyChanged(nameof(IsProgressIndeterminate));
                OnPropertyChanged(nameof(ExportProgressVisibility));
                UpdateFinalStatus(Tracks.Count);
                ExportProgressPercent = 0;
                ExportProgressText = string.IsNullOrWhiteSpace(loadResult.ProgressText)
                    ? BuildIdleExportProgressText()
                    : loadResult.ProgressText;
                StatusText = loadResult.StatusText;
                PublishLoadedProjectStatus(loadResult);
                UpdateManualMergeReviewPresetState(forceRefresh: true, updateStatusText: false);
                RefreshPartialPresetProgressText();

                NotifyExportUiStateChanged();
                OnPropertyChanged(nameof(CoverHintVisibility));
                OnExportPreviewChanged();
                AutoFitTrackColumnsAfterRender();
            });

            phaseTimer.Restart();
            var refreshPerformancePhase = performanceApp is null
                ? default(PerformancePhaseScope)
                : performanceApp.StartPerformancePhase(resumePerformanceScope.OperationId, "resume.refresh");
            try
            {
                RefreshResumeProjects(showStatus: false);
                resumeRefreshElapsed = phaseTimer.Elapsed;
                refreshPerformancePhase.Complete();
            }
            catch (Exception ex)
            {
                resumeRefreshElapsed = phaseTimer.Elapsed;
                refreshPerformancePhase.Fail(ex);
                throw;
            }

            loadSucceeded = true;
            resumePerformanceScope.Complete();
        }
        catch (OperationCanceledException)
        {
            resumePerformanceScope.Cancel();
            throw;
        }
        catch (Exception ex)
        {
            resumePerformanceScope.Fail(ex);
            throw;
        }
        finally
        {
            _isResumeLoading = false;
            IsProgressIndeterminate = false;
            OnPropertyChanged(nameof(IsProgressIndeterminate));
            OnPropertyChanged(nameof(ExportProgressVisibility));
            if (!loadSucceeded && ExportProgressText == loadingText)
            {
                StatusText = previousStatusText;
                ExportProgressText = previousProgressText;
            }
            resumeLoadTimer.Stop();

            _runtimeServices.DiagnosticLogService.WritePerformanceEvent(
                "RESUME PROJECT LOAD",
                resumeLoadTimer.Elapsed,
                $"Success={loadSucceeded}; " +
                $"Prepare={FormatPerformanceMilliseconds(prepareElapsed)} ms; " +
                $"Ffprobe={FormatPerformanceMilliseconds(ffprobeElapsed)} ms; " +
                $"ResumeRefresh={FormatPerformanceMilliseconds(resumeRefreshElapsed)} ms; " +
                $"Tracks={Tracks.Count}");

            IsBusy = false;
            NotifyExportUiStateChanged();
            performanceApp?.TryFinalizeDisabledPerformanceSession();
        }
        if (loadSucceeded && plan.IsMergePaused)
            ApplyPipelineTransition(_services.Pipeline.PipelinePauseTransitionService.CreateCompletedPause());

        if (loadSucceeded && LoadedProjectState.IsMp3Disc && !plan.IsStoppedDiscProject &&
            ProjectReviewState.Phase is ProjectPipelineState.AcquiringSources or ProjectPipelineState.Converting)
            ApplyPipelineTransition(_services.Pipeline.PipelinePauseTransitionService.CreateCompletedPause());

        if (loadSucceeded)
            ConfirmLoadedPresetBitrateIfNeeded();

        if (loadSucceeded && plan.IsStoppedDiscProject && !plan.IsMergePaused)
        {
            ApplyPipelineTransition(_services.Pipeline.PipelinePauseTransitionService.CreateCompletedPause());
            NotifyExportUiStateChanged();
            await ShowCurrentProjectSetupDialogAsync();
        }
    }

    private void BeginWorkspaceTransition()
    {
        if (_isWorkspaceTransitioning)
            throw new InvalidOperationException("A workspace transition is already active.");

        _isWorkspaceTransitioning = true;
        _chapterMetadataRefreshTimer.Stop();
        NotifyExportUiStateChanged();
    }

    private void EndWorkspaceTransition()
    {
        _chapterMetadataRefreshTimer.Stop();
        _isWorkspaceTransitioning = false;
        NotifyExportUiStateChanged();
    }

    private void ApplyLoadedTitleAndAlbum(string? title, string? album)
    {
        var state = TitleAlbumLoadPolicy.Resolve(title, album);
        BookTitle = state.Title;
        Album = state.Album;
    }


    private void PublishLoadedProjectStatus(ProjectResumeLoadResult loadResult)
    {
        if (loadResult.LoadStatusSnapshot is null)
            return;

        var operationId = BeginWorkflowStatusOperation(_currentProjectWorkFolder);
        PublishWorkflowStatus(operationId, loadResult.LoadStatusSnapshot);
        EndWorkflowStatusOperation(operationId);
    }

    private void LoadResumeCover(ProjectResumePlan plan)
    {
        _projectMetadata.ApplyCoverMetadata(
            plan.CoverSourcePath,
            plan.ProcessedCoverPath,
            _projectMetadata.CanonicalMetadata.Cover.Origin);

        if (!string.IsNullOrWhiteSpace(_projectMetadata.ProcessedCoverPath) && File.Exists(_projectMetadata.ProcessedCoverPath))
        {
            CoverPreviewSource = _projectMetadata.ProcessedCoverPath;
            return;
        }

        if (!string.IsNullOrWhiteSpace(_projectMetadata.CoverSourcePath) && File.Exists(_projectMetadata.CoverSourcePath))
        {
            try
            {
                SetCoverFromFile(_projectMetadata.CoverSourcePath);
                return;
            }
            catch
            {
                // Wenn das alte Cover nicht mehr geladen werden kann, bleibt das Projekt trotzdem nutzbar.
            }
        }

        _projectMetadata.ApplyCoverMetadata("", "", MetadataOrigin.None);
        CoverPreviewSource = "";
    }

    private void LoadResumeTracks(ProjectResumePlan plan)
    {
        var loadResult = _services.Workflows.ProjectResumeLoadWorkflowService.Prepare(plan);
        if (!loadResult.Success)
            return;

        Tracks.ReplaceAll(loadResult.Tracks);
        SetOriginalAudioColumnsVisible(false, force: true);
        UpdateIndexes();
    }

    private void RefreshLoadedProjectTrackStateFromPersistedProject(string projectFolder, ProjectResumePlan? preparedPlan = null)
    {
        var statusText = _statusText;
        var progressText = ExportProgressText;
        var plan = preparedPlan ?? _services.Foundation.ProjectResumePlanService.BuildFromProjectFolder(projectFolder);
        if (plan is null || plan.Tracks.Count == 0)
            return;

        var previousTracksByIdentity = Tracks
            .Select(track => new { Track = track, Identity = GetTrackRefreshIdentity(track) })
            .Where(item => !string.IsNullOrWhiteSpace(item.Identity))
            .GroupBy(item => item.Identity, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Track, StringComparer.OrdinalIgnoreCase);

        LoadResumeTracks(plan);

        foreach (var refreshedTrack in Tracks)
        {
            var identity = GetTrackRefreshIdentity(refreshedTrack);
            if (string.IsNullOrWhiteSpace(identity) ||
                !previousTracksByIdentity.TryGetValue(identity, out var previousTrack))
            {
                continue;
            }

            refreshedTrack.TagTitle = previousTrack.TagTitle;
            refreshedTrack.Artist = previousTrack.Artist;
            refreshedTrack.BitrateKbps = previousTrack.BitrateKbps;
            refreshedTrack.SampleRateHz = previousTrack.SampleRateHz;
            refreshedTrack.Channels = previousTrack.Channels;
            refreshedTrack.ChannelLayout = previousTrack.ChannelLayout;
            refreshedTrack.Codec = previousTrack.Codec;
            refreshedTrack.AudioValidationPassed = previousTrack.AudioValidationPassed;
            refreshedTrack.EmbeddedChapters = previousTrack.EmbeddedChapters.ToList();

            if (refreshedTrack.DurationTicks is null or <= 0 && previousTrack.DurationTicks is > 0)
            {
                refreshedTrack.DurationTicks = previousTrack.DurationTicks;
                refreshedTrack.Duration = previousTrack.Duration;
            }

            if (refreshedTrack.SizeMb <= 0 && previousTrack.SizeMb > 0)
                refreshedTrack.SizeMb = previousTrack.SizeMb;
        }

        RecalculateProcessingActionsForCurrentPreset();
        UpdateIndexes();
        UpdateFinalStatus(Tracks.Count);
        RefreshTrackPresentationViews();
        OnExportPreviewChanged();
        StatusText = statusText;
        ExportProgressText = progressText;
    }

    private static string GetTrackRefreshIdentity(TrackInfo track)
    {
        if (!string.IsNullOrWhiteSpace(track.FilePath))
        {
            try
            {
                return "path:" + Path.GetFullPath(track.FilePath)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch
            {
                return "path:" + track.FilePath.Trim();
            }
        }

        if (track.DiscNumber is > 0 && track.TrackNumber is > 0)
            return $"disc:{track.DiscNumber}:track:{track.TrackNumber}";

        return string.Empty;
    }

    private static void ApplySourceIdFromCopiedFile(TrackInfo track, LocalProjectCopiedFile copiedFile)
    {
        if (copiedFile.SourceIdentity?.SourceId is { } sourceId && sourceId != Guid.Empty)
            track.SourceId = sourceId;
    }

    private static void ApplySourceIdFromCopiedFile(TrackInfo track, DiscCopiedFile copiedFile)
    {
        if (copiedFile.SourceIdentity?.SourceId is { } sourceId && sourceId != Guid.Empty)
            track.SourceId = sourceId;
    }

    private void PublishSourceMetadataSnapshot(
        string projectFolder,
        string sourcePath,
        TrackInfo track,
        AudioProbeInfo? probeInfo = null,
        string evidence = "")
    {
        if (string.IsNullOrWhiteSpace(projectFolder) ||
            string.IsNullOrWhiteSpace(sourcePath) ||
            track.SourceId == Guid.Empty)
        {
            return;
        }

        try
        {
            _services.Media.SourceMetadataCaptureService.PublishIfMissing(
                projectFolder,
                new SourceMetadataCaptureRequest(
                    track.SourceId,
                    sourcePath,
                    track,
                    probeInfo,
                    evidence));
        }
        catch (Exception ex)
        {
            _runtimeServices.DiagnosticLogService.WriteError("SourceMetadata capture failed.", ex);
        }
    }

    private void BackfillMissingSourceMetadataSnapshots(string projectFolder)
    {
        if (string.IsNullOrWhiteSpace(projectFolder))
            return;

        try
        {
            _services.Media.SourceMetadataCaptureService.BackfillMissingSnapshots(projectFolder);
        }
        catch (Exception ex)
        {
            _runtimeServices.DiagnosticLogService.WriteError("SourceMetadata backfill failed.", ex);
        }
    }

    private void ApplySourceMetadataAggregation(
        string projectFolder,
        ProjectMetadataInitializationState state,
        ProjectMetadataInitializationOptions? options = null,
        bool refreshUi = true)
    {
        if (string.IsNullOrWhiteSpace(projectFolder))
            return;

        try
        {
            var canonicalMetadata = RequireCanonicalMetadataSessionForProject(projectFolder);
            var result = _services.Media.ProjectMetadataAggregationPersistenceService.Apply(
                canonicalMetadata,
                new ProjectMetadataAggregationPersistenceRequest(projectFolder, state, options));
            _latestMetadataFieldDecisions = result.FieldDecisions;
            _latestMetadataDiagnosisProjectFolder = projectFolder;

            foreach (var diagnostic in result.Diagnostics)
            {
                if (diagnostic.Code is
                    MetadataAggregationDiagnosticCode.StrongIdentityConflict or
                    MetadataAggregationDiagnosticCode.AmbiguousMajority)
                {
                    _runtimeServices.DiagnosticLogService.WriteApplicationEvent(
                        "ProjectMetadata aggregation warning",
                        diagnostic.Message);
                }
            }

            if (!ReferenceEquals(result.CanonicalMetadata, canonicalMetadata))
            {
                throw new InvalidOperationException(
                    "ProjectMetadata aggregation replaced the caller-owned canonical metadata instance.");
            }

            if (!result.Changed)
                return;

            if (refreshUi)
            {
                _projectMetadata.ApplyCanonicalMetadata(canonicalMetadata);
                if (result.AppliedFields.Contains(ProjectMetadataField.Series))
                {
                    OnPropertyChanged(nameof(Series));
                    OnPropertyChanged(nameof(OutputFileNamePreview));
                    OnExportPreviewChanged();
                }
            }

            StopAutomaticMaintenance();
            var saved = _projectMetadataSession.Flush();
            if (saved) _services.Foundation.ProjectIndexService.Catalog.MarkDirty();
        }
        catch (Exception ex)
        {
            _runtimeServices.DiagnosticLogService.WriteError("ProjectMetadata aggregation failed.", ex);
        }
    }

    private void OpenMetadataDialog_Click(object sender, RoutedEventArgs e)
    {
        ShowMetadataDialog(this);
        e.Handled = true;
    }

    internal static bool CanOpenMetadataDialog(bool canChangeBookMetadata, bool allowDuringProjectSetup) =>
        canChangeBookMetadata || allowDuringProjectSetup;

    private void ShowMetadataDialog(Window owner, bool allowDuringProjectSetup = false)
    {
        var canOpenMetadataDialog = CanOpenMetadataDialog(CanChangeBookMetadata, allowDuringProjectSetup);
        if (!canOpenMetadataDialog)
            return;

        if (_developmentMetadataDialog is { } existingDialog)
        {
            existingDialog.RefreshForExternalChanges();
            ActivateDevelopmentMetadataDialog(existingDialog);
            return;
        }

        RefreshMetadataDependentUi();

        var dialog = new MetadataDialog(
            usePrivateGenreList: _settings.UsePrivateGenreList,
            genreSettingsManager: _services.Foundation.GlobalSettingsManager,
            openSettings: settingsOwner => ShowSettingsDialog(settingsOwner),
            metadataState: _projectMetadata,
            appSettings: _settings,
            canChooseCover: canOpenMetadataDialog,
            chooseCover: coverOwner => ChooseCover(coverOwner),
            removeCover: RemoveCover,
            refreshUi: RefreshMetadataDependentUi,
            appSettingsChanged: SaveSettingsIfReady,
            metadataPreferences: _settings.MetadataPreferences,
            metadataPreferencesChanged: _ => SaveSettingsIfReady(),
            projectFolderProvider: () =>
                !string.IsNullOrWhiteSpace(_currentProjectWorkFolder) &&
                Directory.Exists(_currentProjectWorkFolder)
                    ? _currentProjectWorkFolder
                    : null,
            sourceSnapshotsProvider: () =>
                _services.Media.ProjectMetadataPreviewAggregationService.CreateSourceSnapshots(Tracks.ToArray()),
            metadataFieldDecisionsProvider: () =>
                string.Equals(
                    _latestMetadataDiagnosisProjectFolder,
                    _currentProjectWorkFolder,
                    StringComparison.OrdinalIgnoreCase)
                    ? _latestMetadataFieldDecisions
                    : [],
            saveMetadata: SaveMetadataDialogCanonicalMetadata,
            compatibilityPreviewProvider: CreateCurrentCompatibilityPreviewTagData,
            compatibilityDescriptionEnabledProvider: () => _settings.CompatibilityDescriptionEnabled,
            compatibilityDescriptionEnabledChanged: enabled =>
            {
                _settings.CompatibilityDescriptionEnabled = enabled;
                SaveSettingsIfReady();
            },
            tracks: Tracks,
            refreshChapterTimelinePresentation: RefreshChapterTimelinePresentation,
            chapterSettingsChanged: ApplySharedChapterSettingsEditorChange,
            chapterGridColumnLayoutState: _chapterGridColumnLayoutState,
            coverImageService: _services.Media.CoverImageService,
            coverPickerService: _coverPickerService)
        {
            Owner = ResolveDevelopmentMetadataDialogOwner(owner)
        };
        _developmentMetadataDialog = dialog;
        dialog.Closed += (_, _) =>
        {
            if (ReferenceEquals(_developmentMetadataDialog, dialog))
                _developmentMetadataDialog = null;
        };
        dialog.Loaded += (_, _) => { ++_openMetadataContexts; CheckAutomaticMaintenance(); };
        dialog.Closed += (_, _) => { _openMetadataContexts = Math.Max(0, _openMetadataContexts - 1); CheckAutomaticMaintenance(); };
        AttachDebugWindowPlacement(dialog, DebugWindowPlacementTarget.MetadataDialog);

        ModalDialogService.ShowDialog(dialog, dialog.Owner);
    }

    private Window ResolveDevelopmentMetadataDialogOwner(Window owner) =>
        ReferenceEquals(owner, this) || ReferenceEquals(owner, _developmentMetadataDialog)
            ? this
            : owner;

    private void RefreshOpenDevelopmentMetadataForExternalChanges()
    {
        _developmentMetadataDialog?.RefreshForExternalChanges();
    }

    private static void ActivateDevelopmentMetadataDialog(Window dialog)
    {
        if (dialog.WindowState == WindowState.Minimized)
            dialog.WindowState = WindowState.Normal;

        if (!dialog.IsVisible)
            dialog.Show();

        dialog.Activate();
        dialog.Focus();
    }

    private void RefreshMetadataDependentUi()
    {
        OnPropertyChanged(nameof(BookTitle));
        OnPropertyChanged(nameof(Album));
        OnPropertyChanged(nameof(Author));
        OnPropertyChanged(nameof(Narrator));
        OnPropertyChanged(nameof(Genre));
        OnPropertyChanged(nameof(Series));
        OnPropertyChanged(nameof(MetadataSummaryTitle));
        OnPropertyChanged(nameof(MetadataSummaryDetails));
        OnPropertyChanged(nameof(OutputFileNamePreview));
        OnPropertyChanged(nameof(CoverPreviewSource));
        OnPropertyChanged(nameof(CoverHintVisibility));
        OnPropertyChanged(nameof(CanRemoveCover));
        OnExportPreviewChanged();

        ApplyChapterSettingsImmediately();
        RefreshActiveAudioDiscPreviewMetadata();
        RefreshActiveMp3DiscPreviewMetadata(BookTitle);
        RefreshTrackPresentationViews();
        NotifyExportUiStateChanged();

        if (_projectMetadataSession.IsAttached)
        {
            StopAutomaticMaintenance();
            var saved = _projectMetadataSession.Flush();
            if (saved) _services.Foundation.ProjectIndexService.Catalog.MarkDirty();
        }
    }

    private void SaveMetadataDialogCanonicalMetadata(ProjectMetadata metadata)
    {
        if (!ReferenceEquals(_projectMetadataSession.Current, metadata))
        {
            throw new InvalidOperationException(
                "Concept attempted to persist a non-canonical ProjectMetadata instance.");
        }

        if (_projectMetadataSession.IsAttached)
        {
            StopAutomaticMaintenance();
            var saved = _projectMetadataSession.Flush();
            if (saved) _services.Foundation.ProjectIndexService.Catalog.MarkDirty();
        }
    }

    private ProjectMetadataInitializationOptions CreateLocalFolderAggregationOptions(
        string sourceFolder,
        IReadOnlyCollection<string>? sourceFiles = null) =>
        new(
            LastUsedGenre: _settings.DefaultGenre,
            ReplaceableGenreSeed: _replaceableGenreSeed,
            IsGenrePreferenceFixed: _settings.MetadataPreferences.GenrePreferenceLocked,
            PreferredGenres: _settings.MetadataPreferences.PreferredGenres,
            PreferredLanguages: _settings.MetadataPreferences.PreferredLanguages,
            IsLanguagePreferenceFixed: _settings.MetadataPreferences.LanguagePreferenceLocked,
            PreferredAudioWorkType: _settings.MetadataPreferences.PreferredAudioWorkType,
            IsAudioWorkTypePreferenceFixed: _settings.MetadataPreferences.AudioWorkTypePreferenceLocked,
            PreferredOutputTrackNumber: _settings.MetadataPreferences.PreferredOutputTrackNumber,
            IsOutputTrackNumberPreferenceFixed: _settings.MetadataPreferences.OutputTrackNumberPreferenceLocked,
            PreferredOutputTrackTotal: _settings.MetadataPreferences.PreferredOutputTrackTotal,
            IsOutputTrackTotalPreferenceFixed: _settings.MetadataPreferences.OutputTrackTotalPreferenceLocked,
            BookFolderCandidate: GetDirectoryNameOrEmpty(sourceFolder),
            ParentFolderCandidate: GetParentDirectoryNameOrEmpty(sourceFolder),
            SingleFileNameCandidate: sourceFiles?.Count == 1
                ? Path.GetFileNameWithoutExtension(sourceFiles.First())
                : null,
            IsSingleFile: sourceFiles?.Count == 1,
            SourceFileNameCandidates: sourceFiles?
                .Select(Path.GetFileName)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value!)
                .ToArray(),
            AllowFilenameTitleFallback: true,
            AllowFolderStructureFallbacks: true,
            AllowSingleFileNameFallback: true);

    private ProjectMetadataInitializationOptions CreateMp3DiscProjectAggregationOptions() =>
        new(
            LastUsedGenre: _settings.DefaultGenre,
            ReplaceableGenreSeed: _replaceableGenreSeed,
            IsGenrePreferenceFixed: _settings.MetadataPreferences.GenrePreferenceLocked,
            PreferredGenres: _settings.MetadataPreferences.PreferredGenres,
            PreferredLanguages: _settings.MetadataPreferences.PreferredLanguages,
            IsLanguagePreferenceFixed: _settings.MetadataPreferences.LanguagePreferenceLocked,
            PreferredAudioWorkType: _settings.MetadataPreferences.PreferredAudioWorkType,
            IsAudioWorkTypePreferenceFixed: _settings.MetadataPreferences.AudioWorkTypePreferenceLocked,
            PreferredOutputTrackNumber: _settings.MetadataPreferences.PreferredOutputTrackNumber,
            IsOutputTrackNumberPreferenceFixed: _settings.MetadataPreferences.OutputTrackNumberPreferenceLocked,
            PreferredOutputTrackTotal: _settings.MetadataPreferences.PreferredOutputTrackTotal,
            IsOutputTrackTotalPreferenceFixed: _settings.MetadataPreferences.OutputTrackTotalPreferenceLocked,
            AllowFilenameTitleFallback: true,
            AllowFolderStructureFallbacks: false,
            AllowSingleFileNameFallback: false);

    private ProjectMetadataInitializationOptions CreateAudioDiscProjectAggregationOptions() =>
        new(
            LastUsedGenre: _settings.DefaultGenre,
            ReplaceableGenreSeed: _replaceableGenreSeed,
            IsGenrePreferenceFixed: _settings.MetadataPreferences.GenrePreferenceLocked,
            PreferredGenres: _settings.MetadataPreferences.PreferredGenres,
            PreferredLanguages: _settings.MetadataPreferences.PreferredLanguages,
            IsLanguagePreferenceFixed: _settings.MetadataPreferences.LanguagePreferenceLocked,
            PreferredAudioWorkType: _settings.MetadataPreferences.PreferredAudioWorkType,
            IsAudioWorkTypePreferenceFixed: _settings.MetadataPreferences.AudioWorkTypePreferenceLocked,
            PreferredOutputTrackNumber: _settings.MetadataPreferences.PreferredOutputTrackNumber,
            IsOutputTrackNumberPreferenceFixed: _settings.MetadataPreferences.OutputTrackNumberPreferenceLocked,
            PreferredOutputTrackTotal: _settings.MetadataPreferences.PreferredOutputTrackTotal,
            IsOutputTrackTotalPreferenceFixed: _settings.MetadataPreferences.OutputTrackTotalPreferenceLocked,
            AllowFilenameTitleFallback: false,
            AllowFolderStructureFallbacks: false,
            AllowSingleFileNameFallback: false);

    private static string GetDirectoryNameOrEmpty(string path)
    {
        try
        {
            return string.IsNullOrWhiteSpace(path) ? string.Empty : new DirectoryInfo(path).Name;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string GetParentDirectoryNameOrEmpty(string path)
    {
        try
        {
            return string.IsNullOrWhiteSpace(path) ? string.Empty : Directory.GetParent(path)?.Name ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private async Task<bool> EnrichResumeTracksWithFfprobeAsync(
        PerformanceExternalProcessContext? performanceContext = null,
        CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(_ffmpegStatus.FfprobePath))
            return true;

        var trackSnapshot = Tracks.ToList();
        var total = trackSnapshot.Count;
        var failed = 0;
        var preset = ExportPreset.Parse(SelectedExportPreset);
        var probeResults = new AudioProbeInfo?[total];
        var probeRequests = new List<(int Index, string Path)>();

        for (var index = 0; index < trackSnapshot.Count; index++)
        {
            var track = trackSnapshot[index];
            var trackPath = !string.IsNullOrWhiteSpace(track.FilePath)
                ? track.FilePath
                : TrackPathService.GetTrackPath(_currentFolderPath, track);

            if (string.IsNullOrWhiteSpace(trackPath) || !File.Exists(trackPath))
            {
                if (_isAudioDiscProjectAwaitingRip &&
                    ProcessingActionIds.Normalize(track.ProcessingAction) == ProcessingActionIds.RipFlac)
                {
                    track.Warning = string.Empty;
                }

                continue;
            }

            probeRequests.Add((index, trackPath));
        }

        var completed = total - probeRequests.Count;
        IProgress<int> progress = new Progress<int>(value =>
        {
            if (_isResumeLoading && !token.IsCancellationRequested)
                StatusText = LocalizationText.Format("MainWindow.Project.LoadingTechnicalData", value, total);
        });

        if (completed > 0)
            progress.Report(completed);

        if (probeRequests.Count > 0)
        {
            var batchResults = await _services.Media.AudioProbeBatchService.ProbeAsync(
                probeRequests
                    .Select(request => new AudioProbeBatchRequest(request.Index, request.Path))
                    .ToArray(),
                _ffmpegStatus.FfprobePath,
                ParallelJobsInput,
                performanceContext,
                new Progress<int>(value =>
                {
                    if (value % 10 == 0 || value == total)
                        progress.Report(value);
                }),
                completed,
                token);

            if (token.IsCancellationRequested)
                return false;

            for (var index = 0; index < Math.Min(probeResults.Length, batchResults.Length); index++)
                probeResults[index] = batchResults[index];
        }

        for (var index = 0; index < trackSnapshot.Count; index++)
        {
            if (token.IsCancellationRequested)
                return false;

            var probeInfo = probeResults[index];
            if (probeInfo is null)
                continue;

            var track = trackSnapshot[index];

            if (!probeInfo.Success)
            {
                failed++;
                if (string.IsNullOrWhiteSpace(track.Warning))
                    SetTrackValue(track, "Warning", LocalizationText.Get("MainWindow.AudioCheck.Warning"));

                continue;
            }

            _services.Media.TrackAudioProbeApplicationService.ApplySuccessfulProbe(
                track,
                probeInfo,
                preset,
                markAudioValidationPassed: false);
        }

        UpdateIndexes();

        if (failed > 0)
        {
            StatusText = LocalizationText.Format(
                failed == 1 ? "MainWindow.AudioCheck.ProjectLoaded.One" : "MainWindow.AudioCheck.ProjectLoaded.Many",
                failed);
        }

        return true;
    }
    private void RefreshResumeProjects_Click(object sender, RoutedEventArgs e)
    {
        if (IsBusy)
            return;

        RefreshResumeProjects(showStatus: true);
    }

    private void InspectResumeProject_Click(object sender, RoutedEventArgs e)
    {
        if (IsBusy || SelectedResumeProject is null)
            return;

        var plan = _services.Foundation.ProjectResumePlanService.BuildFromProjectFolder(SelectedResumeProject.ProjectFolder);
        if (plan is null)
        {
            AppDialogService.Warning(
                this,
                LocalizationText.Get("MainWindow.Resume.ProjectUnreadable.Title"),
                LocalizationText.Get("MainWindow.Resume.ProjectUnreadable.Message"));
            RefreshResumeProjects(showStatus: true);
            return;
        }

        var details = new List<string>
        {
            LocalizationText.Format("MainWindow.Resume.Detail.Project", plan.DisplayName),
            LocalizationText.Format("MainWindow.Resume.Detail.Type", plan.ProjectType),
            LocalizationText.Format("MainWindow.Resume.Detail.Status", plan.Status),
            LocalizationText.Format("MainWindow.Resume.Detail.TrackCount", plan.Tracks.Count),
            LocalizationText.Format("MainWindow.Resume.Detail.TrackListEditable", LocalizationText.Get(plan.CanEditTrackOrder ? "Common.Yes" : "Common.No")),
            LocalizationText.Format("MainWindow.Resume.Detail.DiscImportContinuable", LocalizationText.Get(plan.CanContinueDiscImport ? "Common.Yes" : "Common.No"))
        };

        if (plan.NextMissingDiscNumber.HasValue)
            details.Add(LocalizationText.Format("MainWindow.Resume.Detail.NextMissingDisc", plan.NextMissingDiscNumber.Value, plan.TotalDiscs));

        if (!string.IsNullOrWhiteSpace(plan.SourceFolder))
            details.Add(LocalizationText.Format("MainWindow.Resume.Detail.Source", plan.SourceFolder));

        if (!string.IsNullOrWhiteSpace(plan.OutputFolder))
            details.Add(LocalizationText.Format("MainWindow.Resume.Detail.Output", plan.OutputFolder));

        AppDialogService.Show(
            this,
            title: LocalizationText.Get("MainWindow.Resume.Detected.Title"),
            heading: LocalizationText.Get("MainWindow.Resume.Detected.Title"),
            message: LocalizationText.Get("MainWindow.Resume.Detected.Message"),
            kind: AppDialogKind.Information,
            details: details,
            buttons: new[]
            {
                new AppDialogButton("OK", AppDialogResult.Ok, IsPrimary: true, IsDefault: true, IsCancel: true)
            });
    }

    private void RefreshResumeProjects(bool showStatus)
    {
        _services.Foundation.ProjectIndexService.Catalog.MarkDirty();
        _ = RefreshProjectCatalogAsync(showStatus);
    }

    private async Task RefreshProjectCatalogAsync(bool showStatus)
    {
        try
        {
            var snapshot = await _services.Foundation.ProjectIndexService.Catalog.RefreshAsync(
                GetWorkingRootFolder(), _settings.ProjectRetentionDays, force: showStatus);
            if (_windowCloseCleanupCompleted || snapshot.IsDirty ||
                _services.Foundation.ProjectIndexService.Catalog.Snapshot?.Generation != snapshot.Generation) return;
            ApplyProjectCatalogSnapshot(snapshot);
            if (showStatus)
                StatusText = ResumeProjects.Count switch
                {
                    0 => LocalizationText.Get("MainWindow.Resume.NoneFound"),
                    1 => LocalizationText.Format("MainWindow.Resume.Found.One", ResumeProjects.Count),
                    _ => LocalizationText.Format("MainWindow.Resume.Found.Many", ResumeProjects.Count)
                };
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { _runtimeServices.DiagnosticLogService.WriteError("Project catalog refresh failed", error); }
    }

    private void ProjectCatalog_SnapshotChanged(object? sender, EventArgs e)
    {
        if (_windowCloseCleanupCompleted || Dispatcher.HasShutdownStarted) return;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            var snapshot = _services.Foundation.ProjectIndexService.Catalog.Snapshot;
            if (!_windowCloseCleanupCompleted && snapshot is not null && !snapshot.IsDirty &&
                string.Equals(snapshot.WorkingRoot, Path.GetFullPath(GetWorkingRootFolder()), StringComparison.OrdinalIgnoreCase))
                ApplyProjectCatalogSnapshot(snapshot);
        }));
    }

    private void ApplyProjectCatalogSnapshot(ProjectCatalogSnapshot snapshot)
    {
        if (_displayedProjectCatalogGeneration == snapshot.Generation) return;
        _displayedProjectCatalogGeneration = snapshot.Generation;
        ProjectCoverThumbnailCache.Shared.InvalidateDeletedFiles();
        var selectedProjectFolder = SelectedResumeProject?.ProjectFolder ?? "";
        ResumeProjects.Clear();
        foreach (var project in snapshot.ProjectIndexItems.Where(project => project.IsSelectableProject))
            ResumeProjects.Add(project);
        SelectedResumeProject = ResumeProjects.FirstOrDefault(project =>
            string.Equals(project.ProjectFolder, selectedProjectFolder, StringComparison.OrdinalIgnoreCase))
            ?? ResumeProjects.FirstOrDefault();
        OnPropertyChanged(nameof(ResumeProjectSummary));
        OnPropertyChanged(nameof(CanInspectSelectedResumeProject));
    }

    private void RefreshActiveMp3DiscPreviewMetadata(string? previousTitle)
    {
        if (_isWorkspaceTransitioning)
            return;
        if (_isDiscStopSaveInProgress)
            return;

        var manifest = _activeMp3DiscManifest;
        if (manifest is null)
            return;

        _services.Workflows.ProjectSnapshotService.UpdateMp3DiscProjectSnapshot(manifest, CreateProjectSnapshotFromUi());

        UpdateIndexes();
        PersistTrackListState();
    }

    private void RefreshActiveAudioDiscPreviewMetadata()
    {
        if (_isWorkspaceTransitioning)
            return;
        if (_isDiscStopSaveInProgress)
            return;

        var manifest = _activeAudioDiscManifest;
        if (manifest is null)
            return;

        _services.Workflows.ProjectSnapshotService.UpdateAudioDiscProjectSnapshot(manifest, CreateProjectSnapshotFromUi());

        var manifestTracks = manifest.Discs
            .SelectMany(disc => disc.Tracks)
            .ToDictionary(track => track.GlobalIndex);
        foreach (var track in Tracks)
        {
            if (!manifestTracks.TryGetValue(track.Index, out var manifestTrack))
                continue;

            track.Artist = Author;
        }

        UpdateIndexes();
        PersistTrackListState();
    }

    private ProjectSnapshotUiState CreateProjectSnapshotFromUi()
    {
        return new ProjectSnapshotUiState(
            SelectedExportPreset,
            ParallelJobsInput,
            OutputExtension,
            EffectiveOutputFolder,
            FileNameTemplate,
            BookTitle,
            Author,
            Album,
            Narrator,
            Genre,
            _projectMetadata.CoverSourcePath,
            _projectMetadata.ProcessedCoverPath,
            OutputFileNamePreview);
    }

    private void SaveCurrentMp3DiscProjectSnapshot(Mp3DiscProjectManifest manifest)
    {
        StopAutomaticMaintenance();
        _services.Workflows.ProjectSnapshotService.SaveMp3DiscProjectSnapshot(manifest, CreateProjectSnapshotFromUi());
        _services.Foundation.ProjectIndexService.Catalog.MarkDirty();
    }

    private void TrySaveCurrentMp3DiscProjectSnapshot(Mp3DiscProjectManifest manifest)
    {
        try
        {
            SaveCurrentMp3DiscProjectSnapshot(manifest);
        }
        catch
        {
            // Snapshot-Fehler dürfen Abbruch- und Fehlermeldungen nicht überdecken.
        }
    }

    private bool SaveCurrentAudioDiscProjectSnapshot(
        AudioDiscProjectManifest manifest,
        bool force = false)
    {
        StopAutomaticMaintenance();
        var saved = _services.Workflows.ProjectSnapshotService.SaveAudioDiscProjectSnapshot(
            manifest,
            CreateProjectSnapshotFromUi(),
            force);
        if (saved) _services.Foundation.ProjectIndexService.Catalog.MarkDirty();
        return saved;
    }

    private void TrySaveCurrentAudioDiscProjectSnapshot(
        AudioDiscProjectManifest manifest,
        bool force = false)
    {
        try
        {
            SaveCurrentAudioDiscProjectSnapshot(manifest, force);
        }
        catch
        {
            // Snapshot-Fehler dürfen Abbruch- und Fehlermeldungen nicht überdecken.
        }
    }

    private void MarkMp3DiscImportCanceled(string progressText, bool preserveWorkflowStatus = false)
    {
        ApplyLoadedProjectState(LoadedProjectState.WithDiscImportRequirement(true));
        EnterPipelinePause(progressText, preserveWorkflowStatus);
    }


    private async Task CancelMp3DiscImportAndShowResumeDialogAsync(
        Mp3DiscProjectManifest manifest,
        string progressText,
        bool cleanupPartFiles = false,
        bool preserveWorkflowStatus = false)
    {
        TrySaveCurrentMp3DiscProjectSnapshot(manifest);

        if (cleanupPartFiles)
        {
            _services.Foundation.ConvertedFileCleanupService.DeletePartFiles(manifest.ProjectFolder);
        }

        var preparedPlan = await Task.Run(() =>
            _services.Foundation.ProjectResumePlanService.BuildFromProjectFolder(manifest.ProjectFolder));
        if (preparedPlan is not null)
            RefreshLoadedProjectTrackStateFromPersistedProject(manifest.ProjectFolder, preparedPlan);
        MarkMp3DiscImportCanceled(progressText, preserveWorkflowStatus);
    }

    private void ShowUserCanceledProjectDialog()
    {
        if (_isShutdownInProgress)
            return;

        AppDialogService.Info(
            this,
            LocalizationText.Get("MainWindow.Project.Canceled.Title"),
            LocalizationText.Get("MainWindow.Project.Canceled.Message"),
            title: LocalizationText.Get("MainWindow.Project.Canceled.Title"));
    }

    private Mp3DiscProjectManifest? TryLoadCurrentMp3DiscProjectManifest(bool resetStateWhenFolderMissing)
    {
        if (string.IsNullOrWhiteSpace(_currentProjectWorkFolder) || !Directory.Exists(_currentProjectWorkFolder))
        {
            AppDialogService.Warning(
                this,
                LocalizationText.Get("MainWindow.Resume.ProjectMissing.Title"),
                LocalizationText.Get("MainWindow.Resume.ProjectMissing.Message"));

            if (resetStateWhenFolderMissing)
                ResetLoadedResumeProjectState();

            return null;
        }

        var manifest = _services.Foundation.Mp3DiscProjectService.TryLoad(_currentProjectWorkFolder);
        if (manifest is null)
        {
            AppDialogService.Warning(
                this,
                LocalizationText.Get("MainWindow.Resume.ProjectUnreadable.Title"),
                LocalizationText.Get("MainWindow.Resume.Mp3ManifestUnreadable.Message"));
            return null;
        }

        manifest.ProjectFolder = _currentProjectWorkFolder;
        manifest.ImportedDiscs ??= [];
        return manifest;
    }

    private void SetSelectedExportPresetSilently(string preset)
    {
        _suppressPresetBitrateWarning = true;
        try
        {
            SelectedExportPreset = preset;
        }
        finally
        {
            _suppressPresetBitrateWarning = false;
        }
    }

    private string ConfirmPresetBitrateIfNeeded(string requestedPreset)
    {
        var maxSourceBitrate = GetMaxTrackBitrateKbps();
        if (maxSourceBitrate is null)
            return requestedPreset;

        var requested = ExportPreset.Parse(requestedPreset);
        if (requested.BitrateKbps <= maxSourceBitrate.Value)
            return requestedPreset;

        var recommendedPreset = FindBestPresetForSourceBitrate(maxSourceBitrate.Value, requested.Channels);
        var recommended = ExportPreset.Parse(recommendedPreset);

        var result = AppDialogService.Show(
            this,
            LocalizationText.Get("Disc.Setup.PresetWarning.Title"),
            LocalizationText.Get("Disc.Setup.PresetWarning.Heading"),
            LocalizationText.Format("Disc.Setup.PresetWarning.Message", maxSourceBitrate.Value, requested.BitrateKbps),
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
            _ => _selectedExportPreset
        };
    }

    private void ConfirmLoadedPresetBitrateIfNeeded()
    {
        if (IsBusy || IsExporting || IsDiscImporting || _isResumeLoading || _isLoadingSettings ||
            _isShutdownInProgress || _allowWindowClose || !string.IsNullOrWhiteSpace(_lockedDiscResumePreset))
            return;

        var requestedPreset = SelectedExportPreset;
        var included = Tracks.Where(track => !track.IsExcluded).ToArray();
        var preparation = _services.Workflows.TrackPreparedStateRefreshService.GetPreparationProgress(included, requestedPreset);
        var fullyPrepared = preparation.TotalCount > 0 && preparation.PendingCount == 0;
        if (!PresetBitrateWarningService.ShouldWarnAfterLoad(Tracks, requestedPreset, fullyPrepared))
            return;

        var confirmedPreset = ConfirmPresetBitrateIfNeeded(requestedPreset);
        if (!string.Equals(confirmedPreset, requestedPreset, StringComparison.Ordinal))
            ApplyExportPresetSelectionToUi(confirmedPreset);
    }

    private int? GetMaxTrackBitrateKbps() => PresetBitrateWarningService.GetMaxSourceBitrateKbps(Tracks);

    private string FindBestPresetForSourceBitrate(int maxSourceBitrateKbps, int preferredChannels)
    {
        var parsedPresets = ExportPresets
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

    private void ExportPresetComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoadingSettings || _suppressPresetBitrateWarning || _isApplyingExportPresetSelectionToUi || Tracks.Count == 0)
            return;

        if (sender is not ComboBox comboBox || comboBox.SelectedItem is not string requestedPreset)
            return;

        var confirmedPreset = ConfirmPresetBitrateIfNeeded(requestedPreset);
        if (string.Equals(confirmedPreset, requestedPreset, StringComparison.Ordinal))
            return;

        ApplyExportPresetSelectionToUi(confirmedPreset);
    }

    private void ApplyExportPresetSelectionToUi(string preset)
    {
        _isApplyingExportPresetSelectionToUi = true;
        _suppressPresetBitrateWarning = true;

        try
        {
            SelectedExportPreset = preset;
            ExportPresetComboBox.SelectedItem = preset;
            ExportPresetComboBox.GetBindingExpression(System.Windows.Controls.Primitives.Selector.SelectedItemProperty)?.UpdateTarget();
        }
        finally
        {
            _suppressPresetBitrateWarning = false;
            _isApplyingExportPresetSelectionToUi = false;
        }

        Dispatcher.BeginInvoke(() =>
        {
            _isApplyingExportPresetSelectionToUi = true;
            try
            {
                ExportPresetComboBox.SelectedItem = _selectedExportPreset;
                ExportPresetComboBox.GetBindingExpression(System.Windows.Controls.Primitives.Selector.SelectedItemProperty)?.UpdateTarget();
                OnPropertyChanged(nameof(SelectedExportPreset));
            }
            finally
            {
                _isApplyingExportPresetSelectionToUi = false;
            }
        }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    }

    private void ParallelJobsInput_LostFocus(object sender, RoutedEventArgs e)
    {
        var text = sender is TextBox textBox
            ? textBox.Text
            : ParallelJobsInput;

        SetParallelJobsInput(text, showMessage: true);
    }

    private void ParallelJobsInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;

        var text = sender is TextBox textBox
            ? textBox.Text
            : ParallelJobsInput;

        SetParallelJobsInput(text, showMessage: true);
        Keyboard.ClearFocus();
        e.Handled = true;
    }


    private void MainWindow_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        // The custom caption replaces non-client window chrome, not workspace interaction.
        if (e.OriginalSource is DependencyObject captionSource &&
            FindVisualAncestor<LargeResizableWindowHeader>(captionSource) is not null)
            return;

        if (_isTrackGridAutoScrollActive && e.ChangedButton != MouseButton.Middle)
            StopTrackGridAutoScroll();

        if (HandleTrackGridAutoScrollMouseDown(e))
            return;

        if (TracksGrid is null || (TracksGrid.SelectedItems.Count == 0 && _activeEmbeddedChapterRow is null))
            return;

        if (e.OriginalSource is not DependencyObject source)
            return;

        // Scrolling is navigation, not a request to change the selected tracks.
        // Preserve the current row selection while the user clicks or drags either
        // TrackGrid scrollbar so they can inspect columns/rows away from the selection.
        if (FindVisualAncestor<ScrollBar>(source) is not null)
            return;

        if (FindVisualAncestor<DataGrid>(source) == TracksGrid)
        {
            if (FindVisualAncestor<DataGridRow>(source) is null &&
                FindVisualAncestor<DataGridCell>(source) is null &&
                FindVisualAncestor<DataGridColumnHeader>(source) is null)
            {
                ClearTrackSelection();
            }

            return;
        }

        if (IsTrackSelectionClearingBlocked(source))
            return;

        ClearTrackSelection();
    }

    private void MainWindow_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle)
            return;

        if (_isTrackGridAutoScrollActive)
            e.Handled = true;
    }

    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!_isTrackGridAutoScrollActive || e.Key != Key.Escape)
            return;

        StopTrackGridAutoScroll();
        e.Handled = true;
    }

    private void MainWindow_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isTrackGridAutoScrollActive || TracksGrid is null)
            return;

        _trackGridAutoScrollCurrent = e.GetPosition(TracksGrid);
    }

    private void ClearTrackSelection()
    {
        ClearEmbeddedChapterSelection();
        ClearParentTrackSelection();
    }

    private void ClearParentTrackSelection()
    {
        TracksGrid.SelectedItems.Clear();
        TracksGrid.SelectedItem = null;
        TracksGrid.CurrentItem = null;
    }

    private static bool IsTrackSelectionClearingBlocked(DependencyObject source)
    {
        return FindVisualAncestor<ButtonBase>(source) is not null ||
               FindVisualAncestor<TextBoxBase>(source) is not null ||
               FindVisualAncestor<ComboBox>(source) is not null ||
               FindVisualAncestor<Slider>(source) is not null ||
               FindVisualAncestor<MenuItem>(source) is not null;
    }

    private void MainWindow_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Handled || IsBusy)
            return;

        if (Keyboard.FocusedElement is not TextBox focusedTextBox)
            return;

        if (!ReferenceEquals(focusedTextBox, ParallelJobsTextBox))
            return;

        AdjustParallelJobsByWheel(e.Delta);
        e.Handled = true;
    }

    private void AdjustParallelJobsByWheel(int delta)
    {
        if (delta > 0)
        {
            IncreaseParallelJobs_Click(ParallelJobsTextBox, new RoutedEventArgs());
            return;
        }

        if (delta < 0)
            DecreaseParallelJobs_Click(ParallelJobsTextBox, new RoutedEventArgs());
    }

    private void ParallelJobsInput_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (IsBusy)
            return;

        AdjustParallelJobsByWheel(e.Delta);
        e.Handled = true;
    }

    private void IncreaseParallelJobs_Click(object sender, RoutedEventArgs e)
    {
        if (IsBusy)
            return;

        if (IsParallelAuto(ParallelJobsInput))
        {
            ParallelJobsInput = "1";
            return;
        }

        var current = TryParseParallelJobs(ParallelJobsInput, out var parsed)
            ? parsed
            : 1;

        ParallelJobsInput = Math.Clamp(current + 1, 1, 40).ToString(CultureInfo.InvariantCulture);
    }

    private void DecreaseParallelJobs_Click(object sender, RoutedEventArgs e)
    {
        if (IsBusy)
            return;

        if (IsParallelAuto(ParallelJobsInput))
            return;

        var current = TryParseParallelJobs(ParallelJobsInput, out var parsed)
            ? parsed
            : 1;

        ParallelJobsInput = current <= 1
            ? "Auto"
            : Math.Clamp(current - 1, 1, 40).ToString(CultureInfo.InvariantCulture);
    }

    private async void ConfigureFfmpeg_Click(object sender, RoutedEventArgs e)
    {
        if (IsBusy)
            return;

        var result = AppDialogService.Show(
            this,
            LocalizationText.Get("MainWindow.Ffmpeg.Setup.Title"),
            LocalizationText.Get("MainWindow.Ffmpeg.Setup.Title"),
            LocalizationText.Get("MainWindow.Ffmpeg.Setup.Message"),
            AppDialogKind.Question,
            null,
            new[]
            {
                new AppDialogButton(LocalizationText.Get("MainWindow.Ffmpeg.InstallAutomatically"), AppDialogResult.Yes, IsPrimary: true, IsDefault: true),
                new AppDialogButton(LocalizationText.Get("MainWindow.Ffmpeg.SelectManually"), AppDialogResult.No),
                new AppDialogButton(LocalizationText.Get("Common.Cancel"), AppDialogResult.Cancel, IsCancel: true)
            });

        if (result == AppDialogResult.Cancel)
            return;

        if (result == AppDialogResult.Yes)
        {
            StatusText = LocalizationText.Get("MainWindow.Ffmpeg.InstallStarted");

            var exitCode = await _services.Media.FfmpegService.InstallWithWingetAsync();

            _ffmpegStatus = _services.Media.FfmpegService.DetectTools(_settings);
            ApplyFfmpegStatusToSettingsAndUi();

            if (_ffmpegStatus.IsComplete)
            {
                AppDialogService.Info(
                    this,
                    LocalizationText.Get("MainWindow.Ffmpeg.Ready.Title"),
                    LocalizationText.Get("MainWindow.Ffmpeg.Ready.AfterInstall"));
            }
            else
            {
                AppDialogService.Warning(
                    this,
                    LocalizationText.Get("MainWindow.Ffmpeg.NotFound.Title"),
                    LocalizationText.Format("MainWindow.Ffmpeg.NotFound.Message", exitCode?.ToString() ?? LocalizationText.Get("Common.Unknown")));
            }

            return;
        }

        SelectFfmpegManually();
    }

    private void SelectFfmpegManually()
    {
        var dialog = new OpenFileDialog
        {
            Title = LocalizationText.Get("MainWindow.Ffmpeg.SelectExecutable"),
            Filter = "ffmpeg.exe|ffmpeg.exe|EXE Dateien (*.exe)|*.exe|Alle Dateien (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        if (!string.IsNullOrWhiteSpace(_settings.FfmpegPath) &&
            File.Exists(_settings.FfmpegPath))
        {
            dialog.InitialDirectory = Path.GetDirectoryName(_settings.FfmpegPath);
        }

        if (dialog.ShowDialog(this) != true)
            return;

        _ffmpegStatus = _services.Media.FfmpegService.DetectToolsFromFfmpegPath(dialog.FileName);
        ApplyFfmpegStatusToSettingsAndUi();

        if (_ffmpegStatus.IsComplete)
        {
            AppDialogService.Info(
                this,
                    LocalizationText.Get("MainWindow.Ffmpeg.Ready.Title"),
                    LocalizationText.Get("MainWindow.Ffmpeg.Ready.Manual"));
        }
        else
        {
            AppDialogService.Warning(
                this,
                LocalizationText.Get("MainWindow.Ffmpeg.Incomplete.Title"),
                LocalizationText.Get("MainWindow.Ffmpeg.Incomplete.Message"));
        }
    }

    private async void SelectFolder_Click(object sender, RoutedEventArgs e)
    {
        var replacementVersion = _workspaceReplacementVersion;
        if (_isWorkspaceReplacementInProgress || _isProjectCloseInProgress || _isShutdownInProgress || IsBusy)
            return;

        var dialog = new OpenFolderDialog
        {
            Title = LocalizationText.Get("MainWindow.Folder.SelectAudiobook"),
            Multiselect = false,
            InitialDirectory = GetExistingFolderOrDefault(_settings.LastLocalProjectFolder, GetDesktopFolder())
        };

        if (dialog.ShowDialog(this) != true)
            return;

        if (IsOpticalDriveSelection(dialog.FolderName))
        {
            AppDialogService.Warning(
                this,
                LocalizationText.Get("MainWindow.Disc.OpenWithButton.Title"),
                LocalizationText.Get("MainWindow.Disc.OpenWithButton.Message"));
            return;
        }

        if (!TryCheckpointWorkspaceReplacement(replacementVersion)) return;

        ClearSelectedSourceDisplayOverride();
        ResetProjectSourceModeStateForNewSource();
        ResetLoadedResumeProjectState();
        ResetMetadataForNewFolderProject();
        Tracks.Clear();
        UpdateIndexes();

        SelectedFolder = dialog.FolderName;

        _settings.LastSelectedFolder = dialog.FolderName;
        _settings.LastLocalProjectFolder = dialog.FolderName;
        SaveSettingsIfReady();

        await LoadFolderAsync(
            dialog.FolderName,
            captureLocalImportPerformance: true,
            analysisCancellationScope: TrackAnalysisCancellationScope.LocalFolderImport);

        if (Tracks.Count == 0)
            return;

        SetMetadataEditingAvailable(true);
        NotifyExportUiStateChanged();
    }

    private async void SelectDiscSource_Click(object sender, RoutedEventArgs e)
    {
        var replacementVersion = _workspaceReplacementVersion;
        if (_isWorkspaceReplacementInProgress || _isProjectCloseInProgress || _isShutdownInProgress || IsBusy)
            return;

        var selectedPath = ShowDiscSourceSelectionDialog(updateWorkspace: false);
        if (string.IsNullOrWhiteSpace(selectedPath))
            return;

        selectedPath = await ResolveSelectedDiscSourceAsync(selectedPath, updateWorkspace: false);
        if (string.IsNullOrWhiteSpace(selectedPath))
            return;

        if (!TryCheckpointWorkspaceReplacement(replacementVersion)) return;

        ClearSelectedSourceDisplayOverride();
        SelectedFolder = selectedPath;
        _settings.LastSelectedOpticalDrive = Path.GetPathRoot(selectedPath) ?? selectedPath;
        SetSelectedSourceDisplayOverride(CreateDiscDriveDisplayName(selectedPath), selectedPath);

        _settings.LastDiscSourceFolder = selectedPath;
        SaveSettingsIfReady();

        ResetLoadedResumeProjectState();
        await StartMp3DiscProjectAsync(selectedPath);
    }

    private async Task<string> ResolveSelectedDiscSourceAsync(string selectedPath, bool updateWorkspace = true)
    {
        var currentPath = selectedPath;

        while (true)
        {
            if (!await _services.Discs.DiscDriveService.IsCdDrivePathAsync(currentPath))
                return currentPath;

            if (await _services.Discs.DiscDriveService.IsDiscSourceReadyAsync(currentPath))
                return currentPath;

            await _services.Discs.DiscDriveService.TryEjectDiscAsync(currentPath);
            var driveLetter = (Path.GetPathRoot(currentPath) ?? currentPath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            var result = AppDialogService.Show(
                this,
                title: LocalizationText.Get("Disc.Source.EmptyDriveTitle"),
                heading: LocalizationText.Format("Disc.Source.EmptyDriveHeading", driveLetter),
                message: LocalizationText.Format("Disc.Source.EmptyDriveMessage", driveLetter),
                kind: AppDialogKind.Information,
                buttons:
                [
                    new AppDialogButton(LocalizationText.Get("Disc.Source.Retry"), AppDialogResult.Yes, IsPrimary: true, IsDefault: true),
                    new AppDialogButton(LocalizationText.Get("Disc.Source.OtherDrive"), AppDialogResult.No),
                    new AppDialogButton(LocalizationText.Get("UiAbbrechen"), AppDialogResult.Cancel, IsCancel: true)
                ]);

            if (result == AppDialogResult.Yes)
                continue;

            if (result == AppDialogResult.No)
            {
                currentPath = ShowDiscSourceSelectionDialog(updateWorkspace);
                if (string.IsNullOrWhiteSpace(currentPath))
                    return string.Empty;

                continue;
            }

            return string.Empty;
        }
    }

    private string ShowDiscSourceSelectionDialog(bool updateWorkspace = true)
    {
        var dialog = new DiscSourceSelectionDialog(
            [],
            string.Empty,
            _settings.LastSelectedOpticalDrive,
            _settings.DiscDriveOrder,
            _services.Discs.DiscDriveService,
            respectDriveCircleEnabled: _settings.ExperimentalDriveRoundEnabled)
        {
            Owner = this
        };

        if (ModalDialogService.ShowDialog(dialog, this) != true)
            return string.Empty;

        if (updateWorkspace && !string.IsNullOrWhiteSpace(dialog.SelectedPath))
        {
            if (!LoadedProjectState.IsLocal)
                SetSelectedSourceDisplayOverride(CreateDiscDriveDisplayName(dialog.SelectedPath), dialog.SelectedPath);
            _settings.LastSelectedOpticalDrive = Path.GetPathRoot(dialog.SelectedPath) ?? dialog.SelectedPath;
            SaveSettingsIfReady();
        }

        return dialog.SelectedPath;
    }

    private async Task StartMp3DiscProjectAsync(string sourceFolder)
    {
        var sourceVersion = _workspaceReplacementVersion;
        SelectedFolder = sourceFolder;
        ResetProjectSourceModeStateForNewSource();

        ResetMetadataForNewDiscProject();
        Tracks.Clear();
        UpdateIndexes();

        DiscSourceAnalysis analysis;
        List<TrackInfo> previewTracks;
        DiscSourceQuickIdentity sourceQuickIdentity;
        string sourceStructureSignature;

        try
        {
            (analysis, previewTracks, sourceQuickIdentity, sourceStructureSignature) = await AnalyzeDiscSourceAndTracksAsync(sourceFolder);
        }
        catch (Exception ex)
        {
            AppDialogService.Error(
                this,
                LocalizationText.Get("MainWindow.Disc.ReadFailed.Title"),
                LocalizationText.Get("MainWindow.Disc.ReadFailed.Message"),
                details: new[] { ex.Message });
            StatusText = LocalizationText.Get("MainWindow.DiscAnalysis.Failed");
            ExportProgressText = BuildIdleExportProgressText();
            return;
        }

        if (sourceVersion != _workspaceReplacementVersion || _isShutdownInProgress || _allowWindowClose) return;
        if (!analysis.IsSupportedDataDisc)
        {
            if (analysis.IsProbablyAudioCd && await TryShowAudioDiscContentsAsync(sourceFolder))
                return;

            ShowUnsupportedDiscSourceMessage(analysis);
            StatusText = LocalizationText.Get("MainWindow.Mp3Analysis.Unsupported");
            ExportProgressText = BuildIdleExportProgressText();
            return;
        }

        // CD 1 sofort vor dem Kopieren anzeigen, damit Titel, Autor und Trackliste direkt sichtbar sind.
        // Die Preview ist bereits ein echter MP3-CD-Chapter-Kontext, bleibt aber rein transient:
        // keine Persistenz und kein Runtime-Catalog-Eintrag, bevor der User den Import startet.
        try
        {
            var plannedPreviewSources = Mp3DiscSourceIdentityPlanner.Create(
                sourceFolder,
                sourceStructureSignature,
                analysis.SupportedAudioFiles);
            _transientMp3DiscChapterPreview = new Mp3DiscPendingChapterDisc(
                1,
                sourceStructureSignature,
                plannedPreviewSources);
            _services.Discs.Mp3DiscTrackReconciliationService.NormalizeProjectDiscNumber(
                previewTracks,
                discNumber: 1);
            _services.Discs.Mp3DiscTrackReconciliationService.ApplyPlannedSourceIds(
                previewTracks,
                sourceFolder,
                discNumber: 1,
                plannedPreviewSources);

            AppendDiscPreviewTracks(
                previewTracks,
                sourceFolder,
                discNumber: 1,
                clearExistingTracks: true,
                refreshChapterProjection: true);
            TryApplyFirstEmbeddedCover(previewTracks);
            SuggestMetadataFromTracksAndFolder(sourceFolder);
            UpdateFinalStatus(Tracks.Count);
            _pendingDiscProjectSourceFolder = sourceFolder;
            NotifyExportUiStateChanged();
            ExportProgressText = LocalizationText.Format("MainWindow.Mp3Analysis.Ready", Tracks.Count);
            AutoFitTrackColumnsAfterRender();
        }
        catch (Exception ex)
        {
            _transientMp3DiscChapterPreview = null;
            AppDialogService.Error(
                this,
                LocalizationText.Get("MainWindow.Mp3Disc.ReadFailed.Title"),
                LocalizationText.Get("MainWindow.Mp3Disc.ReadFailed.Message"),
                details: new[] { ex.Message });
            StatusText = LocalizationText.Get("MainWindow.Mp3Analysis.Failed");
            ExportProgressText = LocalizationText.Get("MainWindow.Mp3Analysis.Failed");
            return;
        }

        ConfirmLoadedPresetBitrateIfNeeded();
        var setupDialog = CreateProjectSetupDialog(new ProjectSetupDialogRequest(
            SourceKind: ProjectSetupSourceKind.Mp3Disc,
            ConfirmDiscCountOnStart: true,
            WindowTitle: LocalizationText.Get("MainWindow.Setup.Mp3Prepare"),
            SourceInformation: LocalizationText.Format("MainWindow.Setup.Mp3SourceInformation", analysis.SupportedAudioFiles.Count, BuildProjectSetupDurationText(previewTracks)),
            Instruction: LocalizationText.Get("MainWindow.Setup.DiscInstruction"),
            DefaultDiscCount: 1,
            MinimumDiscs: 1,
            MaximumDiscs: 99,
            ExportPresets: ExportPresets,
            SelectedExportPreset: SelectedExportPreset,
            ParallelJobs: ParallelJobsInput,
            OutputExtension: OutputExtension,
            OutputFolder: OutputFolder,
            BookTitle: BookTitle,
            Album: Album,
            Author: Author,
            Narrator: Narrator,
            Genre: Genre,
            FileNameTemplate: FileNameTemplate,
            CoverSourcePath: GetCoverSourcePathForProjectSetup(),
            CoverPreviewSource: CoverPreviewSource,
            CoverWorkFolder: GetProjectFolderStructure().CoversFolder,
            AutoMergeAfterConversion: _settings.MergeAutomaticallyAfterConversion,
            UsePrivateGenreList: _settings.UsePrivateGenreList,
            GenreOptions: GenreListService.GetGenres(_settings),
            SourceFolder: sourceFolder,
            MaxSourceBitrateKbps: GetMaxTrackBitrateKbps(),
            LastCoverFolder: _settings.LastCoverFolder ?? string.Empty));

        if (sourceVersion != _workspaceReplacementVersion || _isShutdownInProgress || _allowWindowClose) return;
        SetMetadataEditingAvailable(true);
        var startProject = ModalDialogService.ShowDialog(setupDialog, this) == true;
        FlushProjectSetupDiscCountPreviewRefresh();
        ApplyDiscProjectSetup(setupDialog.Result);

        var setup = setupDialog.Result;
        if (!startProject)
        {
            StorePendingMp3DiscPreparation(
                sourceFolder,
                setup,
                sourceQuickIdentity,
                sourceStructureSignature);
            return;
        }

        await StartConfirmedMp3DiscProjectAsync(
            sourceFolder,
            setup,
            sourceQuickIdentity,
            sourceStructureSignature);
    }

    private void StorePendingMp3DiscPreparation(
        string sourceFolder,
        DiscProjectSetupResult setup,
        DiscSourceQuickIdentity quickIdentity,
        string structureSignature)
    {
        _pendingDiscProjectSourceFolder = sourceFolder;
        _pendingMp3DiscSetupResult = setup;
        _pendingMp3DiscQuickIdentity = quickIdentity;
        _pendingMp3DiscStructureSignature = structureSignature;
        NotifyExportUiStateChanged();
    }

    private void ClearPendingMp3DiscPreparation()
    {
        _pendingDiscProjectSourceFolder = "";
        _pendingMp3DiscSetupResult = null;
        _pendingMp3DiscQuickIdentity = null;
        _pendingMp3DiscStructureSignature = "";
    }

    private void ResetProjectSourceModeStateForNewSource()
    {
        SetOriginalAudioColumnsVisible(true);
        _folderRatePreview = new();
        _sessionProject.Clear();
        _activeLocalPreparation = null;
        ClearPendingMp3DiscPreparation();
        _resumeDiscPreview.Clear();
        _mp3DiscChapterSourceCatalog.Clear();
        _transientMp3DiscChapterPreview = null;
        _activeMp3DiscManifest = null;
        _pendingAudioDisc = null;
        _pendingAudioDiscTotalDiscs = 1;
        _activeAudioDiscManifest = null;
        _isAudioDiscProjectAwaitingRip = false;
        ApplyLoadedProjectState(LoadedProjectStateSnapshot.None);
    }

    private DiscProjectSetupResult CreateMp3DiscSetupFromCurrentUi(DiscProjectSetupResult baseSetup) =>
        baseSetup with
        {
            SelectedExportPreset = SelectedExportPreset,
            ParallelJobs = ParallelJobsInput,
            OutputExtension = OutputExtension,
            OutputFolder = EffectiveOutputFolder,
            BookTitle = BookTitle,
            Album = Album,
            Author = Author,
            Narrator = Narrator,
            Genre = Genre,
            FileNameTemplate = FileNameTemplate,
            CoverSourcePath = GetCoverSourcePathForProjectSetup(),
            ProcessedCoverPath = _projectMetadata.ProcessedCoverPath,
            AutoMergeAfterConversion = _settings.MergeAutomaticallyAfterConversion
        };

    private async Task StartConfirmedMp3DiscProjectAsync(
        string sourceFolder,
        DiscProjectSetupResult setup,
        DiscSourceQuickIdentity sourceQuickIdentity,
        string sourceStructureSignature)
    {
        var sourceVersion = _workspaceReplacementVersion;
        Activate();
        Focus();
        await Dispatcher.InvokeAsync(
            () =>
            {
                Activate();
                Focus();
            },
            System.Windows.Threading.DispatcherPriority.ApplicationIdle);

        var totalDiscs = setup.TotalDiscs;

        var validatedSourceFolder = await EnsurePreparedMp3DiscSourceAsync(
            sourceFolder,
            sourceQuickIdentity,
            sourceStructureSignature);
        if (string.IsNullOrWhiteSpace(validatedSourceFolder))
            return;

        sourceFolder = validatedSourceFolder;
        SelectedFolder = sourceFolder;
        SetSelectedSourceDisplayOverride(CreateDiscDriveDisplayName(sourceFolder), sourceFolder);
        ClearPendingMp3DiscPreparation();
        _settings.LastDiscSourceFolder = sourceFolder;
        SaveSettingsIfReady();
        var sourceDriveInfo = await _services.Discs.DiscDriveService.GetDriveDiagnosticsForPathAsync(sourceFolder);
        if (_isWorkspaceReplacementInProgress || _isProjectCloseInProgress || _isShutdownInProgress || _allowWindowClose ||
            sourceVersion != _workspaceReplacementVersion) return;
        _workspaceReplacementVersion++;
        var projectFolder = _services.Discs.Mp3DiscImportService.CreateDiscProjectFolder(sourceFolder, GetProjectFolderStructure().Mp3DiscProjectsFolder);
        _sessionProject.Register(projectFolder, ProjectManifestTypes.Mp3DiscProject, startAccepted: true);
        _currentProjectWorkFolder = projectFolder;
        AttachCanonicalMetadataSessionToNewProject(projectFolder);

        var projectManifest = _services.Foundation.Mp3DiscProjectService.LoadOrCreate(
            projectFolder,
            sourceFolder,
            totalDiscs,
            setup.SelectedExportPreset,
            setup.ParallelJobs,
            setup.OutputExtension,
            setup.OutputFolder,
            setup.FileNameTemplate,
            sourceDriveInfo, targetSampleRateHz: _settings.DefaultSampleRateHz);

        SaveCurrentMp3DiscProjectSnapshot(projectManifest);
        _activeMp3DiscManifest = projectManifest;

        await ImportMp3DiscProjectAsync(
            projectFolder,
            sourceFolder,
            projectManifest,
            startDiscNumber: 1,
            firstDiscAlreadyReady: true,
            autoExportWhenComplete: true);
    }

    private async Task<string> EnsurePreparedMp3DiscSourceAsync(
        string originalSourceFolder,
        DiscSourceQuickIdentity expectedQuickIdentity,
        string expectedStructureSignature)
    {
        var candidateSourceFolder = originalSourceFolder;

        while (true)
        {
            var quickCheck = await Task.Run(() =>
                _services.Discs.Mp3DiscImportService.CheckQuickIdentity(candidateSourceFolder, expectedQuickIdentity));

            var candidateChanged = !AreSameDiscSourcePath(candidateSourceFolder, originalSourceFolder);

            if (quickCheck.IsMatch && !candidateChanged)
                return candidateSourceFolder;

            if (quickCheck.Status != DiscSourceQuickCheckStatus.Unavailable)
            {
                var detailedMatch = await CheckPreparedDiscSignatureAsync(candidateSourceFolder, expectedStructureSignature);
                if (detailedMatch)
                    return candidateSourceFolder;
            }

            var driveLetter = (Path.GetPathRoot(candidateSourceFolder) ?? candidateSourceFolder)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var heading = quickCheck.Status == DiscSourceQuickCheckStatus.Unavailable
                ? LocalizationText.Format("Disc.Source.PreviousDiscUnavailableHeading", driveLetter)
                : LocalizationText.Get("Disc.Source.DifferentDiscHeading");
            var message = quickCheck.Status == DiscSourceQuickCheckStatus.Unavailable
                ? LocalizationText.Format("Disc.Source.PreviousDiscUnavailableMessage", driveLetter)
                : LocalizationText.Get("Disc.Source.DifferentDiscMessage");

            var result = AppDialogService.Show(
                this,
                title: LocalizationText.Get("Disc.Source.Mp3ProjectTitle"),
                heading: heading,
                message: message,
                kind: AppDialogKind.Warning,
                buttons:
                [
                    new AppDialogButton(LocalizationText.Get("Disc.Source.Retry"), AppDialogResult.Yes, IsPrimary: true, IsDefault: true),
                    new AppDialogButton(LocalizationText.Get("Disc.Source.OtherDrive"), AppDialogResult.No),
                    new AppDialogButton(LocalizationText.Get("Common.Cancel"), AppDialogResult.Cancel, IsCancel: true)
                ]);

            if (result == AppDialogResult.Yes)
                continue;

            if (result == AppDialogResult.No)
            {
                var selectedSource = ShowDiscSourceSelectionDialog();
                if (string.IsNullOrWhiteSpace(selectedSource))
                    return string.Empty;

                selectedSource = await ResolveSelectedDiscSourceAsync(selectedSource);
                if (string.IsNullOrWhiteSpace(selectedSource))
                    return string.Empty;

                candidateSourceFolder = selectedSource;
                continue;
            }

            return string.Empty;
        }
    }

    private static bool AreSameDiscSourcePath(string firstPath, string secondPath)
    {
        try
        {
            var firstFullPath = Path.GetFullPath(firstPath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var secondFullPath = Path.GetFullPath(secondPath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            return string.Equals(firstFullPath, secondFullPath, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return string.Equals(firstPath.Trim(), secondPath.Trim(), StringComparison.OrdinalIgnoreCase);
        }
    }

    private async Task<bool> CheckPreparedDiscSignatureAsync(string sourceFolder, string expectedStructureSignature)
    {
        IsBusy = true;
        IsProgressIndeterminate = true;
        IsDiscSourceAnalysisActive = true;
        DiscSourceAnalysisText = LocalizationText.Get("MainWindow.DiscAnalysis.Checking");
        var previousStatusText = _statusText;
        var previousExportProgressText = ExportProgressText;
        StatusText = string.Empty;
        ExportProgressPercent = 0;
        ExportProgressText = string.Empty;

        using var slowNoticeCancellation = new CancellationTokenSource();
        var slowNoticeTask = ShowSlowDiscSourceNoticeAsync(slowNoticeCancellation.Token);

        try
        {
            return await _services.Discs.DiscDriveService.MeasureOperationAsync(
                sourceFolder,
                "drive.data-analysis",
                () =>
                {
                    var analysis = _services.Discs.Mp3DiscImportService.AnalyzeSource(sourceFolder, DiscDriveOperationService.CurrentCancellationToken);
                    if (!analysis.IsSupportedDataDisc)
                        return false;

                    var currentSignature = _services.Discs.Mp3DiscImportService.CreateDiscStructureSignature(sourceFolder, analysis);
                    return string.Equals(currentSignature, expectedStructureSignature, StringComparison.OrdinalIgnoreCase);
                });
        }
        catch
        {
            return false;
        }
        finally
        {
            slowNoticeCancellation.Cancel();
            await slowNoticeTask;
            IsDiscSourceAnalysisActive = false;
            RestoreTextAfterDiscAnalysis(previousStatusText, previousExportProgressText);
            IsProgressIndeterminate = false;
            IsBusy = false;
        }
    }

    private async Task<(
        DiscSourceAnalysis Analysis,
        List<TrackInfo> PreviewTracks,
        DiscSourceQuickIdentity QuickIdentity,
        string Signature)> AnalyzeDiscSourceAndTracksAsync(
        string sourceFolder)
    {
        IsBusy = true;
        IsProgressIndeterminate = true;
        IsDiscSourceAnalysisActive = true;
        DiscSourceAnalysisText = DiscAnalysisReadingText;
        var previousStatusText = _statusText;
        var previousExportProgressText = ExportProgressText;
        StatusText = string.Empty;
        ExportProgressPercent = 0;
        ExportProgressText = string.Empty;

        using var slowNoticeCancellation = new CancellationTokenSource();
        var slowNoticeTask = ShowSlowDiscSourceNoticeAsync(slowNoticeCancellation.Token);

        try
        {
            return await _services.Discs.DiscDriveService.MeasureOperationAsync(
                sourceFolder,
                "drive.data-analysis",
                () =>
                {
                    var analysis = _services.Discs.Mp3DiscImportService.AnalyzeSource(sourceFolder, DiscDriveOperationService.CurrentCancellationToken);
                    var previewTracks = analysis.IsSupportedDataDisc
                        ? _services.Media.FolderScanner.Scan(sourceFolder, TrackNumberPreference.FileName, cancellationToken: DiscDriveOperationService.CurrentCancellationToken)
                        : [];
                    var quickIdentity = _services.Discs.Mp3DiscImportService.CreateQuickIdentity(sourceFolder, analysis);
                    var signature = analysis.IsSupportedDataDisc
                        ? _services.Discs.Mp3DiscImportService.CreateDiscStructureSignature(sourceFolder, analysis)
                        : string.Empty;

                    return (analysis, previewTracks, quickIdentity, signature);
                });
        }
        finally
        {
            slowNoticeCancellation.Cancel();
            await slowNoticeTask;

            IsDiscSourceAnalysisActive = false;
            RestoreTextAfterDiscAnalysis(previousStatusText, previousExportProgressText);
            IsProgressIndeterminate = false;
            IsBusy = false;
        }
    }


    private async Task<bool> TryShowAudioDiscContentsAsync(string sourceFolder)
    {
        var sourceVersion = _workspaceReplacementVersion;
        IsBusy = true;
        IsProgressIndeterminate = true;
        IsDiscSourceAnalysisActive = true;
        DiscSourceAnalysisText = LocalizationText.Get("MainWindow.DiscAnalysis.AudioReading");
        var previousStatusText = _statusText;
        var previousExportProgressText = ExportProgressText;
        StatusText = string.Empty;
        ExportProgressPercent = 0;
        ExportProgressText = string.Empty;

        using var slowNoticeCancellation = new CancellationTokenSource();
        var slowNoticeTask = ShowSlowDiscSourceNoticeAsync(slowNoticeCancellation.Token);

        AudioDiscReadResult result;
        try
        {
            result = await Task.Run(() => _services.Discs.AudioDiscReaderService.ReadDisc(sourceFolder));
        }
        finally
        {
            slowNoticeCancellation.Cancel();
            await slowNoticeTask;
            IsDiscSourceAnalysisActive = false;
            RestoreTextAfterDiscAnalysis(previousStatusText, previousExportProgressText);
            IsProgressIndeterminate = false;
            IsBusy = false;
        }

        if (_isShutdownInProgress || _allowWindowClose || _windowCloseCleanupCompleted) return true;

        if (result.IsMixedModeUnsupported)
        {
            AppDialogService.Warning(this, LocalizationText.Get("MainWindow.DiscSource.AudioUnreadable.Title"), result.ErrorMessage);
            StatusText = result.ErrorMessage;
            return true;
        }

        if (!result.IsAudioDisc || result.Disc is null)
            return false;

        ResetMetadataForNewDiscProject();
        SetOriginalAudioColumnsVisible(true);
        _pendingAudioDisc = result.Disc;

        const AudioDiscWorkingFormat previewWorkingFormat = AudioDiscWorkingFormat.Flac;
        Tracks.ReplaceAll(_services.Foundation.AudioDiscProjectService.CreateTrackPreview(result.Disc, BookTitle, Author, previewWorkingFormat));
        UpdateIndexes();
        UpdateFinalStatus(Tracks.Count);
        AutoFitTrackColumnsAfterRender();
        _pendingAudioDisc = result.Disc;
        SelectedFolder = result.Disc.DriveRoot;
        SetSelectedSourceDisplayOverride(
            CreateDiscDriveDisplayName(result.Disc.DriveRoot),
            result.Disc.DriveRoot);

        ConfirmLoadedPresetBitrateIfNeeded();
        var setupDialog = CreateProjectSetupDialog(new ProjectSetupDialogRequest(
            SourceKind: ProjectSetupSourceKind.AudioDisc,
            ConfirmDiscCountOnStart: true,
            WindowTitle: LocalizationText.Get("MainWindow.Setup.AudioPrepare"),
            SourceInformation: LocalizationText.Format("MainWindow.Setup.AudioSourceInformation", result.Disc.TrackCount, result.Disc.TotalDurationText),
            Instruction: LocalizationText.Get("MainWindow.Setup.DiscInstruction"),
            DefaultDiscCount: 1,
            MinimumDiscs: 1,
            MaximumDiscs: 99,
            ExportPresets: ExportPresets,
            SelectedExportPreset: SelectedExportPreset,
            ParallelJobs: ParallelJobsInput,
            OutputExtension: OutputExtension,
            OutputFolder: OutputFolder,
            BookTitle: BookTitle,
            Album: Album,
            Author: Author,
            Narrator: Narrator,
            Genre: Genre,
            FileNameTemplate: FileNameTemplate,
            CoverSourcePath: GetCoverSourcePathForProjectSetup(),
            CoverPreviewSource: CoverPreviewSource,
            CoverWorkFolder: GetProjectFolderStructure().CoversFolder,
            AutoMergeAfterConversion: _settings.MergeAutomaticallyAfterConversion,
            UsePrivateGenreList: _settings.UsePrivateGenreList,
            GenreOptions: GenreListService.GetGenres(_settings),
            SourceFolder: sourceFolder,
            LastCoverFolder: _settings.LastCoverFolder ?? string.Empty));

        if (sourceVersion != _workspaceReplacementVersion || _isShutdownInProgress || _allowWindowClose) return true;
        SetMetadataEditingAvailable(true);
        var startProject = ModalDialogService.ShowDialog(setupDialog, this) == true;
        FlushProjectSetupDiscCountPreviewRefresh();
        ApplyDiscProjectSetup(setupDialog.Result);
        _pendingAudioDisc = result.Disc;
        _pendingAudioDiscTotalDiscs = setupDialog.Result.TotalDiscs;
        SelectedFolder = result.Disc.DriveRoot;
        var projectPrepared = false;

        try
        {
            var sourceDriveInfo = await _services.Discs.DiscDriveService.GetDriveDiagnosticsForPathAsync(result.Disc.DriveRoot);
            if (_isShutdownInProgress || _allowWindowClose || _windowCloseCleanupCompleted ||
                sourceVersion != _workspaceReplacementVersion) return true;
            _workspaceReplacementVersion++;

            const AudioDiscWorkingFormat workingFormat = AudioDiscWorkingFormat.Flac;
            var projectFolder = _services.Foundation.AudioDiscProjectService.CreateProjectFolder(
                GetProjectFolderStructure().AudioDiscProjectsFolder,
                setupDialog.Result.BookTitle);
            _currentProjectWorkFolder = projectFolder;
            var manifest = _services.Foundation.AudioDiscProjectService.CreateInitialManifest(
                projectFolder,
                result.Disc,
                discNumber: 1,
                setup: setupDialog.Result,
                workingFormat: workingFormat,
                sourceDriveInfo: sourceDriveInfo, targetSampleRateHz: _settings.DefaultSampleRateHz);

            _services.Foundation.AudioDiscProjectService.Save(manifest);
            _sessionProject.RegisterAudioPreparation(GetWorkingRootFolder(), manifest, startProject);
            AttachCanonicalMetadataSessionToNewProject(projectFolder);
            _activeAudioDiscManifest = manifest;
            ApplyLoadedProjectState(LoadedProjectState.WithAudioDiscState(true));
            ApplyLoadedProjectState(LoadedProjectState.WithLocalState(false));
            _isAudioDiscProjectAwaitingRip = true;

            Tracks.ReplaceAll(_services.Foundation.AudioDiscProjectService.CreateTrackPreview(manifest));

            UpdateIndexes();
            UpdateFinalStatus(Tracks.Count);
            AutoFitTrackColumnsAfterRender();
            NotifyExportUiStateChanged();

            StatusText = LocalizationText.Get("MainWindow.Project.Starting");
            ExportProgressText = LocalizationText.Get("MainWindow.Status.Initializing");
            projectPrepared = true;
        }
        catch (Exception ex)
        {
            AppDialogService.Error(
                this,
                LocalizationText.Get("MainWindow.AudioProject.CreateFailed.Title"),
                LocalizationText.Get("MainWindow.AudioProject.CreateFailed.Message"),
                details: new[] { ex.Message });
            StatusText = LocalizationText.Get("MainWindow.Project.AudioCreateFailed");
            ExportProgressText = BuildIdleExportProgressText();
        }

        if (projectPrepared && !startProject)
        {
            StatusText = LocalizationText.Format("MainWindow.AudioDisc.Prepared", result.Disc.TrackCount);
            ExportProgressText = LocalizationText.Get("MainWindow.Project.ClickStartRipping");
        }

        if (projectPrepared && startProject)
        {
            await RunPreparedAudioDiscRipAsync();
        }

        return true;
    }

    private async Task ShowSlowDiscSourceNoticeAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(20), cancellationToken);
            DiscSourceAnalysisText = DiscAnalysisSlowText;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Die CD-Analyse war vor Ablauf der zwanzig Sekunden abgeschlossen.
        }
    }

    private async Task PreviewDiscSourceAnalysisAsync()
    {
        if (IsDiscSourceAnalysisActive)
            return;

        var previousStatusText = _statusText;
        var previousExportProgressText = ExportProgressText;
        var previousProgressPercent = ExportProgressPercent;
        var previousIsProgressIndeterminate = IsProgressIndeterminate;

        IsDiscSourceAnalysisActive = true;
        DiscSourceAnalysisText = DiscAnalysisReadingText;
        StatusText = string.Empty;
        ExportProgressText = string.Empty;
        ExportProgressPercent = 0;
        IsProgressIndeterminate = true;

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(10));
            DiscSourceAnalysisText = DiscAnalysisSlowText;
            await Task.Delay(TimeSpan.FromSeconds(10));
        }
        finally
        {
            IsDiscSourceAnalysisActive = false;
            StatusText = previousStatusText;
            ExportProgressText = previousExportProgressText;
            ExportProgressPercent = previousProgressPercent;
            IsProgressIndeterminate = previousIsProgressIndeterminate;
        }
    }

    private void RestoreTextAfterDiscAnalysis(string previousStatusText, string previousExportProgressText)
    {
        if (string.IsNullOrEmpty(StatusText))
            StatusText = previousStatusText;

        if (string.IsNullOrEmpty(ExportProgressText))
            ExportProgressText = previousExportProgressText;
    }

    private ProjectExtensionRollbackSnapshot? CaptureAdditionalSourcesBaseline(string projectFolder)
    {
        try
        {
            return _projectExtensionRollbackSnapshot ??= _services.Pipeline.ProjectExtensionRollbackService.CaptureForAdditionalSources(projectFolder);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or PersistentJsonSchemaException)
        {
            _services.Workflows.TrackPreparedStateRefreshService.RefreshForCurrentPreset(Tracks, projectFolder, SelectedExportPreset);
            UpdateManualMergeReviewPresetState();
            AppDialogService.Error(this, LocalizationText.Get("MainWindow.Export.Failed.Title"), LocalizationText.Get("Common.OperationFailed"), details: new[] { error.Message });
            return null;
        }
    }

    private async void AddProjectSources_Click(object sender, RoutedEventArgs e)
    {
        if (IsBusy)
            return;

        if (LoadedProjectState.IsLocal)
        {
            await AddMoreLocalSourcesAsync();
            return;
        }

        if (LoadedProjectState.IsAudioDisc)
        {
            await AddMoreAudioDiscsAsync();
            return;
        }

        if (!LoadedProjectState.IsMp3Disc)
            return;

        var manifest = TryLoadCurrentMp3DiscProjectManifest(resetStateWhenFolderMissing: false);
        if (manifest is null)
            return;

        var additionalImportPlan = _services.Foundation.Mp3DiscProjectService.BuildAdditionalImportPlan(manifest);
        var defaultSourceFolder = await _services.Discs.DiscDriveService.ResolveResumeDiscSourceAsync(
            _currentProjectWorkFolder,
            default,
            manifest.SourceFolder,
            _settings.LastDiscSourceFolder,
            SelectedFolder);

        var dialog = new AddMoreDiscsDialog(
            additionalImportPlan.CompletedDiscCount,
            additionalImportPlan.CurrentTotalDiscs,
            additionalImportPlan.DefaultTotalDiscs,
            additionalImportPlan.MinimumTotalDiscs,
            additionalImportPlan.MaximumTotalDiscs,
            [],
            preferredDriveRoot: defaultSourceFolder,
            sourceDialogInitialDirectory: string.Empty,
            configuredDrives: _settings.DiscDriveOrder,
            discDriveService: _services.Discs.DiscDriveService)
        {
            Owner = this
        };

        if (ModalDialogService.ShowDialog(dialog, this) != true)
            return;

        var addMoreOptions = dialog.Result;
        var sourceFolder = addMoreOptions.SourceFolder.Trim();
        SetSelectedSourceDisplayOverride(CreateDiscDriveDisplayName(sourceFolder), sourceFolder);

        if (!await _services.Discs.DiscDriveService.IsValidResumeDiscSourceAsync(sourceFolder, _currentProjectWorkFolder))
        {
            AppDialogService.Warning(
                this,
                LocalizationText.Get("Disc.AddMore.InvalidDriveTitle"),
                LocalizationText.Get("Disc.AddMore.InvalidDriveMessage"));
            return;
        }

        _settings.LastDiscSourceFolder = sourceFolder;
        SaveSettingsIfReady();

        if (!TryFlushCanonicalMetadataBeforeExtensionSnapshot(manifest.ProjectFolder))
            return;

        var extensionSnapshot = CaptureAdditionalSourcesBaseline(manifest.ProjectFolder);
        if (extensionSnapshot is null) return;
        _services.Workflows.TrackPreparedStateRefreshService.RefreshForCurrentPreset(Tracks, manifest.ProjectFolder, SelectedExportPreset);
        var returnState = ProjectReviewState.Phase;
        var returnCompletedState = ProjectReviewState.IsCompleted;

        try
        {
            _services.Foundation.Mp3DiscProjectService.IncreaseTotalDiscsForAdditionalImport(manifest, addMoreOptions.TotalDiscs);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            _services.Pipeline.ProjectExtensionRollbackService.Rollback(extensionSnapshot);
            _projectExtensionRollbackSnapshot = null;
            AppDialogService.Warning(
                this,
                LocalizationText.Get("Disc.AddMore.TotalRejected"),
                ex.Message);
            return;
        }

        manifest.SourceFolder = sourceFolder;
        SaveCurrentMp3DiscProjectSnapshot(manifest);

        var nextMissingDisc = _services.Foundation.Mp3DiscProjectService.GetNextMissingDiscNumber(manifest, manifest.TotalDiscs);
        if (nextMissingDisc is null)
        {
            StatusText = LocalizationText.Get("Disc.AddMore.AllImported");
            ExportProgressText = LocalizationText.Get("Disc.AddMore.NoNewDisc");
            return;
        }

        ApplyLoadedProjectState(LoadedProjectState.WithDiscImportRequirement(true));
        ApplyLoadedProjectState(LoadedProjectState.WithMp3DiscState(true));
        NotifyExportUiStateChanged();

        StatusText = LocalizationText.Format("Disc.AddMore.Mp3ExtensionStatus", nextMissingDisc.Value, manifest.TotalDiscs);
        ExportProgressText = LocalizationText.Get(_settings.MergeAutomaticallyAfterConversion
            ? "Disc.AddMore.Mp3ExtensionProgressAutoMerge"
            : "Disc.AddMore.Mp3ExtensionProgress");
        _projectReviewState = ProjectReviewState.WithManualMergeReview(false);
        _projectReviewState = ProjectReviewState.WithCompletion(false);

        async Task<bool> RunMp3ExtensionAsync()
        {
            return await ImportMp3DiscProjectAsync(
                manifest.ProjectFolder,
                _resumeDiscStartDriveRoot ?? sourceFolder,
                manifest,
                startDiscNumber: _services.Foundation.Mp3DiscProjectService.GetNextMissingDiscNumber(manifest, manifest.TotalDiscs) ?? nextMissingDisc.Value,
                firstDiscAlreadyReady: false,
                autoExportWhenComplete: true,
                extensionSnapshot: extensionSnapshot,
                extensionReturnState: returnState,
                extensionReturnCompletedState: returnCompletedState,
                startWithSelectedDrive: true);
        }

        async Task RunAndFinalizeMp3ExtensionAsync()
        {
            var extensionCanceled = await RunMp3ExtensionAsync();
            if (extensionCanceled || _presentationState.IsPipelinePaused)
                return;

            ClearProjectExtensionPauseContext();
        }

        BeginProjectExtensionPauseContext(
            RunAndFinalizeMp3ExtensionAsync,
            () =>
            {
                _services.Pipeline.ProjectExtensionRollbackService.Rollback(extensionSnapshot);
                ReloadCanonicalMetadataSessionAfterRollback(manifest.ProjectFolder);
                _services.Foundation.ConvertedFileCleanupService.DeletePartFiles(manifest.ProjectFolder);
                RefreshLoadedProjectTrackStateFromPersistedProject(manifest.ProjectFolder);
                ApplyLoadedProjectState(LoadedProjectState.WithDiscImportRequirement(false));
                ApplyLoadedProjectState(LoadedProjectState.WithMp3DiscState(true));
                _projectReviewState = ProjectReviewState.WithCompletion(returnCompletedState);
                _projectReviewState = ProjectReviewState.WithManualMergeReview(returnState is ProjectPipelineState.ReviewBeforeMerge or ProjectPipelineState.Completed);
                _projectReviewState = ProjectReviewState.WithPhase(returnState);
                StatusText = LocalizationText.Get("Disc.AddMore.StoppedStatus");
                ExportProgressText = LocalizationText.Get("Disc.AddMore.RollbackProgress");
                NotifyExportUiStateChanged();
                RefreshResumeProjects(showStatus: false);
            });

        await RunAndFinalizeMp3ExtensionAsync();
    }

    private async Task<List<TrackInfo>?> ScanFolderForExtensionAsync(string root, Func<string, bool>? includeFile = null)
    {
        using var cancellation = new CancellationTokenSource();
        _trackAnalysisCancellation = cancellation;
        _trackAnalysisCancellationScope = TrackAnalysisCancellationScope.LocalFolderExtension;
        IsBusy = true;
        NotifyExportUiStateChanged();
        try
        {
            return await _services.Media.FolderScanner.ScanAsync(root,
                includeFile: includeFile, cancellationToken: cancellation.Token);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            StatusText = LocalizationText.Get("Sources.AddLocal.CanceledStatus");
            return null;
        }
        finally
        {
            _trackAnalysisCancellation = null;
            _trackAnalysisCancellationScope = TrackAnalysisCancellationScope.None;
            IsBusy = false;
            NotifyExportUiStateChanged();
        }
    }

    private async Task AddMoreLocalSourcesAsync()
    {
        if (string.IsNullOrWhiteSpace(_currentProjectWorkFolder) ||
            !Directory.Exists(_currentProjectWorkFolder))
        {
            AppDialogService.Warning(
                this,
                LocalizationText.Get("Sources.AddLocal.MissingFolderTitle"),
                LocalizationText.Get("Sources.AddLocal.MissingFolderMessage"));
            return;
        }

        var selection = AppDialogService.Show(
            this,
            LocalizationText.Get("Sources.AddLocal.Title"),
            LocalizationText.Get("Sources.AddLocal.Title"),
            LocalizationText.Get("Sources.AddLocal.ChooseKind"),
            AppDialogKind.Question,
            null,
            new[]
            {
                new AppDialogButton(LocalizationText.Get("Sources.AddLocal.AddFolder"), AppDialogResult.Yes, IsPrimary: true, IsDefault: true),
                new AppDialogButton(LocalizationText.Get("Sources.AddLocal.AddFiles"), AppDialogResult.No),
                new AppDialogButton(LocalizationText.Get("UiAbbrechen"), AppDialogResult.Cancel, IsCancel: true)
            });

        if (selection == AppDialogResult.Cancel)
            return;

        string scanRoot;
        IReadOnlyCollection<string> selectedFiles;

        if (selection == AppDialogResult.Yes)
        {
            var folderDialog = new OpenFolderDialog
            {
                Title = LocalizationText.Get("Sources.AddLocal.FolderPickerTitle"),
                Multiselect = false,
                InitialDirectory = GetExistingFolderOrDefault(
                    _settings.LastLocalProjectFolder,
                    GetDesktopFolder())
            };

            if (folderDialog.ShowDialog(this) != true)
                return;

            if (IsOpticalDriveSelection(folderDialog.FolderName))
            {
                AppDialogService.Warning(
                    this,
                    LocalizationText.Get("Sources.AddLocal.OpticalFolderTitle"),
                    LocalizationText.Get("Sources.AddLocal.OpticalFolderMessage"));
                return;
            }

            scanRoot = Path.GetFullPath(folderDialog.FolderName);
            var scannedTracks = await ScanFolderForExtensionAsync(scanRoot);
            if (scannedTracks is null) return;
            selectedFiles = scannedTracks
                .Select(track => TrackPathService.GetTrackPath(scanRoot, track))
                .ToArray();

            _settings.LastLocalProjectFolder = scanRoot;
        }
        else
        {
            var fileDialog = new OpenFileDialog
            {
                Title = LocalizationText.Get("Sources.AddLocal.FilePickerTitle"),
                Filter = AudioInputFormatCatalog.OpenFileDialogFilter,
                CheckFileExists = true,
                Multiselect = true,
                InitialDirectory = GetExistingFolderOrDefault(
                    _settings.LastLocalProjectFolder,
                    GetDesktopFolder())
            };

            if (fileDialog.ShowDialog(this) != true || fileDialog.FileNames.Length == 0)
                return;

            var sourceDirectories = fileDialog.FileNames
                .Select(path => Path.GetDirectoryName(Path.GetFullPath(path)) ?? string.Empty)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (sourceDirectories.Length != 1 || string.IsNullOrWhiteSpace(sourceDirectories[0]))
            {
                AppDialogService.Warning(
                    this,
                    LocalizationText.Get("Sources.AddLocal.FilesRejectedTitle"),
                    LocalizationText.Get("Sources.AddLocal.SingleFolderMessage"));
                return;
            }

            if (IsOpticalDriveSelection(sourceDirectories[0]))
            {
                AppDialogService.Warning(
                    this,
                    LocalizationText.Get("Sources.AddLocal.OpticalFilesTitle"),
                    LocalizationText.Get("Sources.AddLocal.OpticalFilesMessage"));
                return;
            }

            scanRoot = sourceDirectories[0];
            selectedFiles = fileDialog.FileNames
                .Select(Path.GetFullPath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            _settings.LastLocalProjectFolder = scanRoot;
        }

        SaveSettingsIfReady();

        if (selectedFiles.Count == 0)
        {
            AppDialogService.Info(
                this,
                LocalizationText.Get("Sources.AddLocal.NoAudioTitle"),
                LocalizationText.Get("Sources.AddLocal.NoAudioMessage"));
            return;
        }

        var projectFolder = Path.GetFullPath(_currentProjectWorkFolder);
        var folderTargetRate = FolderSampleRateService.ReadTarget(projectFolder);
        var originalsFolder = ProjectFolderLayout.GetOriginalsFolder(projectFolder);
        var importRoot = Directory.GetParent(scanRoot)?.FullName ?? scanRoot;
        ProjectFolderLayout.EnsureProjectFolders(projectFolder);
        var manifestPath = ProjectFolderLayout.GetExportManifestPath(projectFolder);
        var preset = ExportPreset.Parse(SelectedExportPreset);
        var convertedFolder = ProjectFolderLayout.GetConvertedPresetFolder(projectFolder, preset.GetFolderName());
        Directory.CreateDirectory(convertedFolder);

        var manifest = _services.Foundation.WorkManifestService.LoadOrCreate(
            manifestPath,
            ProjectManifestTypes.FolderProject,
            projectFolder,
            originalsFolder,
            preset.DisplayName);

        var knownSourcePaths = manifest.Tracks
            .Select(track => track.SourcePath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var candidates = selectedFiles
            .Select(sourcePath => new
            {
                SourcePath = Path.GetFullPath(sourcePath),
                TargetPath = Path.Combine(
                    originalsFolder,
                    Path.GetRelativePath(importRoot, Path.GetFullPath(sourcePath)))
            })
            .Where(item => !knownSourcePaths.Contains(Path.GetFullPath(item.TargetPath)))
            .ToArray();

        if (candidates.Length == 0)
        {
            AppDialogService.Info(
                this,
                LocalizationText.Get("Sources.AddLocal.NoNewSourcesTitle"),
                LocalizationText.Get("Sources.AddLocal.AlreadyPresentMessage"));
            return;
        }

        var selectedPathSet = candidates
            .Select(item => item.SourcePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var newTracks = await ScanFolderForExtensionAsync(
            scanRoot, path => selectedPathSet.Contains(Path.GetFullPath(path)));
        if (newTracks is null) return;

        if (newTracks.Count == 0)
        {
            AppDialogService.Info(
                this,
                LocalizationText.Get("Sources.AddLocal.NoNewSourcesTitle"),
                LocalizationText.Get("Sources.AddLocal.UnreadableTracksMessage"));
            return;
        }

        var trackBySourcePath = newTracks
            .Select((track, index) => new
            {
                Track = track,
                Index = index,
                SourcePath = Path.GetFullPath(TrackPathService.GetTrackPath(scanRoot, track))
            })
            .ToDictionary(
                item => item.SourcePath,
                item => (item.Index, item.Track),
                StringComparer.OrdinalIgnoreCase);

        if (!TryFlushCanonicalMetadataBeforeExtensionSnapshot(projectFolder))
            return;

        var snapshot = CaptureAdditionalSourcesBaseline(projectFolder);
        if (snapshot is null) return;
        manifest = _services.Foundation.WorkManifestService.LoadOrCreate(manifestPath, ProjectManifestTypes.FolderProject,
            projectFolder, originalsFolder, preset.DisplayName);
        _services.Workflows.TrackPreparedStateRefreshService.RefreshForCurrentPreset(Tracks, projectFolder, SelectedExportPreset);
        var returnState = ProjectReviewState.Phase;
        var returnCompletedState = ProjectReviewState.IsCompleted;
        var previousTrackCount = Tracks.Count;
        var manifestSyncRoot = new object();
        var extensionSourceProbes = new System.Collections.Concurrent.ConcurrentDictionary<Guid, AudioProbeInfo>();
        var sourceAcquisitionCompleted = false;

        async Task RunLocalExtensionAsync()
        {
        _exportCancellation = new CancellationTokenSource();

        var initialPipelineState = _services.Pipeline.LocalProjectExtensionStateService.ResolveInitialState(
            candidates.Select(item => item.TargetPath));
        sourceAcquisitionCompleted = initialPipelineState == ProjectPipelineState.Converting;

        SetPipelineState(initialPipelineState);
        IsBusy = true;
        IsExporting = true;
        _projectReviewState = ProjectReviewState.WithManualMergeReview(false);
        _projectReviewState = ProjectReviewState.WithCompletion(false);
        var workflowOperationId = BeginWorkflowStatusOperation(projectFolder);
        PublishWorkflowStatus(
            workflowOperationId,
            _services.Status.LocalWorkflowStatusAdapter.CreateRunningSnapshot(
                projectFolder,
                initialPipelineState,
                new LocalProjectLivePreparationProgress(
                    sourceAcquisitionCompleted ? candidates.Length : 0,
                    candidates.Length,
                    0,
                    string.Empty,
                    [],
                    []),
                preset,
                isExtension: true,
                existingConvertedCount: 0,
                conversionTotalOverride: previousTrackCount + candidates.Length,
                activeTrackNumberOffset: previousTrackCount));

        await using var preparedLifecycle = new PreparedTrackLifecycleSession(
            _services.Foundation.WorkManifestService, _services.Workflows.AacExportProcessingService,
            manifest, manifestPath, manifestSyncRoot, _ffmpegStatus.FfprobePath ?? "", ResolveParallelJobCount());
        preparedLifecycle.QueuePreset(preset, _exportCancellation.Token);

        try
        {
            manifest.State.Status = initialPipelineState == ProjectPipelineState.Converting
                ? ProjectManifestStatuses.Converting
                : ProjectManifestStatuses.AcquiringSources;
            _services.Foundation.WorkManifestService.Save(manifestPath, manifest);

            var result = await _services.Workflows.LocalProjectLivePreparationService.RunAsync(
                new LocalProjectLivePreparationRequest(
                    importRoot,
                    candidates.Select(item => item.SourcePath).ToArray(),
                    projectFolder,
                    ResolveParallelJobCount(), preparedLifecycle.DrainAsync),
                async (copiedFile, trackPreparationProgress, token) =>
                {
                    var sourcePath = Path.GetFullPath(copiedFile.SourceFile);
                    if (!trackBySourcePath.TryGetValue(sourcePath, out var mappedTrack))
                    {
                        throw new InvalidOperationException(
                            $"Die hinzugefügte Quelldatei konnte keinem Track zugeordnet werden: {copiedFile.SourceFile}");
                    }

                    mappedTrack.Track.TargetSampleRateHz = folderTargetRate;
                    mappedTrack.Track.AllowAacReuse = ProjectSampleRateService.AllowsAacReuse(projectFolder);
                    ApplySourceIdFromCopiedFile(mappedTrack.Track, copiedFile);
                    var probeInfo = await _services.Media.AudioInfoService.ProbeAsync(
                        copiedFile.TargetFile,
                        _ffmpegStatus.FfprobePath,
                        token: token);
                    token.ThrowIfCancellationRequested();
                    if (probeInfo.HasPlausibleAudioProperties)
                    {
                        extensionSourceProbes[mappedTrack.Track.SourceId] = probeInfo;
                        _services.Media.TrackAudioProbeApplicationService.ApplySuccessfulProbe(
                            mappedTrack.Track,
                            probeInfo,
                            preset,
                            markAudioValidationPassed: true);
                    }
                    PublishSourceMetadataSnapshot(
                        projectFolder,
                        copiedFile.TargetFile,
                        mappedTrack.Track,
                        evidence: "local-project-copy");
                    var trackIndex = previousTrackCount + mappedTrack.Index;
                    var convertedPath = ConvertedTrackPathService.GetConvertedTrackPath(
                        convertedFolder,
                        copiedFile.TargetFile,
                        mappedTrack.Track);

                    var canReuseConvertedTrack = false;
                    lock (manifestSyncRoot)
                    {
                        canReuseConvertedTrack = _services.Foundation.WorkManifestService.CanReuseConvertedTrack(
                            manifest,
                            trackIndex,
                            mappedTrack.Track,
                            copiedFile.TargetFile,
                            convertedPath,
                            preset);
                    }

                    if (!canReuseConvertedTrack)
                    {
                        preparedLifecycle.MarkStarted(trackIndex, mappedTrack.Track, copiedFile.TargetFile, convertedPath, preset, token);
                        try
                        {
                            var durationTicks = TrackDurationService.GetEffectiveDurationTicks(mappedTrack.Track);
                            await _services.Workflows.AacExportProcessingService.PrepareTrackForExportAsync(
                                mappedTrack.Track,
                                copiedFile.TargetFile,
                                convertedPath,
                                preset,
                                _ffmpegStatus.FfmpegPath ?? string.Empty,
                                token,
                                ffmpegProgress => trackPreparationProgress.Report(
                                    durationTicks <= 0
                                        ? 0
                                        : Math.Clamp(ffmpegProgress.Ticks / (double)durationTicks, 0d, 0.999d)));
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (ExportTrackException)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            throw new ExportTrackException(
                                mappedTrack.Track.Index > 0 ? mappedTrack.Track.Index : previousTrackCount + mappedTrack.Index + 1,
                                mappedTrack.Track.FileName,
                                copiedFile.SourceFile,
                                ex);
                        }

                        preparedLifecycle.Complete(trackIndex, mappedTrack.Track,
                            copiedFile.TargetFile, convertedPath, preset, token);
                    }
                },
                new Progress<LocalProjectLivePreparationProgress>(progress =>
                {
                    if (!sourceAcquisitionCompleted &&
                        progress.TotalFiles > 0 &&
                        progress.CopiedFiles >= progress.TotalFiles)
                    {
                        sourceAcquisitionCompleted = true;
                        lock (manifestSyncRoot)
                        {
                            manifest.State.Status = ProjectManifestStatuses.Converting;
                            _services.Foundation.WorkManifestService.Save(manifestPath, manifest);
                        }
                        SetPipelineState(ProjectPipelineState.Converting);
                    }

                    PublishWorkflowStatus(
                        workflowOperationId,
                        _services.Status.LocalWorkflowStatusAdapter.CreateRunningSnapshot(
                            projectFolder,
                            sourceAcquisitionCompleted
                                ? ProjectPipelineState.Converting
                                : ProjectPipelineState.AcquiringSources,
                            progress,
                            preset,
                            isExtension: true,
                            existingConvertedCount: 0,
                            conversionTotalOverride: previousTrackCount + candidates.Length,
                            activeTrackNumberOffset: previousTrackCount));
                }),
                _exportCancellation.Token);

            if (result.ImportResult.WasCanceled || _exportCancellation.IsCancellationRequested)
            {
                if (_presentationState.PauseRequested)
                {
                    PublishWorkflowStatus(
                        workflowOperationId,
                        _services.Status.LocalWorkflowStatusAdapter.CreateRunningSnapshot(
                            projectFolder,
                            sourceAcquisitionCompleted
                                ? ProjectPipelineState.Converting
                                : ProjectPipelineState.AcquiringSources,
                            new LocalProjectLivePreparationProgress(
                                result.ImportResult.CompletedFiles,
                                result.ImportResult.TotalFiles,
                                result.PreparedFiles,
                                string.Empty,
                                [],
                                []),
                            preset,
                            isExtension: true,
                            isPaused: true,
                            existingConvertedCount: 0,
                            conversionTotalOverride: previousTrackCount + candidates.Length,
                            activeTrackNumberOffset: previousTrackCount));
                    EndWorkflowStatusOperation(workflowOperationId);
                    EnterPipelinePause(string.Empty, preserveWorkflowStatus: true);
                }
                else
                {
                    EndWorkflowStatusOperation(workflowOperationId);
                    RollbackLocalProjectExtension(snapshot, projectFolder, returnState, returnCompletedState);
                }
                return;
            }

            _services.Foundation.WorkManifestService.MarkConversionPreparationStarted(manifest);
            _services.Foundation.WorkManifestService.Save(manifestPath, manifest);

            ApplySourceMetadataAggregation(
                projectFolder,
                ProjectMetadataInitializationState.SubsequentSource,
                CreateLocalFolderAggregationOptions(scanRoot, candidates.Select(item => item.SourcePath).ToArray()));

            RefreshLoadedProjectTrackStateFromPersistedProject(projectFolder);
            _services.Media.TrackAudioProbeApplicationService.ApplySuccessfulProbesBySourceId(
                Tracks,
                extensionSourceProbes,
                preset);
            _currentFolderPath = originalsFolder;
            SelectedFolder = scanRoot;
            ApplyLoadedProjectState(LoadedProjectState.WithLocalState(true));
            _projectReviewState = ProjectReviewState.WithCompletion(false);
            EndWorkflowStatusOperation(workflowOperationId);
            NotifyExportUiStateChanged();
            RefreshResumeProjects(showStatus: false);
            await RunCurrentExportPlanAsync(projectFolder, ProjectManifestTypes.FolderProject,
                manualMergeRequested: false);
        }
        catch (OperationCanceledException) when (_exportCancellation?.IsCancellationRequested == true)
        {
            if (_presentationState.PauseRequested)
            {
                var last = _services.Status.WorkflowStatusCoordinator.Snapshot;
                PublishWorkflowStatus(workflowOperationId, last with { IsPaused = true });
                EndWorkflowStatusOperation(workflowOperationId);
                EnterPipelinePause(string.Empty, preserveWorkflowStatus: true);
            }
            else
            {
                EndWorkflowStatusOperation(workflowOperationId);
                RollbackLocalProjectExtension(snapshot, projectFolder, returnState, returnCompletedState);
            }
        }
        catch (Exception ex)
        {
            EndWorkflowStatusOperation(workflowOperationId);
            RollbackLocalProjectExtension(
                snapshot,
                projectFolder,
                returnState,
                returnCompletedState);
            ClearProjectExtensionPauseContext();

            AppDialogService.Error(
                this,
                LocalizationText.Get("Sources.AddLocal.FailedTitle"),
                LocalizationText.Get("Sources.AddLocal.FailedMessage"),
                details: new[] { ex.Message });
        }
        finally
        {
            _exportCancellation?.Dispose();
            _exportCancellation = null;
            IsExporting = false;
            IsBusy = false;
            NotifyExportUiStateChanged();
        }
        }

        BeginProjectExtensionPauseContext(
            RunLocalExtensionAsync,
            () => RollbackLocalProjectExtension(snapshot, projectFolder, returnState, returnCompletedState));
        await RunLocalExtensionAsync();
    }

    private void RollbackLocalProjectExtension(
        ProjectExtensionRollbackSnapshot snapshot,
        string projectFolder,
        ProjectPipelineState returnState,
        bool returnCompletedState)
    {
        _services.Pipeline.ProjectExtensionRollbackService.Rollback(snapshot);
        ReloadCanonicalMetadataSessionAfterRollback(projectFolder);
        ClearProjectExtensionPauseContext();
        _services.Foundation.ConvertedFileCleanupService.DeletePartFiles(projectFolder);
        RefreshLoadedProjectTrackStateFromPersistedProject(projectFolder);

        ApplyLoadedProjectState(LoadedProjectState.WithLocalState(true));
        ApplyLoadedProjectState(LoadedProjectState.WithMp3DiscState(false));
        ApplyLoadedProjectState(LoadedProjectState.WithAudioDiscState(false));
        _projectReviewState = ProjectReviewState.WithCompletion(returnCompletedState);
        _projectReviewState = ProjectReviewState.WithManualMergeReview(returnState == ProjectPipelineState.ReviewBeforeMerge);
        _projectReviewState = ProjectReviewState.WithPhase(returnState);
        StatusText = LocalizationText.Get("Sources.AddLocal.CanceledStatus");
        ExportProgressText = LocalizationText.Get("Sources.AddLocal.RollbackProgress");
        NotifyExportUiStateChanged();
        RefreshResumeProjects(showStatus: false);
    }

    private async Task AddMoreAudioDiscsAsync()
    {
        var manifest = _activeAudioDiscManifest;
        if (manifest is null && !string.IsNullOrWhiteSpace(_currentProjectWorkFolder))
            manifest = _services.Foundation.AudioDiscProjectService.TryLoad(_currentProjectWorkFolder);
        if (manifest is null)
            return;

        var completedDiscs = _services.Foundation.AudioDiscProjectService.CountCompletedDiscs(manifest);
        var minimumTotal = Math.Max(manifest.TotalDiscs + 1, completedDiscs + 1);
        var dialog = new AddMoreDiscsDialog(
            completedDiscs,
            manifest.TotalDiscs,
            minimumTotal,
            minimumTotal,
            99,
            [],
            preferredDriveRoot: manifest.SourceDriveRoot,
            sourceDialogInitialDirectory: string.Empty,
            configuredDrives: _settings.DiscDriveOrder,
            discDriveService: _services.Discs.DiscDriveService,
            isAudioDiscProject: true)
        {
            Owner = this
        };

        if (ModalDialogService.ShowDialog(dialog, this) != true)
            return;

        if (!TryFlushCanonicalMetadataBeforeExtensionSnapshot(manifest.ProjectFolder))
            return;

        var snapshot = CaptureAdditionalSourcesBaseline(manifest.ProjectFolder);
        if (snapshot is null) return;
        _services.Workflows.TrackPreparedStateRefreshService.RefreshForCurrentPreset(Tracks, manifest.ProjectFolder, SelectedExportPreset);
        var returnState = ProjectReviewState.Phase;
        var returnCompletedState = ProjectReviewState.IsCompleted;

        try
        {
            _services.Foundation.AudioDiscProjectService.IncreaseTotalDiscsForAdditionalRip(manifest, dialog.Result.TotalDiscs);
            manifest.SourceDriveRoot = dialog.Result.SourceFolder.Trim();
            SetSelectedSourceDisplayOverride(CreateDiscDriveDisplayName(manifest.SourceDriveRoot), manifest.SourceDriveRoot);
            _activeAudioDiscManifest = manifest;
            ApplyLoadedProjectState(LoadedProjectState.WithAudioDiscState(true));
            _isAudioDiscProjectAwaitingRip = true;
            _projectReviewState = ProjectReviewState.WithManualMergeReview(false);
            _projectReviewState = ProjectReviewState.WithCompletion(false);
            SaveCurrentAudioDiscProjectSnapshot(manifest, force: true);
            BeginProjectExtensionPauseContext(
                () => RunPreparedAudioDiscRipAsync(snapshot, returnState, returnCompletedState),
                () =>
                {
                    _services.Pipeline.ProjectExtensionRollbackService.Rollback(snapshot);
                    ReloadCanonicalMetadataSessionAfterRollback(manifest.ProjectFolder);
                    _services.Foundation.ConvertedFileCleanupService.DeletePartFiles(manifest.ProjectFolder);
                    _activeAudioDiscManifest = _services.Foundation.AudioDiscProjectService.TryLoad(manifest.ProjectFolder);
                    _isAudioDiscProjectAwaitingRip = false;
                    _projectReviewState = ProjectReviewState.WithCompletion(returnCompletedState);
                    _projectReviewState = ProjectReviewState.WithManualMergeReview(returnState is ProjectPipelineState.ReviewBeforeMerge or ProjectPipelineState.Completed);
                    _projectReviewState = ProjectReviewState.WithPhase(returnState);
                    RefreshLoadedProjectTrackStateFromPersistedProject(manifest.ProjectFolder);
                    StatusText = LocalizationText.Get("Disc.AddMore.AudioStoppedStatus");
                    ExportProgressText = LocalizationText.Get("Disc.AddMore.RollbackProgress");
                    NotifyExportUiStateChanged();
                    RefreshResumeProjects(showStatus: false);
                });
            await RunPreparedAudioDiscRipAsync(snapshot, returnState, returnCompletedState);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            _services.Pipeline.ProjectExtensionRollbackService.Rollback(snapshot);
            _projectExtensionRollbackSnapshot = null;
            AppDialogService.Warning(this, LocalizationText.Get("Disc.AddMore.TotalRejected"), LocalizationText.Get("Common.OperationFailed"), details: new[] { ex.Message });
        }
    }

    private void ApplyDiscProjectSetup(DiscProjectSetupResult setup)
    {
        SetSelectedExportPresetSilently(setup.SelectedExportPreset);
        SetParallelJobsInput(setup.ParallelJobs, showMessage: false);
        OutputExtension = setup.OutputExtension;
        OutputFolder = setup.OutputFolder;
        BookTitle = setup.BookTitle;
        Album = setup.Album;
        Author = setup.Author;
        Narrator = setup.Narrator;
        Genre = setup.Genre;
        FileNameTemplate = setup.FileNameTemplate;
        MergeAutomaticallyAfterConversion = setup.AutoMergeAfterConversion;

        var processedCoverPath = !string.IsNullOrWhiteSpace(setup.ProcessedCoverPath) &&
                                 File.Exists(setup.ProcessedCoverPath)
            ? setup.ProcessedCoverPath
            : "";
        _projectMetadata.ApplyCoverMetadata(setup.CoverSourcePath, processedCoverPath, MetadataOrigin.User);
        CoverPreviewSource = processedCoverPath;

        OnExportPreviewChanged();
        FlushPendingChapterMetadataRefresh();
    }

    private async Task ContinueLoadedMp3DiscProjectAsync()
    {
        var manifest = TryLoadCurrentMp3DiscProjectManifest(resetStateWhenFolderMissing: true);
        if (manifest is null)
            return;
        if (_resumeDiscSourceSelectionRequired)
        {
            await ShowCurrentProjectSetupDialogAsync();
            return;
        }

        var resumePlan = _services.Foundation.Mp3DiscProjectService.BuildResumePlan(manifest);
        var sourceFolder = await _services.Discs.DiscDriveService.ResolveResumeDiscSourceAsync(
            _currentProjectWorkFolder,
            default,
            manifest.SourceFolder,
            _settings.LastDiscSourceFolder,
            SelectedFolder);

        if (string.IsNullOrWhiteSpace(sourceFolder))
        {
            sourceFolder = ShowDiscSourceSelectionDialog();
            if (string.IsNullOrWhiteSpace(sourceFolder))
                return;

            sourceFolder = await ResolveSelectedDiscSourceAsync(sourceFolder);
            if (string.IsNullOrWhiteSpace(sourceFolder))
                return;
        }

        var setupDialog = CreateProjectSetupDialog(new ProjectSetupDialogRequest(
            SourceKind: ProjectSetupSourceKind.Mp3Disc,
            WindowTitle: LocalizationText.Get("MainWindow.Setup.Mp3Continue"),
            SourceInformation: resumePlan.SetupMessage,
            Instruction: LocalizationText.Get("MainWindow.Setup.FolderInstruction"),
            DefaultDiscCount: resumePlan.CurrentTotalDiscs,
            MinimumDiscs: resumePlan.MinimumTotalDiscs,
            MaximumDiscs: 99,
            ExportPresets: ExportPresets,
            SelectedExportPreset: SelectedExportPreset,
            ParallelJobs: ParallelJobsInput,
            OutputExtension: OutputExtension,
            OutputFolder: OutputFolder,
            BookTitle: BookTitle,
            Album: Album,
            Author: Author,
            Narrator: Narrator,
            Genre: Genre,
            FileNameTemplate: FileNameTemplate,
            CoverSourcePath: GetCoverSourcePathForProjectSetup(),
            CoverPreviewSource: CoverPreviewSource,
            CoverWorkFolder: GetProjectFolderStructure().CoversFolder,
            AutoMergeAfterConversion: _settings.MergeAutomaticallyAfterConversion,
            UsePrivateGenreList: _settings.UsePrivateGenreList,
            GenreOptions: GenreListService.GetGenres(_settings),
            SourceFolder: sourceFolder,
            MaxSourceBitrateKbps: GetMaxTrackBitrateKbps(),
            LastCoverFolder: _settings.LastCoverFolder ?? string.Empty));

        SetMetadataEditingAvailable(true);
        var startProject = ModalDialogService.ShowDialog(setupDialog, this) == true;
        FlushProjectSetupDiscCountPreviewRefresh();
        var setup = setupDialog.Result;
        ApplyDiscProjectSetup(setup);
        if (!startProject)
            return;

        _services.Foundation.Mp3DiscProjectService.UpdateResumeDiscPlan(manifest, setup.TotalDiscs, sourceFolder);
        SaveCurrentMp3DiscProjectSnapshot(manifest);

        resumePlan = _services.Foundation.Mp3DiscProjectService.BuildResumePlan(manifest);

        if (resumePlan.NextMissingDiscNumber is null)
        {
            if (!await PrepareCompletedMp3DiscProjectForCurrentPresetAsync(manifest.ProjectFolder))
                return;

            ApplyLoadedProjectState(LoadedProjectState.WithDiscImportRequirement(false));
            ApplyLoadedProjectState(LoadedProjectState.WithMp3DiscState(true));
            OnPropertyChanged(nameof(CanStartExport));
            OnPropertyChanged(nameof(ExportButtonText));
            OnPropertyChanged(nameof(ExportButtonVisibility));
            RefreshResumeProjects(showStatus: false);
            await RunCurrentExportPlanAsync(manifest.ProjectFolder, ProjectManifestTypes.Mp3DiscProject);
            return;
        }

        ApplyLoadedProjectState(LoadedProjectState.WithDiscImportRequirement(true));
        NotifyExportUiStateChanged();

        await ImportMp3DiscProjectAsync(
            manifest.ProjectFolder,
            sourceFolder,
            manifest,
            startDiscNumber: resumePlan.NextMissingDiscNumber.Value,
            firstDiscAlreadyReady: false,
            autoExportWhenComplete: true);
    }

    private async Task<bool> ImportMp3DiscProjectAsync(
        string projectFolder,
        string sourceFolder,
        Mp3DiscProjectManifest projectManifest,
        int startDiscNumber,
        bool firstDiscAlreadyReady,
        bool autoExportWhenComplete,
        ProjectExtensionRollbackSnapshot? extensionSnapshot = null,
        ProjectPipelineState? extensionReturnState = null,
        bool extensionReturnCompletedState = false,
        bool startWithSelectedDrive = false)
    {
        var totalDiscs = projectManifest.TotalDiscs;
        _currentProjectWorkFolder = projectFolder;
        _currentFolderPath = projectFolder;

        // Sobald ein MP3-CD-Projekt angelegt wurde, muss seine Identität auch nach
        // automatischem Import und Export in der aktuellen Sitzung erhalten bleiben.
        // Andernfalls würde ein anschließender Presetwechsel den lokalen Projektpfad verwenden.
        ApplyLoadedProjectState(LoadedProjectState.WithMp3DiscState(true));
        NotifyExportUiStateChanged();

        var livePreset = ExportPreset.Parse(SelectedExportPreset);
        var liveConvertedFolder = ProjectFolderLayout.GetConvertedPresetFolder(projectFolder, livePreset.GetFolderName());
        Directory.CreateDirectory(liveConvertedFolder);
        _services.Foundation.ConvertedFileCleanupService.DeletePartFiles(projectFolder);

        var liveConversionQueue = _services.Pipeline.LiveConversionQueueFactory();
        var mp3RequestedParallelJobs = ParallelJobsInput;
        using var liveConversionSemaphore = new SemaphoreSlim(liveConversionQueue.GetLiveWorkerLimit(ParallelJobsInput));
        var mp3EffectiveWorkerLimit = liveConversionSemaphore.CurrentCount;
        var liveConversionTasks = new List<Task>();
        var liveConversionTasksLock = new object();
        var extensionGeneratedConvertedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var extensionGeneratedConvertedFilesLock = new object();
        var liveConvertedCount = 0;
        var liveSkippedCount = 0;
        var liveFailedCount = 0;
        var backgroundPreparedCount = 0;
        var backgroundFailedCount = 0;
        var activeConversionTrackNumbers = new HashSet<int>();
        var activeConversionTrackNumbersLock = new object();
        var mp3StatusProgressLock = new object();
        var totalCopiedFiles = _services.Workflows.Mp3DiscImportWorkflowService.CountCompletedCopiedFiles(projectManifest);
        var latestStatusDiscNumber = startDiscNumber;
        var latestCopiedBeforeCurrentDisc = totalCopiedFiles;
        var latestCopiedCurrentDisc = 0;
        var latestTotalCurrentDisc = 0;
        var latestCurrentDiscFinished = false;
        var latestAllDiscsFinished = false;
        var workflowOperationId = BeginWorkflowStatusOperation(projectFolder);

        var mp3PerformanceApp = Application.Current as App;
        PerformanceOperationScope activeMp3DiscPerformanceScope = default;
        PerformancePhaseScope activeMp3DiscPerformancePhase = default;

        void CompleteActiveMp3PerformancePhase()
        {
            activeMp3DiscPerformancePhase.Complete();
            activeMp3DiscPerformancePhase = default;
        }

        void StartActiveMp3PerformancePhase(string phaseName)
        {
            CompleteActiveMp3PerformancePhase();
            if (mp3PerformanceApp is null || !activeMp3DiscPerformanceScope.IsActive)
                return;

            activeMp3DiscPerformancePhase = mp3PerformanceApp.StartPerformancePhase(
                activeMp3DiscPerformanceScope.OperationId,
                phaseName);
        }

        void CompleteActiveMp3PerformanceOperation()
        {
            CompleteActiveMp3PerformancePhase();
            activeMp3DiscPerformanceScope.Complete();
            activeMp3DiscPerformanceScope = default;
            mp3PerformanceApp?.TryFinalizeDisabledPerformanceSession();
        }

        void CancelActiveMp3PerformanceOperation()
        {
            activeMp3DiscPerformancePhase.Cancel(_presentationState.PauseRequested);
            activeMp3DiscPerformancePhase = default;
            activeMp3DiscPerformanceScope.Cancel(_presentationState.PauseRequested);
            activeMp3DiscPerformanceScope = default;
            mp3PerformanceApp?.TryFinalizeDisabledPerformanceSession();
        }

        string ImportCancellationReason() => _presentationState.PauseRequested
            ? LocalizationText.Get("MainWindow.Status.Paused")
            : "Benutzerabbruch";

        void FailActiveMp3PerformanceOperation(Exception exception)
        {
            activeMp3DiscPerformancePhase.Fail(exception);
            activeMp3DiscPerformancePhase = default;
            activeMp3DiscPerformanceScope.Fail(exception);
            activeMp3DiscPerformanceScope = default;
            mp3PerformanceApp?.TryFinalizeDisabledPerformanceSession();
        }

        void StartMp3DiscPerformanceOperation(string sourcePath)
        {
            if (mp3PerformanceApp is null || activeMp3DiscPerformanceScope.IsActive)
                return;

            activeMp3DiscPerformanceScope = mp3PerformanceApp.StartPerformanceOperation(
                "disc.mp3.import",
                PerformanceProjectType.Mp3Disc,
                new PerformanceWorkloadSnapshot
                {
                    ProjectType = PerformanceProjectType.Mp3Disc,
                    DiscCount = 1
                });

            if (!activeMp3DiscPerformanceScope.IsActive)
                return;

            mp3PerformanceApp.PerformanceTraceService?.RecordParallelism(
                activeMp3DiscPerformanceScope.OperationId, mp3RequestedParallelJobs, mp3EffectiveWorkerLimit);

            mp3PerformanceApp.RecordPerformanceDriveRole(
                activeMp3DiscPerformanceScope.OperationId,
                sourcePath,
                PerformanceDriveRole.Source | PerformanceDriveRole.OpticalSource);
            mp3PerformanceApp.RecordPerformanceDriveRole(
                activeMp3DiscPerformanceScope.OperationId,
                projectFolder,
                PerformanceDriveRole.ProjectWorking);
            var performanceOutputFolder = EffectiveOutputFolder;
            if (!string.IsNullOrWhiteSpace(performanceOutputFolder))
            {
                mp3PerformanceApp.RecordPerformanceDriveRole(
                    activeMp3DiscPerformanceScope.OperationId,
                    performanceOutputFolder,
                    PerformanceDriveRole.Output);
            }

            StartActiveMp3PerformancePhase("disc.scan");
        }

        var workManifest = _services.Foundation.WorkManifestService.LoadOrCreate(
            ProjectFolderLayout.ResolveExportManifestPath(projectFolder),
            ProjectManifestTypes.Mp3DiscProject,
            projectFolder,
            ProjectFolderLayout.GetOriginalsFolder(projectFolder),
            livePreset.DisplayName);
        var existingConvertedCount = _services.Foundation.WorkManifestService.CountReusableConvertedTracks(workManifest, livePreset);
        var liveWorkManifestLock = new object();
        await using var preparedLifecycle = new PreparedTrackLifecycleSession(
            _services.Foundation.WorkManifestService, _services.Workflows.AacExportProcessingService,
            workManifest, ProjectFolderLayout.ResolveExportManifestPath(projectFolder), liveWorkManifestLock,
            _ffmpegStatus.FfprobePath ?? "", liveConversionQueue.GetLiveWorkerLimit(ParallelJobsInput));

        var importedDiscSignatures = _services.Workflows.Mp3DiscImportWorkflowService.BuildCompletedDiscSignatureSet(projectManifest);

        RefreshExistingMp3DiscTrackStateBeforeImport(
            projectFolder,
            sourceFolder,
            totalDiscs);

        SetPipelineState(ProjectPipelineState.AcquiringSources);
        IsBusy = true;
        IsDiscImporting = true;
        _activeMp3DiscManifest = projectManifest;
        _discImportCancellation = new CancellationTokenSource();
        var importSourcesCompleted = false;

        void CompleteMp3ImportLifecycle()
        {
            _discImportCancellation?.Dispose();
            _discImportCancellation = null;
            _activeMp3DiscManifest = null;
            IsDiscImporting = false;
            IsBusy = false;
            _trackStateUpdateQueueService.RequestRefresh();
            NotifyExportUiStateChanged();
        }

        preparedLifecycle.QueuePreset(livePreset, _discImportCancellation.Token);

        async Task<bool> HandleImportCancellationAsync(string progressText, bool cleanupPartFiles = false)
        {
            Interlocked.Exchange(ref _mp3DiscBackgroundPresetPreparationActive, 0);
            try
            {
                await LiveConversionWorkflowService.WaitForTasksAsync(
                    liveConversionTasks,
                    liveConversionTasksLock,
                    () => SaveCurrentMp3DiscProjectSnapshot(projectManifest),
                    ProjectSnapshotInterval);
            }
            catch (OperationCanceledException) when (_discImportCancellation?.IsCancellationRequested == true)
            {
                // WhenAll has finished every producer, including canceled background jobs.
            }

            await preparedLifecycle.DrainAsync();

            if (extensionSnapshot is null)
            {
                var last = _services.Status.WorkflowStatusCoordinator.Snapshot;
                PublishWorkflowStatus(workflowOperationId, last with { IsPaused = true });
                EndWorkflowStatusOperation(workflowOperationId);
                await CancelMp3DiscImportAndShowResumeDialogAsync(
                    projectManifest,
                    progressText,
                    cleanupPartFiles,
                    preserveWorkflowStatus: true);
                return true;
            }

            if (_presentationState.PauseRequested)
            {
                _services.Foundation.ConvertedFileCleanupService.DeletePartFiles(projectFolder);
                var last = _services.Status.WorkflowStatusCoordinator.Snapshot;
                PublishWorkflowStatus(workflowOperationId, last with { IsPaused = true });
                EndWorkflowStatusOperation(workflowOperationId);
                EnterPipelinePause(string.Empty, preserveWorkflowStatus: true);
                return true;
            }

            string[] generatedConvertedFiles;
            lock (extensionGeneratedConvertedFilesLock)
            {
                generatedConvertedFiles = extensionGeneratedConvertedFiles.ToArray();
            }

            _services.Pipeline.ProjectExtensionRollbackService.Rollback(extensionSnapshot, generatedConvertedFiles);
            ReloadCanonicalMetadataSessionAfterRollback(projectFolder);
            ClearProjectExtensionPauseContext();
            _services.Foundation.ConvertedFileCleanupService.DeletePartFiles(projectFolder);
            RefreshLoadedProjectTrackStateFromPersistedProject(projectFolder);

            ApplyLoadedProjectState(LoadedProjectState.WithDiscImportRequirement(false));
            ApplyLoadedProjectState(LoadedProjectState.WithMp3DiscState(true));
            _projectReviewState = ProjectReviewState.WithCompletion(extensionReturnCompletedState);
            _projectReviewState = ProjectReviewState.WithManualMergeReview(extensionReturnState == ProjectPipelineState.ReviewBeforeMerge);
            _projectReviewState = ProjectReviewState.WithPhase(extensionReturnState ?? ProjectPipelineState.ReviewBeforeMerge);
            StatusText = LocalizationText.Get("Disc.AddMore.CanceledStatus");
            ExportProgressText = LocalizationText.Get("Disc.AddMore.CanceledProgress");
            NotifyExportUiStateChanged();
            RefreshResumeProjects(showStatus: false);

            if (!_isShutdownInProgress)
            {
                AppDialogService.Info(
                    this,
                    LocalizationText.Get("Disc.AddMore.CanceledTitle"),
                    LocalizationText.Get("Disc.AddMore.CanceledMessage"),
                    title: LocalizationText.Get("Disc.AddMore.CanceledTitle"));
            }

            return true;
        }

        try
        {
            var token = _discImportCancellation.Token;
            var lastMp3DiscSnapshotUtc = DateTime.UtcNow;

            void SaveTimedMp3DiscProjectSnapshot()
            {
                var now = DateTime.UtcNow;
                if (now - lastMp3DiscSnapshotUtc < ProjectSnapshotInterval)
                    return;

                SaveCurrentMp3DiscProjectSnapshot(projectManifest);
                lastMp3DiscSnapshotUtc = now;
            }

            int[] GetActiveConversionTrackNumbers()
            {
                lock (activeConversionTrackNumbersLock)
                    return activeConversionTrackNumbers.OrderBy(number => number).ToArray();
            }

            void PublishMp3Status(
                int? discNumber = null,
                int? copiedBeforeCurrentDisc = null,
                int? copiedCurrentDisc = null,
                int? totalCurrentDisc = null,
                bool? currentDiscFinished = null,
                bool? allDiscsFinished = null,
                bool isPaused = false)
            {
                int statusDiscNumber;
                int statusCopiedBeforeCurrentDisc;
                int statusCopiedCurrentDisc;
                int statusTotalCurrentDisc;
                bool statusCurrentDiscFinished;
                bool statusAllDiscsFinished;

                lock (mp3StatusProgressLock)
                {
                    if (discNumber.HasValue)
                        latestStatusDiscNumber = discNumber.Value;
                    if (copiedBeforeCurrentDisc.HasValue)
                        latestCopiedBeforeCurrentDisc = copiedBeforeCurrentDisc.Value;
                    if (copiedCurrentDisc.HasValue)
                        latestCopiedCurrentDisc = copiedCurrentDisc.Value;
                    if (totalCurrentDisc.HasValue)
                        latestTotalCurrentDisc = totalCurrentDisc.Value;
                    if (currentDiscFinished.HasValue)
                        latestCurrentDiscFinished = currentDiscFinished.Value;
                    if (allDiscsFinished.HasValue)
                        latestAllDiscsFinished = allDiscsFinished.Value;

                    statusDiscNumber = latestStatusDiscNumber;
                    statusCopiedBeforeCurrentDisc = latestCopiedBeforeCurrentDisc;
                    statusCopiedCurrentDisc = latestCopiedCurrentDisc;
                    statusTotalCurrentDisc = latestTotalCurrentDisc;
                    statusCurrentDiscFinished = latestCurrentDiscFinished;
                    statusAllDiscsFinished = latestAllDiscsFinished;
                }

                var knownTotal = statusCopiedBeforeCurrentDisc + statusTotalCurrentDisc;
                if (statusTotalCurrentDisc <= 0)
                    knownTotal = statusCopiedBeforeCurrentDisc;
                var converted = Math.Clamp(
                    existingConvertedCount + Volatile.Read(ref liveConvertedCount) + Volatile.Read(ref backgroundPreparedCount),
                    0,
                    knownTotal);

                PublishWorkflowStatus(
                    workflowOperationId,
                    _services.Status.Mp3DiscWorkflowStatusAdapter.CreateRunningSnapshot(
                        projectFolder,
                        ProjectPipelineState.AcquiringSources,
                        statusDiscNumber,
                        totalDiscs,
                        statusCopiedCurrentDisc,
                        statusTotalCurrentDisc,
                        statusCopiedBeforeCurrentDisc + statusCopiedCurrentDisc,
                        knownTotal,
                        converted,
                        GetActiveConversionTrackNumbers(),
                        livePreset,
                        isExtension: extensionSnapshot is not null,
                        isPaused: isPaused,
                        currentDiscFinished: statusCurrentDiscFinished,
                        allDiscsFinished: statusAllDiscsFinished,
                        driveDisplayName: DiscDriveSelectionPresentationService.FormatStatusDriveLabel(sourceFolder, _settings.DiscDriveOrder)));
            }

            for (var discNumber = startDiscNumber; discNumber <= totalDiscs; discNumber++)
            {
                lock (mp3StatusProgressLock)
                    latestCopiedBeforeCurrentDisc = totalCopiedFiles;

                if (token.IsCancellationRequested)
                {
                    return await HandleImportCancellationAsync(
                        $"{ImportCancellationReason()}: {totalCopiedFiles} Datei(en) kopiert");
                }

                var shouldWaitForDisc = discNumber != startDiscNumber || !firstDiscAlreadyReady;
                var acceptDuplicateForCurrentDisc = false;
                if (discNumber == startDiscNumber && _resumeDiscPreview.Mp3Disc?.DiscNumber == discNumber)
                {
                    var current = await Task.Run(() => _services.Workflows.Mp3DiscImportWorkflowService
                        .AnalyzeDiscForImport(sourceFolder, importedDiscSignatures, token), token);
                    if (_resumeDiscPreview.CanResumeMp3Import(discNumber, current.Signature))
                    {
                        shouldWaitForDisc = false;
                        acceptDuplicateForCurrentDisc = _resumeDiscPreview.Mp3DuplicateAccepted;
                    }
                    else if (current.Analysis.IsSupportedDataDisc)
                        DiscardChangedResumeDiscPreview(discNumber);
                }
                if (shouldWaitForDisc)
                {
                    Interlocked.Exchange(ref _mp3DiscBackgroundPresetPreparationActive, 1);
                    StartBackgroundMp3DiscPresetPreparation(
                        projectFolder,
                        livePreset,
                        liveConvertedFolder,
                        liveConversionSemaphore,
                        liveConversionTasks,
                        liveConversionTasksLock,
                        () => Interlocked.Increment(ref backgroundPreparedCount),
                        () => Interlocked.Increment(ref backgroundFailedCount),
                        token,
                        item => RegisterPreparedMp3Track(preparedLifecycle,
                            item.Index, item.Track, item.SourcePath, item.ConvertedPath, livePreset, token),
                        preparedLifecycle);

                    PublishMp3Status(
                        discNumber - 1,
                        copiedBeforeCurrentDisc: totalCopiedFiles,
                        copiedCurrentDisc: 0,
                        totalCurrentDisc: 0,
                        currentDiscFinished: true,
                        allDiscsFinished: false);

                    var selectedSourceFolder = sourceFolder;
                    var waitOutcome = await WaitForNextDiscAsync(
                        sourceFolder,
                        discNumber,
                        totalDiscs,
                        importedDiscSignatures,
                        token,
                        acceptedSource =>
                        {
                            selectedSourceFolder = acceptedSource;
                            // Keep an accepted, uncommitted source across Pause/Continue.
                            // It is not yet the last completed drive in the rotation.
                            _resumeDiscStartDriveRoot = acceptedSource;
                        },
                        startWithSelectedDrive: startWithSelectedDrive && discNumber == startDiscNumber);
                    token.ThrowIfCancellationRequested();
                    sourceFolder = selectedSourceFolder;
                    projectManifest.SourceFolder = sourceFolder;
                    _settings.LastDiscSourceFolder = sourceFolder;
                    SaveSettingsIfReady();
                    Interlocked.Exchange(ref _mp3DiscBackgroundPresetPreparationActive, 0);
                    if (waitOutcome == DiscWaitDialogOutcome.Deferred)
                    {
                        ApplyPipelinePauseRequest();
                        return await HandleImportCancellationAsync(
                            $"Projekt pausiert • MP3-CD {discNumber} von {totalDiscs} ausstehend");
                    }
                    if (waitOutcome == DiscWaitDialogOutcome.ImportCompleted)
                    {
                        DiscardChangedResumeDiscPreview(discNumber);
                        _services.Foundation.Mp3DiscProjectService.CompleteDiscImportEarly(projectManifest);
                        totalDiscs = projectManifest.TotalDiscs;
                        SaveCurrentMp3DiscProjectSnapshot(projectManifest);
                        break;
                    }
                    acceptDuplicateForCurrentDisc = waitOutcome == DiscWaitDialogOutcome.ReadyWithDuplicateOverride;

                    try
                    {
                        var previewTracks = await _services.Media.FolderScanner.ScanAsync(
                            sourceFolder, TrackNumberPreference.FileName, cancellationToken: token);
                        token.ThrowIfCancellationRequested();
                        AppendDiscPreviewTracks(
                            previewTracks,
                            sourceFolder,
                            discNumber,
                            clearExistingTracks: false,
                            refreshChapterProjection: false);
                        RefreshTrackPresentationViews();
                        NotifyExportUiStateChanged();
                        OnExportPreviewChanged();
                        await Dispatcher.Yield(DispatcherPriority.Background);
                        PublishMp3Status(
                            discNumber,
                            copiedBeforeCurrentDisc: totalCopiedFiles,
                            copiedCurrentDisc: 0,
                            totalCurrentDisc: 0,
                            currentDiscFinished: false,
                            allDiscsFinished: false);
                        AutoFitTrackColumnsAfterRender();
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        ShowMp3DiscAnalysisFailed(discNumber, ex);
                        return false;
                    }
                }

                StartMp3DiscPerformanceOperation(sourceFolder);
                var totalFilesOnDisc = 0;

                using var progress = new DispatcherCoalescingProgress<DiscCopyProgress>(
                    Dispatcher,
                    item =>
                    {
                        PublishMp3Status(
                            discNumber,
                            copiedBeforeCurrentDisc: totalCopiedFiles,
                            copiedCurrentDisc: item.CopiedFiles,
                            totalCurrentDisc: item.TotalFiles);
                        SaveTimedMp3DiscProjectSnapshot();
                    });

                var copiedFileProgress = new InlineProgress<DiscCopiedFile>(copiedFile =>
                    QueueLiveConversionForCopiedFile(
                        copiedFile,
                        projectFolder,
                        workManifest,
                        liveWorkManifestLock,
                        preparedLifecycle,
                        livePreset,
                        liveConvertedFolder,
                        discNumber,
                        totalDiscs,
                        liveConversionQueue,
                        liveConversionSemaphore,
                        liveConversionTasks,
                        liveConversionTasksLock,
                        extensionGeneratedConvertedFiles,
                        extensionGeneratedConvertedFilesLock,
                        activeConversionTrackNumbers,
                        activeConversionTrackNumbersLock,
                        totalCopiedFiles,
                        () => Interlocked.Increment(ref liveSkippedCount),
                        () =>
                        {
                            Interlocked.Increment(ref liveConvertedCount);
                            _trackStateUpdateQueueService.RequestRefresh();
                        },
                        () =>
                        {
                            Interlocked.Increment(ref liveFailedCount);
                            _trackStateUpdateQueueService.RequestRefresh();
                        },
                        () => PublishMp3Status(),
                        token));

                var importResult = await _services.Workflows.Mp3DiscImportWorkflowService.ImportDiscAsync(
                    sourceFolder,
                    projectFolder,
                    projectManifest,
                    discNumber,
                    totalDiscs,
                    importedDiscSignatures,
                    progress,
                    copiedFileProgress,
                    onReadyToCopy: precheck =>
                    {
                        if (_resumeDiscPreview.DiscNumber == discNumber &&
                            !_resumeDiscPreview.MatchesMp3(discNumber, precheck.Signature))
                            DiscardChangedResumeDiscPreview(discNumber);
                        _resumeDiscPreview.Set(new Mp3DiscPendingChapterDisc(
                            discNumber, precheck.Signature, precheck.PlannedSources), acceptDuplicateForCurrentDisc);
                        _resumeDiscPreview.MarkProcessingStarted();
                        PersistTrackListState();
                        _resumeDiscStartDriveRoot = sourceFolder;
                        // The disc is now accepted for import. Stage its stable source identities in
                        // the runtime chapter catalog before copy/conversion starts. Nothing is
                        // persisted merely by selecting or analyzing a disc.
                        _mp3DiscChapterSourceCatalog.Stage(
                            discNumber,
                            precheck.Signature,
                            precheck.PlannedSources);
                        if (_transientMp3DiscChapterPreview?.DiscNumber == discNumber)
                            _transientMp3DiscChapterPreview = null;

                        _services.Discs.Mp3DiscTrackReconciliationService.ApplyPlannedSourceIds(
                            Tracks,
                            sourceFolder,
                            discNumber,
                            precheck.PlannedSources);

                        var previewTracks = _services.Discs.Mp3DiscTrackReconciliationService.CreatePreviewTracksFromKnownFiles(
                            sourceFolder,
                            precheck.Analysis.SupportedAudioFiles);
                        _services.Discs.Mp3DiscTrackReconciliationService.NormalizeProjectDiscNumber(
                            previewTracks,
                            discNumber);

                        _services.Discs.Mp3DiscTrackReconciliationService.ApplyPlannedSourceIds(
                            previewTracks,
                            sourceFolder,
                            discNumber,
                            precheck.PlannedSources);

                        AppendDiscPreviewTracks(
                            previewTracks,
                            sourceFolder,
                            discNumber,
                            clearExistingTracks: false,
                            refreshChapterProjection: true);
                        RefreshTrackPresentationViews();
                        AutoFitTrackColumnsAfterRender();
                        NotifyExportUiStateChanged();
                        CompleteActiveMp3PerformancePhase();
                        StartActiveMp3PerformancePhase("file.copy");
                        totalFilesOnDisc = precheck.TotalFiles;
                        if (activeMp3DiscPerformanceScope.IsActive && mp3PerformanceApp is not null)
                        {
                            try
                            {
                                mp3PerformanceApp.UpdatePerformanceWorkload(
                                    activeMp3DiscPerformanceScope.OperationId,
                                    precheck.CreatePerformanceWorkload(previewTracks.Count));
                            }
                            catch { /* Workload evidence must never affect copying. */ }
                        }
                        PublishMp3Status(
                            discNumber,
                            copiedBeforeCurrentDisc: totalCopiedFiles,
                            copiedCurrentDisc: 0,
                            totalCurrentDisc: totalFilesOnDisc,
                            currentDiscFinished: false,
                            allDiscsFinished: false);
                    },
                    token,
                    acceptDuplicateForCurrentDisc,
                    preparedDisc: _resumeDiscPreview.Mp3Disc,
                    sourceReadStarted: source => PublishDiscReadSource(source, projectFolder, token));

                progress.Flush();

                if (importResult.IsAlreadyImported)
                {
                    CompleteActiveMp3PerformancePhase();
                    activeMp3DiscPerformanceScope.Cancel();
                    activeMp3DiscPerformanceScope = default;
                    mp3PerformanceApp?.TryFinalizeDisabledPerformanceSession();
                    ShowAlreadyImportedMp3DiscWarning(discNumber, totalDiscs);
                    if (extensionSnapshot is not null)
                        return await HandleImportCancellationAsync("Bereits importierte CD erkannt.");
                    return false;
                }

                if (importResult.WasCanceled)
                {
                    CancelActiveMp3PerformanceOperation();
                    PublishMp3Status(
                        discNumber,
                        copiedBeforeCurrentDisc: totalCopiedFiles,
                        copiedCurrentDisc: importResult.CopiedFiles,
                        totalCurrentDisc: totalFilesOnDisc,
                        isPaused: true);
                    return await HandleImportCancellationAsync(
                        $"{ImportCancellationReason()}: {importResult.CopiedFiles}/{totalFilesOnDisc} auf CD {discNumber} kopiert");
                }

                CompleteActiveMp3PerformancePhase();
                // ImportDiscAsync has already committed the same planned SourceIds into the
                // completed manifest. From here on the persisted manifest is authoritative.
                if (_resumeDiscPreview.DiscNumber == discNumber) _resumeDiscPreview.Clear();
                _mp3DiscChapterSourceCatalog.Remove(discNumber);
                SaveCurrentMp3DiscProjectSnapshot(projectManifest);
                ApplySourceMetadataAggregation(
                    projectFolder,
                    discNumber == 1
                        ? ProjectMetadataInitializationState.InitialSetComplete
                        : ProjectMetadataInitializationState.SubsequentSource,
                    CreateMp3DiscProjectAggregationOptions());

                PublishMp3Status(
                    discNumber,
                    copiedBeforeCurrentDisc: totalCopiedFiles,
                    copiedCurrentDisc: importResult.CopiedFiles,
                    totalCurrentDisc: totalFilesOnDisc,
                    currentDiscFinished: discNumber < totalDiscs,
                    allDiscsFinished: discNumber >= totalDiscs);

                // ImportDiscAsync has committed this disc. Only now may the round
                // advance beyond its source, including when Pause follows eject.
                _resumeDiscStartDriveRoot = null;
                totalCopiedFiles += importResult.CopiedFiles;

                await RefreshMp3DiscTrackListAfterDiscImportAsync(
                    projectFolder,
                    sourceFolder,
                    projectManifest,
                    activeMp3DiscPerformanceScope.OperationId,
                    importResult.ImportResult?.ImportedFolder);

                if (activeMp3DiscPerformanceScope.IsActive && mp3PerformanceApp is not null)
                {
                    mp3PerformanceApp.UpdatePerformanceWorkload(
                        activeMp3DiscPerformanceScope.OperationId,
                        CreateMp3DiscPerformanceWorkload(discNumber, totalFilesOnDisc));
                }

                UpdateMp3DiscImportContinuationState(discNumber, totalDiscs);
                if (discNumber < totalDiscs)
                    CompleteActiveMp3PerformanceOperation();

                await WarnIfMp3DiscEjectFailedAsync(sourceFolder, discNumber, totalDiscs);
            }
            importSourcesCompleted = true;
        }
        catch (OperationCanceledException)
        {
            CancelActiveMp3PerformanceOperation();
            return await HandleImportCancellationAsync(
                $"{ImportCancellationReason()}: {totalCopiedFiles} Datei(en) kopiert",
                cleanupPartFiles: true);
        }
        catch (Exception) when (_discImportCancellation?.IsCancellationRequested == true)
        {
            CancelActiveMp3PerformanceOperation();
            return await HandleImportCancellationAsync(
                $"{ImportCancellationReason()}: {totalCopiedFiles} Datei(en) kopiert",
                cleanupPartFiles: true);
        }
        catch (Exception ex)
        {
            FailActiveMp3PerformanceOperation(ex);
            MarkMp3DiscImportFailed(projectManifest, projectFolder, ex);
            return false;
        }
        finally
        {
            Interlocked.Exchange(ref _mp3DiscBackgroundPresetPreparationActive, 0);
            if (_isShutdownInProgress)
                TrySaveCurrentMp3DiscProjectSnapshot(projectManifest);

            if (!importSourcesCompleted)
            {
                _discImportCancellation?.Cancel();
                try
                {
                    await LiveConversionWorkflowService.WaitForTasksAsync(liveConversionTasks, liveConversionTasksLock);
                    lock (liveWorkManifestLock)
                        _services.Foundation.WorkManifestService.Save(
                            ProjectFolderLayout.ResolveExportManifestPath(projectFolder), workManifest);
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    _runtimeServices.DiagnosticLogService.WriteError("MP3 cache checkpoint after cancellation failed", ex);
                }
                finally { await preparedLifecycle.DrainAsync(); CompleteMp3ImportLifecycle(); }
            }
        }

        try
        {
            StartActiveMp3PerformancePhase("preset.prepare");

            await WaitForLiveMp3DiscPreparationAsync(
                projectManifest,
                projectFolder,
                liveConversionTasks,
                liveConversionTasksLock);

            await preparedLifecycle.DrainAsync();
            lock (liveWorkManifestLock)
                _services.Foundation.WorkManifestService.Save(
                    ProjectFolderLayout.ResolveExportManifestPath(projectFolder), workManifest);
            if (_discImportCancellation?.IsCancellationRequested == true)
            {
                CancelActiveMp3PerformanceOperation();
                return await HandleImportCancellationAsync(LocalizationText.Get("MainWindow.Export.ConversionPaused"));
            }
            CompleteMp3ImportLifecycle();

            Guid? presetPreparationPhaseId = activeMp3DiscPerformancePhase.IsActive
                ? activeMp3DiscPerformancePhase.PhaseId
                : null;
            var presetPreparationPerformanceContext =
                activeMp3DiscPerformanceScope.IsActive && mp3PerformanceApp is not null
                    ? mp3PerformanceApp.CreatePerformanceExternalProcessContext(
                        activeMp3DiscPerformanceScope.OperationId,
                        presetPreparationPhaseId,
                        "preset.prepare")
                    : null;

            if (!await PrepareCompletedMp3DiscProjectForCurrentPresetAsync(
                    projectFolder,
                    presetPreparationPerformanceContext))
            {
                var performanceFailure = new InvalidOperationException(
                    "MP3 preset preparation did not complete successfully.");
                FailActiveMp3PerformanceOperation(performanceFailure);
                return false;
            }

            CompleteActiveMp3PerformanceOperation();
        }
        catch (OperationCanceledException)
        {
            CancelActiveMp3PerformanceOperation();
            if (_discImportCancellation?.IsCancellationRequested == true)
                return await HandleImportCancellationAsync(LocalizationText.Get("MainWindow.Export.ConversionPaused"));
            throw;
        }
        catch (Exception ex)
        {
            FailActiveMp3PerformanceOperation(ex);
            throw;
        }
        finally
        {
            if (_discImportCancellation is not null)
                CompleteMp3ImportLifecycle();
        }

        ApplyLoadedProjectState(LoadedProjectState.WithDiscImportRequirement(false));
        _services.Foundation.ConvertedFileCleanupService.DeletePartFiles(projectFolder);
        if (autoExportWhenComplete)
        {
            EndWorkflowStatusOperation(workflowOperationId);
            await RunFinalMp3DiscExportAsync(projectFolder);
            return false;
        }

        SetPipelineState(ProjectPipelineState.ReviewBeforeMerge);
        _projectReviewState = ProjectReviewState.WithManualMergeReview(true);
        _projectReviewState = ProjectReviewState.WithCompletion(false);
        ApplyLoadedProjectState(LoadedProjectState.WithDiscImportRequirement(false));
        ApplyLoadedProjectState(LoadedProjectState.WithMp3DiscState(true));
        PublishWorkflowStatus(
            workflowOperationId,
            _services.Status.Mp3DiscWorkflowStatusAdapter.CreateReadySnapshot(
                projectFolder,
                totalDiscs,
                Tracks.Count,
                livePreset));
        EndWorkflowStatusOperation(workflowOperationId);
        NotifyExportUiStateChanged();
        RefreshResumeProjects(showStatus: false);
        return false;
    }

    private async Task WaitForLiveMp3DiscPreparationAsync(
        Mp3DiscProjectManifest manifest,
        string projectFolder,
        List<Task> liveConversionTasks,
        object liveConversionTasksLock)
    {
        SaveCurrentMp3DiscProjectSnapshot(manifest);
        await LiveConversionWorkflowService.WaitForTasksAsync(
            liveConversionTasks,
            liveConversionTasksLock,
            () => SaveCurrentMp3DiscProjectSnapshot(manifest),
            ProjectSnapshotInterval);

        _services.Foundation.ConvertedFileCleanupService.DeletePartFiles(projectFolder);
    }

    private void ApplyMp3DiscUiStatus(Mp3DiscUiStatus status)
    {
        StatusText = status.StatusText;
        ExportProgressText = status.ExportProgressText;

        if (status.ExportProgressPercent.HasValue)
            ExportProgressPercent = status.ExportProgressPercent.Value;
    }

    private async Task RunFinalMp3DiscExportAsync(string projectFolder)
    {
        await RunCurrentExportPlanAsync(projectFolder, ProjectManifestTypes.Mp3DiscProject,
            manualMergeRequested: false);
    }

    private void RefreshExistingMp3DiscTrackStateBeforeImport(
        string projectFolder,
        string sourceFolder,
        int totalDiscs)
    {
        _services.Discs.Mp3DiscTrackReconciliationService.ReconcileImportedTrackPathsForExistingDiscs(
            Tracks,
            sourceFolder,
            projectFolder,
            totalDiscs);

        var changed = TrackStateUpdateQueueService.ApplyUpdates(new TrackStateUpdateContext(
            Tracks,
            projectFolder,
            SelectedExportPreset,
            IsWorkflowActive: false,
            IsRefreshSuspended: false));

        if (!changed)
            return;

        RefreshTrackPresentationViews();
        UpdateFinalStatus(Tracks.Count);
    }

    private async Task RefreshMp3DiscTrackListAfterDiscImportAsync(
        string projectFolder,
        string sourceFolder,
        Mp3DiscProjectManifest projectManifest,
        Guid performanceOperationId = default,
        string? currentImportedFolder = null)
    {
        var statusText = _statusText;
        var progressText = ExportProgressText;
        var excludedPaths = new List<string>();
        var previousTracksByPath = Tracks
            .Select(track => new
            {
                Track = track,
                Path = GetNormalizedTrackPath(projectFolder, track)
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.Path))
            .GroupBy(item => item.Path, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.First().Track,
                StringComparer.OrdinalIgnoreCase);

        SelectedFolder = sourceFolder;

        await LoadFolderAsync(
            projectFolder,
            metadataSourceFolder: sourceFolder,
            updateMetadata: false,
            removeGeneratedOrWorkFiles: false,
            trackNumberPreference: TrackNumberPreference.FileName,
            includeFile: path =>
            {
                var excluded = _services.Tracks.TrackWorkspaceFilterService.IsMp3DiscGeneratedPath(projectFolder, path);
                if (excluded)
                    excludedPaths.Add(path);
                return !excluded;
            },
            publishWorkflowStatus: false,
            performanceOperationId: performanceOperationId,
            captureMp3RefreshPerformance: performanceOperationId != Guid.Empty,
            reusableMp3ProbeTracksByPath: previousTracksByPath,
            currentMp3ImportedFolder: currentImportedFolder,
            prepareScannedTracksBeforeIndexUpdate: tracks =>
            {
                _services.Discs.Mp3DiscTrackReconciliationService.ApplyManifestDiscOrder(
                    tracks,
                    projectFolder,
                    projectManifest);
                var ordered = _services.Foundation.TrackListStateService.Apply(
                    projectFolder,
                    tracks,
                    TrackOrderProjectType.Mp3Disc,
                    TrackOrderConfirmedContextFactory.CreateMp3DiscContexts(tracks));
                tracks.Clear();
                tracks.AddRange(ordered);
            });

        _runtimeServices.DiagnosticLogService.WriteTrackScan(
            "MP3 project refresh after disc import",
            projectFolder,
            Tracks,
            excludedPaths);

        SelectedFolder = sourceFolder;
        StatusText = statusText;
        ExportProgressText = progressText;
    }

    private void UpdateMp3DiscImportContinuationState(int discNumber, int totalDiscs)
    {
        if (discNumber >= totalDiscs)
            return;

        ApplyLoadedProjectState(LoadedProjectState.WithDiscImportRequirement(true));
        NotifyExportUiStateChanged();
    }

    private async Task WarnIfMp3DiscEjectFailedAsync(string sourceFolder, int discNumber, int totalDiscs)
    {
        var ejected = await _services.Discs.DiscDriveService.TryEjectDiscAsync(sourceFolder);
        if (ejected)
            return;

        var message = LocalizationText.Get(discNumber < totalDiscs
            ? "MainWindow.Mp3Disc.EjectFailed.NextDiscMessage"
            : "MainWindow.Mp3Disc.EjectFailed.LastDiscMessage");

        AppDialogService.Warning(
            this,
            LocalizationText.Get("MainWindow.Mp3Disc.EjectFailed.Title"),
            message);
    }

    private void ShowMp3DiscAnalysisFailed(int discNumber, Exception ex)
    {
        AppDialogService.Error(
            this,
            LocalizationText.Format("Disc.Import.ReadFailedTitle", discNumber),
            LocalizationText.Get("Disc.Import.ReadFailedMessage"),
            details: new[] { ex.Message });
        ApplyMp3DiscUiStatus(_services.Discs.Mp3DiscUiStatusService.CreateAnalysisFailed(discNumber));
    }

    private void ShowAlreadyImportedMp3DiscWarning(int discNumber, int totalDiscs)
    {
        AppDialogService.Warning(
            this,
            LocalizationText.Format("Disc.Import.DuplicateTitle", discNumber),
            LocalizationText.Format("Disc.Import.DuplicateMessage", discNumber, totalDiscs));
        ApplyMp3DiscUiStatus(_services.Discs.Mp3DiscUiStatusService.CreateAlreadyImported(discNumber, totalDiscs));
    }

    private void MarkMp3DiscImportFailed(
        Mp3DiscProjectManifest manifest,
        string projectFolder,
        Exception ex)
    {
        TrySaveCurrentMp3DiscProjectSnapshot(manifest);
        _services.Foundation.ConvertedFileCleanupService.DeletePartFiles(projectFolder);

        var failureTransition = _services.Pipeline.PipelinePauseTransitionService.CreateFailure();
        ApplyPipelineTransition(failureTransition);
        NotifyExportUiStateChanged();

        _runtimeServices.NotificationService.Notify(NotificationEvent.Error);
        AppDialogService.Error(
            this,
            LocalizationText.Get("MainWindow.Mp3Disc.ImportFailed.Title"),
            LocalizationText.Get("MainWindow.Mp3Disc.ImportFailed.Message"),
            details: new[] { ex.Message });
        ApplyMp3DiscUiStatus(_services.Discs.Mp3DiscUiStatusService.CreateImportFailed());
    }

    private void QueueLiveConversionForCopiedFile(
        DiscCopiedFile copiedFile,
        string projectFolder,
        ExportWorkManifest workManifest,
        object liveWorkManifestLock,
        PreparedTrackLifecycleSession preparedLifecycle,
        ExportPreset livePreset,
        string liveConvertedFolder,
        int discNumber,
        int totalDiscs,
        LiveConversionQueueService liveConversionQueue,
        SemaphoreSlim liveConversionSemaphore,
        List<Task> liveConversionTasks,
        object liveConversionTasksLock,
        HashSet<string> extensionGeneratedConvertedFiles,
        object extensionGeneratedConvertedFilesLock,
        HashSet<int> activeConversionTrackNumbers,
        object activeConversionTrackNumbersLock,
        int trackNumberOffset,
        Action onSkipped,
        Action onCompleted,
        Action onFailed,
        Action onStatusChanged,
        CancellationToken token)
    {
        var liveTrack = _services.Discs.Mp3DiscPreparationService.BuildLiveConversionTrack(copiedFile);
        liveTrack.TargetSampleRateHz = ProjectSampleRateService.ReadTarget(projectFolder);
        PublishSourceMetadataSnapshot(
            projectFolder,
            copiedFile.ImportedFile,
            liveTrack,
            evidence: "mp3-disc-copy");
        var convertedPath = ConvertedTrackPathService.GetConvertedTrackPath(liveConvertedFolder, copiedFile.ImportedFile, liveTrack);

        lock (extensionGeneratedConvertedFilesLock)
        {
            extensionGeneratedConvertedFiles.Add(convertedPath);
        }

        QueueMp3DiscCopiedTrackUiUpdate(copiedFile);

        var queueItem = new LiveConversionQueueItem(
            copiedFile.ImportedFile,
            convertedPath,
            livePreset.DisplayName,
            copiedFile.DiscNumber,
            copiedFile.CopiedFiles);

        if (!liveConversionQueue.TryEnqueue(queueItem))
        {
            onSkipped();
            return;
        }

        if (!liveConversionQueue.TryDequeue(out var queuedItem))
            return;

        var trackNumber = trackNumberOffset + copiedFile.CopiedFiles;

        void MarkActiveTrack()
        {
            lock (activeConversionTrackNumbersLock)
                activeConversionTrackNumbers.Add(trackNumber);
            onStatusChanged();
        }

        void CompleteActiveTrack()
        {
            lock (activeConversionTrackNumbersLock)
                activeConversionTrackNumbers.Remove(trackNumber);
            onStatusChanged();
        }

        var conversionTask = _services.Workflows.LiveConversionWorkflowService.RunAsync(
            liveTrack,
            queuedItem,
            livePreset,
            liveConversionQueue,
            liveConversionSemaphore,
            () =>
            {
                preparedLifecycle.MarkStarted(trackNumber - 1, liveTrack, queuedItem.SourcePath, queuedItem.ConvertedPath, livePreset, token);
                MarkActiveTrack();
            },
            () =>
            {
                onCompleted();
                QueueMp3DiscConvertedTrackUiUpdate(copiedFile, convertedPath, livePreset.DisplayName);
            },
            onFailed,
            token,
            persistPreparedTrack: () => RegisterPreparedMp3Track(preparedLifecycle, trackNumber - 1, liveTrack,
                queuedItem.SourcePath, queuedItem.ConvertedPath, livePreset, token));

        _ = conversionTask.ContinueWith(
            _ => CompleteActiveTrack(),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        lock (liveConversionTasksLock)
        {
            liveConversionTasks.Add(conversionTask);
        }

    }

    private void QueueMp3DiscCopiedTrackUiUpdate(DiscCopiedFile copiedFile)
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            var track = _services.Discs.Mp3DiscTrackReconciliationService.FindTrackForCopiedFile(Tracks, copiedFile);
            if (track is null)
                return;

            track.FilePath = copiedFile.ImportedFile;
            track.FileName = Path.GetFileName(copiedFile.ImportedFile);
            track.RelativeFolder = _services.Discs.Mp3DiscTrackReconciliationService.GetImportedRelativeFolder(copiedFile);
            ApplySourceIdFromCopiedFile(track, copiedFile);

            if (File.Exists(copiedFile.ImportedFile))
            {
                var file = new FileInfo(copiedFile.ImportedFile);
                track.SourceSizeAvailable = true;
                track.SizeMb = Math.Round(file.Length / 1024d / 1024d, 2);
            }

        });
    }

    private void QueueMp3DiscConvertedTrackUiUpdate(
        DiscCopiedFile copiedFile,
        string convertedPath,
        string presetName)
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            var track = _services.Discs.Mp3DiscTrackReconciliationService.FindTrackForCopiedFile(Tracks, copiedFile);
            if (track is null || !File.Exists(convertedPath))
                return;

            var file = new FileInfo(convertedPath);
            if (file.Length <= 0)
                return;

            track.PreparedConvertedPath = convertedPath;
            track.PreparedConvertedPreset = presetName;
            track.HasReusableConvertedFile = true;
            track.ConvertedSizeAvailable = true;

        });
    }

    private async Task<bool> PrepareCompletedMp3DiscProjectForCurrentPresetAsync(
        string projectFolder,
        PerformanceExternalProcessContext? performanceContext = null)
    {
        if (string.IsNullOrWhiteSpace(projectFolder) || !Directory.Exists(projectFolder) || Tracks.Count == 0)
            return true;

        var wasBusy = IsBusy;
        IsBusy = true;

        try
        {
            await PrepareMissingMp3DiscConvertedTracksForCurrentPresetAsync(
                projectFolder,
                CancellationToken.None,
                maxParallelOverride: ResolveParallelJobCount(),
                updateProgress: true,
                performanceContext: performanceContext);

            return true;
        }
        catch (Exception ex)
        {
            AppDialogService.Error(
                this,
                LocalizationText.Get("MainWindow.Preset.PreparationFailed"),
                LocalizationText.Get("MainWindow.Preset.PreparationFailed.Message"),
                details: ExportFailureDetailsService.SplitMessageLines(ex.Message));
            StatusText = LocalizationText.Get("MainWindow.Preset.PreparationFailed");
            ExportProgressText = LocalizationText.Get("MainWindow.Preset.NotReady");
            return false;
        }
        finally
        {
            IsBusy = wasBusy;
        }
    }

    private void RegisterPreparedMp3Track(PreparedTrackLifecycleSession lifecycle,
        int index, TrackInfo track, string sourcePath, string convertedPath, ExportPreset preset, CancellationToken token)
    {
        lifecycle.Complete(index, track, sourcePath, convertedPath, preset, token);
    }

    private void StartBackgroundMp3DiscPresetPreparation(
        string projectFolder,
        ExportPreset preset,
        string convertedFolder,
        SemaphoreSlim sharedSemaphore,
        List<Task> taskList,
        object taskListLock,
        Action onCompleted,
        Action onFailed,
        CancellationToken token,
        Action<Mp3DiscPresetPreparationItem>? persistPreparedTrack,
        PreparedTrackLifecycleSession preparedLifecycle)
    {
        _services.Workflows.Mp3DiscPresetPreparationWorkflowService.StartBackgroundPreparation(
            Tracks,
            projectFolder,
            preset,
            convertedFolder,
            (sourcePath, convertedPath) => preparedLifecycle.CanReuse(sourcePath, convertedPath, preset),
            sharedSemaphore,
            taskList,
            taskListLock,
            () => Volatile.Read(ref _mp3DiscBackgroundPresetPreparationActive) == 1,
            Math.Clamp(ResolveParallelJobCount() / 2, 1, Math.Max(1, ResolveParallelJobCount())),
            onCompleted,
            onFailed,
            token,
            persistPreparedTrack,
            item => preparedLifecycle.TryMarkStarted(item.Index, item.Track, item.SourcePath, item.ConvertedPath, preset, token));
    }

    private async Task<int> PrepareMissingMp3DiscConvertedTracksForCurrentPresetAsync(
        string projectFolder,
        CancellationToken token,
        int? maxParallelOverride = null,
        bool updateProgress = false,
        PerformanceExternalProcessContext? performanceContext = null)
    {
        var preset = ExportPreset.Parse(SelectedExportPreset);
        var convertedFolder = ProjectFolderLayout.GetConvertedPresetFolder(projectFolder, preset.GetFolderName());
        Directory.CreateDirectory(convertedFolder);
        _services.Foundation.ConvertedFileCleanupService.DeleteConvertedPartFiles(convertedFolder);

        var manifest = _services.Foundation.WorkManifestService.LoadOrCreate(
            ProjectFolderLayout.ResolveExportManifestPath(projectFolder), ProjectManifestTypes.Mp3DiscProject,
            projectFolder, projectFolder, preset.DisplayName);
        var manifestLock = new object();
        await using var preparedLifecycle = new PreparedTrackLifecycleSession(
            _services.Foundation.WorkManifestService, _services.Workflows.AacExportProcessingService,
            manifest, ProjectFolderLayout.ResolveExportManifestPath(projectFolder), manifestLock,
            _ffmpegStatus.FfprobePath ?? "", Math.Clamp(maxParallelOverride ?? ResolveParallelJobCount(), 1, 40));
        preparedLifecycle.QueuePreset(preset, token);
        try
        {
            var prepared = await _services.Workflows.Mp3DiscPresetPreparationWorkflowService.PrepareMissingTracksAsync(
                Tracks,
                projectFolder,
                preset,
                convertedFolder,
                (sourcePath, convertedPath) => preparedLifecycle.CanReuse(sourcePath, convertedPath, preset),
                Math.Clamp(maxParallelOverride ?? ResolveParallelJobCount(), 1, 40),
                updateProgress
                    ? ReportMp3DiscPresetPreparationProgress
                    : null,
                token,
                performanceContext,
                item => RegisterPreparedMp3Track(preparedLifecycle,
                    item.Index, item.Track, item.SourcePath, item.ConvertedPath, preset, token),
                item => preparedLifecycle.TryMarkStarted(item.Index, item.Track, item.SourcePath, item.ConvertedPath, preset, token));
            await preparedLifecycle.DrainAsync();
            token.ThrowIfCancellationRequested();
            _services.Foundation.WorkManifestService.Save(
                ProjectFolderLayout.ResolveExportManifestPath(projectFolder), manifest);
            return prepared;
        }
        finally
        {
            await preparedLifecycle.DrainAsync();
            _services.Foundation.ConvertedFileCleanupService.DeleteConvertedPartFiles(convertedFolder);
        }
    }

    private void ReportMp3DiscPresetPreparationProgress(int completed, int total)
    {
        var operationId = _workflowStatusOperationId;
        var projectFolder = _currentProjectWorkFolder;
        Dispatcher.BeginInvoke(() =>
        {
            if (_presentationState.IsPipelinePaused || _presentationState.IsPipelineFailed ||
                operationId != _workflowStatusOperationId ||
                !TrackPathService.PathEquals(projectFolder, _currentProjectWorkFolder)) return;
            var percent = total <= 0 ? 100 : Math.Clamp(completed * 100.0 / total, 0, 100);
            ApplyWorkflowStatusViewState(new WorkflowStatusFormatter().Format(
                CreateExportStatusSnapshot(
                    WorkflowProjectKind.Mp3Disc,
                    _currentProjectWorkFolder,
                    total,
                    completed,
                    Array.Empty<int>(),
                    ExportPreset.Parse(SelectedExportPreset),
                    (int)Math.Round(percent))));
        });
    }

    private async Task<DiscWaitDialogOutcome> WaitForNextDiscAsync(
        string sourceFolder,
        int discNumber,
        int totalDiscs,
        HashSet<string> importedDiscSignatures,
        CancellationToken token,
        Action<string>? acceptedSourceFolderChanged = null,
        bool startWithSelectedDrive = false,
        bool previewOnly = false)
    {
        // A chosen entry drive still needs the same type/duplicate gate as later discs.
        // Accept a valid disc immediately; otherwise keep the regular polling dialog.
        if (startWithSelectedDrive && !_settings.ExperimentalDriveRoundEnabled)
        {
            try
            {
                var initialCheck = await _services.Workflows.Mp3DiscPollingService.CheckDiscSourceForNextImportAsync(
                    sourceFolder, discNumber, totalDiscs, importedDiscSignatures, token);
                token.ThrowIfCancellationRequested();
                if (initialCheck.CanImport)
                    return DiscWaitDialogOutcome.Ready;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                _runtimeServices.DiagnosticLogService.WriteError("Initial MP3 disc polling failed; continuing in wait dialog", ex);
            }
        }

        if (await TrySelectNextMp3DiscFromDriveRoundAsync(
                sourceFolder,
                discNumber,
                totalDiscs,
                importedDiscSignatures,
                token,
                acceptedSourceFolderChanged,
                startWithSelectedDrive))
        {
            return DiscWaitDialogOutcome.Ready;
        }

        if (_settings.ExperimentalDriveRoundEnabled)
        {
            NotifyDiscChangeRequired(
                "MP3-CD",
                source: "Mp3DiscWait",
                reason: "DriveRoundWaitDialog");

            return await WaitForNextMp3DiscWithDriveRoundDialogAsync(
                sourceFolder,
                discNumber,
                totalDiscs,
                importedDiscSignatures,
                token,
                acceptedSourceFolderChanged,
                startWithSelectedDrive, previewOnly);
        }

        NotifyDiscChangeRequired(
            "MP3-CD",
            source: "Mp3DiscWait",
            reason: "WaitDialog");

        return await _services.Workflows.Mp3DiscWaitDialogService.WaitForNextDiscAsync(
            this,
            sourceFolder,
            discNumber,
            totalDiscs,
            importedDiscSignatures,
            _ => { },
            _ => { },
            token,
            NotifyDiscPollingState,
            driveDisplayName: CreateDiscDriveDisplayName(sourceFolder),
            canCompleteImport: !previewOnly);
    }

    private async Task<DiscWaitDialogOutcome> WaitForNextMp3DiscWithDriveRoundDialogAsync(
        string sourceFolder,
        int discNumber,
        int totalDiscs,
        HashSet<string> importedDiscSignatures,
        CancellationToken token,
        Action<string>? acceptedSourceFolderChanged,
        bool startWithSelectedDrive = false,
        bool previewOnly = false)
    {
        using var statusAnimation = BeginDriveCircleStatusAnimation(token);
        string? acceptedSourceFolder = null;
        var request = new DiscWaitDialogRequest(
            discNumber,
            totalDiscs,
            LocalizationText.Get("Disc.Media.Mp3"),
            LocalizationText.Format("Disc.DriveRound.InsertInstruction", LocalizationText.Get("Disc.Media.Mp3"), discNumber, totalDiscs),
            LocalizationText.Get("Disc.DriveRound.Mp3DuplicateHint"),
            CreateDiscDriveDisplayName(sourceFolder),
            NotifyDiscPollingState,
            CanCompleteImport: !previewOnly && discNumber > 1,
            IsDriveCircle: _settings.ExperimentalDriveRoundEnabled);

        var accepted = await _services.Workflows.DiscWaitDialogService.WaitForDiscAsync(
            this,
            request,
            async (checkRequest, cancellationToken) =>
            {
                var result = await CheckMp3DriveRoundForWaitDialogAsync(
                    sourceFolder,
                    discNumber,
                    totalDiscs,
                    importedDiscSignatures,
                    checkRequest.AcceptNextDuplicate,
                    cancellationToken,
                    startWithSelectedDrive);
                if (!string.IsNullOrWhiteSpace(result.AcceptedSourceFolder))
                    acceptedSourceFolder = result.AcceptedSourceFolder;
                return result.PollingResult;
            },
            _ => { },
            _ => { },
            token);

        if (accepted is DiscWaitDialogOutcome.Ready or DiscWaitDialogOutcome.ReadyWithDuplicateOverride &&
            !string.IsNullOrWhiteSpace(acceptedSourceFolder))
        {
            SelectedFolder = acceptedSourceFolder;
            acceptedSourceFolderChanged?.Invoke(acceptedSourceFolder);
        }

        return accepted;
    }

    private sealed record Mp3DriveRoundWaitResult(
        DiscPollingResult PollingResult,
        string? AcceptedSourceFolder = null);

    private async Task<Mp3DriveRoundWaitResult> CheckMp3DriveRoundForWaitDialogAsync(
        string lastProcessedSourceFolder,
        int discNumber,
        int totalDiscs,
        HashSet<string> importedDiscSignatures,
        bool acceptDuplicate,
        CancellationToken token,
        bool startWithSelectedDrive = false)
    {
        var round = await BuildDriveRoundAsync(lastProcessedSourceFolder, token, startWithSelectedDrive);
        if (round.Count == 0)
        {
            return new Mp3DriveRoundWaitResult(new DiscPollingResult(
                false,
                LocalizationText.Format("Disc.DriveRound.NoDrive.Dialog", LocalizationText.Get("Disc.Media.Mp3"), discNumber, totalDiscs),
                LocalizationText.Format("Disc.DriveRound.NoDrive.Status", LocalizationText.Get("Disc.Media.Mp3"), discNumber),
                LocalizationText.Get("Disc.DriveRound.NoDrive.Progress")));
        }

        DiscPollingResult? lastProblem = null;
        foreach (var driveRoot in round)
        {
            token.ThrowIfCancellationRequested();
            var driveLetter = FormatDriveLetter(driveRoot);
            ExportProgressText = LocalizationText.Format("Disc.DriveRound.Checking.Progress", driveLetter);

            var typeProbe = await _services.Workflows.DiscDriveCandidateProbeService
                .ProbeTypeAsync(driveRoot, DiscMediaKind.Mp3Disc, token);
            if (!typeProbe.IsAccepted)
            {
                WriteDriveRoundSkip("MP3-CD", discNumber, typeProbe);
                var candidateNotice = CreateDriveRoundWaitingResult(
                    discNumber,
                    totalDiscs,
                    LocalizationText.Get("Disc.Media.Mp3"),
                    driveLetter,
                    typeProbe);
                lastProblem = DiscDriveRoundWaitPresentation.KeepActionable(lastProblem, candidateNotice,
                    typeProbe.Status == DiscDriveCandidateStatus.Error);
                continue;
            }


            Mp3DiscImportPrecheckResult precheck;
            try
            {
                precheck = await Task.Run(
                    () => _services.Workflows.Mp3DiscImportWorkflowService.AnalyzeDiscForImport(typeProbe.DriveRoot, importedDiscSignatures, token),
                    token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                _runtimeServices.DiagnosticLogService.WriteError($"DriveCircle MP3-CD {discNumber}: {driveRoot} konnte nicht geprüft werden", ex);
                lastProblem = new DiscPollingResult(
                    false,
                    LocalizationText.Format("Disc.DriveRound.Unreadable.Dialog", driveLetter),
                    LocalizationText.Format("Disc.DriveRound.Unreadable.Status", LocalizationText.Get("Disc.Media.Mp3"), discNumber, driveLetter),
                    LocalizationText.Format("Disc.DriveRound.Unreadable.Progress", driveLetter));
                continue;
            }

            if (precheck.IsAlreadyImported && !acceptDuplicate)
            {
                var duplicate = _services.Workflows.DiscDriveCandidateProbeService.MarkDuplicate(typeProbe.DriveRoot, typeProbe.MediaKind);
                WriteDriveRoundSkip("MP3-CD", discNumber, duplicate);
                await _services.Discs.DiscDriveService.TryEjectDiscAsync(typeProbe.DriveRoot, token);
                lastProblem = new DiscPollingResult(
                    false,
                    LocalizationText.Format("Disc.DriveRound.Duplicate.Dialog", driveLetter),
                    LocalizationText.Format("Disc.DriveRound.Duplicate.Status", discNumber, driveLetter),
                    LocalizationText.Format("Disc.DriveRound.Duplicate.Progress", driveLetter),
                    DiscPollingDisplayState.Duplicate);
                continue;
            }

            if (precheck.TotalFiles <= 0)
            {
                var noFiles = new DiscDriveCandidateResult(
                    DiscDriveCandidateStatus.WrongType,
                    typeProbe.DriveRoot,
                    typeProbe.MediaKind,
                    "Keine unterstützten Audiodateien gefunden.");
                WriteDriveRoundSkip("MP3-CD", discNumber, noFiles);
                continue;
            }

            _runtimeServices.DiagnosticLogService.WriteApplicationEvent(
                "DRIVECIRCLE",
                $"MP3-CD {discNumber}/{totalDiscs}: {typeProbe.DriveRoot} akzeptiert.");
            return new Mp3DriveRoundWaitResult(
                new DiscPollingResult(
                    true,
                    LocalizationText.Format("Disc.DriveRound.Mp3Ready.Dialog", driveLetter, discNumber, totalDiscs),
                    LocalizationText.Format("Disc.DriveRound.Mp3Ready.Status", discNumber, totalDiscs, driveLetter),
                    LocalizationText.Format("Disc.DriveRound.Mp3Ready.Progress", driveLetter),
                    DiscPollingDisplayState.Ready),
                typeProbe.DriveRoot);
        }

        return new Mp3DriveRoundWaitResult(DiscDriveRoundWaitPresentation.Complete(
            LocalizationText.Get("Disc.Media.Mp3"), discNumber, totalDiscs, lastProblem));
    }

    private async Task<bool> TrySelectNextMp3DiscFromDriveRoundAsync(
        string lastProcessedSourceFolder,
        int discNumber,
        int totalDiscs,
        HashSet<string> importedDiscSignatures,
        CancellationToken token,
        Action<string>? acceptedSourceFolderChanged,
        bool startWithSelectedDrive = false)
    {
        if (!_settings.ExperimentalDriveRoundEnabled)
            return false;


        var round = await BuildDriveRoundAsync(lastProcessedSourceFolder, token, startWithSelectedDrive);
        if (round.Count == 0)
            return false;

        _runtimeServices.DiagnosticLogService.WriteApplicationEvent(
            "DRIVECIRCLE",
            $"MP3-CD {discNumber}/{totalDiscs}: prüfe {string.Join(", ", round)} nach {lastProcessedSourceFolder}.");

        foreach (var driveRoot in round)
        {
            token.ThrowIfCancellationRequested();

            var typeProbe = await _services.Workflows.DiscDriveCandidateProbeService
                .ProbeTypeAsync(driveRoot, DiscMediaKind.Mp3Disc, token);
            if (!typeProbe.IsAccepted)
            {
                WriteDriveRoundSkip("MP3-CD", discNumber, typeProbe);
                continue;
            }

            Mp3DiscImportPrecheckResult precheck;
            try
            {
                precheck = await Task.Run(
                    () => _services.Workflows.Mp3DiscImportWorkflowService.AnalyzeDiscForImport(typeProbe.DriveRoot, importedDiscSignatures, token),
                    token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                _runtimeServices.DiagnosticLogService.WriteError($"DriveCircle MP3-CD {discNumber}: {driveRoot} konnte nicht geprüft werden", ex);
                continue;
            }

            if (precheck.IsAlreadyImported)
            {
                var duplicate = _services.Workflows.DiscDriveCandidateProbeService.MarkDuplicate(typeProbe.DriveRoot, typeProbe.MediaKind);
                WriteDriveRoundSkip("MP3-CD", discNumber, duplicate);
                await _services.Discs.DiscDriveService.TryEjectDiscAsync(typeProbe.DriveRoot, token);
                continue;
            }

            if (precheck.TotalFiles <= 0)
            {
                WriteDriveRoundSkip(
                    "MP3-CD",
                    discNumber,
                    new DiscDriveCandidateResult(
                        DiscDriveCandidateStatus.WrongType,
                        typeProbe.DriveRoot,
                        typeProbe.MediaKind,
                        "Keine unterstützten Audiodateien gefunden."));
                continue;
            }

            acceptedSourceFolderChanged?.Invoke(typeProbe.DriveRoot);
            SelectedFolder = typeProbe.DriveRoot;
            StatusText = LocalizationText.Format("Disc.DriveRound.Accepted.Status", LocalizationText.Get("Disc.Media.Mp3"), discNumber, totalDiscs, FormatDriveLetter(typeProbe.DriveRoot));
            ExportProgressText = LocalizationText.Get("Disc.DriveRound.ImportContinues.Progress");
            _runtimeServices.DiagnosticLogService.WriteApplicationEvent(
                "DRIVECIRCLE",
                $"MP3-CD {discNumber}/{totalDiscs}: {typeProbe.DriveRoot} akzeptiert.");
            return true;
        }

        _runtimeServices.DiagnosticLogService.WriteApplicationEvent(
            "DRIVECIRCLE",
            $"MP3-CD {discNumber}/{totalDiscs}: keine passende neue Disc gefunden.");
        return false;
    }

    private static DiscPollingResult CreateDriveRoundWaitingResult(
        int discNumber,
        int totalDiscs,
        string mediaName,
        string driveLetter,
        DiscDriveCandidateResult result)
    {
        var message = result.Status switch
        {
            DiscDriveCandidateStatus.Error => result.Message,
            DiscDriveCandidateStatus.Unavailable => LocalizationText.Format("Disc.DriveRound.Unavailable.Message", driveLetter),
            DiscDriveCandidateStatus.Empty => LocalizationText.Format("Disc.DriveRound.Empty.Message", driveLetter),
            DiscDriveCandidateStatus.WrongType => LocalizationText.Format("Disc.DriveRound.WrongType.Message", driveLetter),
            DiscDriveCandidateStatus.Duplicate => LocalizationText.Format("Disc.DriveRound.Duplicate.Message", driveLetter),
            _ => LocalizationText.Format("Disc.DriveRound.Skipped.Message", driveLetter)
        };

        var displayState = result.Status == DiscDriveCandidateStatus.Duplicate
            ? DiscPollingDisplayState.Duplicate
            : result.Status == DiscDriveCandidateStatus.WrongType
                ? DiscPollingDisplayState.Unsupported
                : DiscPollingDisplayState.Waiting;

        return new DiscPollingResult(
            false,
            LocalizationText.Format("Disc.DriveRound.Problem.Dialog", message, mediaName, discNumber, totalDiscs),
            LocalizationText.Format("Disc.DriveRound.Problem.Status", mediaName, discNumber, message),
            LocalizationText.Format("Disc.DriveRound.Problem.Progress", driveLetter, message),
            displayState);
    }

    private async Task<IReadOnlyList<string>> BuildDriveRoundAsync(
        string lastProcessedSourceFolder,
        CancellationToken cancellationToken = default,
        bool startWithSelectedDrive = false)
    {
        IReadOnlyList<DiscDriveInfo> detectedDrives;
        try
        {
            detectedDrives = await _services.Discs.DiscDriveService
                .GetCdDrivesAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            detectedDrives = [];
        }

        return await Dispatcher.InvokeAsync(() =>
        {
            var configured = _services.Discs.DiscDriveConfigurationService.Synchronize(_settings, detectedDrives);
            SaveSettingsIfReady();
            return _services.Discs.DiscDriveRotationService.BuildRound(configured, lastProcessedSourceFolder, startWithSelectedDrive);
        });
    }

    private void WriteDriveRoundSkip(string projectKind, int discNumber, DiscDriveCandidateResult result)
    {
        _runtimeServices.DiagnosticLogService.WriteApplicationEvent(
            "DRIVECIRCLE",
            $"{projectKind} {discNumber}: {result.DriveRoot} übersprungen ({result.Status}, {result.MediaKind}) {result.Message}".Trim());
    }

    private void NotifyDiscChangeRequired(string projectType, string source, string reason)
    {
        _runtimeServices.NotificationService.Notify(new NotificationEventContext(
            NotificationEvent.DiscChangeRequired,
            ProjectType: projectType,
            RequiresUserInteraction: true,
            IsBackgroundOperation: true,
            Source: source,
            Reason: reason,
            CircleDriveSearchActive: _settings.ExperimentalDriveRoundEnabled,
            CircleDriveFoundNextDisc: false));
    }

    private string FormatDriveLetter(string driveRoot) =>
        DiscDriveSelectionPresentationService.FormatStatusDriveLabel(driveRoot, _settings.DiscDriveOrder);


    private void AppendDiscPreviewTracks(
        string sourceFolder,
        int discNumber,
        bool clearExistingTracks,
        bool refreshChapterProjection = true)
    {
        var sourceTracks = _services.Media.FolderScanner.Scan(sourceFolder, TrackNumberPreference.FileName, cancellationToken: DiscDriveOperationService.CurrentCancellationToken);
        AppendDiscPreviewTracks(
            sourceTracks,
            sourceFolder,
            discNumber,
            clearExistingTracks,
            refreshChapterProjection);
    }

    private void AppendDiscPreviewTracks(
        IEnumerable<TrackInfo> sourceTracks,
        int discNumber,
        bool clearExistingTracks,
        bool refreshChapterProjection = true)
    {
        AppendDiscPreviewTracks(
            sourceTracks,
            string.Empty,
            discNumber,
            clearExistingTracks,
            refreshChapterProjection);
    }

    private void AppendDiscPreviewTracks(
        IEnumerable<TrackInfo> sourceTracks,
        string sourceFolder,
        int discNumber,
        bool clearExistingTracks,
        bool refreshChapterProjection = true)
    {
        var reconciled = Tracks.ToList();
        _services.Discs.Mp3DiscTrackReconciliationService.AppendMissingPreviewTracks(
            reconciled,
            sourceTracks,
            sourceFolder,
            discNumber,
            clearExistingTracks);
        Tracks.ReplaceAll(reconciled);

        new TrackAutomaticOrderApplicationService().CalculateAndApply(
            Tracks.ToList(),
            TrackOrderProjectType.Mp3Disc,
            TrackOrderConfirmedContextFactory.CreateMp3DiscContexts(Tracks));

        if (refreshChapterProjection)
        {
            UpdateIndexes();
            return;
        }

        RefreshMp3DiscPreviewPresentationWithoutChapterProjection();
    }

    private void RefreshMp3DiscPreviewPresentationWithoutChapterProjection()
    {
        _services.Tracks.TrackListWarningService.Apply(Tracks);
        RefreshTrackPresentationViews();
        OnExportPreviewChanged();
    }

    private void ShowUnsupportedDiscSourceMessage(DiscSourceAnalysis analysis)
    {
        if (analysis.IsProbablyAudioCd)
        {
            AppDialogService.Warning(
                this,
                LocalizationText.Get("MainWindow.DiscSource.AudioUnreadable.Title"),
                LocalizationText.Get("MainWindow.DiscSource.AudioUnreadable.Message"));
            return;
        }

        AppDialogService.Warning(
            this,
            LocalizationText.Get("MainWindow.DiscSource.NoDataDisc.Title"),
            LocalizationText.Format("MainWindow.DiscSource.NoDataDisc.Message", AudioInputFormatCatalog.SupportedInputDisplayText));
    }

    private void ResetMetadataForNewFolderProject()
    {
        ResetMetadataForNewDiscProject();
    }

    private void ResetMetadataForNewDiscProject()
    {
        _resumeDiscPreview.Clear();
        _mp3DiscChapterSourceCatalog.Clear();
        _transientMp3DiscChapterPreview = null;
        _replaceableGenreSeed = "";

        _currentProjectWorkFolder = "";
        _currentFolderPath = "";
        _pendingDiscProjectSourceFolder = "";
        _pendingAudioDisc = null;
        ApplyLoadedProjectState(LoadedProjectStateSnapshot.None);
        _isAudioDiscProjectAwaitingRip = false;
        OnPropertyChanged(nameof(CanStartExport));
        OnPropertyChanged(nameof(ExportButtonText));
        OnPropertyChanged(nameof(ExportButtonVisibility));

        // Every new source starts with a fresh canonical ProjectMetadata session.
        // Even a completely untagged source gets an empty revision-0 metadata model;
        // cross-project exceptions are explicitly locked global metadata preferences.
        _projectMetadataSession.Discard();
        _projectMetadata.Clear("");
        CoverPreviewSource = CoverPreviewPresentationService.GetPlaceholderSource(
            AppearanceSettingsService.GetEffectiveMode(_settings));
        var canonicalMetadata = _projectMetadata.EnsureCanonicalMetadata(MetadataOrigin.None);
        _projectMetadataSession.BeginNew(canonicalMetadata);
        ApplyLockedGenrePreferencesToMetadataState();
        ApplyLockedTrackPreferencesToMetadataState();
        _services.Media.ProjectMetadataPreviewAggregationService.Apply(
            _projectMetadata, [], CreateMp3DiscProjectAggregationOptions(),
            ProjectMetadataInitializationState.InitialSetOpen);
        _developmentMetadataDialog?.NotifyProjectChanged();
        _chapterGridColumnLayoutState.MarkAutoFitPending();

        _lastObservedProjectMetadataBookTitle = "";
        OnPropertyChanged(nameof(Series));
        OnExportPreviewChanged();
    }

    private void InitializeGenreStateForEmptySession()
    {
        _replaceableGenreSeed = "";
        _projectMetadata.Genre = "";
        ApplyLockedGenrePreferencesToMetadataState();
        ApplyLockedTrackPreferencesToMetadataState();
        _services.Media.ProjectMetadataPreviewAggregationService.Apply(
            _projectMetadata, [], CreateMp3DiscProjectAggregationOptions(),
            ProjectMetadataInitializationState.InitialSetOpen);
    }

    private void ApplyLockedGenrePreferencesToMetadataState()
    {
        if (!_settings.MetadataPreferences.GenrePreferenceLocked)
            return;

        var preferred = _settings.MetadataPreferences.PreferredGenres
            .Where(GenreListService.IsSelectableGenre)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (preferred.Length == 0)
            return;

        var metadata = _projectMetadata.EnsureCanonicalMetadata(MetadataOrigin.GlobalPreference);
        metadata.SetValue<IReadOnlyList<string>>(
            ProjectMetadataField.Genres,
            preferred,
            MetadataOrigin.GlobalPreference);
        _projectMetadata.ApplyCanonicalMetadata(metadata);
    }

    private void ApplyLockedTrackPreferencesToMetadataState()
    {
        var preferences = _settings.MetadataPreferences;
        if (!preferences.OutputTrackNumberPreferenceLocked &&
            !preferences.OutputTrackTotalPreferenceLocked)
        {
            return;
        }

        var metadata = _projectMetadata.EnsureCanonicalMetadata(MetadataOrigin.GlobalPreference);
        if (preferences.OutputTrackNumberPreferenceLocked &&
            preferences.PreferredOutputTrackNumber is >= 0)
        {
            metadata.SetValue<int?>(
                ProjectMetadataField.OutputTrackNumber,
                preferences.PreferredOutputTrackNumber,
                MetadataOrigin.GlobalPreference);
        }

        if (preferences.OutputTrackTotalPreferenceLocked &&
            preferences.PreferredOutputTrackTotal is >= 0)
        {
            metadata.SetValue<int?>(
                ProjectMetadataField.OutputTrackTotal,
                preferences.PreferredOutputTrackTotal,
                MetadataOrigin.GlobalPreference);
        }

        _projectMetadata.ApplyCanonicalMetadata(metadata);
    }


    private void Cover_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        ChooseCover(this);
    }

    private void ChooseCover(Window owner)
    {
        if (!CanChangeBookMetadata || _isCoverDialogOpen)
            return;

        _isCoverDialogOpen = true;
        try
        {
            var sourceKind = LoadedProjectState.IsMp3Disc || LoadedProjectState.IsAudioDisc || _isAudioDiscProjectAwaitingRip
                ? ProjectSetupSourceKind.Mp3Disc
                : ProjectSetupSourceKind.Folder;
            var selectedFile = _coverPickerService.ChooseCoverFile(
                owner,
                new CoverPickerContext(
                    sourceKind,
                    _currentFolderPath,
                    _projectMetadata.CoverSourcePath,
                    _settings.LastCoverFolder));
            if (string.IsNullOrWhiteSpace(selectedFile))
                return;

            SetCoverFromFile(selectedFile, owner);
        }
        finally
        {
            Dispatcher.BeginInvoke(
                DispatcherPriority.ApplicationIdle,
                new Action(() => _isCoverDialogOpen = false));
        }
    }

    private void RemoveCover_Click(object sender, RoutedEventArgs e)
    {
        RemoveCover();
    }

    private void RemoveCover()
    {
        if (!CanChangeBookMetadata || string.IsNullOrWhiteSpace(_projectMetadata.ProcessedCoverPath))
            return;

        var sourcePath = _projectMetadata.CoverSourcePath;
        if (!string.IsNullOrWhiteSpace(sourcePath) && File.Exists(sourcePath))
        {
            try
            {
                EmbeddedCoverService.RecordUserRemoval(_projectMetadata.CanonicalMetadata, sourcePath);
            }
            catch (Exception ex)
            {
                _runtimeServices.DiagnosticLogService.WriteError("Cover-Löschung konnte nicht gespeichert werden", ex);
                AppDialogService.Error(this, LocalizationText.Get("Disc.Setup.CoverFailed.Title"),
                    LocalizationText.Get("Disc.Setup.CoverFailed.Message"), new[] { ex.Message });
                return;
            }
        }
        _projectMetadata.ApplyCoverMetadata("", "", MetadataOrigin.User);
        CoverPreviewSource = "";
        StatusText = LocalizationText.Get("MainWindow.Cover.Removed");
        TryFlushCanonicalMetadataSession();
        OnExportPreviewChanged();
    }

    private void Cover_DragOver(object sender, DragEventArgs e)
    {
        if (!CanChangeBookMetadata || !TryGetFirstDroppedCoverFile(e, out _))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        e.Effects = DragDropEffects.Copy;
        e.Handled = true;
    }

    private void Cover_Drop(object sender, DragEventArgs e)
    {
        if (!CanChangeBookMetadata)
            return;

        if (!TryGetFirstDroppedCoverFile(e, out var filePath))
        {
            AppDialogService.Warning(
                this,
                LocalizationText.Get("MainWindow.Cover.NotAccepted.Title"),
                LocalizationText.Get("MainWindow.Cover.DropUnsupported.Message"));
            return;
        }

        SetCoverFromFile(filePath);
    }

    private bool TryGetFirstDroppedCoverFile(DragEventArgs e, out string filePath)
    {
        filePath = "";

        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
            return false;

        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files)
            return false;

        filePath = files.FirstOrDefault(IsSupportedCoverFile) ?? "";
        return !string.IsNullOrWhiteSpace(filePath);
    }

    private static bool IsSupportedCoverFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            return false;

        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        return extension is ".jpg" or ".jpeg" or ".png" or ".webp";
    }

    private string GetCoverSourcePathForProjectSetup()
    {
        return IsInternalBookStitchPath(_projectMetadata.CoverSourcePath)
            ? string.Empty
            : _projectMetadata.CoverSourcePath;
    }

    private bool IsValidExternalCoverFolder(string? folderPath)
    {
        return !string.IsNullOrWhiteSpace(folderPath) &&
            Directory.Exists(folderPath) &&
            !IsInternalBookStitchPath(folderPath) &&
            !IsOpticalDriveSelection(folderPath);
    }

    private bool IsInternalBookStitchPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        try
        {
            var root = Path.GetFullPath(GetWorkingRootFolder())
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var candidate = Path.GetFullPath(path)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var rootPrefix = root + Path.DirectorySeparatorChar;

            return string.Equals(candidate, root, StringComparison.OrdinalIgnoreCase) ||
                candidate.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private void TryApplyFirstEmbeddedCover(IEnumerable<TrackInfo> tracks)
    {
        TryApplyEmbeddedCoverPath(TryExtractFirstEmbeddedCoverPath(tracks));
    }

    private string? TryExtractFirstEmbeddedCoverPath(IEnumerable<TrackInfo> tracks)
    {
        if (!string.IsNullOrWhiteSpace(_projectMetadata.ProcessedCoverPath) && File.Exists(_projectMetadata.ProcessedCoverPath))
            return null;

        try
        {
            var coverFolder = GetProjectFolderStructure().CoversFolder;
            return _services.Media.EmbeddedCoverService.ExtractFirstValidCover(
                BuildEmbeddedCoverCandidates(tracks),
                coverFolder,
                _projectMetadata.CanonicalMetadata.GetSuppressedValues(ProjectMetadataField.Cover));
        }
        catch (Exception ex)
        {
            _runtimeServices.DiagnosticLogService.WriteError("Eingebettetes Cover konnte nicht gelesen werden", ex);
            return null;
        }
    }

    private IReadOnlyList<EmbeddedCoverExtractionCandidate> BuildEmbeddedCoverCandidates(IEnumerable<TrackInfo> tracks)
    {
        var trackList = tracks.ToList();
        var snapshotsBySourceId = TryLoadCoverEvidenceSnapshots();
        if (snapshotsBySourceId.Count == 0)
        {
            return trackList
                .Select(track => new EmbeddedCoverExtractionCandidate(track.FilePath))
                .ToArray();
        }

        var candidates = new List<EmbeddedCoverExtractionCandidate>();
        var hasEvidenceCandidate = false;
        foreach (var track in trackList)
        {
            snapshotsBySourceId.TryGetValue(track.SourceId, out var snapshot);
            var coverCandidate = snapshot?.CoverCandidate;
            var hasEvidence = coverCandidate?.HasEmbeddedCover == true;
            hasEvidenceCandidate |= hasEvidence;
            candidates.Add(new EmbeddedCoverExtractionCandidate(
                track.FilePath,
                hasEvidence,
                coverCandidate?.Fingerprint ?? string.Empty));
        }

        return hasEvidenceCandidate
            ? candidates.Where(candidate => candidate.HasCoverEvidence).ToArray()
            : trackList.Select(track => new EmbeddedCoverExtractionCandidate(track.FilePath)).ToArray();
    }

    private IReadOnlyDictionary<Guid, SourceMetadataSnapshot> TryLoadCoverEvidenceSnapshots()
    {
        if (string.IsNullOrWhiteSpace(_currentProjectWorkFolder) ||
            !Directory.Exists(_currentProjectWorkFolder))
        {
            return new Dictionary<Guid, SourceMetadataSnapshot>();
        }

        try
        {
            return _services.Media.SourceMetadataSnapshotStore
                .LoadAll(_currentProjectWorkFolder)
                .Where(snapshot => snapshot.SourceId != Guid.Empty)
                .ToDictionary(snapshot => snapshot.SourceId);
        }
        catch (Exception ex)
        {
            _runtimeServices.DiagnosticLogService.WriteError("Cover-Evidence aus SourceSnapshots konnte nicht gelesen werden", ex);
            return new Dictionary<Guid, SourceMetadataSnapshot>();
        }
    }

    private void TryApplyEmbeddedCoverPath(string? embeddedCoverPath)
    {
        if (string.IsNullOrWhiteSpace(embeddedCoverPath))
            return;

        try
        {
            SetCoverFromFile(embeddedCoverPath);
        }
        catch (Exception ex)
        {
            _runtimeServices.DiagnosticLogService.WriteError("Eingebettetes Cover konnte nicht übernommen werden", ex);
        }
    }

    private void SetCoverWarningThresholdPixels(int threshold)
    {
        _settings.CoverWarningThresholdPixels = CoverWarningSettingsService.NormalizeThresholdPixels(threshold);
        SaveSettingsIfReady();
        _developmentMetadataDialog?.RefreshCoverWarningThresholdFromSharedState();
    }

    private void SetCoverFromFile(string filePath, Window? owner = null)
    {
        if (!IsSupportedCoverFile(filePath))
        {
            AppDialogService.Warning(
                this,
                LocalizationText.Get("MainWindow.Cover.NotAccepted.Title"),
                LocalizationText.Get("MainWindow.Cover.FormatUnsupported.Message"));
            return;
        }

        try
        {
            var coverFolder = GetProjectFolderStructure().CoversFolder;
            var result = _services.Media.CoverImageService.CreateProcessedCover(
                filePath,
                coverFolder,
                BookTitle,
                _settings.CoverWarningThresholdPixels);

            EmbeddedCoverService.RecordUserSelection(_projectMetadata.CanonicalMetadata, result.SourcePath);
            _projectMetadata.ApplyCoverMetadata(result.SourcePath, result.ProcessedJpegPath, MetadataOrigin.User);
            CoverPreviewSource = result.ProcessedJpegPath;
            TryFlushCanonicalMetadataSession();

            var selectedCoverFolder = Path.GetDirectoryName(result.SourcePath);
            if (IsValidExternalCoverFolder(selectedCoverFolder))
            {
                _settings.LastCoverFolder = selectedCoverFolder;
                SaveSettingsIfReady();
            }

            if (!string.IsNullOrWhiteSpace(result.Warning))
            {
                AppDialogService.CoverWarning(
                    owner ?? this,
                    LocalizationText.Get("MainWindow.Cover.Accepted"),
                    result.Warning,
                    _settings.CoverWarningThresholdPixels,
                    SetCoverWarningThresholdPixels);
            }
            else
            {
                StatusText = LocalizationText.Get("MainWindow.Cover.Prepared");
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

    private void MetadataHeader_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (IsWithinMetadataActionHost(e.OriginalSource as DependencyObject))
        {
            e.Handled = true;
            return;
        }

        SetMetadataPanelExpanded(!_isMetadataPanelExpanded, animate: true);
        e.Handled = true;
    }

    private bool IsWithinMetadataActionHost(DependencyObject? source)
    {
        var current = source;
        while (current is not null)
        {
            if (ReferenceEquals(current, MetadataActionHost))
                return true;

            current = current switch
            {
                Visual or Visual3D => VisualTreeHelper.GetParent(current),
                FrameworkContentElement contentElement => contentElement.Parent,
                _ => null
            };
        }

        return false;
    }


    private bool IsOpticalDriveSelection(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        var root = DiscDriveConfigurationService.NormalizeRootPath(path);
        if (string.IsNullOrWhiteSpace(root))
            return false;

        return (_settings.DiscDriveOrder ?? []).Any(item => string.Equals(
            DiscDriveConfigurationService.NormalizeRootPath(item.DriveRoot),
            root,
            StringComparison.OrdinalIgnoreCase));
    }

    private void SetMetadataPanelExpanded(bool expanded, bool animate)
    {
        _isMetadataPanelExpanded = expanded;
        OnPropertyChanged(nameof(MetadataToggleGlyph));

        if (MetadataDetailsBorder is null)
            return;

        var padding = MetadataDetailsBorder.Padding;
        MetadataDetailsContent.Measure(new Size(
            Math.Max(0, MetadataDetailsBorder.ActualWidth - padding.Left - padding.Right),
            double.PositiveInfinity));
        var expandedHeight = Math.Ceiling(MetadataDetailsContent.DesiredSize.Height + padding.Top + padding.Bottom +
            MetadataDetailsBorder.BorderThickness.Top + MetadataDetailsBorder.BorderThickness.Bottom);
        var targetHeight = expanded ? expandedHeight : 0;
        var targetOpacity = expanded ? 1d : 0d;

        if (!animate)
        {
            MetadataDetailsBorder.BeginAnimation(HeightProperty, null);
            MetadataDetailsBorder.BeginAnimation(OpacityProperty, null);
            MetadataDetailsBorder.Height = targetHeight;
            MetadataDetailsBorder.Opacity = targetOpacity;
            return;
        }

        var animationMilliseconds = AnimationTimingSettingsService
            .GetMetadataPanelMilliseconds(_debugSettings);
        var duration = TimeSpan.FromMilliseconds(animationMilliseconds);
        var easing = new CubicEase { EasingMode = EasingMode.EaseInOut };
        MetadataDetailsBorder.BeginAnimation(HeightProperty, new DoubleAnimation(targetHeight, duration) { EasingFunction = easing });
        MetadataDetailsBorder.BeginAnimation(OpacityProperty, new DoubleAnimation(targetOpacity, duration) { EasingFunction = easing });
    }


    private void OpenTaskManager_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("taskmgr.exe") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppDialogService.Warning(this, LocalizationText.Get("MainWindow.TaskManager.OpenFailed"), LocalizationText.Get("Common.OperationFailed"), details: new[] { ex.Message });
        }
    }

    private void UpdateOutputFolderBadge() => MainOutputSettingsView?.RefreshOutputFolderBadge();

    private void OutputFolderStateChip_Click(object sender, RoutedEventArgs e)
    {
        OpenFolderInExplorer(OutputFolder, LocalizationText.Get("MainWindow.Path.OutputFolder"));
    }

    private void OutputFolderPrimaryAction_Click(object sender, RoutedEventArgs e)
    {
        RefreshOutputFolderPrimaryActionState();

        switch (EffectiveOutputFolderPrimaryAction)
        {
            case OutputFolderPrimaryAction.Open:
                OpenFolderInExplorer(OutputFolder, LocalizationText.Get("MainWindow.Path.OutputFolder"));
                break;

            case OutputFolderPrimaryAction.Change:
                ChooseOutputFolder();
                break;

            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    private void OutputFolderSplitMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.ContextMenu is null)
            return;

        RefreshOutputFolderPrimaryActionState();
        button.ContextMenu.PlacementTarget = button;
        button.ContextMenu.IsOpen = true;
        e.Handled = true;
    }

    private void SelectOutputFolderChangeAction_Click(object sender, RoutedEventArgs e)
    {
        SetOutputFolderPrimaryAction(OutputFolderPrimaryAction.Change);
    }

    private void SetOutputFolderPrimaryAction(OutputFolderPrimaryAction action)
    {
        var value = action.ToString();
        if (string.Equals(_settings.OutputFolderPrimaryAction, value, StringComparison.Ordinal))
            return;

        _settings.OutputFolderPrimaryAction = value;
        OnPropertyChanged(nameof(IsOutputFolderOpenPrimaryAction));
        OnPropertyChanged(nameof(IsOutputFolderChangePrimaryAction));
        SaveSettingsIfReady();
    }

    private void RefreshOutputFolderPrimaryActionState()
    {
        OnPropertyChanged(nameof(CanOpenOutputFolder));
        OnPropertyChanged(nameof(IsOutputFolderOpenPrimaryAction));
        OnPropertyChanged(nameof(IsOutputFolderChangePrimaryAction));
    }

    private void ChooseOutputFolderButton_Click(object sender, RoutedEventArgs e)
    {
        ChooseOutputFolder();
    }

    private void ChooseOutputFolder() => ChooseOutputFolder(this);

    private void ChooseOutputFolder(Window owner)
    {
        if (IsBusy)
            return;

        var dialog = new OpenFolderDialog
        {
            Title = LocalizationText.Get("Disc.Setup.SelectOutputFolder"),
            Multiselect = false
        };

        dialog.InitialDirectory = !string.IsNullOrWhiteSpace(OutputFolder) && Directory.Exists(OutputFolder)
            ? OutputFolder
            : GetDesktopFolder();

        if (dialog.ShowDialog(owner) != true)
            return;

        OutputFolder = dialog.FolderName;
    }

    private void PublishDiscReadSource(string sourceRoot, string projectFolder, CancellationToken operationToken)
    {
        if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished) return;
        try
        {
            // Publish the successful read before the producer can finish or pause.
            // Cancellation does not undo data already read by this current operation.
            Dispatcher.Invoke(() =>
            {
                if (_discImportCancellation?.Token != operationToken ||
                    !TrackPathService.PathEquals(projectFolder, _currentProjectWorkFolder) ||
                    !(LoadedProjectState.IsAudioDisc || LoadedProjectState.IsMp3Disc)) return;
                SetSelectedSourceDisplayOverride(CreateDiscDriveDisplayName(sourceRoot), sourceRoot);
            });
        }
        catch (OperationCanceledException) when (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
        {
        }
        catch (InvalidOperationException) when (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
        {
        }
    }

    private void SetSelectedSourceDisplayOverride(string? displayText, string? openPath)
    {
        _selectedSourceDisplayOverride = displayText?.Trim() ?? string.Empty;
        _selectedSourceOpenPathOverride = openPath?.Trim() ?? string.Empty;
        OnPropertyChanged(nameof(SelectedSourceDisplayText));
        OnPropertyChanged(nameof(SelectedSourceOpenPath));
        OnPropertyChanged(nameof(CanOpenSelectedFolder));
        OnPropertyChanged(nameof(CurrentProjectTypeGlyph));
        OnPropertyChanged(nameof(CurrentProjectTypeGlyphVisibility));
    }

    private void ClearSelectedSourceDisplayOverride()
    {
        if (string.IsNullOrEmpty(_selectedSourceDisplayOverride) &&
            string.IsNullOrEmpty(_selectedSourceOpenPathOverride))
        {
            return;
        }

        _selectedSourceDisplayOverride = string.Empty;
        _selectedSourceOpenPathOverride = string.Empty;
        OnPropertyChanged(nameof(SelectedSourceDisplayText));
        OnPropertyChanged(nameof(SelectedSourceOpenPath));
        OnPropertyChanged(nameof(CanOpenSelectedFolder));
    }

    private void SetAudioDiscSourceDisplayOverride(AudioDiscProjectManifest? manifest)
    {
        var sourceRoot = ResolveAudioDiscSourceRoot(manifest);
        if (string.IsNullOrWhiteSpace(sourceRoot))
            return;

        SelectedFolder = sourceRoot;
        SetSelectedSourceDisplayOverride(
            CreateDiscDriveDisplayName(sourceRoot),
            sourceRoot);
    }

    private string ResolveAudioDiscSourceRoot(AudioDiscProjectManifest? manifest)
    {
        if (!string.IsNullOrWhiteSpace(manifest?.SourceDriveRoot))
            return manifest.SourceDriveRoot;

        var discSourceRoot = manifest?.Discs
            .OrderByDescending(disc => disc.CompletedUtc.HasValue)
            .ThenByDescending(disc => disc.DiscNumber)
            .Select(disc => disc.SourceDriveRoot)
            .FirstOrDefault(path => !string.IsNullOrWhiteSpace(path));
        if (!string.IsNullOrWhiteSpace(discSourceRoot))
            return discSourceRoot;

        return LoadedProjectState.IsAudioDisc
            ? SelectedFolder
            : string.Empty;
    }

    private void OpenSelectedFolder_Click(object sender, RoutedEventArgs e)
    {
        if (!CanOpenSelectedFolder)
            return;

        OpenFolderInExplorer(SelectedSourceOpenPath, LocalizationText.Get("MainWindow.Path.SourceOrProject"));
    }

    private void SelectOutputFolderOpenAction_Click(object sender, RoutedEventArgs e)
    {
        if (!CanOpenOutputFolder)
        {
            RefreshOutputFolderPrimaryActionState();
            return;
        }

        SetOutputFolderPrimaryAction(OutputFolderPrimaryAction.Open);
    }

    private void OpenWorkFolder_Click(object sender, RoutedEventArgs e)
    {
        OpenFolderInExplorer(GetBestWorkFolderToOpen(), LocalizationText.Get("MainWindow.Path.ProjectFolder"), warnForProjectFolder: true);
    }

    private void OpenFfmpegFolder_Click(object sender, RoutedEventArgs e)
    {
        var ffmpegPath = !string.IsNullOrWhiteSpace(_ffmpegStatus.FfmpegPath)
            ? _ffmpegStatus.FfmpegPath
            : _settings.FfmpegPath;

        if (string.IsNullOrWhiteSpace(ffmpegPath) || !File.Exists(ffmpegPath))
        {
            AppDialogService.Warning(
                this,
                LocalizationText.Get("MainWindow.Ffmpeg.NotFound.Title"),
                LocalizationText.Get("MainWindow.Ffmpeg.NotFound.CurrentInstallationMessage"));
            return;
        }

        OpenFolderInExplorer(Path.GetDirectoryName(ffmpegPath) ?? string.Empty, LocalizationText.Get("MainWindow.Path.FfmpegFolder"));
    }

    private ContextMenu? _developerQuickMenu;

    internal void OpenDeveloperQuickMenu(FrameworkElement placementTarget)
    {
        if (!DeveloperToolsCommands.AccessState.IsEnabled)
            return;
        if (_developerQuickMenu is not null) _developerQuickMenu.IsOpen = false;
        var menu = CreateDeveloperQuickMenu();
        _developerQuickMenu = menu;
        menu.Closed += (_, _) => { if (ReferenceEquals(_developerQuickMenu, menu)) _developerQuickMenu = null; };
        menu.PlacementTarget = placementTarget;
        menu.Placement = PlacementMode.MousePoint;
        menu.IsOpen = true;
    }

    private ContextMenu CreateDeveloperQuickMenu()
    {
        var menu = new ContextMenu
        {
            Style = TryFindResource("BookStitchContextMenuStyle") as Style
        };

        menu.Items.Add(CreateDeveloperQuickActionMenuItem(
            "Developer Tools öffnen",
            RestartDeveloperToolsWindowForModalAccess));
        menu.Items.Add(CreateDeveloperQuickActionMenuItem(
            "Neustarten",
            RequestApplicationRestart));
        menu.Items.Add(new Separator());
        menu.Items.Add(CreateBackgroundQuickMenu());
        menu.Items.Add(CreateTextColorQuickMenu());
        menu.Items.Add(CreateDeveloperAccessQuickMenu());

        return menu;
    }

    private MenuItem CreateBackgroundQuickMenu()
    {
        var submenu = CreateDeveloperQuickSubmenuItem("Hintergrund");
        foreach (var preset in AppearanceSettingsService.BackgroundPresets)
        {
            var presetName = preset.Preset.ToString();
            submenu.Items.Add(CreateDeveloperQuickSelectionMenuItem(
                preset.DisplayName,
                string.Equals(AppearanceSettingsService.GetEffectiveSettings(_settings).BackgroundPreset, presetName, StringComparison.Ordinal),
                isEnabled: true,
                () => SetBackgroundPreset(presetName)));
        }

        return submenu;
    }

    private MenuItem CreateTextColorQuickMenu()
    {
        var submenu = CreateDeveloperQuickSubmenuItem("Textfarbe");
        var appearanceMode = AppearanceSettingsService.GetEffectiveMode(_settings);
        foreach (var preset in AppearanceSettingsService.TextColorPresets)
        {
            var presetName = preset.Preset.ToString();
            submenu.Items.Add(CreateDeveloperQuickSelectionMenuItem(
                preset.DisplayName,
                string.Equals(AppearanceSettingsService.GetEffectiveSettings(_settings).TextColorPreset, presetName, StringComparison.Ordinal),
                preset.IsCompatibleWith(appearanceMode),
                () => SetTextColorPreset(presetName)));
        }

        return submenu;
    }

    private MenuItem CreateDeveloperAccessQuickMenu()
    {
        var submenu = CreateDeveloperQuickSubmenuItem(LocalizationText.Get("DeveloperTools.AccessMenu"));
        var access = DeveloperToolsCommands.AccessState;
        foreach (var (choice, key) in new[]
        {
            (DeveloperToolsVisibilityChoice.Show, "DeveloperAccess.Show"),
            (DeveloperToolsVisibilityChoice.Hide, "DeveloperAccess.Hide"),
            (DeveloperToolsVisibilityChoice.Conceal, "DeveloperAccess.Conceal")
        })
        {
            submenu.Items.Add(CreateDeveloperQuickSelectionMenuItem(
                LocalizationText.Get(key), choice != DeveloperToolsVisibilityChoice.Conceal && access.VisibilityChoice == choice,
                isEnabled: true, () => access.VisibilityChoice = choice));
        }
        return submenu;
    }

    private MenuItem CreateDeveloperQuickSubmenuItem(string text) =>
        new()
        {
            Header = text,
            Style = TryFindResource("BookStitchContextSubmenuItemStyle") as Style
        };

    private MenuItem CreateDeveloperQuickSelectionMenuItem(
        string text,
        bool isChecked,
        bool isEnabled,
        Action action)
    {
        var item = CreateDeveloperQuickMenuItem(text);
        item.IsCheckable = true;
        item.IsChecked = isChecked;
        item.IsEnabled = isEnabled;
        item.Click += (_, _) => action();
        return item;
    }

    private MenuItem CreateDeveloperQuickActionMenuItem(string text, Action action)
    {
        var item = CreateDeveloperQuickMenuItem(text);
        item.Click += (_, _) => action();
        return item;
    }

    private MenuItem CreateDeveloperQuickMenuItem(string text) =>
        new()
        {
            Header = text,
            Style = TryFindResource("BookStitchContextMenuItemStyle") as Style
        };

    private void DeveloperAccessState_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(DeveloperToolsAccessState.IsEnabled)) return;
        if (!DeveloperToolsCommands.AccessState.IsEnabled)
        {
            SetPerformanceDiagnosticsEnabled(false);
            _developerToolsWindow?.Close();
            MainExportProgress.StopPreview();
            if (_developerQuickMenu is not null) _developerQuickMenu.IsOpen = false;
            var previousBackground = _settings.BackgroundPreset;
            var previousText = _settings.TextColorPreset;
            AppearanceSettingsService.NormalizeRegular(_settings, false);
            if (previousBackground != _settings.BackgroundPreset || previousText != _settings.TextColorPreset)
                _services.Foundation.GlobalSettingsManager.SaveImmediate();
        }
        ApplyAppearanceResources(_settings);
    }

    private void SetPerformanceDiagnosticsEnabled(bool enabled)
    {
        enabled = enabled && DeveloperToolsCommands.AccessState.IsEnabled;
        if (Application.Current is App app)
            app.SetPerformanceDiagnosticsEnabled(enabled, _settings);

        if (_debugSettings.PerformanceDiagnosticsEnabled == enabled) return;
        _debugSettings.PerformanceDiagnosticsEnabled = enabled;
        try { _services.Foundation.DebugSettingsService.Save(_debugSettings); }
        catch (Exception ex) { Trace.TraceWarning("Performance diagnostics preference could not be saved: {0}", ex); }
    }

    private void SetForceShowFfmpegSetupButton(bool visible)
    {
        _debugSettings.ForceShowFfmpegSetupButton = visible;
        _services.Foundation.DebugSettingsService.Save(_debugSettings);
        OnPropertyChanged(nameof(FfmpegSetupButtonVisibility));
    }

    private void SetTrackGridRowHeightLevel(int level)
    {
        var normalized = TrackGridRowHeightService.NormalizeLevel(level);
        if (_debugSettings.TrackGridRowHeightLevel == normalized)
            return;

        _debugSettings.TrackGridRowHeightLevel = normalized;
        _services.Foundation.DebugSettingsService.Save(_debugSettings);
        OnPropertyChanged(nameof(TrackGridRowHeight));
    }

    private DeveloperToolsWindow CreateDeveloperToolsWindow()
    {
        var developerToolsWindow = new DeveloperToolsWindow(
            _services.Foundation.GlobalSettingsManager,
            _services.Foundation.SettingsService.ProjectRootFolder,
            _debugSettings,
            _services.Foundation.DebugSettingsService,
            _services.Foundation.EnvironmentContext,
            Application.Current is App app ? app.ProjectEnvironmentSelectionService : new ProjectEnvironmentSelectionService(),
            RequestApplicationRestart,
            _runtimeServices.NotificationService,
            _isMetadataPanelExpanded,
            expanded => SetMetadataPanelExpanded(expanded, animate: true),
            SetForceShowFfmpegSetupButton,
            SetPerformanceDiagnosticsEnabled,
            SetTrackGridRowHeightLevel,
            SetBackgroundPreset,
            SetTextColorPreset,
            () => RefreshGenreOptions(),
            PreviewDiscSourceAnalysisAsync,
            () => ProjectReviewState.Phase.ToString(),
            () => StatusText,
            () => ExportProgressText,
            CreateCurrentChapterDebugSnapshot,
            CreateCurrentMetadataDebugSnapshot,
            projectIndexService: _services.Foundation.ProjectIndexService,
            startProgressPreview: (seconds, gold) => MainExportProgress.StartPreview(seconds, gold, StandardProgressPanel),
            stopProgressPreview: () => MainExportProgress.StopPreview());


        developerToolsWindow.Closed += (_, _) =>
        {
            if (ReferenceEquals(_developerToolsWindow, developerToolsWindow))
            {
                _developerToolsWindow = null;
            }
        };

        _developerToolsWindow = developerToolsWindow;
        return developerToolsWindow;
    }

    private ChapterDebugSnapshot CreateCurrentChapterDebugSnapshot() =>
        _services.Foundation.ChapterDebugPresentationService.Create(
            _currentActiveChapterResult,
            TracksGrid?.SelectedItem as TrackInfo);

    private MetadataDebugSnapshot CreateCurrentMetadataDebugSnapshot() =>
        _services.Foundation.MetadataDebugPresentationService.Create(
            _projectMetadata.CanonicalMetadata,
            string.Equals(
                _latestMetadataDiagnosisProjectFolder,
                _currentProjectWorkFolder,
                StringComparison.OrdinalIgnoreCase)
                ? _latestMetadataFieldDecisions
                : []);

    internal void RestartDeveloperToolsWindowForModalAccess()
    {
        if (!DeveloperToolsCommands.AccessState.IsEnabled)
            return;

        if (_developerToolsWindow is { IsLoaded: true })
            _developerToolsWindow.Close();

        _developerToolsWindow = null;
        var developerToolsWindow = CreateDeveloperToolsWindow();
        developerToolsWindow.Show();
        developerToolsWindow.Activate();
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        if (!CanOpenSettings)
            return;

        ShowSettingsDialog(this);
    }

    private void ShowSettingsDialog(
        Window owner,
        string? initialTabKey = null,
        Action? chapterStateChanged = null)
    {
        var previousChapterSettings = CloneChapterSettings(_settings.ChapterSettings);

        var settingsWindow = new SettingsWindow(
            _services.Foundation.GlobalSettingsManager,
            _runtimeServices.NotificationService,
            days => DeleteOldProjects(days, showUserMessage: false),
            ApplyMergeAutomaticallyFromSettingsWindow,
            ApplyRegularAppearanceSettings,
            getOutputFolder: () => OutputFolder,
            chooseOutputFolder: owner => ChooseOutputFolder(owner),
            openOutputFolder: owner => OpenFolderInExplorer(OutputFolder, LocalizationText.Get("MainWindow.Path.OutputFolder"), owner),
            outputFolderChangeSource: this,
            chapterPreviewProjectTracks: Tracks.ToList(),
            chapterPreviewProjectBookTitle: BookTitle,
            chapterPreviewProjectAlbum: Album,
            chapterPreviewResolvedAutoContents: CreateCurrentResolvedAutoContentsBySourceId(),
            chapterPreviewProjectDataProvider: CreateCurrentChapterPreviewData,
            chapterSettingsChanged: () =>
            {
                ApplyPersistedChapterSettingsEditorChange();
                chapterStateChanged?.Invoke();
            },
            initialTabKey: initialTabKey,
            coverPickerService: _coverPickerService,
            projectRootFolder: GetWorkingRootFolder(),
            projectIndexService: _services.Foundation.ProjectIndexService,
            projectResumePlanService: _services.Foundation.ProjectResumePlanService,
            requestApplicationRestart: RequestApplicationRestart,
            checkProjectIntegrity: RunProjectIntegritySweep,rebuildProject: RunProjectRebuild)
        {
            Owner = owner
        };
        var conversionSettings = settingsWindow.ConversionSettingsControl;
        conversionSettings.DataContext = this;
        conversionSettings.ExportPresetComboBox.SelectionChanged += (sender, args) =>
        {
            // Initial binding populates this additional view; only a replacement is a user selection.
            if (args.RemovedItems.Count > 0) ExportPresetComboBox_SelectionChanged(sender, args);
        };
        conversionSettings.ParallelJobsTextBox.LostFocus += ParallelJobsInput_LostFocus;
        conversionSettings.ParallelJobsTextBox.KeyDown += ParallelJobsInput_KeyDown;
        conversionSettings.ParallelJobsTextBox.PreviewMouseWheel += ParallelJobsInput_PreviewMouseWheel;
        conversionSettings.IncreaseParallelJobsButton.Click += IncreaseParallelJobs_Click;
        conversionSettings.DecreaseParallelJobsButton.Click += DecreaseParallelJobs_Click;
        conversionSettings.OpenWorkFolderButton.Click += OpenWorkFolder_Click;
        conversionSettings.OpenTaskManagerButton.Click += OpenTaskManager_Click;
        settingsWindow.Loaded += (_, _) => { ++_openSettingsContexts; CheckAutomaticMaintenance(); };
        settingsWindow.Closed += (_, _) => { _openSettingsContexts = Math.Max(0, _openSettingsContexts - 1); CheckAutomaticMaintenance(); };
        AttachDebugWindowPlacement(settingsWindow, DebugWindowPlacementTarget.GlobalSettings);

        ModalDialogService.ShowDialog(settingsWindow, owner);
        if (IsApplicationRestartOrShutdownPending())
            return;

        ApplyAppearanceResources(_settings);
        if (string.IsNullOrWhiteSpace(_currentProjectWorkFolder))
            RecalculateProcessingActionsForCurrentPreset();

        if (!ChapterSettingsEqual(previousChapterSettings, _settings.ChapterSettings))
        {
            UpdateIndexes();
            PersistTrackListState();
        }

        OutputExtension = _settings.DefaultOutputExtension;
        FileNameTemplate = _settings.DefaultFileNameTemplate;
        OnPropertyChanged(nameof(OutputFolderLayout));
        OnPropertyChanged(nameof(FfmpegSetupButtonVisibility));
        AutoFitTrackColumnsAfterRender();
        OnPropertyChanged(nameof(MergeAutomaticallyAfterConversion));
        OnPropertyChanged(nameof(DriveCircleEnabled));
        OnPropertyChanged(nameof(CanStartNewProject));
        OnPropertyChanged(nameof(ExportPreviewFileName));
        RefreshSystemSleepPrevention();
        RefreshResumeProjects(showStatus: false);
    }

    private bool IsApplicationRestartOrShutdownPending()
    {
        if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
            return true;

        return Application.Current is App app && app.ApplicationRestartService.IsRestartRequested;
    }

    private IReadOnlyDictionary<Guid, string>? CreateCurrentResolvedAutoContentsBySourceId()
    {
        if (_currentAutoChapterResult is null)
            return null;

        var resolvedById = _currentAutoChapterResult.ResolveResult.Chapters.ToDictionary(
            chapter => chapter.EffectiveChapterId,
            StringComparer.Ordinal);
        return _currentAutoChapterResult.AnalysisSnapshot.EffectiveChapters
            .OrderBy(chapter => chapter.ScopeOrder)
            .GroupBy(chapter => chapter.SourceId)
            .ToDictionary(
                group => group.Key,
                group => resolvedById[group.First().EffectiveChapterId].Text);
    }

    private ChapterPreviewData? CreateCurrentChapterPreviewData()
    {
        if (Tracks.Count == 0)
            return null;

        return new ChapterPreviewData(
            BookTitle,
            Album,
            Tracks.ToList(),
            CreateCurrentResolvedAutoContentsBySourceId(),
            _currentActiveChapterResult?.RenderedTimeline);
    }

    private void ConfigureMainChapterSettingsEditor()
    {
        MainChapterHeadingTextBlock.Text = ChapterSettingsEditorPresentation.GetText("GenericChapterLabel");
        MainChapterModeLabelTextBlock.Text = ChapterSettingsEditorPresentation.GetText("Mode");
        MainChapterNumberingStyleLabelTextBlock.Text = ChapterSettingsEditorPresentation.GetText("NumberingStyle");
        MainChapterCustomHeadingTextBlock.Text = ChapterSettingsEditorPresentation.GetText("CustomRules");

        SettingsComboBoxSelection.Populate(
            MainChapterModeComboBox,
            ChapterSettingsEditorPresentation.Modes
                .Select(option => (option.Value, ChapterSettingsEditorPresentation.GetText(option.ResourceKey)))
                .ToArray());
        SettingsComboBoxSelection.Populate(
            MainChapterNumberingStyleComboBox,
            ChapterSettingsEditorPresentation.NumberingStyles
                .Select(option => (option.Value, ChapterSettingsEditorPresentation.GetText(option.ResourceKey)))
                .ToArray());

        var slots = ChapterSettingsEditorPresentation.Slots
            .Select(option => (option.Value, ChapterSettingsEditorPresentation.GetText(option.ResourceKey)))
            .ToArray();
        SettingsComboBoxSelection.Populate(MainChapterSlot1ComboBox, slots);
        SettingsComboBoxSelection.Populate(MainChapterSlot2ComboBox, slots);
        SettingsComboBoxSelection.Populate(MainChapterSlot3ComboBox, slots);

        var separators = ChapterSettingsEditorPresentation.Separators
            .Select(option => (option.Value, ChapterSettingsEditorPresentation.GetText(option.ResourceKey)))
            .ToArray();
        SettingsComboBoxSelection.Populate(MainChapterSeparator1ComboBox, separators);
        SettingsComboBoxSelection.Populate(MainChapterSeparator2ComboBox, separators);
    }

    private void RefreshMainChapterSettingsEditorFromSharedState()
    {
        if (MainChapterModeComboBox is null)
            return;

        var previousGuard = _isRefreshingMainChapterSettingsEditor;
        _isRefreshingMainChapterSettingsEditor = true;
        try
        {
            var state = _chapterSettingsEditorService.Read(_settings);
            SettingsComboBoxSelection.SelectByTag(MainChapterModeComboBox, state.Mode);
            SettingsComboBoxSelection.SelectByTag(MainChapterNumberingStyleComboBox, state.NumberingStyle);
            SettingsComboBoxSelection.SelectByTag(MainChapterSlot1ComboBox, state.Slot1);
            SettingsComboBoxSelection.SelectByTag(MainChapterSeparator1ComboBox, state.Separator1);
            SettingsComboBoxSelection.SelectByTag(MainChapterSlot2ComboBox, state.Slot2);
            SettingsComboBoxSelection.SelectByTag(MainChapterSeparator2ComboBox, state.Separator2);
            SettingsComboBoxSelection.SelectByTag(MainChapterSlot3ComboBox, state.Slot3);
            UpdateMainChapterSettingsEditorVisibility(state.Mode);
            UpdateMainChapterSlotOptionAvailability(state.NumberingStyle);
        }
        finally
        {
            _isRefreshingMainChapterSettingsEditor = previousGuard;
        }
    }

    private void MainChapterSettingsComboBox_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_isRefreshingMainChapterSettingsEditor)
            return;

        var current = _chapterSettingsEditorService.Read(_settings);
        var mode = SettingsComboBoxSelection.GetSelectedTag(MainChapterModeComboBox, current.Mode);
        var numberingStyle = SettingsComboBoxSelection.GetSelectedTag(MainChapterNumberingStyleComboBox, current.NumberingStyle);
        _chapterSettingsEditorService.Apply(
            _settings,
            new ChapterSettingsEditorState(
                mode,
                numberingStyle,
                current.PreviewDataSource,
                SettingsComboBoxSelection.GetSelectedTag(MainChapterSlot1ComboBox, current.Slot1),
                SettingsComboBoxSelection.GetSelectedTag(MainChapterSeparator1ComboBox, current.Separator1),
                SettingsComboBoxSelection.GetSelectedTag(MainChapterSlot2ComboBox, current.Slot2),
                SettingsComboBoxSelection.GetSelectedTag(MainChapterSeparator2ComboBox, current.Separator2),
                SettingsComboBoxSelection.GetSelectedTag(MainChapterSlot3ComboBox, current.Slot3)));

        ApplySharedChapterSettingsEditorChange();
    }

    private void UpdateMainChapterSettingsEditorVisibility(string mode)
    {
        MainChapterNumberingStylePanel.Visibility = ChapterSettingsEditorPresentation.ShowsNumberingStyle(mode)
            ? Visibility.Visible
            : Visibility.Collapsed;
        MainChapterCustomRulePanel.Visibility = ChapterSettingsEditorPresentation.ShowsCustomRule(mode)
            ? Visibility.Visible
            : Visibility.Collapsed;

        if (_isMetadataPanelExpanded)
            SetMetadataPanelExpanded(true, animate: false);
    }

    private void UpdateMainChapterSlotOptionAvailability(string numberingStyle)
    {
        foreach (var comboBox in new[] { MainChapterSlot1ComboBox, MainChapterSlot2ComboBox, MainChapterSlot3ComboBox })
        {
            foreach (var item in comboBox.Items.OfType<ComboBoxItem>())
            {
                var slotValue = item.Tag?.ToString() ?? string.Empty;
                item.IsEnabled = ChapterSettingsEditorPresentation.IsSlotOptionEnabled(slotValue, numberingStyle);
                var helpText = ChapterSettingsEditorPresentation.GetSlotOptionUnavailableHelpText(slotValue, numberingStyle);
                item.ToolTip = helpText;
                ToolTipService.SetShowOnDisabled(item, helpText is not null);
            }
        }
    }

    private void ApplySharedChapterSettingsEditorChange()
    {
        SaveSettingsIfReady();
        ApplyPersistedChapterSettingsEditorChange();
    }

    private void ApplyPersistedChapterSettingsEditorChange()
    {
        ApplyChapterSettingsImmediately();
        RefreshMainChapterSettingsEditorFromSharedState();
        _developmentMetadataDialog?.RefreshChapterSettingsEditorFromSharedState();
    }

    private void ApplyChapterSettingsImmediately()
    {
        _chapterMetadataRefreshTimer.Stop();
        UpdateIndexes();
        PersistTrackListState();
    }

    private void QueueProjectSetupDiscCountPreviewRefresh()
    {
        _projectSetupDiscCountRefreshTimer.Stop();
        _projectSetupDiscCountRefreshTimer.Start();
    }

    private void ProjectSetupDiscCountRefreshTimer_Tick(object? sender, EventArgs e)
    {
        FlushProjectSetupDiscCountPreviewRefresh();
    }

    private void FlushProjectSetupDiscCountPreviewRefresh()
    {
        if (!_projectSetupDiscCountRefreshTimer.IsEnabled)
            return;

        _projectSetupDiscCountRefreshTimer.Stop();
        UpdateIndexes();
    }

    private DiscProjectSetupDialog CreateProjectSetupDialog(ProjectSetupDialogRequest request)
    {
        if (!string.IsNullOrWhiteSpace(_lockedDiscResumePreset) && request.SourceKind != ProjectSetupSourceKind.Folder)
            request = request with { IsDiscResume = true, SelectedExportPreset = _lockedDiscResumePreset };
        _previewEffectiveTotalDiscs = request.SourceKind == ProjectSetupSourceKind.Folder
            ? null
            : Math.Clamp(request.DefaultDiscCount, request.MinimumDiscs, Math.Min(request.MaximumDiscs, 99));
        if (_previewEffectiveTotalDiscs is not null && Tracks.Count > 0)
            UpdateIndexes();

        var dialog = new DiscProjectSetupDialog(
            request,
            AppearanceSettingsService.GetEffectiveMode(_settings),
            openAdvancedSettings: owner =>
            {
                ShowSettingsDialog(owner);
                return new ProjectSetupDialogGlobalSettings(
                    _settings.MergeAutomaticallyAfterConversion,
                    _settings.DefaultOutputExtension,
                    _settings.DefaultFileNameTemplate,
                    OutputFolder);
            },
            setAutoMerge: enabled =>
            {
                MergeAutomaticallyAfterConversion = enabled;
            },
            setOutputExtension: extension =>
            {
                _settings.DefaultOutputExtension = extension;
                OutputExtension = extension;
                SaveSettingsIfReady();
                OnPropertyChanged(nameof(ExportPreviewFileName));
            },
            setOutputFolder: folder =>
            {
                OutputFolder = folder;
            },
            setFileNameTemplate: template =>
            {
                _settings.DefaultFileNameTemplate = template;
                FileNameTemplate = template;
                SaveSettingsIfReady();
                OnPropertyChanged(nameof(ExportPreviewFileName));
            },
            setLastCoverFolder: folder =>
            {
                if (!IsValidExternalCoverFolder(folder))
                    return;

                _settings.LastCoverFolder = folder;
                SaveSettingsIfReady();
            },
            setCoverWarningThresholdPixels: SetCoverWarningThresholdPixels,
            getCoverWarningThresholdPixels: () => _settings.CoverWarningThresholdPixels,
            coverPickerService: _coverPickerService,
            discCountChanged: totalDiscs =>
            {
                _previewEffectiveTotalDiscs = totalDiscs;
                QueueProjectSetupDiscCountPreviewRefresh();
            },
            metadataState: _projectMetadata,
            metadataPreferences: _settings.MetadataPreferences,
            metadataPreferencesChanged: _ => SaveSettingsIfReady(),
            sourceSnapshotsProvider: () =>
                _services.Media.ProjectMetadataPreviewAggregationService.CreateSourceSnapshots(Tracks.ToArray()),
            metadataFieldDecisionsProvider: () =>
                string.Equals(
                    _latestMetadataDiagnosisProjectFolder,
                    _currentProjectWorkFolder,
                    StringComparison.OrdinalIgnoreCase)
                        ? _latestMetadataFieldDecisions
                        : [],
            saveMetadata: SaveMetadataDialogCanonicalMetadata,
            openMetadataDialog: owner => ShowMetadataDialog(owner, allowDuringProjectSetup: true),
            compatibilityPreviewProvider: CreateCurrentCompatibilityPreviewTagData,
            compatibilityDescriptionEnabledProvider: () => _settings.CompatibilityDescriptionEnabled,
            compatibilityDescriptionEnabledChanged: enabled =>
            {
                _settings.CompatibilityDescriptionEnabled = enabled;
                SaveSettingsIfReady();
            },
            getAppearanceMode: () => AppearanceSettingsService.GetEffectiveMode(_settings),
            getProjectSetupPageAnimationMilliseconds: () => AnimationTimingSettingsService
                .GetProjectSetupPageMilliseconds(_debugSettings),
            settingsManager: _services.Foundation.GlobalSettingsManager,
            shouldWarnDiscCount: () => !WarningSuppressionService.IsSuppressed(_settings, WarningSuppressionService.DiscCount),
            setAlwaysWarnDiscCount: enabled =>
            {
                WarningSuppressionService.SetSuppressed(_settings, WarningSuppressionService.DiscCount, !enabled);
                SaveSettingsIfReady();
            })
        {
            Owner = this
        };

        if (request.SourceKind == ProjectSetupSourceKind.Folder && string.IsNullOrWhiteSpace(_currentProjectWorkFolder))
        {
            dialog.AcceptedPresetChanged += preset => ShowFolderPassThroughNotice(dialog, preset);
            dialog.Loaded += (_, _) => _folderSetupPreviewOwner = dialog;
            dialog.Closed += (_, _) => _folderSetupPreviewOwner = null;
            dialog.Loaded += (_, _) => ShowFolderPassThroughNotice(dialog, dialog.PresetComboBox.SelectedItem?.ToString());
        }
        ApplyProjectSetupDialogInitialWidth(dialog);
        AttachDebugWindowPlacement(dialog, DebugWindowPlacementTarget.ProjectSetup);
        return dialog;
    }

    private void AttachDebugWindowPlacement(Window window, DebugWindowPlacementTarget target)
    {
        if (_debugWindowPlacementService is null)
            return;

        _debugWindowPlacementService.Attach(window, target);
    }

    private void ApplyProjectSetupDialogInitialWidth(DiscProjectSetupDialog dialog)
    {
        var ownerWidth = ActualWidth > 0 ? ActualWidth : Width;
        dialog.Width = CalculateProjectSetupDialogInitialWidth(
            ownerWidth,
            dialog.Width,
            dialog.MinWidth,
            SystemParameters.WorkArea.Width);
    }

    internal static double CalculateProjectSetupDialogInitialWidth(
        double ownerWidth,
        double fallbackWidth,
        double minimumWidth,
        double workAreaWidth)
    {
        var targetWidth = ownerWidth > 0 ? ownerWidth - 80 : fallbackWidth;
        var maximumWidth = Math.Min(1200, Math.Max(minimumWidth, workAreaWidth - 80));
        return Math.Clamp(targetWidth, minimumWidth, maximumWidth);
    }

    private static PerformanceProjectType ResolveExportPerformanceProjectType(string projectType)
    {
        if (string.Equals(projectType, ProjectManifestTypes.AudioCdProject, StringComparison.OrdinalIgnoreCase))
            return PerformanceProjectType.AudioCd;

        if (string.Equals(projectType, ProjectManifestTypes.Mp3DiscProject, StringComparison.OrdinalIgnoreCase))
            return PerformanceProjectType.Mp3Disc;

        return PerformanceProjectType.LocalFolder;
    }

    private PerformanceWorkloadSnapshot CreateExportPerformanceWorkload(
        IReadOnlyList<TrackInfo> tracks,
        PerformanceProjectType projectType,
        string? sourceFolder,
        string? outputPath = null,
        ConvertedTrackResumeState? resumeState = null)
    {
        long inputBytes = 0;
        var hasInputBytes = false;
        double inputAudioDurationMilliseconds = 0;
        var hasAudioDuration = false;

        foreach (var track in tracks)
        {
            string? sourcePath = null;
            try
            {
                if (!string.IsNullOrWhiteSpace(track.FilePath) && File.Exists(track.FilePath))
                {
                    sourcePath = track.FilePath;
                }
                else if (!string.IsNullOrWhiteSpace(sourceFolder))
                {
                    var candidate = TrackPathService.GetTrackPath(sourceFolder, track);
                    if (File.Exists(candidate))
                        sourcePath = candidate;
                }

                if (!string.IsNullOrWhiteSpace(sourcePath))
                {
                    inputBytes += new FileInfo(sourcePath).Length;
                    hasInputBytes = true;
                }
            }
            catch
            {
                // Workload-Größen sind best effort und dürfen den Export nie beeinflussen.
            }

            if (track.DurationTicks is > 0)
            {
                inputAudioDurationMilliseconds +=
                    TimeSpan.FromTicks(track.DurationTicks.Value).TotalMilliseconds;
                hasAudioDuration = true;
            }
        }

        long? outputBytes = null;
        if (!string.IsNullOrWhiteSpace(outputPath))
        {
            try
            {
                if (File.Exists(outputPath))
                    outputBytes = new FileInfo(outputPath).Length;
            }
            catch
            {
                // Ausgabedateigröße ist best effort.
            }
        }

        var discCount = tracks
            .Where(track => track.DiscNumber > 0)
            .Select(track => track.DiscNumber)
            .Distinct()
            .Count();

        return new PerformanceWorkloadSnapshot
        {
            ProjectType = projectType,
            TrackCount = tracks.Count,
            FileCount = tracks.Count,
            DiscCount = discCount > 0 ? discCount : null,
            SelectedTrackCount = tracks.Count,
            InputBytes = hasInputBytes ? inputBytes : null,
            OutputBytes = outputBytes,
            InputAudioDurationMilliseconds = hasAudioDuration
                ? inputAudioDurationMilliseconds
                : null,
            OutputAudioDurationMilliseconds = outputBytes.HasValue && hasAudioDuration
                ? inputAudioDurationMilliseconds
                : null,
            PreparedReuseCount = resumeState?.ReusableCount
        };
    }

    private PerformanceWorkloadSnapshot CreateMp3DiscPerformanceWorkload(int discNumber, int totalFilesOnDisc)
    {
        var discTracks = Tracks
            .Where(track => track.DiscNumber == discNumber)
            .ToList();
        long inputBytes = 0;
        var hasInputBytes = false;
        double inputAudioDurationMilliseconds = 0;
        var hasAudioDuration = false;

        foreach (var track in discTracks)
        {
            if (!string.IsNullOrWhiteSpace(track.FilePath))
            {
                try
                {
                    if (File.Exists(track.FilePath))
                    {
                        inputBytes += new FileInfo(track.FilePath).Length;
                        hasInputBytes = true;
                    }
                }
                catch
                {
                    // Workload-Größen sind best effort und dürfen den Import nie beeinflussen.
                }
            }

            if (track.DurationTicks is > 0)
            {
                inputAudioDurationMilliseconds += TimeSpan.FromTicks(track.DurationTicks.Value).TotalMilliseconds;
                hasAudioDuration = true;
            }
        }

        return new PerformanceWorkloadSnapshot
        {
            ProjectType = PerformanceProjectType.Mp3Disc,
            TrackCount = discTracks.Count,
            FileCount = Math.Max(totalFilesOnDisc, discTracks.Count),
            DiscCount = 1,
            InputBytes = hasInputBytes ? inputBytes : null,
            InputAudioDurationMilliseconds = hasAudioDuration
                ? inputAudioDurationMilliseconds
                : null
        };
    }

    private PerformanceWorkloadSnapshot CreateLocalFolderPerformanceWorkload()
    {
        long inputBytes = 0;
        double inputAudioDurationMilliseconds = 0;

        foreach (var track in Tracks)
        {
            if (!string.IsNullOrWhiteSpace(track.FilePath))
            {
                try
                {
                    if (File.Exists(track.FilePath))
                        inputBytes += new FileInfo(track.FilePath).Length;
                }
                catch
                {
                    // Workload-Größen sind best effort und dürfen den Import nie beeinflussen.
                }
            }

            if (track.DurationTicks is > 0)
                inputAudioDurationMilliseconds += TimeSpan.FromTicks(track.DurationTicks.Value).TotalMilliseconds;
        }

        return new PerformanceWorkloadSnapshot
        {
            ProjectType = PerformanceProjectType.LocalFolder,
            TrackCount = Tracks.Count,
            FileCount = Tracks.Count,
            InputBytes = inputBytes,
            InputAudioDurationMilliseconds = inputAudioDurationMilliseconds
        };
    }

    private static string BuildProjectSetupDurationText(IEnumerable<TrackInfo> tracks)
    {
        var totalTicks = tracks.Where(track => track.DurationTicks.HasValue).Sum(track => track.DurationTicks!.Value);
        if (totalTicks <= 0)
            return "00:00:00";

        var duration = TimeSpan.FromTicks(totalTicks);
        return $"{(int)duration.TotalHours}:{duration.Minutes:00}:{duration.Seconds:00}";
    }

    private ProjectCleanupResult DeleteOldProjects(int olderThanDays, bool showUserMessage)
    {
        _maintenanceCoordinator?.StopAsync().GetAwaiter().GetResult();
        ProjectCleanupResult result;

        try
        {
            var nowUtc = DateTime.UtcNow;
            result = _services.Foundation.ProjectIndexService.DeleteProjectsOlderThan(
                GetWorkingRootFolder(),
                nowUtc,
                olderThanDays);

            var coverCleanupFailures = DeleteOldUnreferencedCoverFiles(nowUtc, olderThanDays);
            if (coverCleanupFailures.Count > 0)
            {
                result = new ProjectCleanupResult(
                    result.MatchedCount,
                    result.DeletedCount,
                    result.Failures.Concat(coverCleanupFailures).ToList());
            }
        }
        catch (Exception ex)
        {
            result = new ProjectCleanupResult(0, 0, [LocalizationText.Format("MainWindow.Cleanup.ProjectFailed", ex.Message)]);
        }

        if (result.DeletedCount > 0)
            RefreshResumeProjects(showStatus: false);

        if (showUserMessage && result.DeletedCount > 0)
            StatusText = LocalizationText.Format(
                result.DeletedCount == 1 ? "MainWindow.Cleanup.Deleted.One" : "MainWindow.Cleanup.Deleted.Many",
                result.DeletedCount);

        return result;
    }

    private IReadOnlyList<string> DeleteOldUnreferencedCoverFiles(DateTime nowUtc, int olderThanDays) =>
        new CoverCleanupService().DeleteOldUnreferencedCoverFiles(
            GetWorkingRootFolder(), GetProjectFolderStructure().CoversFolder, nowUtc,
            ProjectIndexService.NormalizeRetentionDays(olderThanDays));

    private static bool IsPathInsideFolder(string? path, string folder, out string fullPath)
    {
        fullPath = string.Empty;
        if (string.IsNullOrWhiteSpace(path))
            return false;

        try
        {
            var fullFolder = Path.GetFullPath(folder)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            fullPath = Path.GetFullPath(path)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var folderPrefix = fullFolder + Path.DirectorySeparatorChar;

            return string.Equals(fullPath, fullFolder, StringComparison.OrdinalIgnoreCase) ||
                fullPath.StartsWith(folderPrefix, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            fullPath = string.Empty;
            return false;
        }
    }

    private string GetBestWorkFolderToOpen()
    {
        var projectFolders = GetProjectFolderStructure();
        var workRoot = projectFolders.ProjectRootFolder;

        if (!string.IsNullOrWhiteSpace(_currentProjectWorkFolder) &&
            Directory.Exists(_currentProjectWorkFolder))
        {
            return _currentProjectWorkFolder;
        }

        if (!string.IsNullOrWhiteSpace(_currentFolderPath))
        {
            var projectWorkFolder = Path.Combine(projectFolders.LocalProjectsFolder, BuildProjectWorkFolderName());

            if (Directory.Exists(projectWorkFolder))
                return projectWorkFolder;
        }

        return workRoot;
    }

    private void OpenFolderInExplorer(string folderPath, string displayName, Window? owner = null, bool warnForProjectFolder = false)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
        {
            AppDialogService.Warning(
                owner ?? this,
                LocalizationText.Get("Disc.Setup.FolderNotFound.Title"),
                LocalizationText.Format("MainWindow.Path.NotFound.Message", displayName, folderPath));
            return;
        }

        if (warnForProjectFolder || ProjectFolderOpenService.IsManagedFolder(folderPath))
        {
            ProjectFolderOpenService.Open(owner ?? this, folderPath, _services.Foundation.GlobalSettingsManager);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = folderPath,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            AppDialogService.Warning(
                owner ?? this,
                LocalizationText.Get("Disc.Setup.ExplorerFailed.Title"),
                LocalizationText.Format("MainWindow.Path.OpenFailed.Message", displayName, ex.Message));
        }
    }

    private async Task LoadFolderAsync(
        string folderPath,
        string? metadataSourceFolder = null,
        bool updateMetadata = true,
        bool removeGeneratedOrWorkFiles = true,
        TrackNumberPreference trackNumberPreference = TrackNumberPreference.EmbeddedTag,
        Func<string, bool>? includeFile = null,
        bool publishWorkflowStatus = true,
        bool captureLocalImportPerformance = false,
        Guid performanceOperationId = default,
        bool captureMp3RefreshPerformance = false,
        IReadOnlyDictionary<string, TrackInfo>? reusableMp3ProbeTracksByPath = null,
        string? currentMp3ImportedFolder = null,
        TrackAnalysisCancellationScope analysisCancellationScope = TrackAnalysisCancellationScope.None,
        Action<List<TrackInfo>>? prepareScannedTracksBeforeIndexUpdate = null)
    {
        var loadSucceeded = false;
        IsBusy = true;
        using var analysisCancellation = analysisCancellationScope == TrackAnalysisCancellationScope.None
            ? null
            : new CancellationTokenSource();
        if (analysisCancellation is not null)
        {
            _trackAnalysisCancellation = analysisCancellation;
            _trackAnalysisCancellationScope = analysisCancellationScope;
            NotifyExportUiStateChanged();
        }

        App? localImportPerformanceApp = null;
        PerformanceOperationScope localImportPerformanceScope = default;
        if (captureLocalImportPerformance && Application.Current is App performanceApp)
        {
            localImportPerformanceApp = performanceApp;
            localImportPerformanceScope = performanceApp.StartPerformanceOperation(
                "project.local.import",
                PerformanceProjectType.LocalFolder,
                new PerformanceWorkloadSnapshot
                {
                    ProjectType = PerformanceProjectType.LocalFolder
                });

            if (localImportPerformanceScope.IsActive)
            {
                performanceApp.RecordPerformanceDriveRole(
                    localImportPerformanceScope.OperationId,
                    folderPath,
                    PerformanceDriveRole.Source);
                performanceApp.RecordPerformanceDriveRole(
                    localImportPerformanceScope.OperationId,
                    _services.Foundation.SettingsService.ProjectRootFolder,
                    PerformanceDriveRole.ProjectWorking);
            }
        }

        App? folderPerformanceApp = localImportPerformanceApp;
        var folderPerformanceOperationId = localImportPerformanceScope.OperationId;
        if (!captureLocalImportPerformance &&
            captureMp3RefreshPerformance &&
            performanceOperationId != Guid.Empty &&
            Application.Current is App mp3RefreshPerformanceApp)
        {
            folderPerformanceApp = mp3RefreshPerformanceApp;
            folderPerformanceOperationId = performanceOperationId;
        }

        try
        {
            ApplyLoadedProjectState(LoadedProjectState.WithDiscImportRequirement(false));
            OnPropertyChanged(nameof(CanStartExport));
            OnPropertyChanged(nameof(ExportButtonText));
            OnPropertyChanged(nameof(ExportButtonVisibility));
            _currentFolderPath = folderPath;
            ExportProgressPercent = 0;
            ExportProgressText = BuildIdleExportProgressText();

            Tracks.Clear();

            StatusText = LocalizationText.Get("MainWindow.Folder.Checking");

            var scanPerformancePhase = localImportPerformanceApp is null
                ? default(PerformancePhaseScope)
                : localImportPerformanceApp.StartPerformancePhase(
                    localImportPerformanceScope.OperationId,
                    "project.scan");
            var reconcilePerformancePhase =
                captureMp3RefreshPerformance && folderPerformanceApp is not null
                    ? folderPerformanceApp.StartPerformancePhase(
                        folderPerformanceOperationId,
                        "track.reconcile")
                    : default;
            List<TrackInfo> tracks;
            try
            {
                tracks = await _services.Media.FolderScanner.ScanAsync(
                    folderPath, trackNumberPreference, includeFile,
                    analysisCancellation?.Token ?? _discImportCancellation?.Token ?? CancellationToken.None);
                scanPerformancePhase.Complete();
            }
            catch (OperationCanceledException)
            {
                scanPerformancePhase.Cancel();
                reconcilePerformancePhase.Cancel();
                reconcilePerformancePhase = default;
                throw;
            }
            catch (Exception ex)
            {
                scanPerformancePhase.Fail(ex);
                reconcilePerformancePhase.Fail(ex);
                reconcilePerformancePhase = default;
                throw;
            }

            if (!reconcilePerformancePhase.IsActive && localImportPerformanceApp is not null)
            {
                reconcilePerformancePhase = localImportPerformanceApp.StartPerformancePhase(
                    localImportPerformanceScope.OperationId,
                    "track.reconcile");
            }
            try
            {
                prepareScannedTracksBeforeIndexUpdate?.Invoke(tracks);

                Tracks.ReplaceAll(tracks);

                _services.Workflows.TrackPreparedStateRefreshService.RefreshForCurrentPreset(
                    Tracks, _currentProjectWorkFolder, SelectedExportPreset);
                UpdateIndexes();
                reconcilePerformancePhase.Complete();
            }
            catch (Exception ex)
            {
                reconcilePerformancePhase.Fail(ex);
                throw;
            }

            var preparePerformancePhase = localImportPerformanceApp is null
                ? default(PerformancePhaseScope)
                : localImportPerformanceApp.StartPerformancePhase(
                    localImportPerformanceScope.OperationId,
                    "project.prepare");
            string? embeddedCoverPath = null;
            try
            {
                embeddedCoverPath = RunPerformanceStep(
                    localImportPerformanceApp,
                    localImportPerformanceScope.OperationId,
                    "project.prepare.cover",
                    () => TryExtractFirstEmbeddedCoverPath(tracks));

                if (updateMetadata)
                {
                    RunPerformanceStep(
                        localImportPerformanceApp,
                        localImportPerformanceScope.OperationId,
                        "project.prepare.metadata",
                        () => SuggestMetadataFromTracksAndFolder(metadataSourceFolder ?? folderPath));
                }

                if (removeGeneratedOrWorkFiles)
                {
                    RunPerformanceStep(
                        localImportPerformanceApp,
                        localImportPerformanceScope.OperationId,
                        "project.prepare.cleanup",
                        () => RemoveGeneratedOrWorkFilesFromTrackList(folderPath));
                }

                RunPerformanceStep(
                    localImportPerformanceApp,
                    localImportPerformanceScope.OperationId,
                    "project.prepare.indexes",
                    UpdateIndexes);
                preparePerformancePhase.Complete();
            }
            catch (Exception ex)
            {
                preparePerformancePhase.Fail(ex);
                throw;
            }

            TryApplyEmbeddedCoverPath(embeddedCoverPath);

            var skipProbePaths = captureMp3RefreshPerformance
                ? RestoreReusableMp3ProbeState(
                    folderPath,
                    currentMp3ImportedFolder,
                    reusableMp3ProbeTracksByPath)
                : null;

            if (_ffmpegStatus.IsComplete)
            {
                var audioProbePerformancePhase = folderPerformanceApp is null
                    ? default(PerformancePhaseScope)
                    : folderPerformanceApp.StartPerformancePhase(
                        folderPerformanceOperationId,
                        "audio.probe");
                try
                {
                    var performanceContext = folderPerformanceApp?.CreatePerformanceExternalProcessContext(
                        folderPerformanceOperationId,
                        audioProbePerformancePhase.IsActive ? audioProbePerformancePhase.PhaseId : null,
                        captureMp3RefreshPerformance ? "audio.probe" : null);
                    var probeCompleted = await EnrichTracksWithFfprobeAsync(
                        folderPath,
                        publishWorkflowStatus,
                        performanceContext,
                        skipProbePaths,
                        analysisCancellation?.Token ?? CancellationToken.None);
                    if (!probeCompleted)
                    {
                        audioProbePerformancePhase.Cancel();
                        ResetWorkspaceAfterCanceledTrackAnalysis(analysisCancellationScope);
                        return;
                    }

                    audioProbePerformancePhase.Complete();
                }
                catch (OperationCanceledException)
                {
                    audioProbePerformancePhase.Cancel();
                    ResetWorkspaceAfterCanceledTrackAnalysis(analysisCancellationScope);
                    return;
                }
                catch (Exception ex)
                {
                    audioProbePerformancePhase.Fail(ex);
                    throw;
                }
            }
            else
            {
                UpdateFinalStatus(Tracks.Count);
                if (!IsExporting)
                    ExportProgressText = BuildIdleExportProgressText();
            }

            if (localImportPerformanceScope.IsActive && localImportPerformanceApp is not null)
            {
                localImportPerformanceApp.UpdatePerformanceWorkload(
                    localImportPerformanceScope.OperationId,
                    CreateLocalFolderPerformanceWorkload());
            }

            if (ResolveCurrentChapterProjectType() == ProjectManifestTypes.FolderProject)
                RecalculateProcessingActionsForCurrentPreset();
            AutoFitTrackColumnsAfterRender();
            RefreshOpenDevelopmentMetadataForExternalChanges();
            localImportPerformanceScope.Complete();
            localImportPerformanceScope = default;
            loadSucceeded = true;
        }
        catch (OperationCanceledException)
        {
            localImportPerformanceScope.Cancel();
            localImportPerformanceScope = default;
            if (analysisCancellationScope == TrackAnalysisCancellationScope.None) throw;
            ResetWorkspaceAfterCanceledTrackAnalysis(analysisCancellationScope);
        }
        catch (Exception ex)
        {
            localImportPerformanceScope.Fail(ex);
            localImportPerformanceScope = default;
            StatusText = LocalizationText.Get("MainWindow.Folder.ReadFailed");

            _runtimeServices.NotificationService.Notify(NotificationEvent.Error);
            AppDialogService.Error(
                this,
                LocalizationText.Get("MainWindow.Folder.ReadFailed.Title"),
                LocalizationText.Format("MainWindow.Folder.ReadFailed.Message", ex.Message));
        }
        finally
        {
            if (analysisCancellation is not null)
            {
                _trackAnalysisCancellation = null;
                _trackAnalysisCancellationScope = TrackAnalysisCancellationScope.None;
                NotifyExportUiStateChanged();
            }
            IsBusy = false;
            localImportPerformanceApp?.TryFinalizeDisabledPerformanceSession();
        }
        if (loadSucceeded && analysisCancellationScope == TrackAnalysisCancellationScope.LocalFolderImport)
            ConfirmLoadedPresetBitrateIfNeeded();
    }


    private HashSet<string> RestoreReusableMp3ProbeState(
        string projectFolder,
        string? currentImportedFolder,
        IReadOnlyDictionary<string, TrackInfo>? previousTracksByPath)
    {
        var skipProbePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (previousTracksByPath is null || previousTracksByPath.Count == 0)
            return skipProbePaths;

        foreach (var refreshedTrack in Tracks)
        {
            var trackPath = GetNormalizedTrackPath(projectFolder, refreshedTrack);
            if (string.IsNullOrWhiteSpace(trackPath) ||
                (!string.IsNullOrWhiteSpace(currentImportedFolder) &&
                 TrackPathService.IsPathInsideFolder(trackPath, currentImportedFolder)) ||
                !previousTracksByPath.TryGetValue(trackPath, out var previousTrack) ||
                previousTrack.AudioValidationPassed is null)
            {
                continue;
            }

            refreshedTrack.AudioValidationPassed = previousTrack.AudioValidationPassed;
            refreshedTrack.Duration = previousTrack.Duration;
            refreshedTrack.DurationTicks = previousTrack.DurationTicks;
            refreshedTrack.BitrateKbps = previousTrack.BitrateKbps;
            refreshedTrack.SampleRateHz = previousTrack.SampleRateHz;
            refreshedTrack.Channels = previousTrack.Channels;
            refreshedTrack.ChannelLayout = previousTrack.ChannelLayout;
            refreshedTrack.Codec = previousTrack.Codec;
            refreshedTrack.EmbeddedChapters = previousTrack.EmbeddedChapters.ToList();
            refreshedTrack.ProcessingAction = previousTrack.ProcessingAction;

            if (previousTrack.AudioValidationPassed == false)
                refreshedTrack.SetWarning(previousTrack.Warning, previousTrack.WarningCodes);

            skipProbePaths.Add(trackPath);
        }

        return skipProbePaths;
    }

    private static string GetNormalizedTrackPath(string folderPath, TrackInfo track)
    {
        try
        {
            return Path.GetFullPath(TrackPathService.GetTrackPath(folderPath, track));
        }
        catch
        {
            return string.Empty;
        }
    }

    private async Task<bool> EnrichTracksWithFfprobeAsync(
        string folderPath,
        bool publishWorkflowStatus = true,
        PerformanceExternalProcessContext? performanceContext = null,
        ISet<string>? skipProbePaths = null,
        CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(_ffmpegStatus.FfprobePath))
            return true;

        var trackSnapshot = Tracks.ToList();
        var total = trackSnapshot.Count;
        var processed = 0;
        var failed = 0;
        var workflowOperationId = publishWorkflowStatus
            ? BeginWorkflowStatusOperation(folderPath)
            : Guid.Empty;
        var workflowEnded = false;
        void EndWorkflowIfNeeded()
        {
            if (!publishWorkflowStatus || workflowEnded)
                return;

            EndWorkflowStatusOperation(workflowOperationId);
            workflowEnded = true;
        }

        var probeRequests = new List<AudioProbeBatchRequest>();
        var skippedProbeIndexes = new HashSet<int>();

        try
        {
            for (var index = 0; index < trackSnapshot.Count; index++)
            {
                var track = trackSnapshot[index];
                processed++;
                var percent = total == 0 ? 0 : processed * 100 / total;
                if (publishWorkflowStatus)
                {
                    PublishWorkflowStatus(workflowOperationId, new WorkflowStatusSnapshot
                    {
                    ProjectId = folderPath,
                    ProjectKind = WorkflowProjectKind.Folder,
                    ProjectState = ProjectPipelineState.Preparing,
                    ActiveActivities = new HashSet<WorkflowActivity> { WorkflowActivity.AnalyzingTracks },
                    AnalysisProgress = new AnalysisProgress(WorkflowAnalysisKind.SourceTracks, processed, total, percent),
                        TotalSourceItems = total
                    });
                }

                var trackPath = TrackPathService.GetTrackPath(folderPath, track);
                var normalizedTrackPath = Path.GetFullPath(trackPath);
                if (skipProbePaths?.Contains(normalizedTrackPath) == true)
                {
                    skippedProbeIndexes.Add(index);
                    continue;
                }

                probeRequests.Add(new AudioProbeBatchRequest(index, trackPath));
            }

            var probeResults = await _services.Media.AudioProbeBatchService.ProbeAsync(
            probeRequests,
            _ffmpegStatus.FfprobePath,
            ParallelJobsInput,
            performanceContext,
            new Progress<int>(value =>
            {
                if (!publishWorkflowStatus)
                    return;

                var percent = total == 0 ? 0 : Math.Clamp(value * 100 / total, 0, 100);
                PublishWorkflowStatus(workflowOperationId, new WorkflowStatusSnapshot
                {
                    ProjectId = folderPath,
                    ProjectKind = WorkflowProjectKind.Folder,
                    ProjectState = ProjectPipelineState.Preparing,
                    ActiveActivities = new HashSet<WorkflowActivity> { WorkflowActivity.AnalyzingTracks },
                    AnalysisProgress = new AnalysisProgress(WorkflowAnalysisKind.SourceTracks, value, total, percent),
                    TotalSourceItems = total
                });
            }),
            initialCompleted: total - probeRequests.Count,
            token: token);

            if (token.IsCancellationRequested)
            {
                EndWorkflowIfNeeded();
                return false;
            }

        processed = 0;
        for (var index = 0; index < trackSnapshot.Count; index++)
        {
            processed++;
            if (token.IsCancellationRequested)
            {
                EndWorkflowIfNeeded();
                return false;
            }
            if (skippedProbeIndexes.Contains(index))
                continue;

            var track = trackSnapshot[index];
            var trackPath = TrackPathService.GetTrackPath(folderPath, track);
            var probeInfo = index < probeResults.Length
                ? probeResults[index]
                : null;
            probeInfo ??= new AudioProbeInfo
            {
                FilePath = trackPath,
                Success = false,
                ErrorMessage = LocalizationText.Get("MainWindow.AudioCheck.Warning")
            };
            var decodeConfirmed = (!probeInfo.IsAac && probeInfo.HasPlausibleAudioProperties) ||
                                  await _services.Media.AudioInfoService.CanDecodeAudioAsync(
                                      trackPath,
                                      _ffmpegStatus.FfmpegPath,
                                      token,
                                      performanceContext);

            if (!decodeConfirmed)
            {
                failed++;
                track.AudioValidationPassed = false;
                track.AacOptimizationEligible = false;
                track.Duration = "";
                track.DurationTicks = null;
                track.BitrateKbps = null;
                track.SampleRateHz = null;
                track.Channels = null;
                track.ChannelLayout = "";
                track.Codec = CodecIds.Invalid;
                track.ProcessingAction = ProcessingActionIds.Invalid;
                track.SetWarning(LocalizationText.Get("WarningInvalidAudioSentence"), TrackWarningCodes.InvalidAudio);

                if (processed % 10 == 0 || processed == total)
                    OnExportPreviewChanged();

                continue;
            }

            _services.Media.TrackAudioProbeApplicationService.ApplySuccessfulProbe(
                track,
                probeInfo,
                ExportPreset.Parse(SelectedExportPreset),
                markAudioValidationPassed: true);

            if (processed % 10 == 0 || processed == total)
                OnExportPreviewChanged();
        }

        UpdateIndexes();

            if (failed > 0)
            {
                if (publishWorkflowStatus)
                {
                    EndWorkflowIfNeeded();
                    StatusText = LocalizationText.Format(
                        failed == 1 ? "MainWindow.AudioCheck.Technical.One" : "MainWindow.AudioCheck.Technical.Many",
                        failed);
                }
                return true;
            }

            if (publishWorkflowStatus)
            {
                PublishWorkflowStatus(workflowOperationId, new WorkflowStatusSnapshot
                {
                    ProjectId = folderPath,
                    ProjectKind = WorkflowProjectKind.Folder,
                    ProjectState = ProjectPipelineState.Preparing,
                    IsProjectPrepared = true,
                    TotalSourceItems = total
                });
                EndWorkflowIfNeeded();
            }

            return true;
        }
        catch (OperationCanceledException)
        {
            EndWorkflowIfNeeded();
            return false;
        }
    }

    private void UpdateFinalStatus(int trackCount)
    {
        if (trackCount <= 0 || Tracks.Count == 0)
        {
            StatusText = NoProjectStatusText;
            OnExportPreviewChanged();
            return;
        }

        // A final refresh updates track presentation, not the completed pause status.
        if (_presentationState.IsPipelinePaused)
        {
            OnExportPreviewChanged();
            return;
        }

        var issueSummary = _services.Tracks.TrackIssueSummaryService.Create(Tracks);

        var types = Tracks
            .GroupBy(track => track.Extension)
            .OrderBy(group => group.Key)
            .Select(group => $"{group.Count()} {group.Key}");

        var typeText = string.Join(", ", types);

        var actionText = BuildProcessingActionSummary();

        var issueText = issueSummary.ToDisplayText();

        StatusText = LocalizationText.Format(
            trackCount == 1 ? "MainWindow.Summary.AudioFiles.One" : "MainWindow.Summary.AudioFiles.Many",
            trackCount, typeText, actionText, issueText);

        OnExportPreviewChanged();
    }

    private void SuggestMetadataFromTracksAndFolder(string displayFolderPath)
    {
        if (Tracks.Count == 0)
            return;

        var sourceFiles = Tracks
            .Select(track => track.FilePath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .ToArray();
        _services.Media.ProjectMetadataPreviewAggregationService.Apply(
            _projectMetadata,
            Tracks.ToArray(),
            CreateLocalFolderAggregationOptions(displayFolderPath, sourceFiles));

        OnExportPreviewChanged();
    }

    private async Task ShowCurrentProjectSetupDialogAsync()
    {
        if (!await EnsureResumeDiscPreviewAsync())
            return;
        var sourceKind = ResolveCurrentProjectSetupSourceKind();
        var sourceFolder = ResolveCurrentProjectSetupSourceFolder(sourceKind);
        var setupDialog = CreateProjectSetupDialog(new ProjectSetupDialogRequest(
            SourceKind: sourceKind,
            IsDiscResume: !string.IsNullOrWhiteSpace(_lockedDiscResumePreset),
            IsContinuation: LoadedProjectState.HasLoadedProject &&
                ProjectReviewState.Phase is not (ProjectPipelineState.Preparing or ProjectPipelineState.Completed),
            ConfirmDiscCountOnStart: _pendingMp3DiscSetupResult is not null ||
                (_pendingAudioDisc is not null && _isAudioDiscProjectAwaitingRip),
            WindowTitle: sourceKind switch
            {
                ProjectSetupSourceKind.Mp3Disc => LocalizationText.Get("MainWindow.Setup.Mp3Prepare"),
                ProjectSetupSourceKind.AudioDisc => LocalizationText.Get("MainWindow.Setup.AudioPrepare"),
                _ => LocalizationText.Get("MainWindow.Setup.FolderPrepare")
            },
            SourceInformation: BuildCurrentProjectSetupSourceInformation(sourceKind),
            Instruction: sourceKind == ProjectSetupSourceKind.Folder
                ? LocalizationText.Get("MainWindow.Setup.FolderInstruction")
                : LocalizationText.Get("MainWindow.Setup.DiscInstruction"),
            DefaultDiscCount: ResolveCurrentProjectSetupDiscCount(sourceKind),
            MinimumDiscs: 1,
            MaximumDiscs: sourceKind == ProjectSetupSourceKind.Folder ? 1 : 99,
            ExportPresets: ExportPresets,
            SelectedExportPreset: SelectedExportPreset,
            ParallelJobs: ParallelJobsInput,
            OutputExtension: OutputExtension,
            OutputFolder: OutputFolder,
            BookTitle: BookTitle,
            Album: Album,
            Author: Author,
            Narrator: Narrator,
            Genre: Genre,
            FileNameTemplate: FileNameTemplate,
            CoverSourcePath: GetCoverSourcePathForProjectSetup(),
            CoverPreviewSource: CoverPreviewSource,
            CoverWorkFolder: GetProjectFolderStructure().CoversFolder,
            AutoMergeAfterConversion: _settings.MergeAutomaticallyAfterConversion,
            UsePrivateGenreList: _settings.UsePrivateGenreList,
            GenreOptions: GenreListService.GetGenres(_settings),
            SourceFolder: sourceFolder,
            MaxSourceBitrateKbps: GetMaxTrackBitrateKbps(),
            LastCoverFolder: _settings.LastCoverFolder ?? string.Empty));

        var startProject = ModalDialogService.ShowDialog(setupDialog, this) == true;
        FlushProjectSetupDiscCountPreviewRefresh();
        ApplyDiscProjectSetup(setupDialog.Result);

        if (sourceKind == ProjectSetupSourceKind.Mp3Disc &&
            _pendingMp3DiscSetupResult is not null &&
            _pendingMp3DiscQuickIdentity is not null)
        {
            StorePendingMp3DiscPreparation(
                _pendingDiscProjectSourceFolder,
                setupDialog.Result,
                _pendingMp3DiscQuickIdentity,
                _pendingMp3DiscStructureSignature);
        }

        if (!startProject)
            return;
        if (!await EnsureResumeDiscPreviewAsync())
            return;

        _resumeDiscPreview.MarkProcessingStarted();
        NotifyExportUiStateChanged();
        if (!string.IsNullOrWhiteSpace(_lockedDiscResumePreset))
            ApplyPipelineTransition(_services.Pipeline.PipelinePauseTransitionService.CreateContinuationStart());

        if (sourceKind == ProjectSetupSourceKind.Mp3Disc && LoadedProjectState.NeedsDiscImport)
        {
            await ContinuePausedMp3DiscProjectAsync();
            return;
        }

        if (sourceKind == ProjectSetupSourceKind.Mp3Disc &&
            _pendingMp3DiscSetupResult is not null &&
            _pendingMp3DiscQuickIdentity is not null &&
            !string.IsNullOrWhiteSpace(_pendingMp3DiscStructureSignature))
        {
            await StartConfirmedMp3DiscProjectAsync(
                _pendingDiscProjectSourceFolder,
                setupDialog.Result,
                _pendingMp3DiscQuickIdentity,
                _pendingMp3DiscStructureSignature);
            return;
        }

        if (sourceKind == ProjectSetupSourceKind.AudioDisc && _isAudioDiscProjectAwaitingRip)
        {
            if (_activeAudioDiscManifest is not null)
            {
                if (_pendingAudioDisc is not null)
                {
                    _pendingAudioDiscTotalDiscs = setupDialog.Result.TotalDiscs;
                    _activeAudioDiscManifest.TotalDiscs = setupDialog.Result.TotalDiscs;
                }
                SaveCurrentAudioDiscProjectSnapshot(_activeAudioDiscManifest, force: true);
            }

            await RunPreparedAudioDiscRipAsync();
            return;
        }

        if (!string.IsNullOrWhiteSpace(_lockedDiscResumePreset))
        {
            await RunCurrentExportPlanAsync(_currentProjectWorkFolder,
                sourceKind == ProjectSetupSourceKind.AudioDisc ? ProjectManifestTypes.AudioCdProject : ProjectManifestTypes.Mp3DiscProject);
            return;
        }
        await RunCurrentExportPlanAsync();
    }

    private ProjectSetupSourceKind ResolveCurrentProjectSetupSourceKind()
    {
        if (_pendingMp3DiscSetupResult is not null || LoadedProjectState.IsMp3Disc)
            return ProjectSetupSourceKind.Mp3Disc;

        if (_activeAudioDiscManifest is not null || _pendingAudioDisc is not null || LoadedProjectState.IsAudioDisc || _isAudioDiscProjectAwaitingRip)
            return ProjectSetupSourceKind.AudioDisc;

        return ProjectSetupSourceKind.Folder;
    }

    private string ResolveCurrentProjectSetupSourceFolder(ProjectSetupSourceKind sourceKind)
    {
        if (sourceKind is ProjectSetupSourceKind.Mp3Disc or ProjectSetupSourceKind.AudioDisc &&
            _resumeDiscPreview.DiscNumber is not null && !string.IsNullOrWhiteSpace(_resumeDiscStartDriveRoot))
            return _resumeDiscStartDriveRoot;

        if (sourceKind == ProjectSetupSourceKind.Mp3Disc && !string.IsNullOrWhiteSpace(_pendingDiscProjectSourceFolder))
            return _pendingDiscProjectSourceFolder;

        if (sourceKind == ProjectSetupSourceKind.AudioDisc)
            return _activeAudioDiscManifest?.SourceDriveRoot ?? _pendingAudioDisc?.DriveRoot ?? SelectedSourceOpenPath;

        return string.IsNullOrWhiteSpace(_currentFolderPath) ? SelectedSourceOpenPath : _currentFolderPath;
    }

    private int ResolveCurrentProjectSetupDiscCount(ProjectSetupSourceKind sourceKind)
    {
        if (sourceKind == ProjectSetupSourceKind.Mp3Disc)
            return _pendingMp3DiscSetupResult?.TotalDiscs ??
                TryLoadCurrentMp3DiscProjectManifest(resetStateWhenFolderMissing: false)?.TotalDiscs ?? 1;

        if (sourceKind == ProjectSetupSourceKind.AudioDisc)
            return _activeAudioDiscManifest?.TotalDiscs ?? _pendingAudioDiscTotalDiscs;

        return 1;
    }

    private string BuildCurrentProjectSetupSourceInformation(ProjectSetupSourceKind sourceKind)
    {
        if (!string.IsNullOrWhiteSpace(_lockedDiscResumePreset))
        {
            var plan = _services.Foundation.ProjectResumePlanService.BuildFromProjectFolder(_currentProjectWorkFolder);
            if (plan is not null)
                return plan.NextMissingDiscNumber is { } next
                    ? LocalizationText.Format("Disc.Resume.NextDisc", next, plan.TotalDiscs)
                    : LocalizationText.Get("Disc.Resume.AllSourcesReady");
        }
        if (sourceKind == ProjectSetupSourceKind.AudioDisc && _pendingAudioDisc is not null)
            return LocalizationText.Format("MainWindow.Setup.AudioSourceInformation", _pendingAudioDisc.TrackCount, _pendingAudioDisc.TotalDurationText);

        if (sourceKind == ProjectSetupSourceKind.Mp3Disc)
            return LocalizationText.Format("MainWindow.Setup.Mp3SourceInformation", Tracks.Count, BuildProjectSetupDurationText(Tracks));

        return LocalizationText.Format("MainWindow.Setup.FolderSourceInformation", Tracks.Count, BuildProjectSetupDurationText(Tracks));
    }

    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        if (_isWorkspaceReplacementInProgress || _isProjectCloseInProgress || _isShutdownInProgress) return;
        if (_presentationState.UiTransition is not null || _presentationState.PauseRequested) return;
        if (_isDiscStopSaveInProgress) return;
        if (IsPipelinePauseCommandActive())
        {
            await RequestPipelinePauseAsync();
            return;
        }

        if (IsBusy)
        {
            if (ProjectReviewState.Phase is ProjectPipelineState.AcquiringSources or ProjectPipelineState.Converting)
                await RequestPipelinePauseAsync();

            return;
        }

        if (_presentationState.IsPipelineFailed &&
            ProjectReviewState.Phase is not (ProjectPipelineState.ReviewBeforeMerge or ProjectPipelineState.Completed))
        {
            CloseCurrentProject();
            return;
        }

        if (_presentationState.IsPipelinePaused)
        {
            await ContinuePausedPipelineAsync();
            return;
        }

        if (ProjectReviewState.Phase == ProjectPipelineState.Preparing &&
            Tracks.Count > 0 &&
            !LoadedProjectState.NeedsDiscImport)
        {
            await ShowCurrentProjectSetupDialogAsync();
            return;
        }

        if (_isAudioDiscProjectAwaitingRip)
        {
            await RunPreparedAudioDiscRipAsync();
            return;
        }

        if (LoadedProjectState.NeedsDiscImport)
        {
            await ContinueLoadedMp3DiscProjectAsync();
            return;
        }

        // Only this user command grants a one-run manual merge. Resume/import paths above
        // and all automatic callers keep the default false, regardless of project history.
        var manualMergeRequested =
            ProjectReviewState.Phase is ProjectPipelineState.ReviewBeforeMerge or ProjectPipelineState.Completed &&
            _manualMergeReviewHasPreparedTracks && !_manualMergeReviewNeedsReconversion;

        if (LoadedProjectState.IsMp3Disc)
        {
            await RunLoadedMp3DiscProjectExportAsync(manualMergeRequested);
            return;
        }

        if (!string.IsNullOrWhiteSpace(_pendingDiscProjectSourceFolder))
        {
            if (_pendingMp3DiscSetupResult is not null &&
                _pendingMp3DiscQuickIdentity is not null &&
                !string.IsNullOrWhiteSpace(_pendingMp3DiscStructureSignature))
            {
                await StartConfirmedMp3DiscProjectAsync(
                    _pendingDiscProjectSourceFolder,
                    CreateMp3DiscSetupFromCurrentUi(_pendingMp3DiscSetupResult),
                    _pendingMp3DiscQuickIdentity,
                    _pendingMp3DiscStructureSignature);
            }
            else
            {
                await StartMp3DiscProjectAsync(_pendingDiscProjectSourceFolder);
            }

            return;
        }

        RestoreActiveAudioDiscManifestFromCurrentProject();

        if (_activeAudioDiscManifest is not null &&
            !_isAudioDiscProjectAwaitingRip &&
            !string.IsNullOrWhiteSpace(_currentProjectWorkFolder))
        {
            await RunCurrentExportPlanAsync(_currentProjectWorkFolder, ProjectManifestTypes.AudioCdProject, manualMergeRequested);
            return;
        }

        await RunCurrentExportPlanAsync(manualMergeRequested: manualMergeRequested);
    }

    private bool IsPipelinePauseCommandActive()
    {
        return ProjectReviewState.Phase is ProjectPipelineState.AcquiringSources or ProjectPipelineState.Converting or ProjectPipelineState.Merging &&
               (IsDiscImporting || IsExporting) &&
               !_presentationState.IsPipelinePaused &&
               !_presentationState.IsPipelineFailed;
    }


    private async Task RunPreparedAudioDiscRipAsync(
        ProjectExtensionRollbackSnapshot? extensionSnapshot = null,
        ProjectPipelineState? extensionReturnState = null,
        bool extensionReturnCompletedState = false,
        Action? restoreUiState = null,
        bool queueExistingRippedTracks = true)
    {
        if (_isWorkspaceReplacementInProgress || _isProjectCloseInProgress || _isShutdownInProgress) return;
        if (_sessionProject.IsUnstartedAudio(_currentProjectWorkFolder) && _activeAudioDiscManifest is { } unstarted)
        {
            unstarted.TargetSampleRateHz = SampleRateSettingsService.Normalize(_settings.DefaultSampleRateHz);
            _services.Foundation.AudioDiscProjectService.Save(unstarted);
        }
        _sessionProject.AcceptStart(_currentProjectWorkFolder);
        var manifest = _activeAudioDiscManifest;
        if (manifest is null || manifest.TotalDiscs < 1)
        {
            AppDialogService.Warning(
                this,
                LocalizationText.Get("MainWindow.AudioProject.NotReady.Title"),
                LocalizationText.Get("MainWindow.AudioProject.NotReady.Message"));
            return;
        }

        if (!_ffmpegStatus.FfmpegAvailable || string.IsNullOrWhiteSpace(_ffmpegStatus.FfmpegPath))
        {
            AppDialogService.Warning(
                this,
                LocalizationText.Get("MainWindow.Ffmpeg.NotReady.Title"),
                LocalizationText.Get("MainWindow.Ffmpeg.NotReady.RippingMessage"));
            return;
        }

        if (!await EnsureResumeDiscPreviewAsync())
            return;
        if (_isShutdownInProgress || _isProjectCloseInProgress || _allowWindowClose ||
            !ReferenceEquals(manifest, _activeAudioDiscManifest)) return;
        var selectedStartDrive = _resumeDiscStartDriveRoot;
        AudioDiscRunPreparation runPreparation = null!;
        var audioRequestedParallelJobs = ParallelJobsInput;
        AudioDiscLiveConversionSession liveConversionSession;
        var audioStatusOperationId = BeginWorkflowStatusOperation(manifest.ProjectFolder);
        var statusDiscNumber = Math.Max(1, _services.Foundation.AudioDiscProjectService.GetNextRequiredDiscNumber(manifest) ?? 1);
        var statusCurrentCompleted = 0;
        var statusCurrentTotal = 0;
        var statusCurrentDiscFinished = false;
        var statusAllDiscsFinished = false;
        var audioPerformanceApp = Application.Current as App;

        PerformanceOperationScope StartAudioDiscConversionPerformanceOperation(AudioDiscRippedTrack rippedTrack)
        {
            if (audioPerformanceApp is null)
                return default;

            var scope = audioPerformanceApp.StartPerformanceOperation(
                "disc.audio.convert",
                PerformanceProjectType.AudioCd,
                new PerformanceWorkloadSnapshot
                {
                    ProjectType = PerformanceProjectType.AudioCd,
                    DiscCount = 1,
                    TrackCount = 1,
                    FileCount = 1,
                    InputAudioDurationMilliseconds = rippedTrack.Duration.TotalMilliseconds
                });

            if (!scope.IsActive)
                return scope;

            long? inputBytes = null;
            try
            {
                if (File.Exists(rippedTrack.FilePath))
                    inputBytes = new FileInfo(rippedTrack.FilePath).Length;
            }
            catch
            {
                // Workload-Größe ist best effort und darf die Konvertierung nie beeinflussen.
            }

            audioPerformanceApp.UpdatePerformanceWorkload(
                scope.OperationId,
                new PerformanceWorkloadSnapshot
                {
                    ProjectType = PerformanceProjectType.AudioCd,
                    DiscCount = 1,
                    TrackCount = 1,
                    FileCount = 1,
                    InputBytes = inputBytes,
                    InputAudioDurationMilliseconds = rippedTrack.Duration.TotalMilliseconds
                });
            audioPerformanceApp.RecordPerformanceDriveRole(
                scope.OperationId,
                manifest.ProjectFolder,
                PerformanceDriveRole.ProjectWorking);

            audioPerformanceApp.PerformanceTraceService?.RecordParallelism(
                scope.OperationId, audioRequestedParallelJobs, runPreparation.ParallelJobs);
            return scope;
        }

        void PublishAudioDiscStatus(AudioDiscLiveConversionSessionSnapshot conversionSnapshot, bool isPaused = false)
        {
            var totalKnownTracks = manifest.Discs.Sum(disc => Math.Max(disc.TrackCount, disc.Tracks.Count));
            var totalRippedTracks = manifest.Discs.Sum(disc => disc.Tracks.Count(track =>
                string.Equals(track.Status, AudioDiscTrackStatus.Ripped, StringComparison.OrdinalIgnoreCase)));
            var convertedTracks = Math.Min(totalKnownTracks, conversionSnapshot.ConvertedCount);
            const string format = "FLAC";

            PublishWorkflowStatus(
                audioStatusOperationId,
                _services.Status.AudioDiscWorkflowStatusAdapter.CreateRunningSnapshot(
                    manifest.ProjectFolder,
                    statusDiscNumber,
                    manifest.TotalDiscs,
                    statusCurrentCompleted,
                    statusCurrentTotal,
                    totalRippedTracks,
                    totalKnownTracks,
                    convertedTracks,
                    conversionSnapshot.ActiveTrackNumbers ?? Array.Empty<int>(),
                    runPreparation.Preset,
                    format,
                    isPaused,
                    statusCurrentDiscFinished,
                    statusAllDiscsFinished,
                    extensionSnapshot is not null,
                    driveDisplayName: DiscDriveSelectionPresentationService.FormatStatusDriveLabel(
                        manifest.Discs.FirstOrDefault(disc => disc.DiscNumber == statusDiscNumber)?.SourceDriveRoot ?? manifest.SourceDriveRoot,
                        _settings.DiscDriveOrder)));
        }

        try
        {
            restoreUiState?.Invoke();
            BackfillMissingSourceMetadataSnapshots(manifest.ProjectFolder);
            runPreparation = _services.Workflows.AudioDiscRunPreparationService.Prepare(
                manifest,
                CreateProjectSnapshotFromUi(),
                ResolveParallelJobCount());
            audioRequestedParallelJobs = ParallelJobsInput;
            restoreUiState?.Invoke();
            var workManifest = _services.Foundation.WorkManifestService.LoadOrCreate(
                ProjectFolderLayout.ResolveExportManifestPath(manifest.ProjectFolder),
                ProjectManifestTypes.AudioCdProject,
                manifest.ProjectFolder,
                ProjectFolderLayout.GetOriginalsFolder(manifest.ProjectFolder),
                runPreparation.Preset.DisplayName);
            var existingConvertedCount = _services.Foundation.WorkManifestService.CountReusableConvertedTracks(
                workManifest,
                runPreparation.Preset);

            liveConversionSession = new AudioDiscLiveConversionSession(
                _services.Discs.AudioDiscLiveConversionService,
                _services.Workflows.LiveConversionWorkflowService,
                _services.Foundation.WorkManifestService,
                manifest,
                runPreparation.Preset,
                runPreparation.ParallelJobs,
                snapshot => PublishAudioDiscStatus(snapshot),
                existingConvertedCount,
                startPerformanceOperation: StartAudioDiscConversionPerformanceOperation,
                createPerformanceContext: operationId =>
                    audioPerformanceApp?.CreatePerformanceExternalProcessContext(
                        operationId,
                        purpose: "audio.convert"),
                tryFinalizePerformanceSession: () =>
                    audioPerformanceApp?.TryFinalizeDisabledPerformanceSession(),
                ffprobePath: _ffmpegStatus.FfprobePath ?? "",
                isPauseRequested: () => _presentationState.PauseRequested);

            var initialDisc = manifest.Discs
                .OrderBy(disc => disc.DiscNumber)
                .FirstOrDefault(disc => disc.DiscNumber == statusDiscNumber);
            statusDiscNumber = initialDisc?.DiscNumber ?? statusDiscNumber;
            statusCurrentTotal = initialDisc is null ? 0 : Math.Max(initialDisc.TrackCount, initialDisc.Tracks.Count);
            statusCurrentCompleted = initialDisc?.Tracks.Count(track =>
                string.Equals(track.Status, AudioDiscTrackStatus.Ripped, StringComparison.OrdinalIgnoreCase)) ?? 0;
            PublishAudioDiscStatus(liveConversionSession.GetSnapshot());
        }
        catch (Exception ex)
        {
            EndWorkflowStatusOperation(audioStatusOperationId);
            AppDialogService.Error(
                this,
                LocalizationText.Get("MainWindow.AudioProject.ResumePreparationFailed.Title"),
                LocalizationText.Get("MainWindow.AudioProject.ResumePreparationFailed.Message"),
                details: new[] { ex.Message });
            return;
        }

        using var liveConversionSessionScope = liveConversionSession;
        _discImportCancellation?.Dispose();
        _discImportCancellation = new CancellationTokenSource();
        SetPipelineState(ProjectPipelineState.AcquiringSources);
        IsBusy = true;
        IsDiscImporting = true;
        IsProgressIndeterminate = false;
        ExportProgressPercent = 0;
        var continueWithExport = false;
        var extensionCanceled = false;
        var lastAudioDiscSnapshotUtc = DateTime.UtcNow;

        async Task WaitForLiveConversionsAsync()
        {
            await liveConversionSession.WaitForCompletionAsync(
                () =>
                {
                    SaveCurrentAudioDiscProjectSnapshot(manifest);
                    liveConversionSession.SaveManifestSnapshot();
                },
                ProjectSnapshotInterval);
        }

        void SaveTimedAudioDiscProjectSnapshot()
        {
            var now = DateTime.UtcNow;
            if (now - lastAudioDiscSnapshotUtc < ProjectSnapshotInterval)
                return;

            SaveCurrentAudioDiscProjectSnapshot(manifest);
            lastAudioDiscSnapshotUtc = now;
        }

        async Task QueueAudioDiscRippedTrackAsync(AudioDiscRippedTrack rippedTrack, CancellationToken token)
        {
            var track = new TrackInfo
            {
                SourceId = rippedTrack.SourceId,
                Index = rippedTrack.GlobalIndex,
                DiscNumber = rippedTrack.DiscNumber,
                TrackNumber = rippedTrack.TrackNumber,
                FilePath = rippedTrack.FilePath,
                FileName = Path.GetFileName(rippedTrack.FilePath),
                Extension = Path.GetExtension(rippedTrack.FilePath).TrimStart('.'),
                Duration = rippedTrack.Duration.ToString(@"hh\:mm\:ss", System.Globalization.CultureInfo.InvariantCulture),
                DurationTicks = rippedTrack.Duration.Ticks,
                BitrateKbps = 1411,
                SampleRateHz = 44100,
                Channels = 2,
                Codec = "PCM"
            };

            PublishSourceMetadataSnapshot(
                manifest.ProjectFolder,
                rippedTrack.FilePath,
                track,
                evidence: "audio-disc-rip");
            await liveConversionSession.QueueAsync(rippedTrack, token);
        }

        async Task<AudioDiscRipResult> RipAudioDiscWithPerformanceAsync(
            AudioDiscProjectManifestDisc disc,
            IProgress<AudioDiscRipProgress> progress,
            CancellationToken token)
        {
            PerformanceOperationScope operationScope = default;
            PerformancePhaseScope ripPhase = default;
            AudioDiscProjectManifestTrack[] measuredTracks = [];
            var retryCount = 0;

            try
            {
                if (audioPerformanceApp is not null)
                {
                    operationScope = audioPerformanceApp.StartPerformanceOperation(
                        "disc.audio.rip",
                        PerformanceProjectType.AudioCd,
                        new PerformanceWorkloadSnapshot
                        {
                            ProjectType = PerformanceProjectType.AudioCd,
                            DiscCount = 1
                        });
                }

                if (operationScope.IsActive && audioPerformanceApp is not null)
                {
                    measuredTracks = disc.Tracks
                        .Where(track =>
                            !(string.Equals(track.Status, AudioDiscTrackStatus.Ripped, StringComparison.OrdinalIgnoreCase) &&
                              File.Exists(Path.Combine(manifest.ProjectFolder, track.RelativePath))))
                        .ToArray();

                    var inputBytes = measuredTracks
                        .Where(track => track.SectorCount > 0)
                        .Sum(track => (long)track.SectorCount * 2352L);
                    var inputDurationMilliseconds = measuredTracks.Sum(track => track.Duration.TotalMilliseconds);

                    audioPerformanceApp.UpdatePerformanceWorkload(
                        operationScope.OperationId,
                        new PerformanceWorkloadSnapshot
                        {
                            ProjectType = PerformanceProjectType.AudioCd,
                            DiscCount = 1,
                            TrackCount = measuredTracks.Length,
                            FileCount = measuredTracks.Length,
                            InputBytes = inputBytes,
                            InputAudioDurationMilliseconds = inputDurationMilliseconds,
                            RetryCount = 0
                        });
                    audioPerformanceApp.RecordPerformanceDriveRole(
                        operationScope.OperationId,
                        disc.SourceDriveRoot,
                        PerformanceDriveRole.Source | PerformanceDriveRole.OpticalSource);
                    audioPerformanceApp.RecordPerformanceDriveRole(
                        operationScope.OperationId,
                        manifest.ProjectFolder,
                        PerformanceDriveRole.ProjectWorking);

                    var knownOutputFolder =
                        !string.IsNullOrWhiteSpace(manifest.OutputFolder) && Directory.Exists(manifest.OutputFolder)
                            ? manifest.OutputFolder
                            : EffectiveOutputFolder;
                    if (!string.IsNullOrWhiteSpace(knownOutputFolder))
                    {
                        audioPerformanceApp.RecordPerformanceDriveRole(
                            operationScope.OperationId,
                            knownOutputFolder,
                            PerformanceDriveRole.Output);
                    }

                    audioPerformanceApp.PerformanceTraceService?.RecordParallelism(
                        operationScope.OperationId, audioRequestedParallelJobs, 1);
                    audioPerformanceApp.PerformanceTraceService?.RecordMetric(operationScope.OperationId, "ConversionWorkerLimit", runPreparation.ParallelJobs, "workers");
                    ripPhase = audioPerformanceApp.StartPerformancePhase(
                        operationScope.OperationId,
                        "disc.rip");
                }

                var performanceContext = operationScope.IsActive && audioPerformanceApp is not null
                    ? audioPerformanceApp.CreatePerformanceExternalProcessContext(
                        operationScope.OperationId,
                        ripPhase.IsActive ? ripPhase.PhaseId : null,
                        "audio.rip.flac")
                    : null;

                var result = await Task.Run(
                    () => _services.Discs.AudioDiscRipService.RipDiscToFlacAsync(
                        manifest,
                        disc,
                        _ffmpegStatus.FfmpegPath,
                        token,
                        progress,
                        QueueAudioDiscRippedTrackAsync,
                        performanceContext,
                        operationScope.IsActive
                            ? () => Interlocked.Increment(ref retryCount)
                            : null,
                        sourceReadStarted: source => PublishDiscReadSource(source, manifest.ProjectFolder, token)),
                    token);

                if (operationScope.IsActive && audioPerformanceApp is not null)
                {
                    var completedMeasuredTracks = measuredTracks
                        .Where(track => string.Equals(
                            track.Status,
                            AudioDiscTrackStatus.Ripped,
                            StringComparison.OrdinalIgnoreCase))
                        .ToArray();
                    var outputBytes = completedMeasuredTracks
                        .Where(track => track.OutputSizeBytes.HasValue)
                        .Sum(track => track.OutputSizeBytes!.Value);
                    var outputDurationMilliseconds = completedMeasuredTracks
                        .Sum(track => track.Duration.TotalMilliseconds);
                    var inputBytes = measuredTracks
                        .Where(track => track.SectorCount > 0)
                        .Sum(track => (long)track.SectorCount * 2352L);

                    audioPerformanceApp.UpdatePerformanceWorkload(
                        operationScope.OperationId,
                        new PerformanceWorkloadSnapshot
                        {
                            ProjectType = PerformanceProjectType.AudioCd,
                            DiscCount = 1,
                            TrackCount = measuredTracks.Length,
                            FileCount = measuredTracks.Length,
                            InputBytes = inputBytes,
                            OutputBytes = outputBytes,
                            InputAudioDurationMilliseconds = measuredTracks.Sum(track => track.Duration.TotalMilliseconds),
                            OutputAudioDurationMilliseconds = outputDurationMilliseconds,
                            RetryCount = retryCount
                        });
                }

                if (result.Succeeded)
                {
                    ripPhase.Complete();
                    operationScope.Complete();
                }
                else if (result.WasCanceled)
                {
                    ripPhase.Cancel(_presentationState.PauseRequested);
                    operationScope.Cancel(_presentationState.PauseRequested);
                }
                else
                {
                    var performanceFailure = new InvalidOperationException("Audio-CD-Ripping fehlgeschlagen.");
                    ripPhase.Fail(performanceFailure);
                    operationScope.Fail(performanceFailure);
                }

                return result;
            }
            catch (OperationCanceledException)
            {
                ripPhase.Cancel(_presentationState.PauseRequested);
                operationScope.Cancel(_presentationState.PauseRequested);
                throw;
            }
            catch
            {
                var performanceFailure = new InvalidOperationException("Audio-CD-Ripping wurde durch eine Ausnahme beendet.");
                ripPhase.Fail(performanceFailure);
                operationScope.Fail(performanceFailure);
                throw;
            }
            finally
            {
                audioPerformanceApp?.TryFinalizeDisabledPerformanceSession();
            }
        }

        try
        {
            _resumeDiscStartDriveRoot = null;
            var firstRequiredDiscNumber = _services.Foundation.AudioDiscProjectService.GetNextRequiredDiscNumber(manifest);
            var workflowResult = await _services.Workflows.AudioDiscRipWorkflowService.RunAsync(
                new AudioDiscRipWorkflowRequest(
                    manifest,
                    _discImportCancellation.Token,
                    (discNumber, token) => WaitForNextAudioDiscAsync(manifest, discNumber, token,
                        startWithSelectedDrive: (extensionSnapshot is not null || selectedStartDrive is not null) && discNumber == firstRequiredDiscNumber,
                        selectedSource: discNumber == firstRequiredDiscNumber ? selectedStartDrive : null),
                    (disc, token) => WaitForRequiredAudioDiscAsync(manifest, disc, token,
                        disc.DiscNumber == firstRequiredDiscNumber ? selectedStartDrive : null),
                    (disc, token) => ConfirmRequiredAudioDiscStillAvailableAsync(manifest, disc, token),
                    (disc, progress, token) =>
                        RipAudioDiscWithPerformanceAsync(disc, progress, token),
                    (disc, token) => _services.Discs.DiscDriveService.TryEjectDiscAsync(disc.SourceDriveRoot, token),
                    () => queueExistingRippedTracks
                        ? liveConversionSession.QueueExistingRippedTracksAsync(
                            manifest,
                            _discImportCancellation.Token)
                        : Task.CompletedTask,
                    WaitForLiveConversionsAsync,
                    () => SaveCurrentAudioDiscProjectSnapshot(manifest, force: true),
                    liveConversionSession.MarkManifestCanceled,
                    liveConversionSession.MarkManifestFailed,
                    (disc, pollingResult) => _services.Foundation.AudioDiscProjectService.UpdateDiscSourceDrive(
                        manifest,
                        disc.DiscNumber,
                        pollingResult.Disc!,
                        pollingResult.DriveInfo),
                    (disc, value) =>
                    {
                        statusDiscNumber = disc.DiscNumber;
                        statusCurrentCompleted = value.CompletedTracks;
                        statusCurrentTotal = value.TotalTracks;
                        statusCurrentDiscFinished = false;
                        statusAllDiscsFinished = false;
                        PublishAudioDiscStatus(liveConversionSession.GetSnapshot());
                        if (!string.Equals(_audioDiscElapsedProjectFolder, manifest.ProjectFolder, StringComparison.OrdinalIgnoreCase) ||
                            _audioDiscElapsedDiscNumber != disc.DiscNumber)
                        {
                            _audioDiscElapsedProjectFolder = manifest.ProjectFolder;
                            _audioDiscElapsedDiscNumber = disc.DiscNumber;
                            _audioDiscElapsedBeforeCurrentRun = TimeSpan.Zero;
                            _audioDiscCurrentRunElapsed = TimeSpan.Zero;
                        }

                        _audioDiscCurrentRunElapsed = value.Elapsed;
                        SaveTimedAudioDiscProjectSnapshot();
                    },
                    disc =>
                    {
                        statusDiscNumber = disc.DiscNumber;
                        PublishAudioDiscStatus(liveConversionSession.GetSnapshot());
                    },
                    (disc, ejected) =>
                    {
                        statusDiscNumber = disc.DiscNumber;
                        statusCurrentTotal = Math.Max(disc.TrackCount, disc.Tracks.Count);
                        statusCurrentCompleted = statusCurrentTotal;
                        statusCurrentDiscFinished = disc.DiscNumber < manifest.TotalDiscs;
                        statusAllDiscsFinished = disc.DiscNumber >= manifest.TotalDiscs;
                        PublishAudioDiscStatus(liveConversionSession.GetSnapshot());
                        ApplySourceMetadataAggregation(
                            manifest.ProjectFolder,
                            disc.DiscNumber == 1
                                ? ProjectMetadataInitializationState.InitialSetComplete
                                : ProjectMetadataInitializationState.SubsequentSource,
                            CreateAudioDiscProjectAggregationOptions());
                    }));

            switch (workflowResult.Outcome)
            {
                case AudioDiscRipWorkflowOutcome.Completed:
                    var completion = await _services.Workflows.AudioDiscRipCompletionWorkflowService.RunAsync(
                        new AudioDiscRipCompletionWorkflowRequest(
                            manifest,
                            Tracks,
                            liveConversionSession.GetSnapshot(),
                            rippedFolder => LoadFolderAsync(
                                rippedFolder,
                                metadataSourceFolder: manifest.ProjectFolder,
                                updateMetadata: false,
                                removeGeneratedOrWorkFiles: false,
                                prepareScannedTracksBeforeIndexUpdate: tracks =>
                                    _services.Foundation.AudioDiscProjectService.ApplyManifestOrderAndMetadataToRippedTracks(
                                        manifest,
                                        tracks)),
                            ApplyPersistedTrackListState,
                            UpdateIndexes,
                            RefreshTrackPresentationViews,
                            OnExportPreviewChanged,
                            NotifyExportUiStateChanged));

                    _isAudioDiscProjectAwaitingRip = false;
                    _currentProjectWorkFolder = manifest.ProjectFolder;
                    _activeAudioDiscManifest = manifest;
                    ApplyLoadedProjectState(LoadedProjectState.WithAudioDiscState(true));
                    statusAllDiscsFinished = true;
                    statusCurrentDiscFinished = true;
                    SetPipelineState(ProjectPipelineState.Converting);
                    PublishAudioDiscStatus(liveConversionSession.GetSnapshot());
                    continueWithExport = true;
                    break;

                case AudioDiscRipWorkflowOutcome.Canceled:
                    _discImportCancellation.Cancel();
                    await WaitForLiveConversionsAsync();
                    extensionCanceled = extensionSnapshot is not null && !_presentationState.PauseRequested;
                    if (_presentationState.PauseRequested)
                    {
                        PauseAudioDiscElapsedTimer();
                        PublishAudioDiscStatus(liveConversionSession.GetSnapshot(), isPaused: true);
                        EnterPipelinePause(string.Empty, preserveWorkflowStatus: true);
                    }
                    else
                    {
                        StatusText = LocalizationText.Format("Disc.Audio.RipCanceledWithTracks", workflowResult.CompletedTracks);
                        ExportProgressText = LocalizationText.Get("Disc.Audio.OperationCanceled");
                    }
                    break;

                case AudioDiscRipWorkflowOutcome.WaitingForDisc:
                    _discImportCancellation.Cancel();
                    await WaitForLiveConversionsAsync();
                    extensionCanceled = false;
                    PauseAudioDiscElapsedTimer();
                    PublishAudioDiscStatus(liveConversionSession.GetSnapshot(), isPaused: true);
                    EnterPipelinePause(string.Empty, preserveWorkflowStatus: true);
                    break;

                case AudioDiscRipWorkflowOutcome.Failed:
                    if (extensionSnapshot is not null)
                    {
                        _discImportCancellation.Cancel();
                        await WaitForLiveConversionsAsync();
                        EnterPipelinePause(LocalizationText.Get("MainWindow.Export.ConversionPaused"));
                    }
                    AppDialogService.Error(
                        this,
                        LocalizationText.Get("Disc.Audio.RipIncompleteTitle"),
                        workflowResult.Message);
                    StatusText = LocalizationText.Get("Disc.Audio.RipFailedStatus");
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            _discImportCancellation?.Cancel();
            await WaitForLiveConversionsAsync();
            extensionCanceled = extensionSnapshot is not null && !_presentationState.PauseRequested;
            _services.Foundation.AudioDiscProjectService.MarkProjectCanceled(manifest);
            SaveCurrentAudioDiscProjectSnapshot(manifest, force: true);
            liveConversionSession.MarkManifestCanceled("Audio-CD-Ripping und AAC-Vorbereitung wurden abgebrochen.");
            if (_presentationState.PauseRequested)
            {
                PauseAudioDiscElapsedTimer();
                PublishAudioDiscStatus(liveConversionSession.GetSnapshot(), isPaused: true);
                EnterPipelinePause(string.Empty, preserveWorkflowStatus: true);
            }
            else
            {
                StatusText = LocalizationText.Get("Disc.Audio.RipCanceledStatus");
                ExportProgressText = LocalizationText.Get("Disc.Audio.CompletedTracksPreserved");
            }
        }
        catch (Exception ex)
        {
            SaveCurrentAudioDiscProjectSnapshot(manifest, force: true);
            liveConversionSession.MarkManifestFailed(ex.Message);
            if (extensionSnapshot is not null)
                EnterPipelinePause(LocalizationText.Get("MainWindow.Export.ConversionPaused"));
            _runtimeServices.NotificationService.Notify(NotificationEvent.Error);
            AppDialogService.Error(
                this,
                LocalizationText.Get("Disc.Audio.RipErrorTitle"),
                LocalizationText.Get("Disc.Audio.RipErrorMessage"),
                details: new[] { ex.Message });
        }
        finally
        {
            await WaitForLiveConversionsAsync();

            if (!continueWithExport)
                RefreshLoadedProjectTrackStateFromPersistedProject(manifest.ProjectFolder);

            IsDiscImporting = false;
            IsBusy = false;
            _discImportCancellation?.Dispose();
            _discImportCancellation = null;
            if (!continueWithExport)
                EndWorkflowStatusOperation(audioStatusOperationId);
            NotifyExportUiStateChanged();
        }

        if (extensionCanceled && extensionSnapshot is not null)
        {
            _services.Pipeline.ProjectExtensionRollbackService.Rollback(extensionSnapshot);
            ReloadCanonicalMetadataSessionAfterRollback(manifest.ProjectFolder);
            ClearProjectExtensionPauseContext();
            var restoredManifest = _services.Foundation.AudioDiscProjectService.TryLoad(manifest.ProjectFolder);
            _activeAudioDiscManifest = restoredManifest;
            _isAudioDiscProjectAwaitingRip = false;
            _projectReviewState = ProjectReviewState.WithCompletion(extensionReturnCompletedState);
            _projectReviewState = ProjectReviewState.WithManualMergeReview(extensionReturnState == ProjectPipelineState.ReviewBeforeMerge || extensionReturnState == ProjectPipelineState.Completed);
            _projectReviewState = ProjectReviewState.WithPhase(extensionReturnState ?? ProjectPipelineState.ReviewBeforeMerge);
            RefreshLoadedProjectTrackStateFromPersistedProject(manifest.ProjectFolder);
            StatusText = LocalizationText.Get("Disc.AddMore.AudioCanceledStatus");
            ExportProgressText = LocalizationText.Get("Disc.AddMore.AudioCanceledProgress");
            NotifyExportUiStateChanged();
            return;
        }

        if (continueWithExport)
        {
            await RunCurrentExportPlanAsync(
                manifest.ProjectFolder,
                ProjectManifestTypes.AudioCdProject,
                manualMergeRequested: false);
        }
    }

    private void NotifyDiscPollingState(DiscPollingDisplayState state)
    {
        if (state is DiscPollingDisplayState.Unsupported or DiscPollingDisplayState.Duplicate)
            _runtimeServices.NotificationService.Notify(NotificationEvent.Warning);
    }

    private string CreateDiscDriveDisplayName(string sourceFolder) =>
        DiscDriveSelectionPresentationService.FormatStatusDriveLabel(sourceFolder, _settings.DiscDriveOrder);

    private async Task<bool> ConfirmRequiredAudioDiscStillAvailableAsync(
        AudioDiscProjectManifest manifest,
        AudioDiscProjectManifestDisc disc,
        CancellationToken token)
    {
        // Optical drives can briefly report stale readiness/TOC data immediately after media removal.
        // Require several successful checks before treating a raw-read failure as a genuine disc error.
        for (var attempt = 0; attempt < 3; attempt++)
        {
            await Task.Delay(attempt == 0 ? 500 : 750, token);

            var sourceReady = await _services.Discs.DiscDriveService.IsDiscSourceReadyAsync(
                disc.SourceDriveRoot,
                token);
            if (!sourceReady)
                return false;

            var check = await _services.Workflows.AudioDiscPollingService.CheckRequiredDiscAsync(
                disc.DiscIdentity,
                disc.DiscNumber,
                manifest.TotalDiscs,
                null,
                token);
            if (!check.PollingResult.CanImport || check.Disc is null)
                return false;
        }

        return true;
    }

    private async Task<AudioDiscPollingResult?> WaitForRequiredAudioDiscAsync(
        AudioDiscProjectManifest manifest,
        AudioDiscProjectManifestDisc disc,
        CancellationToken token,
        string? selectedDriveRoot = null)
    {
        using var statusAnimation = BeginDriveCircleStatusAnimation(token);
        IReadOnlyList<string>? driveRoots = selectedDriveRoot is null ? null
            : _settings.ExperimentalDriveRoundEnabled
                ? await BuildDriveRoundAsync(selectedDriveRoot, token, startWithSelectedDrive: true)
                : new[] { selectedDriveRoot };
        var preferredRoot = selectedDriveRoot;
        var immediateResult = await _services.Workflows.AudioDiscPollingService.CheckRequiredDiscAsync(
            disc.DiscIdentity,
            disc.DiscNumber,
            manifest.TotalDiscs,
            preferredRoot,
            token,
            driveRoots,
            CreateDiscDriveDisplayName);
        if (immediateResult.PollingResult.CanImport && immediateResult.Disc is not null)
            return immediateResult;

        NotifyDiscChangeRequired(
            "Audio-CD",
            source: "AudioDiscWait",
            reason: "RequiredDiscDialog");

        AudioDiscPollingResult? acceptedResult = null;
        var request = new DiscWaitDialogRequest(
            disc.DiscNumber,
            manifest.TotalDiscs,
            LocalizationText.Get("Disc.Media.Audio"),
            LocalizationText.Format("Disc.DriveRound.RequiredAudioInstruction", disc.DiscNumber, manifest.TotalDiscs),
            LocalizationText.Get("Disc.DriveRound.RequiredAudioHint"),
            CreateDiscDriveDisplayName(preferredRoot ?? disc.SourceDriveRoot),
            IsDriveCircle: _settings.ExperimentalDriveRoundEnabled);

        var accepted = await _services.Workflows.DiscWaitDialogService.WaitForDiscAsync(
            this,
            request,
            async (_, cancellationToken) =>
            {
                acceptedResult = await _services.Workflows.AudioDiscPollingService.CheckRequiredDiscAsync(
                    disc.DiscIdentity,
                    disc.DiscNumber,
                    manifest.TotalDiscs,
                    preferredRoot,
                    cancellationToken,
                    driveRoots,
                    CreateDiscDriveDisplayName,
                    checkingDrive: request.IsDriveCircle ? source =>
                    {
                        Dispatcher.Invoke(() =>
                        {
                            if (!cancellationToken.IsCancellationRequested &&
                                TrackPathService.PathEquals(manifest.ProjectFolder, _currentProjectWorkFolder))
                                ExportProgressText = LocalizationText.Format("Disc.DriveRound.Checking.Progress", FormatDriveLetter(source));
                        });
                    } : null);
                return request.IsDriveCircle && !acceptedResult.PollingResult.CanImport
                    ? DiscDriveRoundWaitPresentation.Complete(LocalizationText.Get("Disc.Media.Audio"),
                        disc.DiscNumber, manifest.TotalDiscs,
                        acceptedResult.PollingResult.DisplayState == DiscPollingDisplayState.Waiting ? null : acceptedResult.PollingResult)
                    : acceptedResult.PollingResult;
            },
            value => StatusText = value,
            value => ExportProgressText = value,
            token);

        if (accepted == DiscWaitDialogOutcome.Deferred)
            ApplyPipelinePauseRequest();

        return accepted == DiscWaitDialogOutcome.Ready && acceptedResult?.Disc is not null
            ? acceptedResult
            : null;
    }

    private async Task<AudioDiscProjectManifestDisc?> WaitForNextAudioDiscAsync(
        AudioDiscProjectManifest manifest,
        int discNumber,
        CancellationToken token,
        bool startWithSelectedDrive = false,
        bool previewOnly = false,
        string? selectedSource = null)
    {
        using var statusAnimation = BeginDriveCircleStatusAnimation(token);
        var sourceFolder = selectedSource ?? _resumeDiscStartDriveRoot ?? manifest.SourceDriveRoot;
        var importedIdentities = manifest.Discs
            .Select(item => item.DiscIdentity)
            .Where(identity => !string.IsNullOrWhiteSpace(identity))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        AudioDiscPollingResult? acceptedResult = await TrySelectNextAudioDiscFromDriveRoundAsync(
            manifest,
            sourceFolder,
            discNumber,
            importedIdentities,
            token,
            startWithSelectedDrive);

        if (acceptedResult?.Disc is not null)
            return AcceptOrPreviewAudioDisc(manifest, acceptedResult, discNumber, previewOnly: previewOnly);

        NotifyDiscChangeRequired(
            "Audio-CD",
            source: "AudioDiscWait",
            reason: _settings.ExperimentalDriveRoundEnabled ? "DriveRoundWaitDialog" : "WaitDialog");

        acceptedResult = null;

        var request = new DiscWaitDialogRequest(
            discNumber,
            manifest.TotalDiscs,
            LocalizationText.Get("Disc.Media.Audio"),
            _settings.ExperimentalDriveRoundEnabled
                ? LocalizationText.Format("Disc.DriveRound.InsertInstruction", LocalizationText.Get("Disc.Media.Audio"), discNumber, manifest.TotalDiscs)
                : LocalizationText.Format("Disc.DriveRound.AudioInsertStandard", discNumber, manifest.TotalDiscs),
            LocalizationText.Get("Disc.DriveRound.AudioDuplicateHint"),
            CreateDiscDriveDisplayName(sourceFolder),
            NotifyDiscPollingState,
            CanCompleteImport: !previewOnly && discNumber > 1,
            IsDriveCircle: _settings.ExperimentalDriveRoundEnabled);

        var accepted = await _services.Workflows.DiscWaitDialogService.WaitForDiscAsync(
            this,
            request,
            async (checkRequest, cancellationToken) =>
            {
                if (_settings.ExperimentalDriveRoundEnabled)
                {
                    var driveRoundResult = await CheckAudioDriveRoundForWaitDialogAsync(
                        manifest,
                        sourceFolder,
                        discNumber,
                        importedIdentities,
                        checkRequest.AcceptNextDuplicate,
                        cancellationToken,
                        startWithSelectedDrive);
                    acceptedResult = driveRoundResult.AcceptedResult;
                    return driveRoundResult.PollingResult;
                }

                acceptedResult = await _services.Workflows.AudioDiscPollingService.CheckNextDiscAsync(
                    sourceFolder,
                    discNumber,
                    manifest.TotalDiscs,
                    importedIdentities,
                    cancellationToken,
                    checkRequest.AcceptNextDuplicate);
                return acceptedResult.PollingResult;
            },
            _ => { },
            _ => { },
            token);

        if (accepted == DiscWaitDialogOutcome.Deferred)
        {
            ApplyPipelinePauseRequest();
            return null;
        }

        if (accepted == DiscWaitDialogOutcome.ImportCompleted)
        {
            if (previewOnly) return null;
            DiscardChangedResumeDiscPreview(discNumber);
            _services.Foundation.AudioDiscProjectService.CompleteDiscImportEarly(manifest);
            SaveCurrentAudioDiscProjectSnapshot(manifest, force: true);
            return null;
        }

        if (acceptedResult?.Disc is null)
            return null;

        return AcceptOrPreviewAudioDisc(
            manifest, acceptedResult, discNumber,
            accepted == DiscWaitDialogOutcome.ReadyWithDuplicateOverride, previewOnly);
    }

    private sealed record AudioDriveRoundWaitResult(
        DiscPollingResult PollingResult,
        AudioDiscPollingResult? AcceptedResult = null);

    private async Task<AudioDriveRoundWaitResult> CheckAudioDriveRoundForWaitDialogAsync(
        AudioDiscProjectManifest manifest,
        string lastProcessedSourceFolder,
        int discNumber,
        ISet<string> importedDiscIdentities,
        bool acceptDuplicate,
        CancellationToken token,
        bool startWithSelectedDrive = false)
    {
        var round = await BuildDriveRoundAsync(lastProcessedSourceFolder, token, startWithSelectedDrive);
        if (round.Count == 0)
        {
            return new AudioDriveRoundWaitResult(new DiscPollingResult(
                false,
                LocalizationText.Format("Disc.DriveRound.NoDrive.Dialog", LocalizationText.Get("Disc.Media.Audio"), discNumber, manifest.TotalDiscs),
                LocalizationText.Format("Disc.DriveRound.NoDrive.Status", LocalizationText.Get("Disc.Media.Audio"), discNumber),
                LocalizationText.Get("Disc.DriveRound.NoDrive.Progress")));
        }

        DiscPollingResult? lastProblem = null;
        foreach (var driveRoot in round)
        {
            token.ThrowIfCancellationRequested();
            var driveLetter = FormatDriveLetter(driveRoot);
            ExportProgressText = LocalizationText.Format("Disc.DriveRound.Checking.Progress", driveLetter);

            var typeProbe = await _services.Workflows.DiscDriveCandidateProbeService
                .ProbeTypeAsync(driveRoot, DiscMediaKind.AudioCd, token);
            if (!typeProbe.IsAccepted)
            {
                WriteDriveRoundSkip("Audio-CD", discNumber, typeProbe);
                var candidateNotice = CreateDriveRoundWaitingResult(
                    discNumber,
                    manifest.TotalDiscs,
                    LocalizationText.Get("Disc.Media.Audio"),
                    driveLetter,
                    typeProbe);
                lastProblem = DiscDriveRoundWaitPresentation.KeepActionable(lastProblem, candidateNotice,
                    typeProbe.Status == DiscDriveCandidateStatus.Error);
                continue;
            }

            AudioDiscPollingResult pollingResult;
            try
            {
                pollingResult = await _services.Workflows.AudioDiscPollingService.CheckNextDiscAsync(
                    typeProbe.DriveRoot,
                    discNumber,
                    manifest.TotalDiscs,
                    importedDiscIdentities,
                    token,
                    acceptDuplicate);
            }
            catch (Exception ex)
            {
                _runtimeServices.DiagnosticLogService.WriteError($"DriveCircle Audio-CD {discNumber}: {driveRoot} konnte nicht geprüft werden", ex);
                lastProblem = new DiscPollingResult(
                    false,
                    LocalizationText.Format("Disc.DriveRound.Unreadable.Dialog", driveLetter),
                    LocalizationText.Format("Disc.DriveRound.Unreadable.Status", LocalizationText.Get("Disc.Media.Audio"), discNumber, driveLetter),
                    LocalizationText.Format("Disc.DriveRound.Unreadable.Progress", driveLetter));
                continue;
            }

            if (pollingResult.PollingResult.CanImport && pollingResult.Disc is not null)
            {
                _runtimeServices.DiagnosticLogService.WriteApplicationEvent(
                    "DRIVECIRCLE",
                    $"Audio-CD {discNumber}/{manifest.TotalDiscs}: {typeProbe.DriveRoot} akzeptiert.");
                return new AudioDriveRoundWaitResult(new DiscPollingResult(
                    true,
                    LocalizationText.Format("Disc.DriveRound.AudioReady.Dialog", driveLetter, discNumber, manifest.TotalDiscs),
                    LocalizationText.Format("Disc.DriveRound.AudioReady.Status", discNumber, manifest.TotalDiscs, driveLetter),
                    LocalizationText.Format("Disc.DriveRound.AudioReady.Progress", driveLetter),
                    DiscPollingDisplayState.Ready), pollingResult);
            }

            var pollingNotice = pollingResult.PollingResult with
            {
                ProgressText = LocalizationText.Format("Disc.DriveRound.Skipped.Progress", driveLetter, pollingResult.PollingResult.ProgressText),
                StatusText = LocalizationText.Format("Disc.DriveRound.Skipped.Status", LocalizationText.Get("Disc.Media.Audio"), discNumber, driveLetter)
            };
            lastProblem = DiscDriveRoundWaitPresentation.KeepActionable(lastProblem, pollingNotice,
                pollingNotice.DisplayState is DiscPollingDisplayState.Duplicate or DiscPollingDisplayState.Unsupported);
        }

        return new AudioDriveRoundWaitResult(DiscDriveRoundWaitPresentation.Complete(
            LocalizationText.Get("Disc.Media.Audio"), discNumber, manifest.TotalDiscs, lastProblem));
    }

    private async Task<AudioDiscPollingResult?> TrySelectNextAudioDiscFromDriveRoundAsync(
        AudioDiscProjectManifest manifest,
        string lastProcessedSourceFolder,
        int discNumber,
        ISet<string> importedDiscIdentities,
        CancellationToken token,
        bool startWithSelectedDrive = false)
    {
        if (!_settings.ExperimentalDriveRoundEnabled)
            return null;

        var round = await BuildDriveRoundAsync(lastProcessedSourceFolder, token, startWithSelectedDrive);
        if (round.Count == 0)
            return null;

        _runtimeServices.DiagnosticLogService.WriteApplicationEvent(
            "DRIVECIRCLE",
            $"Audio-CD {discNumber}/{manifest.TotalDiscs}: prüfe {string.Join(", ", round)} nach {lastProcessedSourceFolder}.");

        foreach (var driveRoot in round)
        {
            token.ThrowIfCancellationRequested();

            var typeProbe = await _services.Workflows.DiscDriveCandidateProbeService
                .ProbeTypeAsync(driveRoot, DiscMediaKind.AudioCd, token);
            if (!typeProbe.IsAccepted)
            {
                WriteDriveRoundSkip("Audio-CD", discNumber, typeProbe);
                continue;
            }

            AudioDiscPollingResult pollingResult;
            try
            {
                pollingResult = await _services.Workflows.AudioDiscPollingService.CheckNextDiscAsync(
                    typeProbe.DriveRoot,
                    discNumber,
                    manifest.TotalDiscs,
                    importedDiscIdentities,
                    token);
            }
            catch (Exception ex)
            {
                _runtimeServices.DiagnosticLogService.WriteError($"DriveCircle Audio-CD {discNumber}: {driveRoot} konnte nicht geprüft werden", ex);
                continue;
            }

            if (pollingResult.PollingResult.CanImport && pollingResult.Disc is not null)
            {
            StatusText = LocalizationText.Format("Disc.DriveRound.Accepted.Status", LocalizationText.Get("Disc.Media.Audio"), discNumber, manifest.TotalDiscs, FormatDriveLetter(typeProbe.DriveRoot));
            ExportProgressText = LocalizationText.Get("Disc.DriveRound.RippingContinues.Progress");
                _runtimeServices.DiagnosticLogService.WriteApplicationEvent(
                    "DRIVECIRCLE",
                    $"Audio-CD {discNumber}/{manifest.TotalDiscs}: {typeProbe.DriveRoot} akzeptiert.");
                return pollingResult;
            }

            var status = pollingResult.PollingResult.DisplayState == DiscPollingDisplayState.Duplicate
                ? DiscDriveCandidateStatus.Duplicate
                : DiscDriveCandidateStatus.WrongType;
            WriteDriveRoundSkip(
                "Audio-CD",
                discNumber,
                new DiscDriveCandidateResult(status, typeProbe.DriveRoot, typeProbe.MediaKind, pollingResult.PollingResult.StatusText));
        }

        _runtimeServices.DiagnosticLogService.WriteApplicationEvent(
            "DRIVECIRCLE",
            $"Audio-CD {discNumber}/{manifest.TotalDiscs}: keine passende neue Disc gefunden.");
        return null;
    }

    private AudioDiscProjectManifestDisc AcceptOrPreviewAudioDisc(
        AudioDiscProjectManifest manifest, AudioDiscPollingResult result, int discNumber,
        bool acceptDuplicate = false, bool previewOnly = false)
    {
        if (previewOnly)
            return _services.Foundation.AudioDiscProjectService.BuildDiscEntry(manifest, result.Disc!,
                discNumber, _services.Foundation.AudioDiscProjectService.GetNextGlobalTrackIndex(manifest), result.DriveInfo);
        return AddAcceptedAudioDiscFromPollingResult(manifest, result, discNumber, acceptDuplicate);
    }

    private AudioDiscProjectManifestDisc AddAcceptedAudioDiscFromPollingResult(
        AudioDiscProjectManifest manifest,
        AudioDiscPollingResult acceptedResult,
        int discNumber,
        bool acceptDuplicate = false)
    {
        if (_resumeDiscPreview.DiscNumber == discNumber &&
            !_resumeDiscPreview.MatchesAudio(discNumber, acceptedResult.Disc!.DiscIdentity))
            DiscardChangedResumeDiscPreview(discNumber);
        PersistTrackListState();
        SaveCurrentAudioDiscProjectSnapshot(manifest);

        var addedDisc = _services.Foundation.AudioDiscProjectService.AddDisc(
            manifest,
            acceptedResult.Disc!,
            discNumber,
            acceptedResult.DriveInfo,
            acceptDuplicate,
            preparedDisc: _resumeDiscPreview.AudioDisc);
        SaveCurrentAudioDiscProjectSnapshot(manifest, force: true);

        Tracks.ReplaceAll(DiscResumePreviewState.AppendAudioTracks(Tracks,
            _services.Foundation.AudioDiscProjectService.CreateTrackPreview(manifest)));
        if (_resumeDiscPreview.DiscNumber == discNumber) _resumeDiscPreview.Clear();

        RecalculateProcessingActionsForCurrentPreset(requestTrackStateRefresh: false);
        UpdateIndexes();
        RefreshTrackPresentationViews();
        AutoFitTrackColumnsAfterRender();
        NotifyExportUiStateChanged();

        return addedDisc;
    }


    private async Task RunLoadedMp3DiscProjectExportAsync(bool manualMergeRequested = false)
    {
        if (string.IsNullOrWhiteSpace(_currentProjectWorkFolder) || !Directory.Exists(_currentProjectWorkFolder))
        {
            AppDialogService.Warning(
                this,
                LocalizationText.Get("MainWindow.Resume.ProjectMissing.Title"),
                LocalizationText.Get("MainWindow.Resume.Mp3FolderMissing.Message"));
            ResetLoadedResumeProjectState();
            return;
        }

        ApplyLoadedProjectState(LoadedProjectState.WithDiscImportRequirement(false));
        ApplyLoadedProjectState(LoadedProjectState.WithMp3DiscState(true));
        NotifyExportUiStateChanged();

        // Geladene MP3-CD-Projekte laufen auch bei einem Presetwechsel direkt durch den
        // normalen Export-Workflow. Dadurch gelten derselbe Converting-Zustand,
        // dieselbe Pause-/Fortsetzen-Logik und dieselben aktiven Jobmeldungen wie bei
        // allen anderen Exportpfaden. Eine separate Vorabkonvertierung würde den
        // fachlichen Pipelinezustand umgehen und die falsche Buttongruppe stehen lassen.
        await RunCurrentExportPlanAsync(_currentProjectWorkFolder, ProjectManifestTypes.Mp3DiscProject, manualMergeRequested);
    }

    private void RestoreActiveAudioDiscManifestFromCurrentProject()
    {
        if (_activeAudioDiscManifest is not null || string.IsNullOrWhiteSpace(_currentProjectWorkFolder))
            return;

        var manifestPath = Path.Combine(
            _currentProjectWorkFolder,
            AudioDiscProjectService.ManifestFileName);
        if (!File.Exists(manifestPath))
            return;

        _activeAudioDiscManifest = _services.Foundation.AudioDiscProjectService.TryLoad(_currentProjectWorkFolder);
    }

    private string ResolveCurrentProjectType(string requestedProjectType)
    {
        RestoreActiveAudioDiscManifestFromCurrentProject();

        if (_activeAudioDiscManifest is not null)
            return ProjectManifestTypes.AudioCdProject;

        if (_activeMp3DiscManifest is not null || LoadedProjectState.IsMp3Disc)
            return ProjectManifestTypes.Mp3DiscProject;

        return requestedProjectType;
    }

    private async Task RunCurrentExportPlanAsync(
        string? projectWorkFolderOverride = null,
        string projectType = ProjectManifestTypes.FolderProject,
        bool manualMergeRequested = false)
    {
        if (!TryCheckpointEditableProject()) return;
        var exportStartTransition =
            _services.Pipeline.PipelinePauseTransitionService.CreateFailureClear(
                _presentationState.IsPipelinePaused,
                _presentationState.PauseRequested,
                IsBusy,
                IsExporting,
                IsDiscImporting);
        ApplyPipelineTransition(exportStartTransition);
        projectType = ResolveCurrentProjectType(projectType);

        var presetReconversionProjectFolder =
            string.IsNullOrWhiteSpace(projectWorkFolderOverride)
                ? _currentProjectWorkFolder
                : projectWorkFolderOverride;
        var pendingPresetReconversionContext =
            CapturePresetReconversionPauseContext(presetReconversionProjectFolder);
        var requiresPresetReconversionRollback =
            ProjectReviewState.Phase is ProjectPipelineState.ReviewBeforeMerge or ProjectPipelineState.Completed &&
            _manualMergeReviewNeedsReconversion;

        if (requiresPresetReconversionRollback && pendingPresetReconversionContext is null)
        {
            AppDialogService.Error(
                this,
                LocalizationText.Get("MainWindow.Reconversion.StartFailed.Title"),
                LocalizationText.Get("MainWindow.Reconversion.StartFailed.Message"));
            return;
        }

        ActivatePresetReconversionPauseContext(pendingPresetReconversionContext);

        var preparedLocalProjectForReview = false;
        if (string.Equals(projectType, ProjectManifestTypes.FolderProject, StringComparison.OrdinalIgnoreCase))
        {
            if (!IsCurrentLocalProjectSourceInternal())
            {
                var localPreparationCompleted = await PrepareCurrentLocalProjectSourcesAsync();
                if (!localPreparationCompleted)
                    return;

                preparedLocalProjectForReview = true;
            }

            projectWorkFolderOverride = _currentProjectWorkFolder;
        }
        if (string.Equals(projectType, ProjectManifestTypes.AudioCdProject, StringComparison.OrdinalIgnoreCase) &&
            string.IsNullOrWhiteSpace(projectWorkFolderOverride))
        {
            projectWorkFolderOverride = _currentProjectWorkFolder;
        }

        if (_activeAudioDiscManifest is not null)
            TrySaveCurrentAudioDiscProjectSnapshot(_activeAudioDiscManifest, force: true);

        if (preparedLocalProjectForReview)
            EnterPreparedProjectReviewStateAfterPreparation();

        FlushPendingChapterMetadataRefresh();

        // Vor jedem Export den Plan neu berechnen, damit Preset, Bitrate und Mono/Stereo wirklich berücksichtigt werden.
        RecalculateProcessingActionsForCurrentPreset();

        var exportCheck = ValidateExportPlan();

        if (exportCheck.Errors.Count > 0)
        {
            ShowExportBlockingErrors(exportCheck.Errors);
            RestoreReviewStateAfterPreExportValidationStopped();
            return;
        }

        if (exportCheck.Warnings.Count > 0 && !ShowExportWarnings(exportCheck.Warnings))
        {
            RestoreReviewStateAfterPreExportValidationStopped();
            return;
        }

        await ExportToAacWithWorkFolderAsync(
            exportCheck.TrackSnapshot,
            exportCheck.OutputPath,
            projectWorkFolderOverride,
            projectType,
            manualMergeRequested);
    }

    private bool IsCurrentLocalProjectSourceInternal()
    {
        if (string.IsNullOrWhiteSpace(_currentProjectWorkFolder) ||
            string.IsNullOrWhiteSpace(_currentFolderPath))
        {
            return false;
        }

        var originalsFolder = Path.Combine(
            _currentProjectWorkFolder,
            LocalProjectImportService.OriginalsFolderName);

        return TrackPathService.PathEquals(_currentFolderPath, originalsFolder) ||
            (LoadedProjectState.IsLocal && Tracks.Count > 0 && Tracks.All(track =>
                Path.IsPathRooted(track.FilePath) && TrackPathService.IsPathInsideFolder(track.FilePath, originalsFolder)));
    }

    private void EnterPreparedProjectReviewStateAfterPreparation()
    {
        _projectReviewState = ProjectReviewState.WithManualMergeReview(true);
        _manualMergeReviewPreparedPreset = SelectedExportPreset;
        _manualMergeReviewNeedsReconversion = false;
        _manualMergeReviewHasPreparedTracks = true;
        _projectReviewState = ProjectReviewState.WithCompletion(false);
        SetPipelineState(ProjectPipelineState.ReviewBeforeMerge);
        SetTrackActionDisplayOverride(LocalizationText.Get("Workflow.Review.MergeAction"));
        NotifyExportUiStateChanged();
    }

    private void RestoreReviewStateAfterPreExportValidationStopped()
    {
        if (IsBusy ||
            ProjectReviewState.Phase is ProjectPipelineState.ReviewBeforeMerge or ProjectPipelineState.Completed or ProjectPipelineState.Merging ||
            Tracks.Count == 0 ||
            string.IsNullOrWhiteSpace(_currentProjectWorkFolder))
        {
            return;
        }

        EnterPreparedProjectReviewStateAfterPreparation();
    }

    private async Task<bool> PrepareCurrentLocalProjectSourcesAsync()
    {
        if (Tracks.Count == 0 || string.IsNullOrWhiteSpace(_currentFolderPath))
            return false;

        var ffmpegPath = _ffmpegStatus.FfmpegPath ?? string.Empty;
        if (string.IsNullOrWhiteSpace(ffmpegPath))
        {
            AppDialogService.Error(
                this,
                LocalizationText.Get("MainWindow.Export.FfmpegMissing.Title"),
                LocalizationText.Get("MainWindow.Ffmpeg.Missing.ProjectMessage"));
            return false;
        }

        if (!ShowFolderPassThroughNotice(requireAcceptance: true)) return false;
        ApplyFolderSampleRateContext();
        var sourceFolder = Path.GetFullPath(_currentFolderPath);
        var trackSnapshot = Tracks.ToList();
        var sourceFiles = trackSnapshot
            .Select(track => TrackPathService.GetTrackPath(sourceFolder, track))
            .ToArray();

        var createsNewProject = string.IsNullOrWhiteSpace(_currentProjectWorkFolder) || !Directory.Exists(_currentProjectWorkFolder);
        var projectFolder = !string.IsNullOrWhiteSpace(_currentProjectWorkFolder) &&
                            Directory.Exists(_currentProjectWorkFolder)
            ? _currentProjectWorkFolder
            : _services.Pipeline.LocalProjectImportService.CreateProjectFolder(
                GetProjectFolderStructure().LocalProjectsFolder,
                sourceFolder,
                targetSampleRateHz: _folderRatePreview.EffectiveRate,
                allowAacReuse: _folderRatePreview.Decision.Qualified);

        if (createsNewProject)
        {
            _workspaceReplacementVersion++;
            _sessionProject.Register(projectFolder, ProjectManifestTypes.FolderProject, startAccepted: true);
        }
        var originalsFolder = Path.Combine(
            projectFolder,
            LocalProjectImportService.OriginalsFolderName);
        var preset = ExportPreset.Parse(SelectedExportPreset);
        var exportPlan = _services.Pipeline.ExportPlanService.Create(new ExportPlanRequest(
            trackSnapshot,
            originalsFolder,
            GetProjectFolderStructure().LocalProjectsFolder,
            BuildFinalOutputPath(),
            SelectedExportPreset,
            ResolveParallelJobCount(),
            projectFolder,
            ProjectManifestTypes.FolderProject,
            Author,
            BookTitle));

        Directory.CreateDirectory(exportPlan.ConvertedFolder);
        Directory.CreateDirectory(exportPlan.MergeFolder);

        _services.Foundation.ConvertedFileCleanupService.DeletePartFiles(projectFolder);
        var trackBySourcePath = sourceFiles
            .Select((sourcePath, index) => new
            {
                SourcePath = Path.GetFullPath(sourcePath),
                Index = index,
                Track = trackSnapshot[index]
            })
            .ToDictionary(
                item => item.SourcePath,
                item => (item.Index, item.Track),
                StringComparer.OrdinalIgnoreCase);

        var workflowOperationId = BeginWorkflowStatusOperation(projectFolder);
        try
        {
            var manifest = _services.Foundation.WorkManifestService.LoadOrCreate(
                exportPlan.ManifestPath,
                ProjectManifestTypes.FolderProject,
                projectFolder,
                originalsFolder,
                preset.DisplayName);
            _activeLocalPreparation = new(projectFolder, ProjectManifestTypes.FolderProject, manifest.ProjectId);
            var manifestSyncRoot = new object();
            var sourceAcquisitionCompleted = false;

            _currentProjectWorkFolder = projectFolder;
            EnsureCanonicalMetadataSessionAttachedToProject(projectFolder);
            manifest.State.Status = ProjectManifestStatuses.AcquiringSources;
            _services.Foundation.WorkManifestService.Save(exportPlan.ManifestPath, manifest);
            SetPipelineState(ProjectPipelineState.AcquiringSources);
            IsBusy = true;
            IsExporting = true;
            _exportCancellation = new CancellationTokenSource();
            PublishWorkflowStatus(
                workflowOperationId,
                _services.Status.LocalWorkflowStatusAdapter.CreateRunningSnapshot(
                    projectFolder,
                    ProjectPipelineState.AcquiringSources,
                    new LocalProjectLivePreparationProgress(0, sourceFiles.Length, 0, string.Empty, [], []),
                    preset));

            await using var preparedLifecycle = new PreparedTrackLifecycleSession(
                _services.Foundation.WorkManifestService, _services.Workflows.AacExportProcessingService,
                manifest, exportPlan.ManifestPath, manifestSyncRoot, _ffmpegStatus.FfprobePath ?? "", exportPlan.ParallelConversions);
            preparedLifecycle.QueuePreset(preset, _exportCancellation.Token);

            var result = await _services.Workflows.LocalProjectLivePreparationService.RunAsync(
                new LocalProjectLivePreparationRequest(
                    sourceFolder,
                    sourceFiles,
                    projectFolder,
                    ResolveParallelJobCount(),
                    preparedLifecycle.DrainAsync),
                async (copiedFile, trackPreparationProgress, token) =>
                {
                    var sourcePath = Path.GetFullPath(copiedFile.SourceFile);
                    if (!trackBySourcePath.TryGetValue(sourcePath, out var mappedTrack))
                    {
                        throw new InvalidOperationException(
                            $"Die kopierte Quelldatei konnte keinem Track zugeordnet werden: {copiedFile.SourceFile}");
                    }

                    ApplySourceIdFromCopiedFile(mappedTrack.Track, copiedFile);
                    PublishSourceMetadataSnapshot(
                        projectFolder,
                        copiedFile.TargetFile,
                        mappedTrack.Track,
                        evidence: "local-project-copy");
                    var convertedPath = ConvertedTrackPathService.GetConvertedTrackPath(
                        exportPlan.ConvertedFolder,
                        copiedFile.TargetFile,
                        mappedTrack.Track);
                    var canReuseConvertedTrack = false;

                    lock (manifestSyncRoot)
                    {
                        canReuseConvertedTrack = _services.Foundation.WorkManifestService.CanReuseConvertedTrack(
                            manifest,
                            mappedTrack.Index,
                            mappedTrack.Track,
                            copiedFile.TargetFile,
                            convertedPath,
                            preset);
                    }

                    if (!canReuseConvertedTrack)
                    {
                        preparedLifecycle.MarkStarted(mappedTrack.Index, mappedTrack.Track, copiedFile.TargetFile, convertedPath, preset, token);
                        try
                        {
                            var durationTicks = TrackDurationService.GetEffectiveDurationTicks(mappedTrack.Track);
                            await _services.Workflows.AacExportProcessingService.PrepareTrackForExportAsync(
                                mappedTrack.Track,
                                copiedFile.TargetFile,
                                convertedPath,
                                preset,
                                ffmpegPath,
                                token,
                                ffmpegProgress => trackPreparationProgress.Report(
                                    durationTicks <= 0
                                        ? 0
                                        : Math.Clamp(ffmpegProgress.Ticks / (double)durationTicks, 0d, 0.999d)));
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (ExportTrackException)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            throw new ExportTrackException(
                                mappedTrack.Track.Index > 0 ? mappedTrack.Track.Index : mappedTrack.Index + 1,
                                mappedTrack.Track.FileName,
                                copiedFile.SourceFile,
                                ex);
                        }

                        preparedLifecycle.Complete(mappedTrack.Index, mappedTrack.Track,
                            copiedFile.TargetFile, convertedPath, preset, token);
                    }
                },
                new Progress<LocalProjectLivePreparationProgress>(snapshot =>
                {
                    if (!sourceAcquisitionCompleted &&
                        snapshot.TotalFiles > 0 &&
                        snapshot.CopiedFiles >= snapshot.TotalFiles)
                    {
                        sourceAcquisitionCompleted = true;
                        lock (manifestSyncRoot)
                        {
                            manifest.State.Status = ProjectManifestStatuses.Converting;
                            _services.Foundation.WorkManifestService.Save(exportPlan.ManifestPath, manifest);
                        }
                        SetPipelineState(ProjectPipelineState.Converting);
                    }

                    PublishWorkflowStatus(
                        workflowOperationId,
                        _services.Status.LocalWorkflowStatusAdapter.CreateRunningSnapshot(
                            projectFolder,
                            sourceAcquisitionCompleted
                                ? ProjectPipelineState.Converting
                                : ProjectPipelineState.AcquiringSources,
                            snapshot,
                            preset));
                }),
                _exportCancellation.Token);

            if (result.ImportResult.WasCanceled || result.WasCanceled)
            {
                PublishWorkflowStatus(
                    workflowOperationId,
                    _services.Status.LocalWorkflowStatusAdapter.CreateRunningSnapshot(
                        projectFolder,
                        sourceAcquisitionCompleted
                            ? ProjectPipelineState.Converting
                            : ProjectPipelineState.AcquiringSources,
                        new LocalProjectLivePreparationProgress(
                            result.ImportResult.CompletedFiles,
                            result.ImportResult.TotalFiles,
                            result.PreparedFiles,
                            string.Empty,
                            [],
                            []),
                        preset,
                        isPaused: true));
                EndWorkflowStatusOperation(workflowOperationId);
                EnterPipelinePause(string.Empty, preserveWorkflowStatus: true);
                return false;
            }

            foreach (var track in trackSnapshot)
            {
                var sourcePath = TrackPathService.GetTrackPath(sourceFolder, track);
                var relativePath = Path.GetRelativePath(sourceFolder, sourcePath);
                var targetPath = Path.Combine(originalsFolder, relativePath);
                track.FilePath = targetPath;
                track.RelativeFolder = Path.GetDirectoryName(relativePath) ?? string.Empty;
                track.PreparedConvertedPath = ConvertedTrackPathService.GetConvertedTrackPath(
                    exportPlan.ConvertedFolder,
                    targetPath,
                    track);
                track.PreparedConvertedPreset = preset.DisplayName;
                track.HasReusableConvertedFile = File.Exists(track.PreparedConvertedPath) &&
                                                 new FileInfo(track.PreparedConvertedPath).Length > 0;
                if (track.HasReusableConvertedFile)
                {
                    track.ConvertedSizeAvailable = true;
                }
            }

            ApplySourceMetadataAggregation(
                projectFolder,
                ProjectMetadataInitializationState.InitialSetComplete,
                CreateLocalFolderAggregationOptions(sourceFolder, sourceFiles));

            _currentFolderPath = originalsFolder;
            SelectedFolder = sourceFolder;
            ApplyLoadedProjectState(LoadedProjectState.WithLocalState(true));
            ApplyLoadedProjectState(LoadedProjectState.WithMp3DiscState(false));
            ApplyLoadedProjectState(LoadedProjectState.WithAudioDiscState(false));
            SetSelectedSourceDisplayOverride(
                LocalizationText.Format("MainWindow.Source.FolderProject", new DirectoryInfo(sourceFolder).Name),
                sourceFolder);
            RefreshTrackPresentationViews();
            OnExportPreviewChanged();
            PublishWorkflowStatus(
                workflowOperationId,
                _services.Status.LocalWorkflowStatusAdapter.CreateReadySnapshot(
                    projectFolder,
                    trackSnapshot.Count,
                    preset));
            EndWorkflowStatusOperation(workflowOperationId);
            return true;
        }
        catch (OperationCanceledException)
        {
            var last = _services.Status.WorkflowStatusCoordinator.Snapshot;
            PublishWorkflowStatus(
                workflowOperationId,
                last with { IsPaused = true });
            EndWorkflowStatusOperation(workflowOperationId);
            EnterPipelinePause(string.Empty, preserveWorkflowStatus: true);
            return false;
        }
        catch (Exception ex)
        {
            EndWorkflowStatusOperation(workflowOperationId);
            EnterPipelineFailure(
                LocalizationText.Get("MainWindow.LocalPreparation.Failed.Status"),
                LocalizationText.Get("MainWindow.LocalPreparation.Failed.Progress"));

            var convertedFolder = string.IsNullOrWhiteSpace(_currentProjectWorkFolder)
                ? string.Empty
                : ProjectFolderLayout.GetConvertedFolder(_currentProjectWorkFolder);
            var details = _services.Status.ExportFailureDetailsService.BuildExportFailureDetails(
                ex,
                convertedFolder);
            var errorText = string.Join(Environment.NewLine, details);
            _ = ShowHardWorkflowErrorDialog(
                LocalizationText.Get("MainWindow.LocalPreparation.Failed.Title"),
                LocalizationText.Get("MainWindow.LocalPreparation.Failed.Title"),
                LocalizationText.Get("MainWindow.LocalPreparation.Failed.Message"),
                errorText,
                details);

            return false;
        }
        finally
        {
            _exportCancellation?.Dispose();
            _exportCancellation = null;
            IsExporting = false;
            IsBusy = false;
        }
    }

    private void EnterPipelineFailure(string statusText, string progressText)
    {
        if (!string.IsNullOrWhiteSpace(_currentProjectWorkFolder))
            _services.Foundation.ConvertedFileCleanupService.DeletePartFiles(_currentProjectWorkFolder);

        TrySaveActiveProjectSnapshot();
        PersistTrackListState();
        var failureTransition =
            _services.Pipeline.PipelinePauseTransitionService.CreateFailure();

        ApplyPipelineTransition(failureTransition);
        StatusText = statusText;
        ExportProgressText = progressText;
        NotifyExportUiStateChanged();
    }

    private AppDialogResult ShowHardWorkflowErrorDialog(
        string title,
        string heading,
        string message,
        string errorText,
        IReadOnlyList<string>? details = null)
    {
        var detailItems = details ?? AppDialogService.LimitDetails(
            (errorText ?? string.Empty).Split(Environment.NewLine),
            80);
        var clipboardText = string.IsNullOrWhiteSpace(errorText)
            ? string.Join(Environment.NewLine, detailItems)
            : errorText;

        var buttons = new[]
        {
            new AppDialogButton(LocalizationText.Get("MainWindow.Error.Copy"), AppDialogResult.None, ClipboardText: clipboardText, ClosesDialog: false),
            new AppDialogButton("OK", AppDialogResult.Ok, IsPrimary: true, IsDefault: true, IsCancel: true)
        };
        var recoveryMessage = LocalizationText.Get("MainWindow.Error.RecoveryWithoutAbort");

        return AppDialogService.Show(
            this,
            title: title,
            heading: heading,
            message: message + "\n\n" + recoveryMessage,
            kind: AppDialogKind.Error,
            details: detailItems,
            buttons: buttons,
            width: 740,
            height: 420);
    }


    private bool DiscardPausedMerge()
    {
        var baseline = _services.Pipeline.ProjectExtensionRollbackService.GetActive(_currentProjectWorkFolder);
        if (baseline?.Kind == "Merge")
        {
            try
            {
                _services.Pipeline.ProjectExtensionRollbackService.Rollback(baseline);
            }
            catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or
                System.Text.Json.JsonException or InvalidOperationException or ArgumentException or NotSupportedException)
            {
                _runtimeServices.DiagnosticLogService.WriteError("Merge rollback failed; pending journal retained", error);
                ShowHardWorkflowErrorDialog(LocalizationText.Get("ProjectAttempt.Recovery.Heading"),
                    LocalizationText.Get("ProjectAttempt.Recovery.Heading"),
                    LocalizationText.Get("ProjectAttempt.Recovery.Message"), error.ToString());
                return false;
            }
            ReloadCanonicalMetadataSessionAfterRollback(_currentProjectWorkFolder);
        }
        var path = ProjectFolderLayout.ResolveExportManifestPath(_currentProjectWorkFolder);
        var manifest = _services.Foundation.WorkManifestService.LoadOrCreate(path,
            LoadedProjectState.IsMp3Disc ? ProjectManifestTypes.Mp3DiscProject :
            LoadedProjectState.IsAudioDisc ? ProjectManifestTypes.AudioCdProject : ProjectManifestTypes.FolderProject,
            _currentProjectWorkFolder, _currentFolderPath, SelectedExportPreset);
        // Only the rollback owner above grants permission to delete its final part.
        _services.Foundation.ConvertedFileCleanupService.DeletePartFiles(_currentProjectWorkFolder);
        manifest.Resume.MergePaused = false;
        _services.Foundation.WorkManifestService.MarkConversionCompleted(manifest);
        _services.Foundation.WorkManifestService.Save(path, manifest);
        ApplyPipelineTransition(_services.Pipeline.PipelinePauseTransitionService.CreateContinuationStart());
        ReturnToReviewAfterInterruptedExport();
        ExportProgressPercent = 100;
        ApplyWorkflowStatusViewState(new WorkflowStatusFormatter().Format(new WorkflowStatusSnapshot
        {
            ProjectId = _currentProjectWorkFolder,
            ProjectKind = LoadedProjectState.IsMp3Disc ? WorkflowProjectKind.Mp3Disc :
                LoadedProjectState.IsAudioDisc ? WorkflowProjectKind.AudioDisc : WorkflowProjectKind.Folder,
            ProjectState = ProjectPipelineState.ReviewBeforeMerge,
            IsReadyToMerge = true,
            TotalSourceItems = Tracks.Count
        }));
        ExportProgressText = LocalizationText.Get("MainWindow.Export.MergeReadyAfterDiscard");
        StatusText = ExportProgressText;
        return true;
    }

    private async void CancelExport_Click(object sender, RoutedEventArgs e)
    {
        if (_isDiscStopSaveInProgress || _presentationState.UiTransition is not null || _presentationState.PauseRequested) return;
        if (_presentationState.IsPipelineFailed)
        {
            if (_presetReconversionRollbackContext is not null || _presentationState.IsProjectExtensionRun)
            {
                ApplyPipelineTransition(_services.Pipeline.PipelinePauseTransitionService.CreateCompletedPause());
                if (_presetReconversionRollbackContext is not null) await StopPausedPresetReconversionAsync();
                else await StopPausedProjectExtensionAsync();
            }
            else CloseCurrentProject();
            return;
        }

        if (_presentationState.IsPipelinePaused && ProjectReviewState.Phase == ProjectPipelineState.Merging)
        {
            DiscardPausedMerge();
            return;
        }

        if (_presentationState.IsPipelinePaused)
        {
            if (_presetReconversionRollbackContext is not null)
                await StopPausedPresetReconversionAsync();
            else if (_presentationState.IsProjectExtensionRun)
                await StopPausedProjectExtensionAsync();
            return;
        }

        if (!IsBusy && ProjectReviewState.Phase is ProjectPipelineState.ReviewBeforeMerge or ProjectPipelineState.Completed)
        {
            CloseCurrentProject();
            return;
        }

        if (!IsBusy &&
            ProjectReviewState.Phase is ProjectPipelineState.AcquiringSources or ProjectPipelineState.Converting &&
            !IsDiscImporting &&
            !IsExporting)
        {
            RestoreReviewStateAfterPreExportValidationStopped();
            if (ProjectReviewState.Phase is ProjectPipelineState.ReviewBeforeMerge or ProjectPipelineState.Completed)
                return;
        }

        if (ProjectReviewState.Phase is ProjectPipelineState.AcquiringSources or ProjectPipelineState.Converting)
        {
            await RequestPipelinePauseAsync();
            return;
        }

        if (ProjectReviewState.Phase == ProjectPipelineState.Merging && (LoadedProjectState.IsMp3Disc || LoadedProjectState.IsAudioDisc))
        {
            await RequestPipelinePauseAsync();
            return;
        }

        if (_trackAnalysisCancellation is not null && !_trackAnalysisCancellation.IsCancellationRequested)
        {
            StatusText = LocalizationText.Get("MainWindow.TrackAnalysis.Canceling");
            ExportProgressText = LocalizationText.Get("MainWindow.Cancel.Requested");
            await RequestCancellationWithFeedbackAsync(_trackAnalysisCancellation);
            NotifyExportUiStateChanged();
            return;
        }

        if (IsDiscImporting && _discImportCancellation is not null && !_discImportCancellation.IsCancellationRequested)
        {
            StatusText = LocalizationText.Get(_isAudioDiscProjectAwaitingRip
                ? "MainWindow.Cancel.AudioRipping"
                : "MainWindow.Cancel.Mp3Import");
            ExportProgressText = LocalizationText.Get("MainWindow.Cancel.Requested");
            await RequestCancellationWithFeedbackAsync(_discImportCancellation);
            return;
        }

        if (!IsExporting || _exportCancellation is null || _exportCancellation.IsCancellationRequested)
            return;

        StatusText = LocalizationText.Get("MainWindow.Export.Canceling");
        ExportProgressText = LocalizationText.Get("MainWindow.Cancel.Requested");
        await RequestCancellationWithFeedbackAsync(_exportCancellation);
    }

    private async Task RequestCancellationWithFeedbackAsync(CancellationTokenSource cancellation)
    {
        var transitionOwner = BeginPipelineUiTransition(PipelineUiTransitionKind.Stopping, PipelineUiTransitionTarget.Secondary);
        try
        {
            await Dispatcher.Yield(DispatcherPriority.Background);
            if (ReferenceEquals(cancellation, _trackAnalysisCancellation) ||
                ReferenceEquals(cancellation, _discImportCancellation) || ReferenceEquals(cancellation, _exportCancellation))
                await cancellation.CancelAsync();
            NotifyExportUiStateChanged();
        }
        catch (Exception ex)
        {
            _runtimeServices.DiagnosticLogService.WriteError("Cancellation request failed", ex);
            StatusText = LocalizationText.Get("Disc.StopSave.Failed");
            EndPipelineUiTransition(transitionOwner);
        }
    }

    private void PauseAudioDiscElapsedTimer()
    {
        _audioDiscElapsedBeforeCurrentRun += _audioDiscCurrentRunElapsed;
        _audioDiscCurrentRunElapsed = TimeSpan.Zero;
    }

    private Guid BeginWorkflowStatusOperation(string? projectId)
    {
        _workflowStatusOperationId = _services.Status.WorkflowStatusCoordinator.BeginOperation(projectId);
        return _workflowStatusOperationId;
    }

    private void PublishWorkflowStatus(Guid operationId, WorkflowStatusSnapshot snapshot)
    {
        if (!_services.Status.WorkflowStatusCoordinator.Publish(operationId, snapshot))
            return;

        ApplyWorkflowStatusViewState(_services.Status.WorkflowStatusCoordinator.CurrentViewState, operationId);
    }

    private void ApplyWorkflowStatusViewState(WorkflowStatusViewState viewState, Guid? expectedOperationId = null)
    {
        void Apply()
        {
            if (expectedOperationId is not null && expectedOperationId != _workflowStatusOperationId) return;
            StatusText = viewState.TeletextText;
            ExportProgressText = viewState.ProgressText;
            ExportProgressPercent = viewState.ProgressPercent;
            IsProgressIndeterminate = viewState.IsProgressIndeterminate;
            ExportProgressForeground = viewState.ProgressVisualKind switch
            {
                WorkflowProgressVisualKind.Merge => "#D9A441",
                _ => "#16858A"
            };
        }

        if (Dispatcher.CheckAccess())
            Apply();
        else
            Dispatcher.Invoke(Apply);
    }

    private void EndWorkflowStatusOperation(Guid operationId)
    {
        _services.Status.WorkflowStatusCoordinator.EndOperation(operationId);
        if (_workflowStatusOperationId == operationId)
            _workflowStatusOperationId = Guid.Empty;
    }

    private PipelineUiTransition? BeginPipelineUiTransition(PipelineUiTransitionKind kind, PipelineUiTransitionTarget target)
    {
        var owner = _presentationState.BeginUiTransition(kind, target);
        NotifyExportUiStateChanged();
        return owner;
    }

    private void EndPipelineUiTransition(PipelineUiTransition? owner)
    {
        _presentationState.EndUiTransition(owner);
        NotifyExportUiStateChanged();
    }

    private void ApplyPipelinePauseRequest()
    {
        var pauseRequestTransition =
            _services.Pipeline.PipelinePauseTransitionService.CreatePauseRequest(
                _presentationState.IsPipelinePaused,
                _presentationState.IsPipelineFailed,
                IsBusy,
                IsExporting,
                IsDiscImporting);
        ApplyPipelineTransition(pauseRequestTransition);
    }

    private void ApplyPipelineTransition(PipelinePauseTransitionPlan transition)
    {
        if (!transition.PauseRequested && _presentationState.UiTransition?.Kind == PipelineUiTransitionKind.Pausing)
            _presentationState.ClearUiTransition();
        _presentationState.IsPipelinePaused = transition.IsPipelinePaused;
        _presentationState.IsPipelineFailed = transition.IsPipelineFailed;
        _presentationState.PauseRequested = transition.PauseRequested;
        IsBusy = transition.IsBusy;
        IsExporting = transition.IsExporting;
        IsDiscImporting = transition.IsDiscImporting;
        NotifyExportUiStateChanged();
    }

    private void EnterPipelinePause(string progressText, bool preserveWorkflowStatus = false)
    {
        if (_discImportCancellation is null && !string.IsNullOrWhiteSpace(_currentProjectWorkFolder))
            _services.Foundation.ConvertedFileCleanupService.DeletePartFiles(_currentProjectWorkFolder);

        TrySaveActiveProjectSnapshot();
        PersistTrackListState();

        var pauseTransition =
            _services.Pipeline.PipelinePauseTransitionService.CreateCompletedPause();

        ApplyPipelineTransition(pauseTransition);
        if (!preserveWorkflowStatus)
        {
            StatusText = LocalizationText.Get("MainWindow.Project.Paused");
            ExportProgressText = progressText;
        }
        NotifyExportUiStateChanged();
    }

    private async Task ContinuePausedPipelineAsync()
    {
        if (!_presentationState.IsPipelinePaused || IsBusy || _isDiscStopSaveInProgress)
            return;
        if (_discImportCancellation is not null || _exportCancellation is not null || _trackAnalysisCancellation is not null)
        {
            StatusText = LocalizationText.Get("Disc.StopSave.WorkersRunning");
            return;
        }
        if (new DiscProjectStopSaveService().HasPendingCommit(_currentProjectWorkFolder))
        {
            await StopAndSaveDiscProjectAsync();
            return;
        }

        if (_resumeDiscSourceSelectionRequired)
        {
            await ShowCurrentProjectSetupDialogAsync();
            return;
        }

        var resumeMerge = ProjectReviewState.Phase == ProjectPipelineState.Merging;
        _resumeDiscPreview.MarkProcessingStarted();
        var continuationStart =
            _services.Pipeline.PipelinePauseTransitionService.CreateContinuationStart();

        ApplyPipelineTransition(continuationStart);
        StatusText = LocalizationText.Get("MainWindow.Project.Continuing");
        NotifyExportUiStateChanged();

        if (resumeMerge)
        {
            var projectType = LoadedProjectState.IsMp3Disc ? ProjectManifestTypes.Mp3DiscProject :
                LoadedProjectState.IsAudioDisc ? ProjectManifestTypes.AudioCdProject : ProjectManifestTypes.FolderProject;
            await RunCurrentExportPlanAsync(_currentProjectWorkFolder, projectType, manualMergeRequested: true);
            return;
        }

        if (_presentationState.IsProjectExtensionRun && ProjectReviewState.Phase == ProjectPipelineState.Converting &&
            !LoadedProjectState.NeedsDiscImport && !_isAudioDiscProjectAwaitingRip)
        {
            RefreshLoadedProjectTrackStateFromPersistedProject(_currentProjectWorkFolder);
            await RunCurrentExportPlanAsync(_currentProjectWorkFolder, ResolveCurrentProjectType(ProjectManifestTypes.FolderProject));
            return;
        }

        if (_presentationState.IsProjectExtensionRun && _pausedProjectExtensionContinuation is not null)
        {
            await _pausedProjectExtensionContinuation();
            return;
        }

        var continuationPlan =
            _services.Pipeline.PausedPipelineContinuationService.CreatePlan(
                new PausedPipelineContinuationInput(
                    ProjectReviewState.Phase,
                    _isAudioDiscProjectAwaitingRip,
                    new PipelineProjectState(
                        LoadedProjectState.NeedsDiscImport,
                        LoadedProjectState.IsMp3Disc,
                        LoadedProjectState.IsAudioDisc,
                        LoadedProjectState.IsLocal)));

        if (continuationPlan.Kind == PausedPipelineContinuationKind.AudioDiscRip)
        {
            await RunPreparedAudioDiscRipAsync();
            return;
        }

        if (continuationPlan.Kind == PausedPipelineContinuationKind.Mp3DiscImport)
        {
            await ContinuePausedMp3DiscProjectAsync();
            return;
        }

        await RunCurrentExportPlanAsync(
            _currentProjectWorkFolder,
            continuationPlan.ProjectManifestType);
    }

    private async Task ContinuePausedMp3DiscProjectAsync()
    {
        var manifest = TryLoadCurrentMp3DiscProjectManifest(resetStateWhenFolderMissing: false);
        if (manifest is null)
        {
            EnterPipelinePause(LocalizationText.Get("Disc.Resume.Mp3ManifestMissing"));
            return;
        }

        var resumePlan = _services.Foundation.Mp3DiscProjectService.BuildResumePlan(manifest);
        if (resumePlan.NextMissingDiscNumber is null)
        {
            ApplyLoadedProjectState(LoadedProjectState.WithDiscImportRequirement(false));
            ApplyLoadedProjectState(LoadedProjectState.WithMp3DiscState(true));
            await RunCurrentExportPlanAsync(manifest.ProjectFolder, ProjectManifestTypes.Mp3DiscProject);
            return;
        }

        if (!await EnsureResumeDiscPreviewAsync())
            return;
        var selectedStartDrive = _resumeDiscStartDriveRoot;
        var sourceFolder = selectedStartDrive ?? await _services.Discs.DiscDriveService.ResolveResumeDiscSourceAsync(
            manifest.ProjectFolder,
            default,
            manifest.SourceFolder,
            _settings.LastDiscSourceFolder,
            SelectedFolder);

        if (string.IsNullOrWhiteSpace(sourceFolder))
        {
            sourceFolder = ShowDiscSourceSelectionDialog();
            if (!string.IsNullOrWhiteSpace(sourceFolder))
                sourceFolder = await ResolveSelectedDiscSourceAsync(sourceFolder);
        }

        if (string.IsNullOrWhiteSpace(sourceFolder))
        {
            EnterPipelinePause(LocalizationText.Format("Disc.Resume.SelectDrivePaused", resumePlan.NextMissingDiscNumber.Value));
            return;
        }

        SelectedFolder = sourceFolder;
        _settings.LastDiscSourceFolder = sourceFolder;
        SaveSettingsIfReady();
        ApplyLoadedProjectState(LoadedProjectState.WithDiscImportRequirement(true));
        ApplyLoadedProjectState(LoadedProjectState.WithMp3DiscState(true));

        await ImportMp3DiscProjectAsync(
            manifest.ProjectFolder,
            sourceFolder,
            manifest,
            startDiscNumber: resumePlan.NextMissingDiscNumber.Value,
            // Recheck availability after Pause; an accepted but uncommitted source
            // remains the first candidate until the current disc is committed.
            firstDiscAlreadyReady: false,
            autoExportWhenComplete: true,
            startWithSelectedDrive: selectedStartDrive is not null);
    }

    private PresetReconversionRollbackContext? CapturePresetReconversionPauseContext(string? projectFolder)
    {
        if (_presetReconversionRollbackContext is not null)
            return _presetReconversionRollbackContext;
        if (_presentationState.IsProjectExtensionRun) return null;
        if (ProjectReviewState.Phase is not (ProjectPipelineState.ReviewBeforeMerge or ProjectPipelineState.Completed) ||
            !_manualMergeReviewNeedsReconversion ||
            string.IsNullOrWhiteSpace(projectFolder))
        {
            return null;
        }

        var previousPreset = ResolvePresetReconversionPreviousPreset(projectFolder);
        if (string.IsNullOrWhiteSpace(previousPreset))
            return null;

        _manualMergeReviewPreparedPreset = previousPreset;
        return _services.Pipeline.ProjectExtensionRollbackService.CapturePresetReconversion(
            projectFolder,
            previousPreset,
            SelectedExportPreset) with { PreviousPhase = ProjectReviewState.Phase };
    }

    private string ResolvePresetReconversionPreviousPreset(string projectFolder)
    {
        if (!string.IsNullOrWhiteSpace(_manualMergeReviewPreparedPreset))
            return _manualMergeReviewPreparedPreset.Trim();

        var trackPresets = Tracks
            .Where(track => !track.IsExcluded)
            .Select(track => track.PreparedConvertedPreset?.Trim() ?? string.Empty)
            .Where(preset => !string.IsNullOrWhiteSpace(preset))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (trackPresets.Length == 1)
            return trackPresets[0];

        try
        {
            var persistedPlan = _services.Foundation.ProjectResumePlanService.BuildFromProjectFolder(projectFolder);
            var persistedPreset = persistedPlan?.SelectedPreset;
            if (!string.IsNullOrWhiteSpace(persistedPreset))
                return persistedPreset.Trim();
        }
        catch
        {
            // Starting a reconversion must fail closed below if no reliable baseline can be recovered.
        }

        return string.Empty;
    }

    private void ActivatePresetReconversionPauseContext(PresetReconversionRollbackContext? context)
    {
        if (context is null)
            return;

        _presetReconversionRollbackContext = context;
        NotifyExportUiStateChanged();
    }

    private void ClearPresetReconversionPauseContext()
    {
        if (_presetReconversionRollbackContext is null)
            return;

        _services.Pipeline.ProjectExtensionRollbackService.Commit(_presetReconversionRollbackContext.Snapshot);
        _presetReconversionRollbackContext = null;
        NotifyExportUiStateChanged();
    }

    private async Task StopPausedPresetReconversionAsync()
    {
        var context = _presetReconversionRollbackContext;
        if (_isDiscStopSaveInProgress || !_presentationState.IsPipelinePaused || context is null)
            return;

        _isDiscStopSaveInProgress = true;
        IsBusy = true;

        var transitionOwner = BeginPipelineUiTransition(PipelineUiTransitionKind.Discarding, PipelineUiTransitionTarget.Secondary);
        try
        {
            await Dispatcher.Yield(DispatcherPriority.Background);
            await WaitForProjectProducersAsync();
            _services.Pipeline.ProjectExtensionRollbackService.RollbackPresetReconversion(context);
            ReloadCanonicalMetadataSessionAfterRollback(context.Snapshot.ProjectFolder);

            if (LoadedProjectState.IsMp3Disc)
                _activeMp3DiscManifest = _services.Foundation.Mp3DiscProjectService.TryLoad(context.Snapshot.ProjectFolder);
            if (LoadedProjectState.IsAudioDisc)
                _activeAudioDiscManifest = _services.Foundation.AudioDiscProjectService.TryLoad(context.Snapshot.ProjectFolder);

            var neutralTransition =
                _services.Pipeline.PipelinePauseTransitionService.CreateContinuationStart();
            ApplyPipelineTransition(neutralTransition);

            // Restore the preset and review/completion state from before this run.
            if (!string.Equals(SelectedExportPreset, context.PreviousPreset, StringComparison.OrdinalIgnoreCase))
                SetSelectedExportPresetSilently(context.PreviousPreset);

            RefreshLoadedProjectTrackStateFromPersistedProject(context.Snapshot.ProjectFolder);
            _manualMergeReviewPreparedPreset = context.PreviousPreset;
            _projectReviewState = new ProjectReviewStateSnapshot(context.PreviousPhase,
                context.PreviousPhase == ProjectPipelineState.ReviewBeforeMerge,
                context.PreviousPhase == ProjectPipelineState.Completed);
            UpdateManualMergeReviewPresetState();
            UpdateFinalStatus(Tracks.Count);
            RefreshPartialPresetProgressText();
            ClearPresetReconversionPauseContext();
            NotifyExportUiStateChanged();
        }
        catch (Exception ex)
        {
            // A failed restoration keeps the rollback owner available for retry and close.
            SetPipelineState(ProjectPipelineState.Converting);
            ApplyPipelineTransition(_services.Pipeline.PipelinePauseTransitionService.CreateCompletedPause());
            AppDialogService.Error(
                this,
                LocalizationText.Get("MainWindow.Preset.StopFailed.Title"),
                LocalizationText.Get("MainWindow.Preset.StopFailed.Message"),
                details: ExportFailureDetailsService.SplitMessageLines(ex.Message));
            StatusText = LocalizationText.Get("MainWindow.Preset.CleanupFailed");
            NotifyExportUiStateChanged();
        }
        finally
        {
            EndPipelineUiTransition(transitionOwner);
            _isDiscStopSaveInProgress = false;
            IsBusy = false;
            NotifyExportUiStateChanged();
        }
    }

    private void BeginProjectExtensionPauseContext(Func<Task> continuation, Action stopAction)
    {
        _presentationState.IsProjectExtensionRun = true;
        _pausedProjectExtensionContinuation = continuation;
        _pausedProjectExtensionStopAction = stopAction;
        NotifyExportUiStateChanged();
    }

    private void ClearProjectExtensionPauseContext()
    {
        if (_resumeDiscPreview.Mp3Disc is not null)
        {
            _resumeDiscPreview.Clear();
            _resumeDiscStartDriveRoot = null;
        }
        if (_projectExtensionRollbackSnapshot is not null)
            _services.Pipeline.ProjectExtensionRollbackService.Commit(_projectExtensionRollbackSnapshot);
        _projectExtensionRollbackSnapshot = null;
        _presentationState.IsProjectExtensionRun = false;
        _pausedProjectExtensionContinuation = null;
        _pausedProjectExtensionStopAction = null;
        NotifyExportUiStateChanged();
    }

    private async Task StopPausedProjectExtensionAsync(bool confirm = true)
    {
        if (_isDiscStopSaveInProgress || !_presentationState.IsPipelinePaused || !_presentationState.IsProjectExtensionRun)
            return;

        if (confirm && !ConfirmProjectCloseIntent(ProjectCloseIntent.RollbackRun))
            return;

        var isDiscExtension = LoadedProjectState.IsAudioDisc || LoadedProjectState.IsMp3Disc;
        _isDiscStopSaveInProgress = true;
        IsBusy = true;
        var transitionOwner = BeginPipelineUiTransition(PipelineUiTransitionKind.Discarding, PipelineUiTransitionTarget.Secondary);
        try
        {
            await Dispatcher.Yield(DispatcherPriority.Background);
            if (isDiscExtension)
                await WaitForProjectProducersAsync();
            await Task.Yield();
            var stopAction = _pausedProjectExtensionStopAction;
            var neutralTransition =
                _services.Pipeline.PipelinePauseTransitionService.CreateContinuationStart();

            ApplyPipelineTransition(neutralTransition);
            stopAction?.Invoke();
            ClearProjectExtensionPauseContext();
        }
        catch (Exception ex)
        {
            ApplyPipelineTransition(_services.Pipeline.PipelinePauseTransitionService.CreateCompletedPause());
            StatusText = LocalizationText.Get("Disc.StopSave.Failed");
            AppDialogService.Error(this, LocalizationText.Get("Disc.StopSave.Failed"), LocalizationText.Get("Common.OperationFailed"), details: new[] { ex.Message });
        }
        finally
        {
            EndPipelineUiTransition(transitionOwner);
            _isDiscStopSaveInProgress = false;
            IsBusy = false;
            NotifyExportUiStateChanged();
        }
    }

    private async void DeleteNewProject_Click(object sender, RoutedEventArgs e)
    {
        if (CanDeletePausedNewProject) await DeleteNewProjectAsync(confirm: true);
    }

    private bool ConfirmProjectCloseIntent(ProjectCloseIntent intent)
    {
        var delete = intent == ProjectCloseIntent.DeleteNewProject;
        var save = intent == ProjectCloseIntent.Save;
        var actionKey = delete ? "Project.Close.Delete" : save ? "MainWindow.Action.Save" : "MainWindow.Action.Discard";
        return AppDialogService.Show(this,
            LocalizationText.Get(actionKey),
            LocalizationText.Get(delete ? "Project.Close.DeleteHeading" : save ? "MainWindow.Action.Save" : "Project.Close.DiscardHeading"),
            LocalizationText.Get(delete ? "Project.Close.DeleteMessage" : save ? "Project.Close.SaveMessage" : "Project.Close.DiscardMessage"),
            AppDialogKind.Warning,
            buttons: new[]
            {
                new AppDialogButton(LocalizationText.Get("Project.Close.Back"), AppDialogResult.Cancel, IsDefault: true, IsCancel: true),
                new AppDialogButton(LocalizationText.Get(actionKey), AppDialogResult.Yes, IsDanger: true)
            }) == AppDialogResult.Yes;
    }

    private async Task<bool> DeleteNewProjectAsync(bool confirm)
    {
        if (_isDiscStopSaveInProgress || !IsNewProjectPreparation || (confirm && !ConfirmProjectCloseIntent(ProjectCloseIntent.DeleteNewProject))) return false;
        var projectFolder = _currentProjectWorkFolder;
        _isDiscStopSaveInProgress = true;
        var transitionOwner = BeginPipelineUiTransition(PipelineUiTransitionKind.Stopping, PipelineUiTransitionTarget.DeleteNewProject);
        try
        {
            await Dispatcher.Yield(DispatcherPriority.Background);
            RequestActiveOperationCancellation();
            await WaitForProjectProducersAsync();
            // Release the idle workspace reference only for this explicitly authorized deletion.
            // The normal project-management guard continues to protect loaded projects.
            _currentProjectWorkFolder = string.Empty;
            var deletion = _services.Foundation.ProjectIndexService.DeleteProject(GetWorkingRootFolder(), projectFolder);
            if (!deletion.Deleted)
            {
                _currentProjectWorkFolder = projectFolder;
                throw new IOException(deletion.ErrorMessage);
            }
            ApplyPipelineTransition(_services.Pipeline.PipelinePauseTransitionService.CreateContinuationStart());
            ClearCurrentProjectWorkspace();
            RefreshResumeProjects(showStatus: false);
            return true;
        }
        catch (Exception error)
        {
            _currentProjectWorkFolder = projectFolder;
            AppDialogService.Error(this, LocalizationText.Get("MainWindow.Project.DeleteFailed.Title"), LocalizationText.Get("Common.OperationFailed"), details: new[] { error.Message });
            return false;
        }
        finally
        {
            EndPipelineUiTransition(transitionOwner);
            _isDiscStopSaveInProgress = false;
            IsBusy = false;
            NotifyExportUiStateChanged();
        }
    }

    private async Task<bool> CloseProjectFromWindowAsync(bool confirmIntent = true, bool cancelActiveWork = false)
    {
        var intent = ApplicationShutdownService.ResolveProjectCloseIntent(_sessionProject.DeleteOnWindowClose(_currentProjectWorkFolder,
                ProjectReviewState.Phase is ProjectPipelineState.AcquiringSources or ProjectPipelineState.Converting),
            _presentationState.IsProjectExtensionRun, _presetReconversionRollbackContext is not null,
            ProjectReviewState.Phase == ProjectPipelineState.Merging);
        // Capture eligibility before cancellation changes the presentation state.
        var saveDiscBoundary = intent == ProjectCloseIntent.Save &&
            (LoadedProjectState.IsAudioDisc || LoadedProjectState.IsMp3Disc) &&
            (ProjectReviewState.Phase is ProjectPipelineState.AcquiringSources or ProjectPipelineState.Converting ||
             (_sessionProject.Owns(_currentProjectWorkFolder) && _sessionProject.StartAccepted &&
              ProjectReviewState.Phase == ProjectPipelineState.Preparing));
        if (confirmIntent && (intent != ProjectCloseIntent.Save || saveDiscBoundary) && !ConfirmProjectCloseIntent(intent))
        {
            CancelApplicationRestartRequest();
            return false;
        }
        _isProjectCloseInProgress = true;
        NotifyExportUiStateChanged();
        var transitionOwner = BeginPipelineUiTransition(intent is ProjectCloseIntent.RollbackRun or ProjectCloseIntent.DiscardMerge
            ? PipelineUiTransitionKind.Discarding : PipelineUiTransitionKind.Stopping, PipelineUiTransitionTarget.Secondary);
        try
        {
            await Dispatcher.Yield(DispatcherPriority.Background);
            if (intent == ProjectCloseIntent.DeleteNewProject)
            {
                if (cancelActiveWork)
                    ApplyPipelineTransition(_services.Pipeline.PipelinePauseTransitionService.CreateFailureClear(
                        _presentationState.IsPipelinePaused, false, IsBusy, IsExporting, IsDiscImporting));
                // Request a pause so producers settle without their own abort/resume dialogs.
                if (!cancelActiveWork && !_presentationState.IsPipelinePaused) await RequestPipelinePauseAsync();
                return await DeleteNewProjectAsync(confirm: false);
            }
            if (cancelActiveWork)
            {
                // A confirmed shutdown is a real cancellation, not a newly requested pause.
                ApplyPipelineTransition(_services.Pipeline.PipelinePauseTransitionService.CreateFailureClear(
                    _presentationState.IsPipelinePaused, false, IsBusy, IsExporting, IsDiscImporting));
            }
            else if (IsDiscImporting || IsExporting) await RequestPipelinePauseAsync();
            RequestActiveOperationCancellation();
            await WaitForProjectProducersAsync();
            if (intent == ProjectCloseIntent.DiscardMerge && !DiscardPausedMerge()) return false;
            if (intent == ProjectCloseIntent.RollbackRun)
            {
                ApplyPipelineTransition(_services.Pipeline.PipelinePauseTransitionService.CreateCompletedPause());
                if (_presetReconversionRollbackContext is not null) await StopPausedPresetReconversionAsync();
                else await StopPausedProjectExtensionAsync(confirm: false);
                if (_presetReconversionRollbackContext is not null || _presentationState.IsProjectExtensionRun) return false;
            }
            if (saveDiscBoundary)
            {
                _isDiscStopSaveInProgress = true;
                try
                {
                    await CommitStoppedDiscProjectAsync(_currentProjectWorkFolder);
                    // The disk commit now owns manifests and track state. Detach stale UI
                    // snapshots before any final window-close checkpoint can run.
                    ClearCurrentProjectWorkspace();
                    return true;
                }
                finally { _isDiscStopSaveInProgress = false; }
            }
            if (!TryCheckpointMetadataBeforeClose() || !TryCheckpointEditableProject()) return false;
            if (ProjectReviewState.Phase is not (ProjectPipelineState.ReviewBeforeMerge or ProjectPipelineState.Completed))
                _services.Workflows.ProjectSnapshotService.SaveEditableProjectSnapshot(
                    _currentProjectWorkFolder, ResolveCurrentProjectType(ProjectManifestTypes.FolderProject),
                    CreateProjectSnapshotFromUi(), Tracks.ToList(), BuildFinalOutputPath());
            TrySaveActiveProjectSnapshot();
            PersistTrackListState();
            return true;
        }
        catch (Exception error)
        {
            AppDialogService.Error(this, LocalizationText.Get("Disc.StopSave.Failed"), LocalizationText.Get("Common.OperationFailed"), details: new[] { error.Message });
            return false;
        }
        finally
        {
            EndPipelineUiTransition(transitionOwner);
            _isProjectCloseInProgress = false;
            NotifyExportUiStateChanged();
        }
    }

    private void ClearCurrentProjectWorkspace()
    {
        _sessionProject.Clear();
        _activeLocalPreparation = null;
        ClearPresetReconversionPauseContext();
        ClearProjectExtensionPauseContext();
        ClearSelectedSourceDisplayOverride();
        _activeMp3DiscManifest = null;
        ResetLoadedResumeProjectState();
        ResetMetadataForNewFolderProject();
        SetMetadataEditingAvailable(false);
        _currentFolderPath = string.Empty;
        _currentProjectWorkFolder = string.Empty;
        SelectedFolder = string.Empty;
        Tracks.Clear();
        UpdateIndexes();
        ExportProgressPercent = 0;
        ExportProgressText = BuildIdleExportProgressText();
        StatusText = NoProjectStatusText;
        ApplyWorkflowStatusViewState(new WorkflowStatusFormatter().Format(WorkflowStatusSnapshot.Empty));
        SetPipelineState(ProjectPipelineState.Preparing);
        NotifyExportUiStateChanged();
        OnExportPreviewChanged();
    }

    private async void StopAndSaveDiscProject_Click(object sender, RoutedEventArgs e)
    {
        if (CanStopAndSaveDiscProject)
            await StopAndSaveDiscProjectAsync();
    }

    private async Task StopAndSaveDiscProjectAsync()
    {
        if (_isDiscStopSaveInProgress || _presentationState.IsProjectExtensionRun ||
            _presetReconversionRollbackContext is not null || ProjectReviewState.Phase == ProjectPipelineState.Merging) return;
        _isDiscStopSaveInProgress = true;
        var projectFolder = _currentProjectWorkFolder;
        IsBusy = true;
        StatusText = LocalizationText.Get("Disc.StopSave.Saving");
        IsProgressIndeterminate = true;
        var performanceApp = Application.Current as App;
        using var operation = performanceApp?.StartPerformanceOperation("disc.stop-save",
            LoadedProjectState.IsMp3Disc ? PerformanceProjectType.Mp3Disc : PerformanceProjectType.AudioCd) ?? default;
        PerformancePhaseScope phase = default;
        var transitionOwner = BeginPipelineUiTransition(PipelineUiTransitionKind.Stopping, PipelineUiTransitionTarget.DiscStopSave);
        try
        {
            await Dispatcher.Yield(DispatcherPriority.Background);
            phase = performanceApp?.StartPerformancePhase(operation.OperationId, "stop-save.wait-for-producers") ?? default;
            RequestActiveOperationCancellation();
            await WaitForProjectProducersAsync();
            phase.Complete();
            phase = performanceApp?.StartPerformancePhase(operation.OperationId, "stop-save.checkpoint") ?? default;
            IsBusy = true;
            await CommitStoppedDiscProjectAsync(projectFolder, () =>
            {
                phase.Complete();
                phase = performanceApp?.StartPerformancePhase(operation.OperationId, "stop-save.commit-cleanup") ?? default;
            });
            phase.Complete();
            phase = performanceApp?.StartPerformancePhase(operation.OperationId, "stop-save.workspace-catalog") ?? default;
            ApplyPipelineTransition(_services.Pipeline.PipelinePauseTransitionService.CreateContinuationStart());
            ClearProjectExtensionPauseContext();
            ClearCurrentProjectWorkspace();
            StatusText = LocalizationText.Get("Disc.StopSave.Saved");
            RefreshResumeProjects(showStatus: false);
            phase.Complete();
            operation.Complete();
        }
        catch (Exception ex)
        {
            if (ex is OperationCanceledException) { phase.Cancel(); operation.Cancel(); }
            else { phase.Fail(ex); operation.Fail(ex); }
            StatusText = LocalizationText.Get("Disc.StopSave.Failed");
            AppDialogService.Error(this, LocalizationText.Get("Disc.StopSave.Failed"), LocalizationText.Get("Common.OperationFailed"), details: new[] { ex.Message });
        }
        finally
        {
            EndPipelineUiTransition(transitionOwner);
            phase.Dispose();
            performanceApp?.TryFinalizeDisabledPerformanceSession();
            _isDiscStopSaveInProgress = false;
            IsProgressIndeterminate = false;
            IsBusy = false;
            NotifyExportUiStateChanged();
        }
    }

    private async Task CommitStoppedDiscProjectAsync(string projectFolder, Action? checkpointCompleted = null)
    {
        var service = new DiscProjectStopSaveService();
        if (!service.HasPendingCommit(projectFolder))
        {
            _projectMetadataSession.Flush();
            if (_activeAudioDiscManifest is not null)
                SaveCurrentAudioDiscProjectSnapshot(_activeAudioDiscManifest, force: true);
            if (LoadedProjectState.IsMp3Disc)
            {
                var mp3 = TryLoadCurrentMp3DiscProjectManifest(resetStateWhenFolderMissing: false);
                if (mp3 is not null)
                    SaveCurrentMp3DiscProjectSnapshot(mp3);
            }
            _services.Foundation.TrackListStateService.Save(projectFolder, Tracks.ToList());
        }
        checkpointCompleted?.Invoke();
        await Task.Run(() => service.Save(projectFolder));
    }

    private async Task WaitForProjectProducersAsync()
    {
        var wait = Stopwatch.StartNew();
        while (_discImportCancellation is not null || _exportCancellation is not null || _trackAnalysisCancellation is not null)
        {
            if (wait.Elapsed > TimeSpan.FromSeconds(30))
                throw new TimeoutException(LocalizationText.Get("Disc.StopSave.WorkersRunning"));
            await Task.Delay(50);
        }
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);
    }

    private void CloseCurrentProject()
    {
        if (!TryCheckpointMetadataBeforeClose()) return;
        if (!TryCheckpointEditableProject()) return;
        var closeTransition =
            _services.Pipeline.PipelinePauseTransitionService.CreateContinuationStart();

        ApplyPipelineTransition(closeTransition);
        TrySaveActiveProjectSnapshot();
        PersistTrackListState();
        ClearSelectedSourceDisplayOverride();
        ResetLoadedResumeProjectState();
        ResetMetadataForNewFolderProject();
        SetMetadataEditingAvailable(false);
        _currentFolderPath = string.Empty;
        _currentProjectWorkFolder = string.Empty;
        SelectedFolder = string.Empty;
        Tracks.Clear();
        UpdateIndexes();
        ExportProgressPercent = 0;
        ExportProgressText = BuildIdleExportProgressText();
        StatusText = NoProjectStatusText;
        NotifyExportUiStateChanged();
        OnExportPreviewChanged();
    }

    private void ResetWorkspaceAfterCanceledTrackAnalysis(TrackAnalysisCancellationScope scope)
    {
        _trackAnalysisCancellation = null;
        _trackAnalysisCancellationScope = TrackAnalysisCancellationScope.None;
        ClearSelectedSourceDisplayOverride();
        ClearPendingMp3DiscPreparation();
        _activeMp3DiscManifest = null;
        ResetLoadedResumeProjectState();
        ResetMetadataForNewFolderProject();
        SetMetadataEditingAvailable(false);
        _currentFolderPath = string.Empty;
        _currentProjectWorkFolder = string.Empty;
        SelectedFolder = string.Empty;
        Tracks.Clear();
        UpdateIndexes();
        ExportProgressPercent = 0;
        IsProgressIndeterminate = false;
        ExportProgressText = BuildIdleExportProgressText();
        StatusText = scope == TrackAnalysisCancellationScope.ResumeLoad
            ? "Projektladen abgebrochen."
            : "Ordnerimport abgebrochen.";
        NotifyExportUiStateChanged();
        OnExportPreviewChanged();
    }

    private ExportCheckResult ValidateExportPlan()
    {
        return _services.Pipeline.ExportValidationService.Validate(
            Tracks,
            _ffmpegStatus,
            _currentFolderPath,
            EffectiveOutputFolder,
            OutputFileNamePreview,
            OutputExtension,
            GetWorkingRootFolder(),
            finalOutputPathOverride: BuildFinalOutputPath(),
            chapterMode: _settings.ChapterSettings?.Mode);
    }

    private string BuildFinalOutputPath()
    {
        return _services.Pipeline.OutputFolderLayoutService.BuildOutputPath(
            EffectiveOutputFolder,
            Author,
            BookTitle,
            OutputFileNamePreview,
            _settings.OutputFolderLayout,
            Album,
            Series);
    }

    private string BuildOutputRelativePathPreview()
    {
        return _services.Pipeline.OutputFolderLayoutService.BuildRelativeOutputPath(
            Author,
            BookTitle,
            OutputFileNamePreview,
            _settings.OutputFolderLayout,
            Album,
            Series);
    }

    private bool ShowExportWarnings(IReadOnlyList<string> warnings)
    {
        var result = AppDialogService.Show(
            this,
            title: LocalizationText.Get("MainWindow.Export.Review.Title"),
            heading: LocalizationText.Get("MainWindow.Export.Review.Title"),
            message: LocalizationText.Get("MainWindow.Export.Review.Message"),
            kind: AppDialogKind.Warning,
            details: AppDialogService.LimitDetails(warnings, 80),
            buttons: new[]
            {
                new AppDialogButton(LocalizationText.Get("MainWindow.Export.ExportAction"), AppDialogResult.Yes, IsPrimary: true, IsDefault: true),
                new AppDialogButton(LocalizationText.Get("Common.Cancel"), AppDialogResult.Cancel, IsCancel: true)
            });

        return result == AppDialogResult.Yes;
    }

    private void ShowExportBlockingErrors(IReadOnlyList<string> errors)
    {
        var containsInvalidAudio = errors.Any(error =>
            error.Contains("Keine gültige Audiodatei:", StringComparison.OrdinalIgnoreCase));

        _runtimeServices.NotificationService.Notify(new NotificationEventContext(
            NotificationEvent.Error,
            RequiresUserInteraction: true,
            IsBackgroundOperation: true,
            Source: "ExportValidation",
            Reason: containsInvalidAudio ? "InvalidAudio" : "BlockingErrors"));

        AppDialogService.Show(
            this,
            title: LocalizationText.Get(containsInvalidAudio ? "MainWindow.Export.InvalidAudio.Title" : "MainWindow.Export.Review.Title"),
            heading: LocalizationText.Get(containsInvalidAudio ? "MainWindow.Export.InvalidAudio.Heading" : "MainWindow.Export.Blocked.Heading"),
            message: containsInvalidAudio
                ? LocalizationText.Get("MainWindow.Export.InvalidAudio.Message")
                : LocalizationText.Get("MainWindow.Export.Blocked.Message"),
            kind: AppDialogKind.Error,
            details: AppDialogService.LimitDetails(errors, 80),
            buttons: new[]
            {
                new AppDialogButton("OK", AppDialogResult.Ok, IsPrimary: true, IsDefault: true, IsCancel: true)
            });
    }

    private async Task ExportToAacWithWorkFolderAsync(
        List<TrackInfo> trackSnapshot,
        string outputPath,
        string? projectWorkFolderOverride = null,
        string projectType = ProjectManifestTypes.FolderProject,
        bool manualMergeRequested = false)
    {
        FlushPendingChapterMetadataRefresh();
        var ffmpegPath = _ffmpegStatus.FfmpegPath ?? string.Empty;
        if (string.IsNullOrWhiteSpace(ffmpegPath))
        {
            AppDialogService.Error(
                this,
                LocalizationText.Get("MainWindow.Export.FfmpegMissing.Title"),
                LocalizationText.Get("MainWindow.Export.FfmpegMissing.Message"));
            StatusText = LocalizationText.Get("MainWindow.Export.FfmpegMissing.Status");
            return;
        }

        var exportPlan = _services.Pipeline.ExportPlanService.Create(new ExportPlanRequest(
            trackSnapshot,
            _currentFolderPath,
            GetProjectFolderStructure().LocalProjectsFolder,
            outputPath,
            SelectedExportPreset,
            ResolveParallelJobCount(),
            projectWorkFolderOverride,
            projectType,
            Author,
            BookTitle));

        _currentProjectWorkFolder = exportPlan.ProjectWorkFolder;

        var exportPerformanceProjectType = ResolveExportPerformanceProjectType(projectType);
        var exportPerformanceApp = Application.Current as App;
        var exportPerformanceScope = exportPerformanceApp?.StartPerformanceOperation(
            "export.run",
            exportPerformanceProjectType,
            CreateExportPerformanceWorkload(
                trackSnapshot,
                exportPerformanceProjectType,
                _currentFolderPath)) ?? default;

        if (exportPerformanceScope.IsActive && exportPerformanceApp is not null)
        {
            exportPerformanceApp.PerformanceTraceService?.RecordParallelism(
                exportPerformanceScope.OperationId, ParallelJobsInput, exportPlan.ParallelConversions);
            exportPerformanceApp.RecordPerformanceDriveRole(
                exportPerformanceScope.OperationId,
                _currentFolderPath,
                PerformanceDriveRole.Source);
            exportPerformanceApp.RecordPerformanceDriveRole(
                exportPerformanceScope.OperationId,
                exportPlan.ProjectWorkFolder,
                PerformanceDriveRole.ProjectWorking);
            exportPerformanceApp.RecordPerformanceDriveRole(
                exportPerformanceScope.OperationId,
                Path.GetDirectoryName(exportPlan.FinalOutputPath),
                PerformanceDriveRole.Output);
        }

        void CompleteExportPerformanceOperation(ExportWorkflowResult result)
        {
            if (!exportPerformanceScope.IsActive || exportPerformanceApp is null)
                return;

            try
            {
                exportPerformanceApp.UpdatePerformanceWorkload(
                    exportPerformanceScope.OperationId,
                    CreateExportPerformanceWorkload(
                        trackSnapshot,
                        exportPerformanceProjectType,
                        _currentFolderPath,
                        result.Status == ExportWorkflowResultStatus.Completed
                            ? result.OutputPath
                            : null,
                        result.ConversionResumeState));

                switch (result.Status)
                {
                    case ExportWorkflowResultStatus.Completed:
                    case ExportWorkflowResultStatus.PausedBeforeMerge:
                        exportPerformanceScope.Complete();
                        break;

                    case ExportWorkflowResultStatus.Canceled:
                    case ExportWorkflowResultStatus.FinalOutputDiscarded:
                        exportPerformanceScope.Cancel(_presentationState.PauseRequested);
                        break;

                    case ExportWorkflowResultStatus.Failed:
                        exportPerformanceScope.Fail(
                            result.Error ?? new InvalidOperationException("Export workflow failed."));
                        break;
                }
            }
            catch
            {
                // Performance instrumentation must never alter export result handling.
            }
            finally
            {
                exportPerformanceScope = default;
                exportPerformanceApp.TryFinalizeDisabledPerformanceSession();
            }
        }

        void FailExportPerformanceOperation(Exception exception)
        {
            if (!exportPerformanceScope.IsActive || exportPerformanceApp is null)
                return;

            try
            {
                exportPerformanceScope.Fail(exception);
            }
            catch
            {
                // Performance instrumentation must never replace the export exception.
            }
            finally
            {
                exportPerformanceScope = default;
                exportPerformanceApp.TryFinalizeDisabledPerformanceSession();
            }
        }

        var exportPerformanceContext =
            exportPerformanceScope.IsActive && exportPerformanceApp is not null
                ? new ExportPerformanceContext(
                    phaseName => exportPerformanceApp.StartPerformancePhase(
                        exportPerformanceScope.OperationId,
                        phaseName),
                    (phaseId, purpose) => exportPerformanceApp.CreatePerformanceExternalProcessContext(
                        exportPerformanceScope.OperationId,
                        phaseId,
                        purpose),
                    () => _presentationState.PauseRequested)
                : null;

        var audioDiscManifest = string.Equals(
                projectType,
                ProjectManifestTypes.AudioCdProject,
                StringComparison.OrdinalIgnoreCase)
            ? _activeAudioDiscManifest
            : null;

        if (audioDiscManifest is not null)
        {
            _services.Foundation.AudioDiscProjectService.MarkExportStarted(audioDiscManifest);
            TrySaveCurrentAudioDiscProjectSnapshot(audioDiscManifest, force: true);
        }

        var useLocalWorkflowStatus = string.Equals(projectType, ProjectManifestTypes.FolderProject, StringComparison.OrdinalIgnoreCase);
        var useMp3WorkflowStatus = string.Equals(projectType, ProjectManifestTypes.Mp3DiscProject, StringComparison.OrdinalIgnoreCase);
        var useAudioWorkflowStatus = string.Equals(projectType, ProjectManifestTypes.AudioCdProject, StringComparison.OrdinalIgnoreCase);
        var useWorkflowStatus = useLocalWorkflowStatus || useMp3WorkflowStatus || useAudioWorkflowStatus;
        var workflowProjectKind = useAudioWorkflowStatus
            ? WorkflowProjectKind.AudioDisc
            : useMp3WorkflowStatus
                ? WorkflowProjectKind.Mp3Disc
                : WorkflowProjectKind.Folder;
        var audioDiscCount = useAudioWorkflowStatus
            ? ResolveAudioDiscExportDiscCount(audioDiscManifest, trackSnapshot)
            : 0;

        IsBusy = true;
        IsExporting = true;
        // The shared workflow enters Converting only when audio preparation is required.
        ExportProgressPercent = 0;
        if (!useWorkflowStatus)
            ExportProgressText = LocalizationText.Get("MainWindow.Export.Preparing");

        _exportCancellation = new CancellationTokenSource();
        var refreshTrackStateAfterRun = false;
        var exportStatusOperationId = useWorkflowStatus
            ? BeginWorkflowStatusOperation(exportPlan.ProjectWorkFolder)
            : Guid.Empty;
        var exportPreset = ExportPreset.Parse(SelectedExportPreset);

        void PublishPreparedAudioAnalysis(int completed, int total)
        {
            PublishWorkflowStatus(exportStatusOperationId, new WorkflowStatusSnapshot
            {
                ProjectId = exportPlan.ProjectWorkFolder,
                ProjectKind = workflowProjectKind,
                ProjectState = ProjectReviewState.Phase,
                ActiveActivities = new HashSet<WorkflowActivity> { WorkflowActivity.AnalyzingConvertedTracks },
                AnalysisProgress = new AnalysisProgress(WorkflowAnalysisKind.ConvertedTracks,
                    completed, total, total <= 0 ? 0 : completed * 100 / total)
            });
        }

        if (useWorkflowStatus)
        {
            PublishPreparedAudioAnalysis(0, trackSnapshot.Count);
        }

        try
        {
            var request = new ExportWorkflowRequest(
                exportPlan,
                _currentFolderPath,
                ffmpegPath,
                CreateProjectSnapshotFromUi(),
                CreateCurrentFinalAudioTagData(isFinalOutput: false),
                ChapterSettings: _settings.ChapterSettings,
                LegacyUseLeadingZerosInChapterSuggestions: false,
                FfprobePath: _ffmpegStatus.FfprobePath ?? string.Empty,
                CreateFinalTags: (duration, chapterCount) => CreateCurrentFinalAudioTagData(isFinalOutput: true, duration, chapterCount),
                CanonicalChapterMetadata: ChapterCanonicalMetadataSnapshot.Capture(_projectMetadata.CanonicalMetadata),
                ManualMergeRequested: manualMergeRequested);

            var callbacks = new ExportWorkflowCallbacks
            {
                SetStatusText = useWorkflowStatus ? null : text => Dispatcher.Invoke(() => StatusText = text),
                SetProgressText = useWorkflowStatus ? null : text => Dispatcher.Invoke(() => ExportProgressText = text),
                SetProgressPercent = useWorkflowStatus ? null : percent => Dispatcher.Invoke(() => ExportProgressPercent = percent),
                BeforePrepareConvertedFile = path =>
                {
                    var baseline = _projectExtensionRollbackSnapshot ?? _presetReconversionRollbackContext?.Snapshot;
                    if (baseline is not null)
                        _services.Pipeline.ProjectExtensionRollbackService.PreserveConvertedFile(baseline, path);
                },
                SetPipelineState = state => Dispatcher.Invoke(() =>
                {
                    SetPipelineState(state);
                    if (state == ProjectPipelineState.ReviewBeforeMerge)
                    {
                        ClearPresetReconversionPauseContext();
                        ClearProjectExtensionPauseContext();
                    }
                }),
                ReportPreparedAudioAnalysis = useWorkflowStatus ? PublishPreparedAudioAnalysis : null,
                ReportConversionProgress = useWorkflowStatus
                    ? (completed, total, currentTicks, totalTicks, activeIndexes) =>
                    {
                        var percent = totalTicks <= 0 ? 0 : (int)Math.Clamp(currentTicks * 100 / totalTicks, 0, 100);
                        PublishWorkflowStatus(exportStatusOperationId, CreateExportStatusSnapshot(
                            workflowProjectKind,
                            exportPlan.ProjectWorkFolder,
                            total,
                            completed,
                            activeIndexes.Select(index => index + 1).ToArray(),
                            exportPreset,
                            percent,
                            totalDiscCount: audioDiscCount));
                    }
                    : ReportExportProgress,
                ReportMergeProgress = useWorkflowStatus
                    ? (currentFile, totalFiles, percent) => PublishWorkflowStatus(
                        exportStatusOperationId,
                        new WorkflowStatusSnapshot
                        {
                            ProjectId = exportPlan.ProjectWorkFolder,
                            ProjectKind = workflowProjectKind,
                            ProjectState = ProjectPipelineState.Merging,
                            MergeProgress = new MergeProgress(currentFile, totalFiles, (int)Math.Round(percent))
                        })
                    : null,
                NotifyWritingMetadata = useWorkflowStatus
                    ? () => PublishWorkflowStatus(
                        exportStatusOperationId,
                        new WorkflowStatusSnapshot
                        {
                            ProjectId = exportPlan.ProjectWorkFolder,
                            ProjectKind = workflowProjectKind,
                            ProjectState = ProjectPipelineState.Merging,
                            MergeProgress = new MergeProgress(trackSnapshot.Count, trackSnapshot.Count, 100, IsWritingMetadata: true)
                        })
                    : null,
                IsPauseRequested = () => _presentationState.PauseRequested,
                MergeAutomaticallyAfterConversion = () => _settings.MergeAutomaticallyAfterConversion,
                ResolveFinalOutputConflict = ResolveFinalOutputConflict,
                ResolveFinalOutputFailure = ResolveFinalOutputFailure
            };

            var result = await _services.Workflows.ExportWorkflowService.RunAsync(
                request,
                callbacks,
                _exportCancellation.Token,
                exportPerformanceContext);

            CompleteExportPerformanceOperation(result);

            switch (result.Status)
            {
                case ExportWorkflowResultStatus.PausedBeforeMerge:
                    if (audioDiscManifest is not null)
                    {
                        _services.Foundation.AudioDiscProjectService.MarkExportPausedBeforeMerge(audioDiscManifest);
                        TrySaveCurrentAudioDiscProjectSnapshot(audioDiscManifest, force: true);
                    }

                    ClearPresetReconversionPauseContext();
                    SetPipelineState(ProjectPipelineState.ReviewBeforeMerge);
                    if (useWorkflowStatus)
                        EndWorkflowStatusOperation(exportStatusOperationId);
                    PauseBeforeMergeForManualReview();
                    return;

                case ExportWorkflowResultStatus.Completed:
                    _lockedDiscResumePreset = null;
                    if (LoadedProjectState.IsMp3Disc)
                    {
                        var completedMp3Manifest = _services.Foundation.Mp3DiscProjectService.TryLoad(exportPlan.ProjectWorkFolder);
                        if (completedMp3Manifest is not null)
                        {
                            completedMp3Manifest.IsStoppedForResume = false;
                            SaveCurrentMp3DiscProjectSnapshot(completedMp3Manifest);
                        }
                    }
                    if (audioDiscManifest is not null)
                    {
                        _services.Foundation.AudioDiscProjectService.MarkExportCompleted(audioDiscManifest, result.OutputPath);
                        TrySaveCurrentAudioDiscProjectSnapshot(audioDiscManifest, force: true);
                    }

                    ClearPresetReconversionPauseContext();
                    _projectReviewState = ProjectReviewState.WithManualMergeReview(true);
                    _manualMergeReviewPreparedPreset = SelectedExportPreset;
                    _manualMergeReviewNeedsReconversion = false;
                    _manualMergeReviewHasPreparedTracks = true;
                    _projectReviewState = ProjectReviewState.WithCompletion(true);
                    SetPipelineState(ProjectPipelineState.Completed);
                    SetTrackActionDisplayOverride(LocalizationText.Get("Workflow.Review.MergeAction"));
                    NotifyExportUiStateChanged();
                    if (useWorkflowStatus)
                    {
                        var outputSize = File.Exists(result.OutputPath) ? new FileInfo(result.OutputPath).Length : (long?)null;
                        PublishWorkflowStatus(
                            exportStatusOperationId,
                            new WorkflowStatusSnapshot
                            {
                                ProjectId = exportPlan.ProjectWorkFolder,
                                ProjectKind = workflowProjectKind,
                                ProjectState = ProjectPipelineState.Completed,
                                IsSuccessfulExport = true,
                                TotalSourceItems = trackSnapshot.Count,
                                TotalChapters = ExportPreviewMetricsService.CountPlannedChapters(trackSnapshot),
                                OutputFileSizeBytes = outputSize,
                                SourceProgress = CreateCompletedExportSourceProgress(
                                    workflowProjectKind,
                                    trackSnapshot,
                                    audioDiscCount)
                            });
                    }
                    else
                    {
                        ExportProgressPercent = 100;
                        ExportProgressText = LocalizationText.Get("MainWindow.Export.SavedProgress");
                        StatusText = LocalizationText.Get("MainWindow.Export.SavedStatus");
                    }
                    _runtimeServices.NotificationService.Notify(NotificationEvent.ProjectCompleted);

                    var exportFinishedResult = AppDialogService.Show(
                        this,
                        title: LocalizationText.Get("MainWindow.Export.Completed.Title"),
                        heading: LocalizationText.Get("MainWindow.Export.Completed.Heading"),
                        message: result.OutputPath,
                        kind: AppDialogKind.Information,
                        buttons: new[]
                        {
                            new AppDialogButton(LocalizationText.Get("MainWindow.Export.OpenFolder"), AppDialogResult.Yes, IsPrimary: true),
                            new AppDialogButton(LocalizationText.Get("Common.OK"), AppDialogResult.Ok, IsDefault: true, IsCancel: true)
                        },
                        restoreOwnerFocus: false);

                    if (exportFinishedResult == AppDialogResult.Yes)
                        OpenFolderInExplorer(Path.GetDirectoryName(result.OutputPath) ?? EffectiveOutputFolder, LocalizationText.Get("MainWindow.Export.OutputFolder"));

                    return;

                case ExportWorkflowResultStatus.FinalOutputDiscarded:
                    refreshTrackStateAfterRun = true;
                    if (audioDiscManifest is not null)
                    {
                        _services.Foundation.AudioDiscProjectService.MarkExportCanceled(audioDiscManifest);
                        TrySaveCurrentAudioDiscProjectSnapshot(audioDiscManifest, force: true);
                    }

                    ClearPresetReconversionPauseContext();
                    ReturnToReviewAfterInterruptedExport();
                    ExportProgressText = LocalizationText.Get("MainWindow.Export.DiscardedProgress");
                    StatusText = LocalizationText.Get("MainWindow.Export.DiscardedStatus");
                    return;

                case ExportWorkflowResultStatus.Canceled:
                    refreshTrackStateAfterRun = true;
                    if (audioDiscManifest is not null)
                    {
                        _services.Foundation.AudioDiscProjectService.MarkExportCanceled(audioDiscManifest);
                        TrySaveCurrentAudioDiscProjectSnapshot(audioDiscManifest, force: true);
                    }

                    if (_presentationState.PauseRequested &&
                        (ProjectReviewState.Phase == ProjectPipelineState.Converting ||
                         ProjectReviewState.Phase == ProjectPipelineState.Merging))
                    {
                        // Keep Merging distinct from conversion while paused.
                        if (useWorkflowStatus)
                        {
                            var last = _services.Status.WorkflowStatusCoordinator.Snapshot;
                            PublishWorkflowStatus(exportStatusOperationId, last with
                            {
                                SourceProgress = null,
                                IsPaused = true
                            });
                            EndWorkflowStatusOperation(exportStatusOperationId);
                            EnterPipelinePause(string.Empty, preserveWorkflowStatus: true);
                        }
                        else
                        {
                            EnterPipelinePause(LocalizationText.Get("MainWindow.Export.ConversionPaused"));
                        }
                        return;
                    }

                    if (_isShutdownInProgress) return; // Close owns the captured rollback and confirmation.
                    if (_presetReconversionRollbackContext is not null || _presentationState.IsProjectExtensionRun)
                    {
                        EnterPipelinePause(LocalizationText.Get("MainWindow.Export.ConversionPaused"));
                        return;
                    }
                    ClearPresetReconversionPauseContext();
                    var canceledDuringMerge = ProjectReviewState.Phase == ProjectPipelineState.Merging;
                    var lastStatus = useWorkflowStatus ? _services.Status.WorkflowStatusCoordinator.Snapshot : null;
                    ReturnToReviewAfterInterruptedExport();
                    if (useWorkflowStatus && canceledDuringMerge)
                    {
                        PublishWorkflowStatus(
                            exportStatusOperationId,
                            (lastStatus ?? WorkflowStatusSnapshot.Empty) with
                            {
                                ProjectId = exportPlan.ProjectWorkFolder,
                                ProjectKind = workflowProjectKind,
                                ProjectState = ProjectPipelineState.ReviewBeforeMerge,
                                IsMergeAborted = true
                            });
                        EndWorkflowStatusOperation(exportStatusOperationId);

                        AppDialogService.Show(
                            this,
                            title: LocalizationText.Get("MainWindow.Export.MergeAborted.Title"),
                            heading: LocalizationText.Get("MainWindow.Export.MergeAborted.Title"),
                            message: LocalizationText.Get("MainWindow.Export.MergeAborted.Message"),
                            kind: AppDialogKind.Information,
                            buttons: new[]
                            {
                                new AppDialogButton(LocalizationText.Get("Common.OK"), AppDialogResult.Ok, IsPrimary: true, IsDefault: true, IsCancel: true)
                            });
                    }
                    else
                    {
                        ExportProgressText = LocalizationText.Get("MainWindow.Export.CanceledProgress");
                        StatusText = LocalizationText.Get("MainWindow.Export.CanceledStatus");
                        ShowUserCanceledProjectDialog();
                    }
                    return;

                case ExportWorkflowResultStatus.Failed:
                    if (_isShutdownInProgress) return; // Preserve the rollback owner for controlled close.
                    if (_presetReconversionRollbackContext is not null || _presentationState.IsProjectExtensionRun)
                    {
                        SetPipelineState(ProjectPipelineState.Converting);
                        EnterPipelinePause(LocalizationText.Get("MainWindow.Export.ConversionPaused"));
                        AppDialogService.Error(this, LocalizationText.Get("MainWindow.Preset.StopFailed.Title"),
                            result.Error?.Message ?? LocalizationText.Get("MainWindow.Preset.CleanupFailed"));
                        return;
                    }
                    ClearPresetReconversionPauseContext();
                    refreshTrackStateAfterRun = true;
                    if (audioDiscManifest is not null)
                    {
                        _services.Foundation.AudioDiscProjectService.MarkExportFailed(
                            audioDiscManifest,
                            result.Error?.Message);
                        TrySaveCurrentAudioDiscProjectSnapshot(audioDiscManifest, force: true);
                    }

                    var failedStatus = useWorkflowStatus ? _services.Status.WorkflowStatusCoordinator.Snapshot : null;
                    ReturnToReviewAfterInterruptedExport();
                    if (useWorkflowStatus)
                    {
                        PublishWorkflowStatus(
                            exportStatusOperationId,
                            (failedStatus ?? WorkflowStatusSnapshot.Empty) with
                            {
                                ProjectId = exportPlan.ProjectWorkFolder,
                                ProjectKind = workflowProjectKind,
                                ProjectState = ProjectPipelineState.ReviewBeforeMerge,
                                Error = _services.Status.WorkflowExportFailureStatusService.Create(result.Error)
                            });
                        EndWorkflowStatusOperation(exportStatusOperationId);
                    }
                    else
                    {
                        ExportProgressText = LocalizationText.Get("MainWindow.Export.Failed.Progress");
                        StatusText = LocalizationText.Get("MainWindow.Export.Failed.Status");
                    }

                    var exportFailureTransition =
                        _services.Pipeline.PipelinePauseTransitionService.CreateFailureClear(
                            isPipelinePaused: false,
                            pauseRequested: false,
                            isBusy: IsBusy,
                            isExporting: IsExporting,
                            isDiscImporting: IsDiscImporting);
                    ApplyPipelineTransition(exportFailureTransition);
                    NotifyExportUiStateChanged();

                    _runtimeServices.NotificationService.Notify(NotificationEvent.Error);
                    var failureDetails = _services.Status.ExportFailureDetailsService.BuildExportFailureDetails(
                        result.Error ?? new InvalidOperationException("Unbekannter Exportfehler."),
                        result.ConvertedFolder);
                    var failureText = string.Join(Environment.NewLine, failureDetails);
                    _ = ShowHardWorkflowErrorDialog(
                        LocalizationText.Get("MainWindow.Export.Failed.Title"),
                        LocalizationText.Get("MainWindow.Export.Failed.Title"),
                        LocalizationText.Get("MainWindow.Export.Failed.Message"),
                        failureText,
                        failureDetails);

                    return;
            }
        }
        catch (Exception ex)
        {
            FailExportPerformanceOperation(ex);
            throw;
        }
        finally
        {
            try
            {
                if (refreshTrackStateAfterRun)
                    RefreshLoadedProjectTrackStateFromPersistedProject(exportPlan.ProjectWorkFolder);
                if (useLocalWorkflowStatus)
                    EndWorkflowStatusOperation(exportStatusOperationId);
            }
            finally
            {
                _exportCancellation?.Dispose();
                _exportCancellation = null;
                IsExporting = false;
                IsBusy = false;
            }
        }
    }


    private void ReturnToReviewAfterInterruptedExport()
    {
        // Transition first: WithManualMergeReview already sets Phase and would bypass persistence/notifications.
        SetPipelineState(ProjectPipelineState.ReviewBeforeMerge);
        _projectReviewState = ProjectReviewState.WithManualMergeReview(true);
        _manualMergeReviewPreparedPreset = SelectedExportPreset;
        _services.Workflows.TrackPreparedStateRefreshService.RefreshForCurrentPreset(Tracks, _currentProjectWorkFolder, SelectedExportPreset);
        UpdateManualMergeReviewPresetState();
        NotifyExportUiStateChanged();
    }

    private FinalOutputConflictAction ResolveFinalOutputConflict(string outputPath, string renamedOutputPath)
    {
        if (!Dispatcher.CheckAccess())
            return Dispatcher.Invoke(() => ResolveFinalOutputConflict(outputPath, renamedOutputPath));

        if (_settings.OverwriteFinalOutputWithoutAsking)
            return FinalOutputConflictAction.Overwrite;

        var result = AppDialogService.Show(
            this,
            LocalizationText.Get("MainWindow.Export.Conflict.Title"),
            LocalizationText.Get("MainWindow.Export.Conflict.Heading"),
            LocalizationText.Format("MainWindow.Export.Conflict.Message", outputPath, Path.GetFileName(renamedOutputPath)),
            AppDialogKind.Question,
            null,
            new[]
            {
                new AppDialogButton(LocalizationText.Get("MainWindow.Export.Conflict.Overwrite"), AppDialogResult.Yes, IsPrimary: true, IsDefault: true),
                new AppDialogButton(LocalizationText.Get("MainWindow.Export.Conflict.Rename"), AppDialogResult.No),
                new AppDialogButton(LocalizationText.Get("Common.Cancel"), AppDialogResult.Cancel, IsCancel: true)
            });

        return result switch
        {
            AppDialogResult.Yes => FinalOutputConflictAction.Overwrite,
            AppDialogResult.No => FinalOutputConflictAction.Rename,
            _ => FinalOutputConflictAction.Cancel
        };
    }


    private FinalOutputFailureAction ResolveFinalOutputFailure(
        string outputPath,
        string desktopOutputPath,
        Exception error)
    {
        if (!Dispatcher.CheckAccess())
        {
            return Dispatcher.Invoke(() => ResolveFinalOutputFailure(
                outputPath,
                desktopOutputPath,
                error));
        }

        var result = AppDialogService.Show(
            this,
            title: LocalizationText.Get("MainWindow.Export.Failure.Title"),
            heading: LocalizationText.Get("MainWindow.Export.Failure.Heading"),
            message: LocalizationText.Format("MainWindow.Export.Failure.Message", outputPath, Path.GetFileName(desktopOutputPath)),
            kind: AppDialogKind.Warning,
            details: new[] { error.Message },
            buttons: new[]
            {
                new AppDialogButton(LocalizationText.Get("MainWindow.Export.Failure.SaveDesktop"), AppDialogResult.Yes, IsPrimary: true, IsDefault: true),
                new AppDialogButton(LocalizationText.Get("Common.Cancel"), AppDialogResult.Cancel, IsCancel: true)
            },
            width: 760,
            height: 460);

        return result == AppDialogResult.Yes
            ? FinalOutputFailureAction.SaveToDesktop
            : FinalOutputFailureAction.Discard;
    }


    private void PauseBeforeMergeForManualReview(bool showCompletionDialog = true)
    {
        _projectReviewState = ProjectReviewState.WithCompletion(false);
        _projectReviewState = ProjectReviewState.WithManualMergeReview(true);
        SetPipelineState(ProjectPipelineState.ReviewBeforeMerge);
        _manualMergeReviewPreparedPreset = SelectedExportPreset;
        _manualMergeReviewNeedsReconversion = false;
        _manualMergeReviewHasPreparedTracks = true;
        SetTrackActionDisplayOverride(LocalizationText.Get("Workflow.Review.MergeAction"));
        NotifyExportUiStateChanged();
        if (LoadedProjectState.IsMp3Disc)
        {
            var operationId = BeginWorkflowStatusOperation(_currentProjectWorkFolder);
            var totalDiscs = Math.Max(1, Tracks.Select(track => track.DiscNumber ?? 1).DefaultIfEmpty(1).Max());
            PublishWorkflowStatus(
                operationId,
                _services.Status.Mp3DiscWorkflowStatusAdapter.CreateReadySnapshot(
                    _currentProjectWorkFolder,
                    totalDiscs,
                    Tracks.Count,
                    ExportPreset.Parse(SelectedExportPreset)));
            EndWorkflowStatusOperation(operationId);
        }
        else if (LoadedProjectState.IsLocal || !LoadedProjectState.IsAudioDisc)
        {
            var operationId = BeginWorkflowStatusOperation(_currentProjectWorkFolder);
            PublishWorkflowStatus(
                operationId,
                _services.Status.LocalWorkflowStatusAdapter.CreateReadySnapshot(
                    _currentProjectWorkFolder,
                    Tracks.Count,
                    ExportPreset.Parse(SelectedExportPreset)));
            EndWorkflowStatusOperation(operationId);
        }
        else
        {
            ExportProgressPercent = 100;
            ExportProgressText = LocalizationText.Get("Workflow.Review.AudioProgress");
            StatusText = LocalizationText.Get("Workflow.Review.AudioStatus");
        }

        if (!showCompletionDialog)
            return;

        _runtimeServices.NotificationService.Notify(NotificationEvent.UserActionRequired);

        AppDialogService.Show(
            this,
            title: LocalizationText.Get("Workflow.Review.Title"),
            heading: LocalizationText.Get("Workflow.Review.Heading"),
            message: LocalizationText.Get("Workflow.Review.Message"),
            kind: AppDialogKind.Information,
            buttons: new[]
            {
                new AppDialogButton(LocalizationText.Get("UiOK"), AppDialogResult.Ok, IsPrimary: true, IsDefault: true, IsCancel: true)
            });
    }

    private void WriteFinalTags(string finalAudioPath)
    {
        _services.Foundation.FinalTagService.WriteFinalTags(
            finalAudioPath,
            CreateCurrentFinalAudioTagData(isFinalOutput: true));
    }

    private FinalAudioTagData CreateCurrentFinalAudioTagData(bool isFinalOutput, TimeSpan? duration = null, int? chapterCount = null)
    {
        if (isFinalOutput &&
            !string.IsNullOrWhiteSpace(_currentProjectWorkFolder) &&
            Directory.Exists(_currentProjectWorkFolder))
        {
            var metadata = RequireCanonicalMetadataSessionForProject(_currentProjectWorkFolder);
            _projectMetadataSession.Flush();

            return new ProjectMetadataOutputProjectionService().Create(
                metadata,
                new ProjectMetadataOutputProjectionOptions(
                    _projectMetadata.ProcessedCoverPath,
                    _settings.CompatibilityDescriptionEnabled,
                    CultureInfo.CurrentUICulture.Name,
                    duration,
                    chapterCount),
                MetadataPreferencesService.CreateSnapshot(_settings.MetadataPreferences),
                MetadataFieldBindingPreferencesService.CreateSnapshot(_settings.MetadataPreferences));
        }

        return new FinalAudioTagData
        {
            Title = BookTitle,
            Album = Album,
            Author = Author,
            Narrator = Narrator,
            Genre = Genre,
            CoverPath = _projectMetadata.ProcessedCoverPath,
            Duration = duration,
            ChapterCount = chapterCount
        };
    }

    private FinalAudioTagData CreateCurrentCompatibilityPreviewTagData()
    {
        var knownDurations = Tracks
            .Where(track => !track.IsExcluded)
            .Select(TrackDurationService.GetPreciseDuration)
            .Where(duration => duration.HasValue)
            .Select(duration => duration!.Value)
            .ToArray();
        var duration = knownDurations.Length > 0
            ? TimeSpan.FromTicks(knownDurations.Sum(value => value.Ticks))
            : (TimeSpan?)null;
        var chapterCount = _currentActiveChapterResult?.RenderedTimeline.Count
            ?? ExportPreviewMetricsService.CountPlannedChapters(Tracks);

        return new ProjectMetadataOutputProjectionService().Create(
            _projectMetadata.CanonicalMetadata,
            new ProjectMetadataOutputProjectionOptions(
                _projectMetadata.ProcessedCoverPath,
                _settings.CompatibilityDescriptionEnabled,
                CultureInfo.CurrentUICulture.Name,
                duration,
                chapterCount),
            MetadataPreferencesService.CreateSnapshot(_settings.MetadataPreferences),
                MetadataFieldBindingPreferencesService.CreateSnapshot(_settings.MetadataPreferences));
    }

    private void EnsureCanonicalMetadataSessionForCurrentState()
    {
        if (_projectMetadataSession.HasCurrent)
            return;

        var metadata = _projectMetadata.EnsureCanonicalMetadata(MetadataOrigin.None);
        _projectMetadataSession.BeginNew(metadata);
    }

    private void AttachCanonicalMetadataSessionToNewProject(string projectFolder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectFolder);

        var metadata = _projectMetadata.EnsureCanonicalMetadata(MetadataOrigin.None);
        if (!_projectMetadataSession.HasCurrent)
            _projectMetadataSession.BeginNew(metadata);

        if (!ReferenceEquals(_projectMetadataSession.Current, metadata))
        {
            throw new InvalidOperationException(
                "The active ProjectMetadataState and ProjectMetadataSession do not share the same canonical metadata instance.");
        }

        if (_projectMetadataSession.IsAttached)
            throw new InvalidOperationException("The canonical metadata session is already attached to a project.");

        _projectMetadataSession.AttachNewProject(projectFolder);
    }

    private void EnsureCanonicalMetadataSessionAttachedToProject(string projectFolder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectFolder);

        if (!_projectMetadataSession.IsAttached)
        {
            AttachCanonicalMetadataSessionToNewProject(projectFolder);
            return;
        }

        _ = RequireCanonicalMetadataSessionForProject(projectFolder);
    }

    private ProjectMetadata RequireCanonicalMetadataSessionForProject(string projectFolder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectFolder);

        if (!_projectMetadataSession.IsAttached ||
            _projectMetadataSession.Current is null ||
            string.IsNullOrWhiteSpace(_projectMetadataSession.ProjectFolder))
        {
            throw new InvalidOperationException(
                "No canonical ProjectMetadata session is attached to the active project.");
        }

        var normalizedProjectFolder = Path.GetFullPath(projectFolder);
        if (!string.Equals(
                normalizedProjectFolder,
                _projectMetadataSession.ProjectFolder,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The canonical ProjectMetadata session belongs to a different project folder.");
        }

        if (!ReferenceEquals(_projectMetadataSession.Current, _projectMetadata.CanonicalMetadata))
        {
            throw new InvalidOperationException(
                "ProjectMetadataState and ProjectMetadataSession no longer share the same canonical metadata instance.");
        }

        return _projectMetadataSession.Current;
    }

    private void FlushCanonicalMetadataSessionForProject(string projectFolder)
    {
        StopAutomaticMaintenance();
        _ = RequireCanonicalMetadataSessionForProject(projectFolder);
        var saved = _projectMetadataSession.Flush();
        if (saved) _services.Foundation.ProjectIndexService.Catalog.MarkDirty();
    }

    private bool TryFlushCanonicalMetadataBeforeExtensionSnapshot(string projectFolder)
    {
        try
        {
            FlushCanonicalMetadataSessionForProject(projectFolder);
            return true;
        }
        catch (Exception ex)
        {
            _runtimeServices.DiagnosticLogService.WriteError(
                "Canonical ProjectMetadata checkpoint before project extension failed.",
                ex);
            AppDialogService.Error(
                this,
                LocalizationText.Get("MainWindow.Metadata.SaveFailed.Title"),
                LocalizationText.Get("MainWindow.Metadata.SaveFailed.Message"),
                details: new[] { ex.Message });
            return false;
        }
    }

    private void ReloadCanonicalMetadataSessionAfterRollback(string projectFolder)
    {
        if (LoadedProjectState.IsMp3Disc)
            _activeMp3DiscManifest = _services.Foundation.Mp3DiscProjectService.TryLoad(projectFolder);
        if (LoadedProjectState.IsAudioDisc)
            _activeAudioDiscManifest = _services.Foundation.AudioDiscProjectService.TryLoad(projectFolder);
        _ = RequireCanonicalMetadataSessionForProject(projectFolder);
        var metadata = _projectMetadataSession.ReloadFromStore();
        _projectMetadata.ApplyCanonicalMetadata(metadata);
    }

    private static WorkflowStatusSnapshot CreateExportStatusSnapshot(
        WorkflowProjectKind projectKind,
        string? projectId,
        int total,
        int completed,
        IReadOnlyList<int> activeTrackNumbers,
        ExportPreset preset,
        int? percentOverride = null,
        int totalDiscCount = 0)
    {
        var safeTotal = Math.Max(0, total);
        var safeCompleted = Math.Clamp(completed, 0, safeTotal);
        var percent = percentOverride ?? (safeTotal == 0 ? 0 : safeCompleted * 100 / safeTotal);

        return new WorkflowStatusSnapshot
        {
            ProjectId = projectId,
            ProjectKind = projectKind,
            ProjectState = ProjectPipelineState.Converting,
            ActiveActivities = new HashSet<WorkflowActivity> { WorkflowActivity.Converting },
            SourceProgress = projectKind == WorkflowProjectKind.AudioDisc
                ? CreateCompletedExportSourceProgress(projectKind, safeTotal, totalDiscCount)
                : null,
            ConversionProgress = new ConversionActivityProgress(
                safeCompleted,
                safeTotal,
                percent,
                activeTrackNumbers,
                preset.BitrateKbps,
                preset.Channels == 1,
                IsLive: false),
            TotalSourceItems = safeTotal
        };
    }


    private static SourceAcquisitionProgress? CreateCompletedExportSourceProgress(
        WorkflowProjectKind projectKind,
        IReadOnlyCollection<TrackInfo> tracks,
        int totalDiscCount)
    {
        var totalTracks = tracks.Count;
        var resolvedDiscCount = totalDiscCount > 0
            ? totalDiscCount
            : Math.Max(1, tracks.Select(track => track.DiscNumber ?? 1).DefaultIfEmpty(1).Max());

        return projectKind switch
        {
            WorkflowProjectKind.Folder => CreateCompletedExportSourceProgress(projectKind, totalTracks, 0),
            WorkflowProjectKind.Mp3Disc => CreateCompletedExportSourceProgress(projectKind, totalTracks, resolvedDiscCount),
            WorkflowProjectKind.AudioDisc => CreateCompletedExportSourceProgress(projectKind, totalTracks, resolvedDiscCount),
            _ => null
        };
    }

    private static SourceAcquisitionProgress? CreateCompletedExportSourceProgress(
        WorkflowProjectKind projectKind,
        int totalTracks,
        int totalDiscCount)
    {
        var safeTotalTracks = Math.Max(0, totalTracks);
        var safeDiscCount = Math.Max(1, totalDiscCount);

        return projectKind switch
        {
            WorkflowProjectKind.Folder => new SourceAcquisitionProgress(
                SourceAcquisitionKind.Copying,
                safeTotalTracks,
                safeTotalTracks,
                safeTotalTracks,
                safeTotalTracks,
                Percent: 100,
                CurrentSourceFinished: true,
                AllSourcesFinished: true),
            WorkflowProjectKind.Mp3Disc => new SourceAcquisitionProgress(
                SourceAcquisitionKind.Copying,
                safeTotalTracks,
                safeTotalTracks,
                safeTotalTracks,
                safeTotalTracks,
                CurrentDisc: safeDiscCount,
                TotalDiscs: safeDiscCount,
                Percent: 100,
                CurrentSourceFinished: true,
                AllSourcesFinished: true),
            WorkflowProjectKind.AudioDisc => new SourceAcquisitionProgress(
                SourceAcquisitionKind.Ripping,
                safeTotalTracks,
                safeTotalTracks,
                safeTotalTracks,
                safeTotalTracks,
                CurrentDisc: safeDiscCount,
                TotalDiscs: safeDiscCount,
                Percent: 100,
                WorkingFormat: "WAV",
                CurrentSourceFinished: true,
                AllSourcesFinished: true),
            _ => null
        };
    }

    private static int ResolveAudioDiscExportDiscCount(
        AudioDiscProjectManifest? manifest,
        IReadOnlyCollection<TrackInfo> tracks)
    {
        var manifestDiscCount = manifest?.TotalDiscs ?? 0;
        var trackDiscCount = tracks
            .Select(track => track.DiscNumber ?? 1)
            .DefaultIfEmpty(1)
            .Max();

        return Math.Max(1, Math.Max(manifestDiscCount, trackDiscCount));
    }

    private void ReportExportProgress(int completedCount, int totalCount, long currentTicks, long totalTicks, IEnumerable<int> activeIndexes)
    {
        var percent = totalTicks <= 0
            ? 0
            : Math.Clamp(currentTicks * 100.0 / totalTicks, 0, 100);

        var activeTrackNumbers = activeIndexes
            .Distinct()
            .OrderBy(index => index)
            .Select(index => (index + 1).ToString())
            .ToList();

        var activeText = activeTrackNumbers.Count > 0
            ? LocalizationText.Format("MainWindow.Progress.ActiveTracks", string.Join(", ", activeTrackNumbers))
            : "";

        Dispatcher.InvokeAsync(() =>
        {
            ExportProgressPercent = percent;
            ExportProgressText = LocalizationText.Format("MainWindow.Progress.Completed", percent, completedCount, totalCount, activeText);
        });
    }

    private string BuildIdleExportProgressText()
    {
        var total = Tracks.Count;
        return LocalizationText.Format("MainWindow.Progress.Idle", total);
    }

    private bool SetParallelJobsInput(string? value, bool showMessage)
    {
        var raw = (value ?? "").Trim();

        if (string.IsNullOrWhiteSpace(raw) || IsParallelAuto(raw))
            return ApplyParallelJobsInput("Auto");

        if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var jobs))
        {
            if (showMessage)
            {
                AppDialogService.Warning(
                    this,
                    LocalizationText.Get("MainWindow.Parallel.Invalid.Title"),
                    LocalizationText.Get("MainWindow.Parallel.Invalid.InputMessage"));
            }

            OnPropertyChanged(nameof(ParallelJobsInput));
            return false;
        }

        if (jobs < 1 || jobs > 40)
        {
            if (showMessage)
            {
                AppDialogService.Warning(
                    this,
                    LocalizationText.Get("MainWindow.Parallel.Invalid.Title"),
                    LocalizationText.Get("MainWindow.Parallel.Invalid.RangeMessage"));
            }

            jobs = Math.Clamp(jobs, 1, 40);
        }

        return ApplyParallelJobsInput(jobs.ToString(CultureInfo.InvariantCulture));
    }

    private bool ApplyParallelJobsInput(string normalizedValue)
    {
        if (_parallelJobsInput == normalizedValue)
        {
            OnPropertyChanged(nameof(ParallelJobsInput));
            return true;
        }

        _parallelJobsInput = normalizedValue;
        OnPropertyChanged(nameof(ParallelJobsInput));

        _settings.SelectedParallelJobs = _parallelJobsInput;
        SaveSettingsIfReady();

        return true;
    }

    private int ResolveParallelJobCount()
    {
        if (TryParseParallelJobs(ParallelJobsInput, out var configuredJobs))
            return Math.Clamp(configuredJobs, 1, 40);

        return GetAutomaticParallelJobCount();
    }

    private static int GetAutomaticParallelJobCount()
    {
        return Math.Clamp(Environment.ProcessorCount + 4, 2, 32);
    }

    private static bool TryParseParallelJobs(string? value, out int jobs)
    {
        jobs = 0;
        var raw = (value ?? "").Trim();
        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out jobs) && jobs is >= 1 and <= 40;
    }

    private static bool IsParallelAuto(string? value)
    {
        return string.Equals((value ?? "").Trim(), "Auto", StringComparison.OrdinalIgnoreCase);
    }

    private WorkFolderStructure GetProjectFolderStructure()
    {
        return _services.Foundation.WorkFolderStructureService.Ensure(_services.Foundation.SettingsService.ProjectRootFolder);
    }

    private string GetWorkingRootFolder()
    {
        return GetProjectFolderStructure().ProjectRootFolder;
    }

    private string BuildProjectWorkFolderName()
    {
        return _services.Pipeline.ExportPlanService.BuildProjectWorkFolderName(_currentFolderPath, Author, BookTitle);
    }

    private void RemoveGeneratedOrWorkFilesFromTrackList(string folderPath)
    {
        if (Tracks.Count == 0)
            return;

        var expectedOutputInSourceFolder = Path.Combine(folderPath, OutputFileNamePreview);
        var workRoot = _settings.WorkingFolder;
        var removeList = _services.Tracks.TrackWorkspaceFilterService.GetGeneratedOrWorkTracks(folderPath, Tracks, expectedOutputInSourceFolder, workRoot);

        foreach (var track in removeList)
            Tracks.Remove(track);
    }

    private AacSampleRatePreviewState _folderRatePreview = new();
    private DiscProjectSetupDialog? _folderSetupPreviewOwner;
    private bool _isFolderRateDialogOpen;

    private bool ShowFolderPassThroughNotice(Window? owner = null, string? selectedPreset = null, bool requireAcceptance = false)
    {
        if (_isFolderRateDialogOpen) return !requireAcceptance;
        if ((!IsVisible || IsBusy || IsExporting || _isLoadingSettings) && !requireAcceptance) return true;
        if (!string.IsNullOrWhiteSpace(_currentProjectWorkFolder) ||
            ResolveCurrentChapterProjectType() != ProjectManifestTypes.FolderProject) return true;
        if (owner is null && _folderSetupPreviewOwner is { IsVisible: true } setup)
        { owner = setup; selectedPreset = setup.PresetComboBox.SelectedItem?.ToString(); }
        if (owner is not null && !owner.IsVisible) return true;
        var presetName = selectedPreset ?? SelectedExportPreset;
        var preset = ExportPreset.Parse(presetName);
        var decision = FolderSampleRateService.Decide(Tracks, preset, _settings.DefaultSampleRateHz);
        _folderRatePreview.Refresh(FolderSampleRateService.DecisionKey(Tracks, preset, decision.StandardRate), decision);
        if (!_folderRatePreview.ShouldPresent && !(requireAcceptance && _folderRatePreview.NeedsAcceptance)) return true;
        _folderRatePreview.Presented();
        _isFolderRateDialogOpen = true;
        try
        {
            var dialog = new AacSampleRateDialog(decision, presetName);
            ModalDialogService.ShowDialog(dialog, owner ?? this);
            if (dialog.Result != AppDialogResult.Ok) return false;
            _folderRatePreview.Accept(dialog.PreferStandard);
            return true;
        }
        finally { _isFolderRateDialogOpen = false; }
    }

    private void ApplyFolderSampleRateContext()
    {
        if (ResolveCurrentChapterProjectType() != ProjectManifestTypes.FolderProject) return;
        if (string.IsNullOrWhiteSpace(_currentProjectWorkFolder))
        {
            var preset = ExportPreset.Parse(SelectedExportPreset);
            var decision = FolderSampleRateService.Decide(Tracks, preset, _settings.DefaultSampleRateHz);
            _folderRatePreview.Refresh(FolderSampleRateService.DecisionKey(Tracks, preset, decision.StandardRate), decision);
            FolderSampleRateService.Apply(Tracks, _folderRatePreview.EffectiveRate);
            foreach (var track in Tracks) track.AllowAacReuse = decision.Qualified;
        }
        else
        {
            FolderSampleRateService.Apply(Tracks, ProjectSampleRateService.ReadTarget(_currentProjectWorkFolder));
            foreach (var track in Tracks) track.AllowAacReuse = ProjectSampleRateService.AllowsAacReuse(_currentProjectWorkFolder);
        }
    }

    private void RecalculateProcessingActionsForCurrentPreset(bool requestTrackStateRefresh = true)
    {
        if (Tracks.Count == 0)
            return;

        ApplyFolderSampleRateContext();
        _services.Workflows.TrackPreparedStateRefreshService.RefreshForCurrentPreset(Tracks, _currentProjectWorkFolder, SelectedExportPreset);
        var preset = ExportPreset.Parse(SelectedExportPreset);

        foreach (var track in Tracks)
        {
            track.HasReusableConvertedFile = _services.Workflows.TrackPreparedStateRefreshService.IsReusablePreparedConvertedTrack(track, SelectedExportPreset);
            if (_isAudioDiscProjectAwaitingRip &&
                !string.IsNullOrWhiteSpace(track.FilePath) &&
                !File.Exists(track.FilePath))
            {
                track.ProcessingAction = ProcessingActionIds.RipFlac;
                continue;
            }

            track.ProcessingAction = AudioProcessingService.DetermineProcessingAction(track, preset);
        }

        RefreshTrackPresentationViews();
        OnExportPreviewChanged();
        if (requestTrackStateRefresh) _trackStateUpdateQueueService.RequestRefresh();
        Dispatcher.BeginInvoke(() => ShowFolderPassThroughNotice(), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    }



    private void RefreshPartialPresetProgressText()
    {
        if (!ProjectReviewState.IsReviewBeforeMerge && !ProjectReviewState.IsCompleted)
            return;

        var includedTracks = Tracks.Where(track => !track.IsExcluded).ToArray();
        var preparation = _services.Workflows.TrackPreparedStateRefreshService.GetPreparationProgress(
            includedTracks,
            SelectedExportPreset);
        if (preparation.TotalCount <= 0 || preparation.PendingCount <= 0)
            return;

        IsProgressIndeterminate = false;
        ExportProgressPercent = preparation.Percent;
        ExportProgressText = LocalizationText.Format("MainWindow.Progress.Prepared", preparation.Percent, preparation.PreparedCount, preparation.TotalCount);
    }


    private string BuildProcessingActionSummary()
    {
        if (ProjectReviewState.IsReviewBeforeMerge || ProjectReviewState.IsCompleted)
        {
            var reviewTracks = Tracks.Where(track => !track.IsExcluded).ToArray();
            var preparation = _services.Workflows.TrackPreparedStateRefreshService.GetPreparationProgress(
                reviewTracks,
                SelectedExportPreset);
            if (preparation.TotalCount > 0 && preparation.PendingCount > 0)
                return LocalizationText.Format("MainWindow.Summary.ConvertCount", preparation.PendingCount);
        }

        if (_activeAudioDiscManifest is not null &&
            !string.Equals(_activeAudioDiscManifest.Status, AudioDiscProjectStatus.RippingCompleted, StringComparison.OrdinalIgnoreCase) &&
            Tracks.Count > 0)
        {
            return AudioProcessingService.BuildAudioDiscPipelineActionSummary(Tracks, SelectedExportPreset);
        }

        return AudioProcessingService.BuildProcessingActionSummary(Tracks);
    }

    private static TimeSpan? GetPreciseDuration(TrackInfo track)
    {
        if (track.DurationTicks is > 0)
            return TimeSpan.FromTicks(track.DurationTicks.Value);

        return TryParseDuration(track.Duration);
    }

    private static TimeSpan? TryParseDuration(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var parts = value.Trim().Split(':');

        if (parts.Length == 2 &&
            int.TryParse(parts[0], out var minutes) &&
            double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
        {
            return TimeSpan.FromMinutes(minutes) + TimeSpan.FromSeconds(seconds);
        }

        if (parts.Length == 3 &&
            int.TryParse(parts[0], out var hours) &&
            int.TryParse(parts[1], out minutes) &&
            double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out seconds))
        {
            return TimeSpan.FromHours(hours) + TimeSpan.FromMinutes(minutes) + TimeSpan.FromSeconds(seconds);
        }

        return null;
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalHours >= 1)
            return $"{(int)duration.TotalHours:00}:{duration.Minutes:00}:{duration.Seconds:00}";

        return $"{duration.Minutes:00}:{duration.Seconds:00}";
    }

    private static string NormalizeComboBoxValue(object? value)
    {
        if (value is ComboBoxItem item)
            return item.Content?.ToString() ?? "";

        return value?.ToString() ?? "";
    }

    private sealed class InlineProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
    }

    private static string EscapeFfmpegConcatPath(string path)
    {
        var normalized = Path.GetFullPath(path).Replace('\\', '/');
        return normalized.Replace("'", "'\\''");
    }

    private void TracksGrid_Sorting(object sender, DataGridSortingEventArgs e)
    {
        e.Handled = true;

        if (!CanEditTrackOrder || Tracks.Count == 0)
            return;

        ClearEmbeddedChapterSelection();

        var selected = TracksGrid
            .SelectedItems
            .OfType<TrackInfo>()
            .ToList();

        var sortKey = e.Column.SortMemberPath;

        if (string.IsNullOrWhiteSpace(sortKey))
            sortKey = GetTrackGridColumnKey(e.Column);

        var currentOrder = Tracks.ToList();
        var direction = GetNextTrackGridSortDirection(sortKey, currentOrder);
        var sortedTracks = _services.Tracks.TrackListActionService.Sort(currentOrder, sortKey, direction);

        ClearGridSorting();
        ReplaceTrackList(sortedTracks);

        _trackGridSortKey = sortKey;
        _trackGridSortDirection = direction;
        e.Column.SortDirection = direction;
        RestoreSelection(selected);
    }

    private void TrackFileWarningSummary_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _lastFileWarningJumpTrack = JumpToNextTrackWarning(
            track => !string.IsNullOrWhiteSpace(track.DisplayFileWarning),
            _lastFileWarningJumpTrack);
        e.Handled = true;
    }

    private void TrackChapterWarningSummary_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _lastChapterWarningJumpTrack = JumpToNextTrackWarning(
            track => !string.IsNullOrWhiteSpace(track.DisplayChapterWarning),
            _lastChapterWarningJumpTrack);
        e.Handled = true;
    }

    private bool HandleTrackGridAutoScrollMouseDown(MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle || TracksGrid is null)
            return false;

        if (!_isTrackGridAutoScrollActive && !CanStartTrackGridAutoScroll())
            return false;

        if (e.OriginalSource is not DependencyObject source ||
            FindVisualAncestor<DataGrid>(source) != TracksGrid)
        {
            return false;
        }

        if (_isTrackGridAutoScrollActive)
            StopTrackGridAutoScroll();
        else
            StartTrackGridAutoScroll(e.GetPosition(TracksGrid));

        e.Handled = true;
        return true;
    }

    private void StartTrackGridAutoScroll(Point anchor)
    {
        if (TracksGrid is null || !CanStartTrackGridAutoScroll())
            return;

        if (!TracksGrid.CaptureMouse())
            return;

        _trackGridAutoScrollAnchor = anchor;
        _trackGridAutoScrollCurrent = anchor;
        _trackGridAutoScrollLastTimestamp = TimeSpan.Zero;
        _trackGridCursorBeforeAutoScroll = TracksGrid.Cursor;
        _isTrackGridAutoScrollActive = true;
        TracksGrid.Cursor = Cursors.ScrollAll;
        TracksGrid.Focus();
        Keyboard.Focus(TracksGrid);
        _trackGridAutoScrollClock.Restart();
        _trackGridAutoScrollTimer.Start();
    }

    private void StopTrackGridAutoScroll()
    {
        if (!_isTrackGridAutoScrollActive)
            return;

        _trackGridAutoScrollTimer.Stop();
        _trackGridAutoScrollClock.Reset();
        _isTrackGridAutoScrollActive = false;

        if (TracksGrid is not null)
        {
            if (TracksGrid.IsMouseCaptured)
                TracksGrid.ReleaseMouseCapture();

            TracksGrid.Cursor = _trackGridCursorBeforeAutoScroll;
        }

        _trackGridCursorBeforeAutoScroll = null;
    }

    private void TrackGridAutoScrollTimer_Tick(object? sender, EventArgs e)
    {
        if (!_isTrackGridAutoScrollActive || TracksGrid is null)
            return;

        if (!CanStartTrackGridAutoScroll())
        {
            StopTrackGridAutoScroll();
            return;
        }

        var scrollViewer = FindVisualChild<ScrollViewer>(TracksGrid);
        if (scrollViewer is null)
        {
            StopTrackGridAutoScroll();
            return;
        }

        _trackGridAutoScrollCurrent = Mouse.GetPosition(TracksGrid);

        var elapsed = GetTrackGridAutoScrollElapsed();
        var delta = _trackGridAutoScrollCurrent - _trackGridAutoScrollAnchor;
        var vertical = CreateTrackGridAutoScrollStep(delta.Y, elapsed);
        var horizontal = CreateTrackGridAutoScrollStep(delta.X, elapsed);

        if (vertical != 0)
            scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset + vertical);

        if (horizontal != 0)
            scrollViewer.ScrollToHorizontalOffset(scrollViewer.HorizontalOffset + horizontal);
    }

    private bool CanStartTrackGridAutoScroll()
    {
        if (IsBusy || IsExporting || IsDiscImporting || Tracks.Count == 0 || TracksGrid is null)
            return false;

        var scrollViewer = FindVisualChild<ScrollViewer>(TracksGrid);
        return scrollViewer is not null &&
               (scrollViewer.ScrollableHeight > 0 || scrollViewer.ScrollableWidth > 0);
    }

    private static double CreateTrackGridAutoScrollStep(double delta, TimeSpan interval)
    {
        const double deadZone = 10;
        const double accelerationRange = 140;
        const double maxSpeedPerSecond = 1000;
        const double minSpeedPerSecond = 35;

        var distance = Math.Abs(delta);
        if (distance <= deadZone)
            return 0;

        var t = Math.Clamp((distance - deadZone) / accelerationRange, 0, 1);
        var speed = Math.Max(minSpeedPerSecond, maxSpeedPerSecond * t * t);
        return Math.Sign(delta) * speed * interval.TotalSeconds;
    }

    private TimeSpan GetTrackGridAutoScrollElapsed()
    {
        var now = _trackGridAutoScrollClock.Elapsed;
        var elapsed = now - _trackGridAutoScrollLastTimestamp;
        _trackGridAutoScrollLastTimestamp = now;

        if (elapsed <= TimeSpan.Zero)
            return _trackGridAutoScrollTimer.Interval;

        return elapsed <= TrackGridAutoScrollMaxElapsed
            ? elapsed
            : TrackGridAutoScrollMaxElapsed;
    }

    private void TracksGrid_LostMouseCapture(object sender, MouseEventArgs e)
    {
        if (_isTrackGridAutoScrollActive)
            StopTrackGridAutoScroll();
    }

    private TrackInfo? JumpToNextTrackWarning(Func<TrackInfo, bool> matchesWarning, TrackInfo? lastJumpTrack)
    {
        var visibleTracks = GetVisibleTracks();
        if (visibleTracks.Count == 0)
            return null;

        var warningTracks = visibleTracks
            .Select((track, index) => new { Track = track, Index = index })
            .Where(item => matchesWarning(item.Track))
            .ToList();

        if (warningTracks.Count == 0)
            return null;

        var lastIndex = lastJumpTrack is null
            ? -1
            : visibleTracks.IndexOf(lastJumpTrack);

        var target = warningTracks.FirstOrDefault(item => item.Index > lastIndex)
            ?? warningTracks[0];

        TracksGrid.SelectedItem = target.Track;
        TracksGrid.CurrentItem = target.Track;
        ScrollTrackIntoContext(visibleTracks, target.Index, contextRowsBefore: 3);
        TracksGrid.Focus();
        Keyboard.Focus(TracksGrid);
        return target.Track;
    }

    private void ScrollTrackIntoContext(IReadOnlyList<TrackInfo> visibleTracks, int targetIndex, int contextRowsBefore)
    {
        if (targetIndex < 0 || targetIndex >= visibleTracks.Count)
            return;

        TracksGrid.UpdateLayout();

        var scrollViewer = FindVisualChild<ScrollViewer>(TracksGrid);
        if (scrollViewer is not null)
        {
            scrollViewer.ScrollToVerticalOffset(Math.Max(0, targetIndex - contextRowsBefore));
            TracksGrid.UpdateLayout();
        }

        TracksGrid.ScrollIntoView(visibleTracks[targetIndex]);
    }

    private static bool HasTrackListWarning(TrackInfo track)
    {
        return !string.IsNullOrWhiteSpace(track.DisplayFileWarning) ||
               !string.IsNullOrWhiteSpace(track.DisplayChapterWarning);
    }

    private ListSortDirection GetNextTrackGridSortDirection(string sortKey, IReadOnlyList<TrackInfo> currentOrder)
    {
        var ascending = _services.Tracks.TrackListActionService.Sort(currentOrder, sortKey, ListSortDirection.Ascending);
        var descending = _services.Tracks.TrackListActionService.Sort(currentOrder, sortKey, ListSortDirection.Descending);

        if (string.Equals(_trackGridSortKey, sortKey, StringComparison.Ordinal))
        {
            if (HasSameTrackOrder(currentOrder, ascending))
                return ListSortDirection.Descending;

            if (HasSameTrackOrder(currentOrder, descending))
                return ListSortDirection.Ascending;

            return _trackGridSortDirection == ListSortDirection.Ascending
                ? ListSortDirection.Descending
                : ListSortDirection.Ascending;
        }

        return HasSameTrackOrder(currentOrder, ascending)
            ? ListSortDirection.Descending
            : ListSortDirection.Ascending;
    }

    private static bool HasSameTrackOrder(IReadOnlyList<TrackInfo> left, IReadOnlyList<TrackInfo> right)
    {
        if (left.Count != right.Count)
            return false;

        for (var i = 0; i < left.Count; i++)
        {
            if (!ReferenceEquals(left[i], right[i]))
                return false;
        }

        return true;
    }

    private void ConfigureTrackGridColumnLayoutHandlers()
    {
        TracksGrid.AddHandler(
            Thumb.DragCompletedEvent,
            new DragCompletedEventHandler(TracksGridColumnHeader_DragCompleted),
            handledEventsToo: true);
    }

    private void TracksGridColumnHeader_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        if (_isApplyingTrackGridColumnLayout || _isLoadingSettings || TracksGrid is null)
            return;

        if (FindVisualAncestor<DataGridColumnHeader>(e.OriginalSource as DependencyObject) is null)
            return;

        // A deliberate manual resize takes ownership from AutoFit. Persist the
        // user's widths so the layout remains stable across refreshes/restarts.
        _settings.TrackGridAutoFitEnabled = false;
        SaveTrackGridColumnLayout();
        RefreshEmbeddedChapterRowColumnLayouts(force: true);
    }

    private void TracksGrid_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var header = FindVisualAncestor<DataGridColumnHeader>(e.OriginalSource as DependencyObject);
        _trackGridContextMenuForHeader = header?.Column is not null;
    }

    private void TracksGrid_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (!_trackGridContextMenuForHeader && !CanEditTrackOrder)
        {
            e.Handled = true;
            return;
        }

        if (TracksGrid.ContextMenu is null)
            return;

        if (_trackGridContextMenuForHeader)
            BuildTrackGridColumnContextMenu(TracksGrid.ContextMenu);
        else
            BuildTrackGridRowContextMenu(TracksGrid.ContextMenu);
    }

    private void EmbeddedChapterDetails_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        e.Handled = true;
    }

    private void TracksGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingTrackSelectionFromChapterNavigation)
            return;

        ClearEmbeddedChapterSelection();
    }

    private void ChapterDetailsToggle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: TrackInfo track })
            return;

        if (_activeEmbeddedChapterRow is not null)
        {
            SelectParentTrack(track, focusGrid: true, scrollIntoView: true);
        }

        RefreshChapterTimelinePresentation();
    }

    private void EmbeddedChapterCollapse_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: TrackInfo track })
        {
            track.IsChapterDetailsExpanded = false;
            if (IsActiveEmbeddedChapterFromTrack(track))
                SelectParentTrack(track, focusGrid: true, scrollIntoView: true);

            e.Handled = true;
        }
    }

    private void EmbeddedChapterRow_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source &&
            FindVisualAncestor<ButtonBase>(source) is not null)
        {
            return;
        }

        if (sender is FrameworkElement { Tag: TrackEmbeddedChapterDisplayInfo chapter } &&
            chapter.SourceTrack is not null)
        {
            SelectEmbeddedChapter(chapter, focusChapter: false);
            if (sender is IInputElement inputElement)
                Keyboard.Focus(inputElement);

            e.Handled = true;
        }
    }

    private void EmbeddedChapterRow_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { Tag: TrackEmbeddedChapterDisplayInfo })
        {
            e.Handled = true;
        }
    }

    private void EmbeddedChapterRow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Handled || sender is not FrameworkElement { Tag: TrackEmbeddedChapterDisplayInfo chapter })
            return;

        _activeEmbeddedChapterRow ??= chapter;
        e.Handled = HandleActiveVisibleTrackGridRowKey(e.Key);
    }

    private void FocusEmbeddedChapterRow(object sender, TrackEmbeddedChapterDisplayInfo row)
    {
        if (sender is not DependencyObject source)
            return;

        var itemsControl = FindVisualAncestor<ItemsControl>(source);
        if (itemsControl is null)
        {
            FocusEmbeddedChapterRow(row, scrollParentIntoView: true);
            return;
        }

        FocusEmbeddedChapterRow(itemsControl, row);
    }

    private bool HandleActiveVisibleTrackGridRowKey(Key key)
    {
        if (Keyboard.Modifiers != ModifierKeys.None)
            return false;

        var direction = key switch
        {
            Key.Down => 1,
            Key.Up => -1,
            _ => 0
        };

        if (direction == 0)
            return false;

        if (_activeEmbeddedChapterRow is null && TracksGrid.SelectedItems.Count > 1)
            return false;

        var rows = BuildVisibleTrackGridRows();
        if (rows.Count == 0)
            return false;

        var currentIndex = GetActiveVisibleTrackGridRowIndex(rows);
        if (currentIndex < 0)
            return false;

        var nextIndex = currentIndex + direction;
        if (nextIndex < 0 || nextIndex >= rows.Count)
            return true;

        SelectVisibleTrackGridRow(rows[nextIndex], focusTarget: true);
        return true;
    }

    private List<VisibleTrackGridRow> BuildVisibleTrackGridRows()
    {
        var rows = new List<VisibleTrackGridRow>();

        foreach (var track in GetVisibleTracks())
        {
            rows.Add(VisibleTrackGridRow.ForTrack(track));

            if (!track.IsChapterDetailsExpanded)
                continue;

            foreach (var chapter in track.EmbeddedChapterRows)
                rows.Add(VisibleTrackGridRow.ForChapter(track, chapter));
        }

        return rows;
    }

    private int GetActiveVisibleTrackGridRowIndex(IReadOnlyList<VisibleTrackGridRow> rows)
    {
        if (_activeEmbeddedChapterRow is { SourceTrack: { } activeTrack } activeChapter)
        {
            for (var index = 0; index < rows.Count; index++)
            {
                var row = rows[index];
                if (ReferenceEquals(row.Track, activeTrack) &&
                    row.Chapter is not null &&
                    row.Chapter.StartTicks == activeChapter.StartTicks)
                {
                    return index;
                }
            }
        }

        var selectedTrack = TracksGrid.SelectedItem as TrackInfo ?? TracksGrid.CurrentItem as TrackInfo;
        if (selectedTrack is null)
            return -1;

        for (var index = 0; index < rows.Count; index++)
        {
            if (rows[index].Chapter is null && ReferenceEquals(rows[index].Track, selectedTrack))
                return index;
        }

        return -1;
    }

    private void SelectVisibleTrackGridRow(VisibleTrackGridRow row, bool focusTarget)
    {
        if (row.Chapter is not null)
        {
            SelectEmbeddedChapter(row.Chapter, focusTarget);
            return;
        }

        var shouldScrollParentIntoView = _activeEmbeddedChapterRow is null;
        SelectParentTrack(row.Track, focusTarget, shouldScrollParentIntoView);
    }

    private void SelectParentTrack(TrackInfo track, bool focusGrid, bool scrollIntoView)
    {
        ClearEmbeddedChapterSelection();

        _isUpdatingTrackSelectionFromChapterNavigation = true;
        try
        {
            TracksGrid.SelectedItems.Clear();
            TracksGrid.SelectedItem = track;
            TracksGrid.CurrentItem = track;
        }
        finally
        {
            _isUpdatingTrackSelectionFromChapterNavigation = false;
        }

        if (scrollIntoView)
            EnsureTrackRowVisible(track);

        if (focusGrid)
        {
            TracksGrid.Focus();
            Keyboard.Focus(TracksGrid);
        }
    }

    private void SelectEmbeddedChapter(TrackEmbeddedChapterDisplayInfo chapter, bool focusChapter)
    {
        if (chapter.SourceTrack is not { } track)
            return;

        _isUpdatingTrackSelectionFromChapterNavigation = true;
        try
        {
            ClearParentTrackSelection();
        }
        finally
        {
            _isUpdatingTrackSelectionFromChapterNavigation = false;
        }

        ClearEmbeddedChapterSelection();
        _activeEmbeddedChapterRow = chapter;
        track.SelectEmbeddedChapter(chapter.StartTicks);

        if (focusChapter)
            FocusEmbeddedChapterRow(chapter, scrollParentIntoView: false);
    }

    private void ClearEmbeddedChapterSelection()
    {
        if (_activeEmbeddedChapterRow?.SourceTrack is { } track)
            track.SelectEmbeddedChapter(null);

        _activeEmbeddedChapterRow = null;
    }

    private bool IsActiveEmbeddedChapterFromTrack(TrackInfo track)
    {
        return _activeEmbeddedChapterRow?.SourceTrack is { } activeTrack &&
               ReferenceEquals(activeTrack, track);
    }

    private void FocusEmbeddedChapterRow(TrackEmbeddedChapterDisplayInfo row, bool scrollParentIntoView)
    {
        if (row.SourceTrack is not { } track)
            return;

        if (scrollParentIntoView)
            TracksGrid.ScrollIntoView(track);

        TracksGrid.UpdateLayout();

        if (TracksGrid.ItemContainerGenerator.ContainerFromItem(track) is not DependencyObject trackContainer)
            return;

        var itemsControl = FindEmbeddedChapterItemsControl(trackContainer, track);
        if (itemsControl is not null)
            FocusEmbeddedChapterRow(itemsControl, row);
    }

    private void EnsureTrackRowVisible(TrackInfo track)
    {
        TracksGrid.UpdateLayout();

        if (FindVisualChild<ScrollViewer>(TracksGrid) is not { } scrollViewer ||
            TracksGrid.ItemContainerGenerator.ContainerFromItem(track) is not DataGridRow row)
        {
            TracksGrid.ScrollIntoView(track);
            return;
        }

        var rowVisual = FindVisualChild<DataGridCellsPresenter>(row) as FrameworkElement ?? row;
        if (!TryGetBoundsRelativeTo(rowVisual, scrollViewer, out var rowBounds))
            return;

        if (rowBounds.Top < 0)
        {
            scrollViewer.ScrollToVerticalOffset(Math.Max(0, scrollViewer.VerticalOffset + rowBounds.Top));
        }
        else if (rowBounds.Bottom > scrollViewer.ViewportHeight)
        {
            scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset + rowBounds.Bottom - scrollViewer.ViewportHeight);
        }
    }

    private static bool TryGetBoundsRelativeTo(FrameworkElement element, Visual ancestor, out Rect bounds)
    {
        bounds = Rect.Empty;

        if (element.ActualWidth <= 0 || element.ActualHeight <= 0)
            return false;

        try
        {
            bounds = element.TransformToAncestor(ancestor)
                .TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static void FocusEmbeddedChapterRow(ItemsControl itemsControl, TrackEmbeddedChapterDisplayInfo row)
    {
        itemsControl.UpdateLayout();
        if (itemsControl.ItemContainerGenerator.ContainerFromItem(row) is not DependencyObject container)
            return;

        var focusTarget = FindVisualChild<Grid>(container);
        if (focusTarget is null)
            return;

        Keyboard.Focus(focusTarget);
        focusTarget.BringIntoView();
    }

    private static ItemsControl? FindEmbeddedChapterItemsControl(DependencyObject source, TrackInfo track)
    {
        if (source is ItemsControl itemsControl &&
            ReferenceEquals(itemsControl.ItemsSource, track.EmbeddedChapterRows))
        {
            return itemsControl;
        }

        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(source); index++)
        {
            var result = FindEmbeddedChapterItemsControl(VisualTreeHelper.GetChild(source, index), track);
            if (result is not null)
                return result;
        }

        return null;
    }

    private void TracksGrid_ColumnReordered(object sender, DataGridColumnEventArgs e)
    {
        if (_isApplyingTrackGridColumnLayout)
            return;

        SaveTrackGridColumnLayout();
        RefreshEmbeddedChapterRowColumnLayouts(force: true);
    }

    private void TracksGrid_LayoutUpdated(object sender, EventArgs e)
    {
        RefreshEmbeddedChapterRowColumnLayouts(force: false);
    }

    private void EmbeddedChapterRow_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Grid rowGrid)
            return;

        _embeddedChapterRowGrids.Add(rowGrid);
        ApplyEmbeddedChapterRowColumnLayout(rowGrid);
    }

    private void EmbeddedChapterRow_Unloaded(object sender, RoutedEventArgs e)
    {
        if (sender is Grid rowGrid)
            _embeddedChapterRowGrids.Remove(rowGrid);
    }

    private void RefreshEmbeddedChapterRowColumnLayouts(bool force)
    {
        if (TracksGrid is null || _embeddedChapterRowGrids.Count == 0)
            return;

        var signature = BuildEmbeddedChapterColumnLayoutSignature();
        if (!force && string.Equals(_embeddedChapterColumnLayoutSignature, signature, StringComparison.Ordinal))
            return;

        _embeddedChapterColumnLayoutSignature = signature;

        foreach (var rowGrid in _embeddedChapterRowGrids.ToList())
        {
            if (!rowGrid.IsLoaded)
            {
                _embeddedChapterRowGrids.Remove(rowGrid);
                continue;
            }

            ApplyEmbeddedChapterRowColumnLayout(rowGrid);
        }
    }

    private string BuildEmbeddedChapterColumnLayoutSignature()
    {
        if (TracksGrid is null)
            return string.Empty;

        var builder = new StringBuilder();
        foreach (var column in TracksGrid.Columns.OrderBy(column => column.DisplayIndex))
        {
            builder
                .Append(GetTrackGridColumnKey(column))
                .Append(':')
                .Append(column.DisplayIndex.ToString(CultureInfo.InvariantCulture))
                .Append(':')
                .Append(column.Visibility)
                .Append(':')
                .Append(Math.Round(GetTrackGridColumnLayoutWidth(column), 1).ToString(CultureInfo.InvariantCulture))
                .Append('|');
        }

        return builder.ToString();
    }

    private void ApplyEmbeddedChapterRowColumnLayout(Grid rowGrid)
    {
        if (TracksGrid is null)
            return;

        var columns = TracksGrid.Columns
            .OrderBy(column => column.DisplayIndex)
            .ToList();
        if (columns.Count == 0)
            return;

        rowGrid.MinWidth = Math.Max(0, TracksGrid.ActualWidth);
        rowGrid.ColumnDefinitions.Clear();

        var displayColumnByKey = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var index = 0; index < columns.Count; index++)
        {
            var column = columns[index];
            var width = column.Visibility == Visibility.Visible
                ? GetTrackGridColumnLayoutWidth(column)
                : 0;

            rowGrid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(Math.Max(0, width))
            });

            displayColumnByKey[GetTrackGridColumnKey(column)] = index;
        }

        foreach (UIElement child in rowGrid.Children)
        {
            if (child is not FrameworkElement element)
                continue;

            if (TrackGridChildColumn.GetSpansAllColumns(element))
            {
                Grid.SetColumn(element, 0);
                Grid.SetColumnSpan(element, columns.Count);
                continue;
            }

            var key = TrackGridChildColumn.GetKey(element);
            if (string.IsNullOrWhiteSpace(key) || !displayColumnByKey.TryGetValue(key, out var displayColumn))
                continue;

            Grid.SetColumn(element, displayColumn);
            Grid.SetColumnSpan(element, 1);
        }
    }

    private void BuildTrackGridRowContextMenu(ContextMenu menu)
    {
        menu.Items.Clear();
        menu.Items.Add(CreateTrackGridMenuItem(
            LocalizationText.Get("UiAusgewaehlteTracksAusschliessen"),
            (_, _) => RemoveSelectedTracks_Click(this, new RoutedEventArgs())));
        menu.Items.Add(CreateTrackGridMenuItem(
            LocalizationText.Get("UiAusgewaehlteTracksWiederherstellen"),
            (_, _) => RestoreSelectedTracks_Click(this, new RoutedEventArgs())));
        menu.Items.Add(new Separator());
        var restoreAuto = CreateTrackGridMenuItem(
            LocalizationText.Get("MainWindow.Tracks.RestoreAutomaticOrder"),
            (_, _) => RestoreAutomaticTrackOrder_Click(this, new RoutedEventArgs()));
        restoreAuto.IsEnabled = CanEditTrackOrder && Tracks.Any(track => track.AutomaticOrderPosition.HasValue);
        menu.Items.Add(restoreAuto);
    }

    private void BuildTrackGridColumnContextMenu(ContextMenu menu)
    {
        menu.Items.Clear();

        foreach (var column in TracksGrid.Columns.OrderBy(column => column.DisplayIndex))
        {
            var menuItem = CreateTrackGridMenuItem(
                CreateTrackGridColumnMenuHeader(column),
                (_, _) => ToggleTrackGridColumnVisibility(column));
            menu.Items.Add(menuItem);
        }

        menu.Items.Add(new Separator());
        menu.Items.Add(CreateTrackGridMenuItem(
            CreateTrackGridCheckedMenuHeader(LocalizationText.Get("MainWindow.Tracks.UseAutoFit"), _settings.TrackGridAutoFitEnabled),
            (_, _) => ToggleTrackGridAutoFit()));
        menu.Items.Add(CreateTrackGridMenuItem(
            LocalizationText.Get("MainWindow.Tracks.RestoreDefaultLayout"),
            (_, _) => ResetTrackGridColumnLayout()));
    }

    private MenuItem CreateTrackGridMenuItem(object header, RoutedEventHandler click)
    {
        var item = new MenuItem
        {
            Header = header,
            Style = (Style)FindResource("BookStitchContextMenuItemStyle")
        };
        item.Click += click;
        return item;
    }

    private static FrameworkElement CreateTrackGridColumnMenuHeader(DataGridColumn column)
    {
        return CreateTrackGridCheckedMenuHeader(
            GetTrackGridColumnKey(column) switch
            {
                "BitrateKbps" => LocalizationText.Get("UiOriginalBitrate"),
                "SampleRateHz" => LocalizationText.Get("UiOriginalSampleRate"),
                "Channels" => LocalizationText.Get("UiOriginalMonoStereo"),
                _ => column.Header?.ToString() ?? GetTrackGridColumnKey(column)
            },
            column.Visibility == Visibility.Visible);
    }

    private static FrameworkElement CreateTrackGridCheckedMenuHeader(string text, bool isChecked)
    {
        var panel = new Grid();
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var check = new TextBlock
        {
            Text = isChecked ? UiSymbolResources.Checked : string.Empty,
            FontWeight = FontWeights.Bold,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(check, 0);
        panel.Children.Add(check);

        var label = new TextBlock
        {
            Text = text,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(label, 1);
        panel.Children.Add(label);

        return panel;
    }

    private bool _showOriginalAudioColumns = true;

    private void SetOriginalAudioColumnsVisible(bool showOriginal, bool force = false)
    {
        if (!force && !showOriginal && !_showOriginalAudioColumns) return;
        _showOriginalAudioColumns = showOriginal;
        if (TracksGrid is null) return;
        var applyingLayout = _isApplyingTrackGridColumnLayout;
        _isApplyingTrackGridColumnLayout = true;
        try
        {
            TrackGridOriginalBitrateColumn.Visibility = showOriginal ? Visibility.Visible : Visibility.Collapsed;
            TrackGridOriginalSampleRateColumn.Visibility = showOriginal ? Visibility.Visible : Visibility.Collapsed;
            TrackGridOriginalChannelLayoutColumn.Visibility = showOriginal ? Visibility.Visible : Visibility.Collapsed;
            TrackGridBitrateColumn.Visibility = showOriginal ? Visibility.Collapsed : Visibility.Visible;
            TrackGridChannelLayoutColumn.Visibility = showOriginal ? Visibility.Collapsed : Visibility.Visible;
        }
        finally { _isApplyingTrackGridColumnLayout = applyingLayout; }
        AutoFitTrackColumnsAfterRender();
        RefreshEmbeddedChapterRowColumnLayouts(force: true);
    }

    private void ToggleTrackGridColumnVisibility(DataGridColumn column)
    {
        column.Visibility = column.Visibility == Visibility.Visible
            ? Visibility.Collapsed
            : Visibility.Visible;

        SaveTrackGridColumnLayout();
        RefreshEmbeddedChapterRowColumnLayouts(force: true);
    }

    private void ToggleTrackGridAutoFit()
    {
        _settings.TrackGridAutoFitEnabled = !_settings.TrackGridAutoFitEnabled;
        SaveSettingsIfReady();

        if (_settings.TrackGridAutoFitEnabled)
            AutoFitTrackColumnsAfterRender();
        else
            SaveTrackGridColumnLayout();
    }

    private void ResetTrackGridColumnLayout()
    {
        _settings.TrackGridColumns = new List<TrackGridColumnLayoutItem>();
        _settings.TrackGridAutoFitEnabled = true;

        _isApplyingTrackGridColumnLayout = true;
        try
        {
            for (var index = 0; index < TracksGrid.Columns.Count; index++)
            {
                var column = TracksGrid.Columns[index];
                column.DisplayIndex = index;
                column.Visibility = GetDefaultTrackGridColumnVisibility(column);
                column.Width = GetDefaultTrackGridColumnWidth(column);
            }

            ApplyDefaultTrackGridColumnOrder();
        }
        finally
        {
            _isApplyingTrackGridColumnLayout = false;
        }

        SaveSettingsIfReady();
        AutoFitTrackColumnsAfterRender();
        RefreshEmbeddedChapterRowColumnLayouts(force: true);
    }

    private void ApplyTrackGridColumnLayout()
    {
        if (TracksGrid is null || TracksGrid.Columns.Count == 0)
            return;

        var saved = (_settings.TrackGridColumns ?? new List<TrackGridColumnLayoutItem>())
            .Where(item => !string.IsNullOrWhiteSpace(item.Key))
            .GroupBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        if (saved.Count == 0)
            return;

        var shouldPersistMigratedStatusIndicator = !saved.ContainsKey("StatusIndicator");
        var statusIndicatorDisplayIndex = ResolveMigratedStatusIndicatorDisplayIndex(saved);
        var ordered = TracksGrid.Columns
            .Select((column, originalIndex) => new
            {
                Column = column,
                OriginalIndex = originalIndex,
                Key = GetTrackGridColumnKey(column),
                Saved = saved.GetValueOrDefault(GetTrackGridColumnKey(column))
            })
            .OrderBy(item => ResolveSavedDisplayIndex(item.Key, item.Saved, statusIndicatorDisplayIndex))
            .ThenBy(item => item.OriginalIndex)
            .ToList();

        _isApplyingTrackGridColumnLayout = true;
        try
        {
            for (var index = 0; index < ordered.Count; index++)
                ordered[index].Column.DisplayIndex = index;

            foreach (var item in ordered)
            {
                if (item.Saved is not null)
                {
                    item.Column.Visibility = item.Saved.IsVisible
                        ? Visibility.Visible
                        : Visibility.Collapsed;

                    if (!_settings.TrackGridAutoFitEnabled && item.Saved.Width is > 0)
                        item.Column.Width = new DataGridLength(item.Saved.Width.Value);
                }
                else
                {
                    item.Column.Visibility = GetDefaultTrackGridColumnVisibility(item.Column);
                }
                if (GetTrackGridColumnKey(item.Column) is "BitrateKbps" or "SampleRateHz" or "Channels" or "DisplayPreparedBitrateKbps" or "DisplayPreparedChannelLayout")
                    item.Column.Visibility = GetDefaultTrackGridColumnVisibility(item.Column);
            }

            ApplyPinnedTrackGridColumnOrder();
        }
        finally
        {
            _isApplyingTrackGridColumnLayout = false;
        }

        if (shouldPersistMigratedStatusIndicator)
            SaveTrackGridColumnLayout();

        RefreshEmbeddedChapterRowColumnLayouts(force: true);
    }

    private static double ResolveMigratedStatusIndicatorDisplayIndex(
        IReadOnlyDictionary<string, TrackGridColumnLayoutItem> saved)
    {
        if (saved.TryGetValue("AutomaticOrderPosition", out var autoColumn))
            return autoColumn.DisplayIndex + 0.5d;

        return 1d;
    }

    private static double ResolveSavedDisplayIndex(
        string key,
        TrackGridColumnLayoutItem? saved,
        double migratedStatusIndicatorDisplayIndex)
    {
        if (saved is not null)
            return saved.DisplayIndex;

        return string.Equals(key, "StatusIndicator", StringComparison.OrdinalIgnoreCase)
            ? migratedStatusIndicatorDisplayIndex
            : int.MaxValue;
    }

    private void SaveTrackGridColumnLayout()
    {
        if (_isLoadingSettings || _isApplyingTrackGridColumnLayout || TracksGrid is null)
            return;

        _settings.TrackGridColumns = TracksGrid.Columns
            .OrderBy(column => column.DisplayIndex)
            .Select(column => new TrackGridColumnLayoutItem
            {
                Key = GetTrackGridColumnKey(column),
                DisplayIndex = column.DisplayIndex,
                IsVisible = column.Visibility == Visibility.Visible,
                Width = GetTrackGridColumnWidth(column)
            })
            .ToList();

        SaveSettingsIfReady();
    }

    private static string GetTrackGridColumnKey(DataGridColumn column)
    {
        var explicitKey = TrackGridChildColumn.GetKey(column);
        if (!string.IsNullOrWhiteSpace(explicitKey))
            return explicitKey;

        return !string.IsNullOrWhiteSpace(column.SortMemberPath)
            ? column.SortMemberPath
            : throw new InvalidOperationException("Track grid column has no stable key.");
    }

    private void ApplyPinnedTrackGridColumnOrder()
    {
        if (TracksGrid is null)
            return;

        var fileColumn = TracksGrid.Columns.FirstOrDefault(column => string.Equals(GetTrackGridColumnKey(column), "FileName", StringComparison.Ordinal));
        var startColumn = TracksGrid.Columns.FirstOrDefault(column => string.Equals(GetTrackGridColumnKey(column), "Start", StringComparison.Ordinal));
        var chapterColumn = TracksGrid.Columns.FirstOrDefault(column => string.Equals(GetTrackGridColumnKey(column), "ChapterProposal", StringComparison.Ordinal));
        if (fileColumn is null || startColumn is null || chapterColumn is null)
            return;

        var audioColumns = TracksGrid.Columns.OrderBy(column => column.DisplayIndex)
            .Where(column => column != TrackGridOriginalBitrateColumn && column != TrackGridOriginalSampleRateColumn && column != TrackGridOriginalChannelLayoutColumn).ToList();
        var bitrateIndex = audioColumns.IndexOf(TrackGridBitrateColumn);
        audioColumns.Insert(bitrateIndex, TrackGridOriginalBitrateColumn);
        audioColumns.Insert(bitrateIndex + 1, TrackGridOriginalSampleRateColumn);
        audioColumns.Insert(bitrateIndex + 2, TrackGridOriginalChannelLayoutColumn);
        for (var index = 0; index < audioColumns.Count; index++)
            audioColumns[index].DisplayIndex = index;

        startColumn.DisplayIndex = Math.Min(fileColumn.DisplayIndex + 1, TracksGrid.Columns.Count - 1);
        chapterColumn.DisplayIndex = Math.Min(startColumn.DisplayIndex + 1, TracksGrid.Columns.Count - 1);
    }

    private void ApplyDefaultTrackGridColumnOrder()
    {
        if (TracksGrid is null)
            return;

        var autoColumn = TracksGrid.Columns.FirstOrDefault(column => string.Equals(GetTrackGridColumnKey(column), "AutomaticOrderPosition", StringComparison.Ordinal));
        var statusColumn = TracksGrid.Columns.FirstOrDefault(column => string.Equals(GetTrackGridColumnKey(column), "StatusIndicator", StringComparison.Ordinal));
        if (autoColumn is not null && statusColumn is not null)
            statusColumn.DisplayIndex = Math.Min(autoColumn.DisplayIndex + 1, TracksGrid.Columns.Count - 1);

        ApplyPinnedTrackGridColumnOrder();
    }

    private static double? GetTrackGridColumnWidth(DataGridColumn column)
    {
        var width = GetTrackGridColumnLayoutWidth(column);

        return width > 0
            ? Math.Round(width, 1)
            : null;
    }

    private static double GetTrackGridColumnLayoutWidth(DataGridColumn column)
    {
        if (column.ActualWidth > 0)
            return column.ActualWidth;

        if (column.Width.IsAbsolute)
            return column.Width.Value;

        return 0;
    }

    private Visibility GetDefaultTrackGridColumnVisibility(DataGridColumn column)
    {
        var key = GetTrackGridColumnKey(column);
        if (key is "BitrateKbps" or "SampleRateHz" or "Channels")
            return _showOriginalAudioColumns ? Visibility.Visible : Visibility.Collapsed;
        if (key is "DisplayPreparedBitrateKbps" or "DisplayPreparedChannelLayout")
            return _showOriginalAudioColumns ? Visibility.Collapsed : Visibility.Visible;
        return string.Equals(GetTrackGridColumnKey(column), "Extension", StringComparison.Ordinal)
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private static DataGridLength GetDefaultTrackGridColumnWidth(DataGridColumn column) =>
        DataGridLength.Auto;

    private static T? FindVisualAncestor<T>(DependencyObject? source)
        where T : DependencyObject
    {
        var current = source;
        while (current is not null)
        {
            if (current is T match)
                return match;

            current = current switch
            {
                Visual or Visual3D => VisualTreeHelper.GetParent(current),
                FrameworkContentElement contentElement => contentElement.Parent,
                _ => null
            };
        }

        return null;
    }

    private static T? FindVisualChild<T>(DependencyObject source)
        where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(source); i++)
        {
            var child = VisualTreeHelper.GetChild(source, i);
            if (child is T match)
                return match;

            var nested = FindVisualChild<T>(child);
            if (nested is not null)
                return nested;
        }

        return null;
    }

    private void TracksGrid_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_isTrackGridAutoScrollActive && e.Key == Key.Escape)
        {
            StopTrackGridAutoScroll();
            e.Handled = true;
            return;
        }

        HandleTracksGridDeleteKey(e);
        if (HandleActiveVisibleTrackGridRowKey(e.Key))
        {
            e.Handled = true;
            return;
        }

        HandleTracksGridChapterToggleKey(e);
    }

    private void TracksGrid_KeyDown(object sender, KeyEventArgs e)
    {
        HandleTracksGridDeleteKey(e);
    }

    private void HandleTracksGridChapterToggleKey(KeyEventArgs e)
    {
        if (e.Handled || (e.Key != Key.Enter && e.Key != Key.Space))
            return;

        if (e.OriginalSource is DependencyObject source &&
            FindVisualAncestor<ButtonBase>(source) is not null)
        {
            return;
        }

        if (TracksGrid.SelectedItem is not TrackInfo track || !track.HasEmbeddedChapterDetails)
            return;

        track.IsChapterDetailsExpanded = !track.IsChapterDetailsExpanded;
        e.Handled = true;
    }

    private void HandleTracksGridDeleteKey(KeyEventArgs e)
    {
        if (e.Handled || e.Key != Key.Delete || !CanEditTrackOrder)
            return;

        if (ToggleSelectedTracksFromDeleteKey())
            e.Handled = true;
    }

    private void RemoveSelectedTracks_Click(object sender, RoutedEventArgs e)
    {
        RemoveSelectedTracksFromList();
    }

    private bool ToggleSelectedTracksFromDeleteKey()
    {
        if (!CanEditTrackOrder)
            return false;

        var visibleTracks = GetVisibleTracks();
        var selected = GetSelectedTracksInVisibleOrder(visibleTracks);
        var result = _services.Tracks.TrackListActionService.ToggleSelectedForDelete(selected);
        if (result.ChangedCount == 0)
            return false;

        UpdateIndexes();
        PersistTrackListState();
        UpdateFinalStatus(Tracks.Count(track => !track.IsExcluded));
        ExportProgressText = BuildIdleExportProgressText();
        StatusText = result.Action == TrackExclusionToggleAction.Restored
            ? LocalizationText.Format(result.ChangedCount == 1 ? "MainWindow.Tracks.Restored.One" : "MainWindow.Tracks.Restored.Many", result.ChangedCount)
            : LocalizationText.Format(result.ChangedCount == 1 ? "MainWindow.Tracks.Excluded.One" : "MainWindow.Tracks.Excluded.Many", result.ChangedCount);
        OnPropertyChanged(nameof(CanStartExport));
        OnExportPreviewChanged();
        RefreshTrackPresentationViews();
        RestoreSelection(selected);
        return true;
    }

    private bool RemoveSelectedTracksFromList()
    {
        if (!CanEditTrackOrder)
            return false;

        var visibleTracks = GetVisibleTracks();
        var selected = GetSelectedTracksInVisibleOrder(visibleTracks);
        if (selected.Count == 0)
            return false;

        var changed = _services.Tracks.TrackListActionService.ExcludeSelected(selected);
        if (changed == 0)
            return false;

        UpdateIndexes();
        PersistTrackListState();
        UpdateFinalStatus(Tracks.Count(track => !track.IsExcluded));
        ExportProgressText = BuildIdleExportProgressText();
        StatusText = LocalizationText.Format(changed == 1 ? "MainWindow.Tracks.Excluded.One" : "MainWindow.Tracks.Excluded.Many", changed);
        OnPropertyChanged(nameof(CanStartExport));
        OnExportPreviewChanged();
        RefreshTrackPresentationViews();
        return true;
    }

    private void RestoreSelectedTracks_Click(object sender, RoutedEventArgs e)
    {
        if (!CanEditTrackOrder)
            return;

        var visibleTracks = GetVisibleTracks();
        var selected = GetSelectedTracksInVisibleOrder(visibleTracks);
        var changed = _services.Tracks.TrackListActionService.RestoreSelected(selected);
        if (changed == 0)
            return;

        UpdateIndexes();
        PersistTrackListState();
        UpdateFinalStatus(Tracks.Count(track => !track.IsExcluded));
        ExportProgressText = BuildIdleExportProgressText();
        StatusText = LocalizationText.Format(changed == 1 ? "MainWindow.Tracks.Restored.One" : "MainWindow.Tracks.Restored.Many", changed);
        OnPropertyChanged(nameof(CanStartExport));
        OnExportPreviewChanged();
        RefreshTrackPresentationViews();
    }

    private void MoveSelectedUp_Click(object sender, RoutedEventArgs e)
    {
        if (!CanEditTrackOrder)
            return;

        var visibleTracks = GetVisibleTracks();
        var selected = GetSelectedTracksInVisibleOrder(visibleTracks);

        if (selected.Count == 0)
            return;

        ClearGridSorting();

        var newOrder = _services.Tracks.TrackListActionService.MoveSelectedUp(visibleTracks, selected);

        ReplaceTrackList(newOrder);
        RestoreSelection(selected);
    }

    private void RestoreAutomaticTrackOrder_Click(object sender, RoutedEventArgs e)
    {
        if (!CanEditTrackOrder || Tracks.All(track => !track.AutomaticOrderPosition.HasValue))
            return;

        ClearGridSorting();
        var selected = TracksGrid
            .SelectedItems
            .OfType<TrackInfo>()
            .ToList();
        var restored = _services.Tracks.TrackListActionService.RestoreAutomaticOrder(Tracks);

        ReplaceTrackList(restored);
        RestoreSelection(selected);
        StatusText = LocalizationText.Get("MainWindow.Tracks.AutoOrderRestored");
    }

    private void MoveSelectedDown_Click(object sender, RoutedEventArgs e)
    {
        if (!CanEditTrackOrder)
            return;

        var visibleTracks = GetVisibleTracks();
        var selected = GetSelectedTracksInVisibleOrder(visibleTracks);

        if (selected.Count == 0)
            return;

        ClearGridSorting();

        var newOrder = _services.Tracks.TrackListActionService.MoveSelectedDown(visibleTracks, selected);

        ReplaceTrackList(newOrder);
        RestoreSelection(selected);
    }

    private void MoveSelectedToTop_Click(object sender, RoutedEventArgs e)
    {
        if (!CanEditTrackOrder)
            return;

        var visibleTracks = GetVisibleTracks();
        var selected = GetSelectedTracksInVisibleOrder(visibleTracks);

        if (selected.Count == 0)
            return;

        ClearGridSorting();

        var newOrder = _services.Tracks.TrackListActionService.MoveSelectedToTop(visibleTracks, selected);

        ReplaceTrackList(newOrder);
        RestoreSelection(selected);
    }

    private void MoveSelectedToBottom_Click(object sender, RoutedEventArgs e)
    {
        if (IsBusy)
            return;

        var visibleTracks = GetVisibleTracks();
        var selected = GetSelectedTracksInVisibleOrder(visibleTracks);

        if (selected.Count == 0)
            return;

        ClearGridSorting();

        var newOrder = _services.Tracks.TrackListActionService.MoveSelectedToBottom(visibleTracks, selected);

        ReplaceTrackList(newOrder);
        RestoreSelection(selected);
    }

    private List<TrackInfo> GetVisibleTracks()
    {
        var view = CollectionViewSource.GetDefaultView(Tracks);

        return view
            .Cast<TrackInfo>()
            .ToList();
    }

    private List<TrackInfo> GetSelectedTracksInVisibleOrder(List<TrackInfo> visibleTracks)
    {
        var selected = TracksGrid
            .SelectedItems
            .OfType<TrackInfo>()
            .ToHashSet();

        return visibleTracks
            .Where(selected.Contains)
            .ToList();
    }

    private void ReplaceTrackList(List<TrackInfo> newOrder)
    {
        Tracks.ReplaceAll(newOrder);

        UpdateIndexes();
        PersistTrackListState();
        AutoFitTrackColumnsAfterRender();
    }

    private void PersistTrackListState()
    {
        if (_isWorkspaceTransitioning)
            return;
        if (_isDiscStopSaveInProgress ||
            new DiscProjectStopSaveService().HasPendingCommit(_currentProjectWorkFolder))
            return;

        var folder = !string.IsNullOrWhiteSpace(_currentProjectWorkFolder)
            ? _currentProjectWorkFolder
            : string.Empty;
        if (!string.IsNullOrWhiteSpace(folder))
        {
            StopAutomaticMaintenance();
            _services.Foundation.TrackListStateService.Save(folder, Tracks.ToList());
        }
    }

    private void ApplyPersistedTrackListState(string projectFolder)
    {
        var projectOrderType = _loadedProjectState.IsAudioDisc
            ? TrackOrderProjectType.AudioDisc
            : _loadedProjectState.IsMp3Disc
                ? TrackOrderProjectType.Mp3Disc
                : TrackOrderProjectType.LocalFolder;
        var confirmedContexts = _loadedProjectState.IsAudioDisc
            ? TrackOrderConfirmedContextFactory.CreateAudioDiscContexts(Tracks)
            : _loadedProjectState.IsMp3Disc
                ? TrackOrderConfirmedContextFactory.CreateMp3DiscContexts(Tracks)
                : null;
        var ordered = _services.Foundation.TrackListStateService.Apply(
            projectFolder,
            Tracks,
            projectOrderType,
            confirmedContexts);
        Tracks.ReplaceAll(ordered);
    }

    private void RestoreSelection(List<TrackInfo> selected)
    {
        TracksGrid.SelectedItems.Clear();

        foreach (var track in selected)
            TracksGrid.SelectedItems.Add(track);

        if (selected.Count > 0)
            TracksGrid.ScrollIntoView(selected[0]);

        TracksGrid.Focus();
        Keyboard.Focus(TracksGrid);
    }

    private void ClearGridSorting()
    {
        var view = CollectionViewSource.GetDefaultView(Tracks);
        view.SortDescriptions.Clear();

        foreach (var column in TracksGrid.Columns)
            column.SortDirection = null;
    }

    private void UpdateIndexes()
    {
        if (_isWorkspaceTransitioning)
            return;

        var effectiveActiveChapterCount = EstimateEffectiveActiveChapterCountForCurrentProject();
        _currentActiveChapterResult = _services.Foundation.ChapterModeEngineService.Apply(
            CreateCurrentChapterAutoEngineRequest(effectiveActiveChapterCount));
        _currentAutoChapterResult = _currentActiveChapterResult.EngineResult;
        _services.Tracks.TrackListWarningService.Apply(Tracks);
        RefreshChapterTimelinePresentation();

        RefreshTrackPresentationViews();
        OnExportPreviewChanged();
    }

    private void RefreshChapterTimelinePresentation()
    {
        _services.Foundation.ChapterTimelinePresentationService.ApplyResolvedTimeline(
            Tracks,
            _currentActiveChapterResult?.RenderedTimeline ?? []);
    }

    private ChapterAutoEngineRequest CreateCurrentChapterAutoEngineRequest(
        int? effectiveActiveChapterCount)
    {
        var projectType = ResolveCurrentChapterProjectType();
        var projectIdentity = ResolveCurrentChapterProjectIdentity(projectType);

        return new ChapterAutoEngineRequest(
            Tracks,
            projectType,
            projectIdentity,
            _currentProjectWorkFolder,
            ChapterCanonicalMetadataSnapshot.Capture(_projectMetadata.CanonicalMetadata),
            _settings.ChapterSettings,
            LegacyUseLeadingZeros: false,
            effectiveActiveChapterCount,
            _activeMp3DiscManifest,
            _activeAudioDiscManifest,
            AllowTransientLocalFilenameEvidence:
                string.Equals(projectType, ProjectManifestTypes.FolderProject, StringComparison.OrdinalIgnoreCase) &&
                string.IsNullOrWhiteSpace(_currentProjectWorkFolder),
            PendingMp3Discs: BuildCurrentMp3DiscChapterSources(),
            PendingAudioDiscIdentity: _activeAudioDiscManifest is null
                ? _pendingAudioDisc?.DiscIdentity
                : null,
            PendingAudioDisc: _resumeDiscPreview.AudioDisc);
    }

    private string ResolveCurrentChapterProjectType()
    {
        if (_activeAudioDiscManifest is not null || _pendingAudioDisc is not null || LoadedProjectState.IsAudioDisc)
            return ProjectManifestTypes.AudioCdProject;

        if (_activeMp3DiscManifest is not null || _transientMp3DiscChapterPreview is not null || LoadedProjectState.IsMp3Disc)
            return ProjectManifestTypes.Mp3DiscProject;

        return ProjectManifestTypes.FolderProject;
    }

    private string ResolveCurrentChapterProjectIdentity(string projectType)
    {
        if (!string.IsNullOrWhiteSpace(_currentProjectWorkFolder))
            return _currentProjectWorkFolder;

        if (string.Equals(projectType, ProjectManifestTypes.Mp3DiscProject, StringComparison.OrdinalIgnoreCase) &&
            _transientMp3DiscChapterPreview is not null)
        {
            return "mp3-preview:" + _transientMp3DiscChapterPreview.Signature;
        }

        if (string.Equals(projectType, ProjectManifestTypes.AudioCdProject, StringComparison.OrdinalIgnoreCase) &&
            _pendingAudioDisc is not null)
        {
            return "audio-preview:" + _pendingAudioDisc.DiscIdentity;
        }

        return !string.IsNullOrWhiteSpace(_currentFolderPath)
            ? _currentFolderPath
            : "current-project";
    }

    private IReadOnlyCollection<Mp3DiscPendingChapterDisc> BuildCurrentMp3DiscChapterSources()
    {
        var current = _mp3DiscChapterSourceCatalog.Snapshot().ToList();
        if (_transientMp3DiscChapterPreview is null)
            return current;

        if (!current.Any(item =>
                item.DiscNumber == _transientMp3DiscChapterPreview.DiscNumber ||
                string.Equals(
                    item.Signature,
                    _transientMp3DiscChapterPreview.Signature,
                    StringComparison.OrdinalIgnoreCase)))
        {
            current.Add(_transientMp3DiscChapterPreview);
        }

        return current
            .OrderBy(item => item.DiscNumber)
            .ToArray();
    }

    private void QueueChapterMetadataRefreshIfNeeded()
    {
        if (_isWorkspaceTransitioning || _isLoadingSettings || Tracks.Count == 0 || !ChapterSettingsUseProjectMetadata())
            return;

        _chapterMetadataRefreshTimer.Stop();
        _chapterMetadataRefreshTimer.Start();
    }

    private void ChapterMetadataRefreshTimer_Tick(object? sender, EventArgs e)
    {
        FlushPendingChapterMetadataRefresh();
    }

    private void FlushPendingChapterMetadataRefresh()
    {
        if (!_chapterMetadataRefreshTimer.IsEnabled)
            return;

        _chapterMetadataRefreshTimer.Stop();
        UpdateIndexes();
        PersistTrackListState();
    }

    private bool ChapterSettingsUseProjectMetadata()
    {
        var settings = ChapterSettingsService.Normalize(
            _settings.ChapterSettings,
            legacyUseLeadingZeros: false);

        if (!string.Equals(settings.Mode, ChapterSettingsService.ModeCustom, StringComparison.Ordinal))
            return true;

        return IsProjectMetadataSlot(settings.Slot1) ||
               IsProjectMetadataSlot(settings.Slot2) ||
               IsProjectMetadataSlot(settings.Slot3);
    }

    private static bool IsProjectMetadataSlot(string? slot)
    {
        var normalized = ChapterSettingsService.NormalizeSlot(slot);
        return string.Equals(normalized, "BookTitle", StringComparison.Ordinal) ||
               string.Equals(normalized, "Album", StringComparison.Ordinal);
    }

    private int? EstimateEffectiveActiveChapterCountForCurrentProject()
    {
        if (_activeAudioDiscManifest?.TotalDiscs is > 1)
            return ChapterEffectiveCountService.EstimateEffectiveActiveChapterCount(
                Tracks,
                _activeAudioDiscManifest.TotalDiscs);

        if (_activeMp3DiscManifest?.TotalDiscs is > 1)
            return ChapterEffectiveCountService.EstimateEffectiveActiveChapterCount(
                Tracks,
                _activeMp3DiscManifest.TotalDiscs);

        if (_previewEffectiveTotalDiscs is > 1)
            return ChapterEffectiveCountService.EstimateEffectiveActiveChapterCount(
                Tracks,
                _previewEffectiveTotalDiscs.Value);

        return null;
    }

    private static ChapterSettings CloneChapterSettings(ChapterSettings? settings)
    {
        settings = ChapterSettingsService.Normalize(settings, legacyUseLeadingZeros: true);
        return new ChapterSettings
        {
            Mode = settings.Mode,
            NumberingStyle = settings.NumberingStyle,
            Slot1 = settings.Slot1,
            Separator1 = settings.Separator1,
            Slot2 = settings.Slot2,
            Separator2 = settings.Separator2,
            Slot3 = settings.Slot3
        };
    }

    private static bool ChapterSettingsEqual(ChapterSettings? left, ChapterSettings? right)
    {
        var a = CloneChapterSettings(left);
        var b = CloneChapterSettings(right);
        return string.Equals(a.Mode, b.Mode, StringComparison.Ordinal)
               && string.Equals(a.NumberingStyle, b.NumberingStyle, StringComparison.Ordinal)
               && string.Equals(a.Slot1, b.Slot1, StringComparison.Ordinal)
               && string.Equals(a.Separator1, b.Separator1, StringComparison.Ordinal)
               && string.Equals(a.Slot2, b.Slot2, StringComparison.Ordinal)
               && string.Equals(a.Separator2, b.Separator2, StringComparison.Ordinal)
               && string.Equals(a.Slot3, b.Slot3, StringComparison.Ordinal);
    }

    private void AutoFitTrackColumnsAfterRender()
    {
        if (!_settings.TrackGridAutoFitEnabled)
            return;

        var allTracks = Tracks.ToList();

        Dispatcher.InvokeAsync(() =>
        {
            if (TracksGrid.Columns.Count == 0)
                return;

            TracksGrid.UpdateLayout();

            foreach (var column in TracksGrid.Columns)
                column.Width = DataGridLength.Auto;

            TracksGrid.UpdateLayout();

            foreach (var column in TracksGrid.Columns)
            {
                var header = column.Header?.ToString() ?? "";

                var width = Math.Max(column.ActualWidth + 12, EstimateTrackColumnWidth(GetTrackGridColumnKey(column), header, allTracks));
                column.Width = new DataGridLength(width);
            }
        });
    }


    private static double EstimateTrackColumnWidth(string key, string header, IReadOnlyList<TrackInfo> tracks)
    {
        var maxTextLength = EstimateTextWidth(header) + 28;

        foreach (var track in tracks)
        {
            var text = key switch
            {
                "DiscNumber" => track.DiscNumber?.ToString(CultureInfo.InvariantCulture) ?? "",
                "TrackNumber" => track.TrackNumber?.ToString(CultureInfo.InvariantCulture) ?? "",
                "FileName" => track.FileName,
                "Start" => track.DisplayChapterStart,
                "DisplayFolder" => track.DisplayFolder,
                "TagTitle" => track.TagTitle,
                "ChapterProposal" => track.DisplayChapterProposal,
                "Duration" => track.Duration,
                "BitrateKbps" => track.BitrateKbps?.ToString(CultureInfo.InvariantCulture) ?? "",
                "SampleRateHz" => track.DisplayOriginalSampleRateHz,
                "Channels" => track.DisplayOriginalChannelLayout,
                "DisplayPreparedChannelLayout" => track.DisplayPreparedChannelLayout,
                "DisplayPreparedBitrateKbps" => track.DisplayPreparedBitrateKbps?.ToString(CultureInfo.InvariantCulture) ?? "",
                "ChannelLayout" => track.ChannelLayout,
                "FileWarningText" => track.DisplayFileWarning,
                "ChapterWarningText" => track.DisplayChapterWarning,
                "Extension" => track.Extension,
                "Codec" => track.DisplayCodec,
                "ProcessingAction" => track.DisplayProcessingAction,
                "SizeMb" => track.SizeMb > 0 ? track.SizeMb.ToString("0.00", CultureInfo.InvariantCulture) : "",
                _ => ""
            };

            maxTextLength = Math.Max(maxTextLength, EstimateTextWidth(text) + 28);
        }

        return maxTextLength;
    }

    private static double EstimateTextWidth(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        var width = 0.0;

        foreach (var character in text)
        {
            width += character switch
            {
                'i' or 'l' or 'I' or '.' or ',' or ':' or ';' or '!' or '|' => 4.0,
                'm' or 'w' or 'M' or 'W' or '@' or '#' => 10.0,
                >= '0' and <= '9' => 7.0,
                >= 'A' and <= 'Z' => 8.0,
                ' ' => 4.0,
                _ => 7.0
            };
        }

        return width;
    }

    private static void SetTrackValue(TrackInfo track, string propertyName, object? value)
    {
        if (value is null)
            return;

        var property = typeof(TrackInfo).GetProperty(propertyName);

        if (property is null || !property.CanWrite)
            return;

        var targetType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;

        try
        {
            if (targetType == typeof(string))
            {
                property.SetValue(track, value.ToString());
                return;
            }

            if (targetType == typeof(int))
            {
                property.SetValue(track, Convert.ToInt32(value));
                return;
            }

            if (targetType == typeof(long))
            {
                property.SetValue(track, Convert.ToInt64(value));
                return;
            }

            if (targetType == typeof(double))
            {
                property.SetValue(track, Convert.ToDouble(value));
            }
        }
        catch
        {
            // Wenn ein Wert nicht gesetzt werden kann, bleibt der alte Wert erhalten.
        }
    }


    public event PropertyChangedEventHandler? PropertyChanged;

    private static string Pluralize(int count, string singular, string plural)
    {
        return count == 1 ? singular : plural;
    }

    private void OnExportPreviewChanged()
    {
        OnPropertyChanged(nameof(ExportPreviewFileName));
        OnPropertyChanged(nameof(ExportPreviewCodecSummary));
        OnPropertyChanged(nameof(ExportPreviewTrackCount));
        OnPropertyChanged(nameof(ExportPreviewTotalDuration));
        OnPropertyChanged(nameof(ExportPreviewActionSummary));
        OnPropertyChanged(nameof(ExportPreviewChapterSummary));
        OnPropertyChanged(nameof(ExportPreviewIssueSummary));
        OnPropertyChanged(nameof(TrackFileWarningSummary));
        OnPropertyChanged(nameof(TrackChapterWarningSummary));
        OnPropertyChanged(nameof(TrackFileWarningSummaryVisibility));
        OnPropertyChanged(nameof(TrackChapterWarningSummaryVisibility));
        OnPropertyChanged(nameof(TrackWarningSummarySeparatorVisibility));
        OnPropertyChanged(nameof(TrackWarningSummaryVisibility));
    }

    private void InitializeEmptyProjectSweepState()
    {
        try
        {
            var service = new ProjectFormatSweepService(Path.Combine(SettingsService.GetSettingsFolder(GetWorkingRootFolder()),
                ProjectFormatSweepService.StateFileName), ProjectIndexService.DiscoverProjectFolders);
            // Automatic update prompts are disabled for release. Only a proven
            // empty inventory receives an initial baseline; manual checks remain.
            service.TryInitializeEmptyRoot(GetWorkingRootFolder());
        }
        catch (Exception ex) { _runtimeServices.DiagnosticLogService.WriteError("PROJECT SWEEP INITIALIZATION", ex); }
    }
    private async void RunProjectIntegritySweep(Window owner,bool fullHashes)=>await RunProjectIntegrityOperation(owner,fullHashes,null);
    private async Task<ProjectOperationOutcome> RunProjectRebuild(Window owner,string projectFolder,bool fullHashes)
    {
        var result=AppDialogService.Show(owner,LocalizationText.Get("Integrity.Rebuild"),LocalizationText.Get("Integrity.Rebuild"),
            LocalizationText.Get("Integrity.RebuildConfirm"),AppDialogKind.Warning,buttons:[
                new AppDialogButton(LocalizationText.Get("Integrity.Rebuild"),AppDialogResult.Yes,IsPrimary:true),
                new AppDialogButton(LocalizationText.Get("Common.Cancel"),AppDialogResult.Cancel)]);
        if(result!=AppDialogResult.Yes)return ProjectOperationOutcome.Canceled;
        return await RunProjectIntegrityOperation(owner,fullHashes,projectFolder);
    }
    private async Task<ProjectOperationOutcome> RunProjectIntegrityOperation(Window owner, bool fullHashes,string? rebuildProject)
    {
        if (_integritySweepRunning || IsBusy || IsExporting || IsDiscImporting || _isWorkspaceTransitioning ||
            _isResumeLoading || _isDiscStopSaveInProgress || _isProjectCloseInProgress || _trackAnalysisCancellation is not null ||
            ProjectReviewState.Phase is ProjectPipelineState.AcquiringSources or ProjectPipelineState.Converting or ProjectPipelineState.Merging)
        {
            AppDialogService.Show(owner, LocalizationText.Get("Integrity.Title"), LocalizationText.Get("Integrity.Title"),
                LocalizationText.Get("Integrity.Busy"), AppDialogKind.Warning); return ProjectOperationOutcome.Failed;
        }
        if (!File.Exists(_ffmpegStatus.FfprobePath))
        { ConfigureFfmpeg_Click(this, new RoutedEventArgs()); return ProjectOperationOutcome.Canceled; }
        if (!TryCheckpointEditableProject()) return ProjectOperationOutcome.Canceled;
        _integritySweepRunning = true;
        try
        {
            CheckAutomaticMaintenance();
            var dialog = new ProjectIntegrityDialog(GetWorkingRootFolder(), _services.Media.AudioProbeBatchService,
                new(_ffmpegStatus.FfprobePath!, ParallelJobsInput, fullHashes), _runtimeServices.DiagnosticLogService,
                () => _maintenanceCoordinator?.StopAsync() ?? Task.CompletedTask,rebuildProject) { Owner = owner };
            ModalDialogService.ShowDialog(dialog, owner);
            if(dialog.Outcome!=ProjectOperationOutcome.Completed)return dialog.Outcome;
            // Reload from checked documents rather than retaining a stale canonical editing session.
            var checkedFolder = _currentProjectWorkFolder;
            if (!string.IsNullOrWhiteSpace(checkedFolder))
            {
                owner.IsEnabled = false;
                try
                {
                    var healthPath = new[] { ProjectFolderLayout.GetFolderManifestPath(checkedFolder), ProjectFolderLayout.GetMp3DiscManifestPath(checkedFolder),
                        ProjectFolderLayout.GetAudioDiscManifestPath(checkedFolder) }.FirstOrDefault(File.Exists);
                    var health = healthPath is null ? "Damaged" : ProjectIntegrityValidator.Text(ProjectIntegrityValidator.Read(healthPath)["Health"], "LastIntegrityStatus");
                    if (health == "Damaged") ClearCurrentProjectWorkspace();
                    else
                    {
                        var plan = await Task.Run(() => _services.Foundation.ProjectResumePlanService.BuildFromProjectFolder(checkedFolder));
                        if (plan is not null) await LoadResumeProjectIntoWorkspaceAsync(plan);
                        else ClearCurrentProjectWorkspace();
                    }
                }
                catch { ClearCurrentProjectWorkspace(); throw; }
                finally { owner.IsEnabled = true; }
            }
            _services.Foundation.ProjectIndexService.Catalog.MarkDirty();
            RefreshResumeProjects(showStatus: false);
            return ProjectOperationOutcome.Completed;
        }
        catch (Exception ex) { _runtimeServices.DiagnosticLogService.WriteError("PROJECT INTEGRITY", ex); return ProjectOperationOutcome.Failed; }
        finally { _integritySweepRunning = false; CheckAutomaticMaintenance(); }
    }

    private void CheckAutomaticMaintenance()
    {
        if (_maintenanceCoordinator is null) return;
        var safeSpace = MaintenanceSafeSpace.None;
        if (_openSettingsContexts > 0) safeSpace |= MaintenanceSafeSpace.GlobalSettings;
        if (_openMetadataContexts > 0) safeSpace |= MaintenanceSafeSpace.Metadata;
        if (ProjectReviewState.Phase == ProjectPipelineState.Completed) safeSpace |= MaintenanceSafeSpace.Complete;
        var eligibility = new MaintenanceEligibility(safeSpace,
            ActiveWork: IsBusy || IsExporting || IsDiscImporting || _trackAnalysisCancellation is not null ||
                ProjectReviewState.Phase is ProjectPipelineState.AcquiringSources or ProjectPipelineState.Converting or ProjectPipelineState.Merging,
            WorkspaceTransition: _isWorkspaceTransitioning,
            ProjectOperation: _integritySweepRunning || !_mainWindowReady || _isResumeLoading || _isProjectBrowserOpen || _isDiscStopSaveInProgress ||
                _isProjectCloseInProgress || _isShutdownInProgress || _windowCloseCleanupCompleted);
        _maintenanceCoordinator.SetEligibility(eligibility);
        if (eligibility.CanRun && _maintenanceCoordinator.IsDue(_settings.LastAutomaticMaintenanceUtc, DateTime.UtcNow))
            _ = RunAutomaticMaintenanceAsync();
    }

    private async Task RunAutomaticMaintenanceAsync()
    {
        try
        {
            var folders = WorkFolderStructure.FromRoot(_services.Foundation.SettingsService.ProjectRootFolder);
            var request = new ProjectMaintenanceRequest(folders.ProjectRootFolder, folders.CoversFolder,
                ProjectIndexService.NormalizeRetentionDays(_settings.ProjectRetentionDays), _settings.ProjectRetentionMode, DateTime.UtcNow);
            var completed = await _maintenanceCoordinator!.TryStartAsync(request, _settings.LastAutomaticMaintenanceUtc, timestamp =>
            {
                var previous = _settings.LastAutomaticMaintenanceUtc;
                _settings.LastAutomaticMaintenanceUtc = timestamp;
                if (_services.Foundation.GlobalSettingsManager.SaveImmediate()) return true;
                _settings.LastAutomaticMaintenanceUtc = previous;
                return false;
            });
            if (!_windowCloseCleanupCompleted && completed)
            {
                ProjectCoverThumbnailCache.Shared.InvalidateDeletedFiles();
                RefreshResumeProjects(showStatus: false);
            }
        }
        catch (Exception error)
        {
            if (!_windowCloseCleanupCompleted)
                _runtimeServices.DiagnosticLogService.WriteError("Automatic maintenance failed", error);
        }
    }

    private bool _wasPreparationProcessing;

    private void NotifyExportUiStateChanged()
    {
        // Producer-finally paths also cover fast normal completion and cancellation failures.
        if (!IsBusy && !IsExporting && !IsDiscImporting &&
            _exportCancellation is null && _discImportCancellation is null && _trackAnalysisCancellation is null &&
            !_isDiscStopSaveInProgress && !_isProjectCloseInProgress && !_isShutdownInProgress)
        {
            _presentationState.ClearUiTransition();
        }
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(StopAndSaveDiscProjectButtonText));
        OnPropertyChanged(nameof(DeleteNewProjectButtonText));
        OnPropertyChanged(nameof(DeleteNewProjectVisibility));
        CheckAutomaticMaintenance();
        var processing = ExportUiStateService.IsPreparationProcessing(ProjectReviewState.Phase,
            IsExporting, IsDiscImporting, _presentationState.IsPipelinePaused, _presentationState.IsPipelineFailed);
        if (!_wasPreparationProcessing && processing && _isMetadataPanelExpanded)
            SetMetadataPanelExpanded(false, animate: true);
        _wasPreparationProcessing = processing;
        OnPropertyChanged(nameof(CanSelectFolder));
        OnPropertyChanged(nameof(CanStartNewProject));
        OnPropertyChanged(nameof(FolderDiscActionVisualState));
        OnPropertyChanged(nameof(ProjectsActionVisualState));
        OnPropertyChanged(nameof(MetadataActionVisualState));
        OnPropertyChanged(nameof(CanConfigureFfmpeg));
        OnPropertyChanged(nameof(CanEditTrackOrder));
        OnPropertyChanged(nameof(CanStartExport));
        OnPropertyChanged(nameof(ExportButtonText));
        OnPropertyChanged(nameof(ExportButtonVisibility));
        OnPropertyChanged(nameof(SecondaryButtonText));
        OnPropertyChanged(nameof(SecondaryButtonVisibility));
        OnPropertyChanged(nameof(CanCancelExport));
        OnPropertyChanged(nameof(CanDeletePausedNewProject));
        OnPropertyChanged(nameof(CanStopAndSaveDiscProject));
        OnPropertyChanged(nameof(StopAndSaveDiscProjectVisibility));
        OnPropertyChanged(nameof(CanAddProjectSources));
        OnPropertyChanged(nameof(AddProjectSourcesVisibility));
        OnPropertyChanged(nameof(AddProjectSourcesButtonText));
        OnPropertyChanged(nameof(CanChooseOutputFolder));
        OnPropertyChanged(nameof(CanChangeExportOptions));
        OnPropertyChanged(nameof(CanChangeExportPreset));
        OnPropertyChanged(nameof(CanOpenSettings));
        OnPropertyChanged(nameof(DriveCircleVisibility));
        OnPropertyChanged(nameof(DriveCircleEnabled));
        OnPropertyChanged(nameof(CanChangeBookMetadata));
        OnPropertyChanged(nameof(CanRemoveCover));
        OnPropertyChanged(nameof(CoverHintVisibility));
        OnPropertyChanged(nameof(CanRefreshResumeProjects));
        OnPropertyChanged(nameof(CanInspectSelectedResumeProject));
        OnPropertyChanged(nameof(ExportProgressVisibility));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public static class TrackGridChildColumn
{
    public static readonly DependencyProperty KeyProperty = DependencyProperty.RegisterAttached(
        "Key",
        typeof(string),
        typeof(TrackGridChildColumn),
        new FrameworkPropertyMetadata(string.Empty));

    public static readonly DependencyProperty SpansAllColumnsProperty = DependencyProperty.RegisterAttached(
        "SpansAllColumns",
        typeof(bool),
        typeof(TrackGridChildColumn),
        new FrameworkPropertyMetadata(false));

    public static string GetKey(DependencyObject element)
    {
        return (string)element.GetValue(KeyProperty);
    }

    public static void SetKey(DependencyObject element, string value)
    {
        element.SetValue(KeyProperty, value);
    }

    public static bool GetSpansAllColumns(DependencyObject element)
    {
        return (bool)element.GetValue(SpansAllColumnsProperty);
    }

    public static void SetSpansAllColumns(DependencyObject element, bool value)
    {
        element.SetValue(SpansAllColumnsProperty, value);
    }
}
