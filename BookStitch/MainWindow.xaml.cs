using BookStitch.Dialog;
using BookStitch.Models;
using BookStitch.Services;
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
using System.Windows.Threading;
using WinForms = System.Windows.Forms;

namespace BookStitch;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private const string DefaultCoverPreviewSource = "/Assets/Icons/BookStitchLogo-Round.png";

    private static readonly TimeSpan ProjectSnapshotInterval = TimeSpan.FromSeconds(30);

    private readonly FolderScanner _folderScanner = new();
    private readonly EmbeddedCoverService _embeddedCoverService = new();
    private readonly SettingsService _settingsService = new();
    private readonly FfmpegService _ffmpegService = new();
    private readonly AudioInfoService _audioInfoService = new();
    private readonly TrackListActionService _trackListActionService = new();
    private readonly TrackListStateService _trackListStateService = new();
    private readonly TrackListWarningService _trackListWarningService = new();
    private readonly CoverImageService _coverImageService = new();
    private readonly FinalTagService _finalTagService = new();
    private readonly Mp3DiscImportService _mp3DiscImportService = new();
    private readonly Mp3DiscProjectService _mp3DiscProjectService = new();
    private readonly Mp3DiscUiStatusService _mp3DiscUiStatusService = new();
    private readonly Mp3DiscPreparationService _mp3DiscPreparationService = new();
    private readonly Mp3DiscTrackReconciliationService _mp3DiscTrackReconciliationService = new();
    private readonly DiscDriveService _discDriveService = new();
    private readonly DiscDriveConfigurationService _discDriveConfigurationService = new();
    private readonly DiscDriveRotationService _discDriveRotationService = new();
    private readonly DiscDriveCandidateProbeService _discDriveCandidateProbeService;
    private readonly AudioDiscReaderService _audioDiscReaderService = new();
    private readonly AudioDiscProjectService _audioDiscProjectService = new();
    private readonly AudioDiscRipService _audioDiscRipService = new();
    private readonly AudioDiscPipelineTimingService _audioDiscPipelineTimingService = new();
    private readonly AudioDiscLiveConversionService _audioDiscLiveConversionService = new();
    private readonly IDiscWaitDialogService _discWaitDialogService;
    private readonly Mp3DiscPollingService _mp3DiscPollingService;
    private readonly Mp3DiscWaitDialogService _mp3DiscWaitDialogService;
    private readonly AudioDiscPollingService _audioDiscPollingService;
    private readonly BookMetadataService _bookMetadataService = new();
    private readonly ExportValidationService _exportValidationService = new();
    private readonly OutputFolderLayoutService _outputFolderLayoutService = new();
    private readonly WorkManifestService _workManifestService = new();
    private readonly ProjectSnapshotService _projectSnapshotService;
    private readonly ProjectExtensionRollbackService _projectExtensionRollbackService = new();
    private readonly AudioDiscRunPreparationService _audioDiscRunPreparationService;
    private readonly AudioDiscRipWorkflowService _audioDiscRipWorkflowService;
    private readonly AudioDiscRipCompletionWorkflowService _audioDiscRipCompletionWorkflowService;
    private readonly ExportPlanService _exportPlanService = new();
    private readonly ConvertedFileCleanupService _convertedFileCleanupService = new();
    private readonly TrackWorkspaceFilterService _trackWorkspaceFilterService = new();
    private readonly WorkFolderStructureService _workFolderStructureService = new();
    private readonly ProjectIndexService _projectIndexService = new();
    private readonly ProjectResumePlanService _projectResumePlanService = new();
    private readonly TrackPreparedStateRefreshService _trackPreparedStateRefreshService;
    private readonly ProjectResumeLoadWorkflowService _projectResumeLoadWorkflowService;
    private readonly ExportUiStateService _exportUiStateService = new();
    private readonly PausedPipelineContinuationService _pausedPipelineContinuationService = new();
    private readonly ExportChapterService _exportChapterService = new();
    private readonly ExportFailureDetailsService _exportFailureDetailsService = new();
    private readonly WorkflowExportFailureStatusService _workflowExportFailureStatusService = new();
    private readonly FfmpegRunnerService _ffmpegRunnerService = new();
    private readonly AacExportProcessingService _aacExportProcessingService;
    private readonly LocalProjectImportService _localProjectImportService = new();
    private readonly LocalProjectExtensionStateService _localProjectExtensionStateService = new();
    private readonly LocalProjectLivePreparationService _localProjectLivePreparationService;
    private readonly LiveConversionWorkflowService _liveConversionWorkflowService;
    private readonly Mp3DiscPresetPreparationWorkflowService _mp3DiscPresetPreparationWorkflowService;
    private readonly Mp3DiscImportWorkflowService _mp3DiscImportWorkflowService;
    private readonly ExportWorkflowService _exportWorkflowService;
    private readonly ApplicationShutdownService _applicationShutdownService = new();
    private readonly TrackIssueSummaryService _trackIssueSummaryService = new();
    private readonly NotificationService _notificationService;
    private readonly TrackStateUpdateQueueService _trackStateUpdateQueueService;
    private readonly DiagnosticLogService _diagnosticLogService;
    private readonly WorkflowStatusCoordinator _workflowStatusCoordinator = new();
    private readonly WorkflowStatusFormatter _workflowStatusFormatter = new();
    private readonly LocalWorkflowStatusAdapter _localWorkflowStatusAdapter = new();
    private readonly Mp3DiscWorkflowStatusAdapter _mp3DiscWorkflowStatusAdapter = new();
    private readonly AudioDiscWorkflowStatusAdapter _audioDiscWorkflowStatusAdapter = new();

    private AppSettings _settings = new();
    private string? _developerAudioDiscTestDriveRoot;
    private FfmpegToolStatus _ffmpegStatus = new();
    private bool _isLoadingSettings;
    private bool _suppressPresetBitrateWarning;
    private bool _isApplyingExportPresetSelectionToUi;
    private bool _isBusy;
    private bool _isExporting;
    private bool _isDiscImporting;
    private bool _isPipelinePaused;
    private bool _isPipelineFailed;
    private bool _pauseRequested;
    private bool _isProjectExtensionRun;
    private Func<Task>? _pausedProjectExtensionContinuation;
    private Action? _pausedProjectExtensionStopAction;
    private TimeSpan _audioDiscElapsedBeforeCurrentRun;
    private TimeSpan _audioDiscCurrentRunElapsed;
    private int? _audioDiscElapsedDiscNumber;
    private string _audioDiscElapsedProjectFolder = string.Empty;
    private CancellationTokenSource? _exportCancellation;
    private CancellationTokenSource? _discImportCancellation;
    private Guid _workflowStatusOperationId;
    private Mp3DiscProjectManifest? _activeMp3DiscManifest;
    private bool _isShutdownInProgress;
    private bool _allowWindowClose;
    private bool _isApplyingTrackGridColumnLayout;
    private bool _trackGridContextMenuForHeader;
    private bool _isCoverDialogOpen;
    private string _trackGridSortKey = string.Empty;
    private ListSortDirection _trackGridSortDirection = ListSortDirection.Descending;
    private TrackInfo? _lastFileWarningJumpTrack;
    private TrackInfo? _lastChapterWarningJumpTrack;

    private string _selectedFolder = "Noch kein Ordner ausgewählt.";
    private string _selectedSourceDisplayOverride = "";
    private string _selectedSourceOpenPathOverride = "";
    private string _statusText = "Bereit.";
    private string _currentFolderPath = "";
    private string _currentProjectWorkFolder = "";
    private string _pendingDiscProjectSourceFolder = "";
    private DiscProjectSetupResult? _pendingMp3DiscSetupResult;
    private DiscSourceQuickIdentity? _pendingMp3DiscQuickIdentity;
    private string _pendingMp3DiscStructureSignature = "";
    private AudioDiscInfo? _pendingAudioDisc;
    private int _pendingAudioDiscTotalDiscs = 1;
    private bool _isAudioDiscProjectAwaitingRip;
    private AudioDiscProjectManifest? _activeAudioDiscManifest;
    private bool _loadedResumeProjectNeedsDiscImport;
    private bool _loadedResumeProjectIsMp3Disc;
    private bool _loadedResumeProjectIsAudioDisc;
    private bool _loadedResumeProjectIsLocal;
    private int _mp3DiscBackgroundPresetPreparationActive;
    private bool _isWaitingForManualMergeReview;
    private string _manualMergeReviewPreparedPreset = "";
    private bool _manualMergeReviewNeedsReconversion;
    private bool _isCurrentProjectCompleted;
    private ProjectPipelineState _pipelineState = ProjectPipelineState.Preparing;
    private bool? _pauseBeforeMergeOverride;
    private string _outputFolder = "";

    private string _bookTitle = "";
    private string _author = "";
    private string _narrator = "";
    private string _album = "";
    private string _series = "";
    private bool _isSynchronizingTitleAndAlbum;
    private bool _isMetadataPanelExpanded;
    private bool _isMetadataEditingAvailable;
    private string _genre = "Audiobook";
    private string _outputExtension = ".m4a";
    private string _fileNameTemplate = "{Autor} - {Titel}";
    private string _selectedExportPreset = "AAC Stereo 192 kbps";
    private string _parallelJobsInput = "Auto";
    private string _coverSourcePath = "";
    private string _processedCoverPath = "";
    private string _coverPreviewSource = DefaultCoverPreviewSource;
    private double _exportProgressPercent;
    private string _exportProgressForeground = "#16858A";
    private bool _isProgressIndeterminate;
    private const string DiscAnalysisReadingText = "CD wird gelesen …";
    private const string DiscAnalysisSlowText = "Das Laufwerk antwortet langsam. Bitte einen Moment warten …";

    private bool _isDiscSourceAnalysisActive;
    private string _discSourceAnalysisText = DiscAnalysisReadingText;
    private string _exportProgressText = "0,0 % | 0/0 fertig";
    private ProjectIndexItem? _selectedResumeProject;

    public ObservableCollection<TrackInfo> Tracks { get; } = [];
    public ObservableCollection<ProjectIndexItem> ResumeProjects { get; } = [];

    public IReadOnlyList<string> ExportPresets { get; } =
    [
        "AAC Mono 64 kbps",
        "AAC Mono 96 kbps",
        "AAC Stereo 96 kbps",
        "AAC Stereo 128 kbps",
        "AAC Stereo 160 kbps",
        "AAC Stereo 192 kbps",
        "AAC Stereo 256 kbps"
    ];

    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (_isBusy == value)
                return;

            _isBusy = value;
            OnPropertyChanged();
            NotifyExportUiStateChanged();
        }
    }

    public bool IsExporting
    {
        get => _isExporting;
        set
        {
            if (_isExporting == value)
                return;

            _isExporting = value;
            OnPropertyChanged();
            NotifyExportUiStateChanged();
        }
    }

    public bool IsDiscImporting
    {
        get => _isDiscImporting;
        set
        {
            if (_isDiscImporting == value)
                return;

            _isDiscImporting = value;
            OnPropertyChanged();
            NotifyExportUiStateChanged();
        }
    }

    private ExportUiStateSnapshot ExportUiState => _exportUiStateService.Create(new ExportUiStateInput(
        _pipelineState,
        IsBusy,
        _isPipelinePaused,
        _isPipelineFailed,
        _isProjectExtensionRun,
        _manualMergeReviewNeedsReconversion,
        _ffmpegStatus.FfmpegAvailable,
        _loadedResumeProjectNeedsDiscImport,
        _loadedResumeProjectIsMp3Disc,
        _loadedResumeProjectIsAudioDisc,
        _loadedResumeProjectIsLocal,
        Tracks.Count,
        !string.IsNullOrWhiteSpace(OutputFolder),
        ExportProgressPercent,
        SelectedResumeProject is not null));

    public bool CanSelectFolder => ExportUiState.CanSelectFolder;
    public bool CanStartNewProject => CanSelectFolder && _ffmpegStatus.FfmpegAvailable;
    public bool CanConfigureFfmpeg => ExportUiState.CanConfigureFfmpeg;
    public string FolderButtonGlyph => "📁";
    public string DiscButtonGlyph => "💿";
    public Visibility FfmpegSetupButtonVisibility => !_ffmpegStatus.FfmpegAvailable || _settings.ForceShowFfmpegSetupButton
        ? Visibility.Visible
        : Visibility.Collapsed;
    public bool CanEditTrackOrder => ExportUiState.CanEditTrackOrder;
    public bool CanStartExport => ExportUiState.CanStartExport ||
        (_isAudioDiscProjectAwaitingRip &&
         !IsBusy &&
         !IsExporting &&
         !IsDiscImporting &&
         _activeAudioDiscManifest is not null &&
         _ffmpegStatus.FfmpegAvailable);
    public string ExportButtonText => ExportUiState.ExportButtonText;
    public string SecondaryButtonText => ExportUiState.SecondaryButtonText;
    public Visibility SecondaryButtonVisibility => ExportUiState.SecondaryButtonVisibility;
    public bool CanCancelExport => ExportUiState.CanCancelExport;
    public bool CanAddProjectSources => ExportUiState.CanAddProjectSources;
    public Visibility AddProjectSourcesVisibility => ExportUiState.AddProjectSourcesVisibility;
    public string AddProjectSourcesButtonText => ExportUiState.AddProjectSourcesButtonText;
    public bool CanChooseOutputFolder => ExportUiState.CanChooseOutputFolder;
    public bool CanOpenSelectedFolder => Directory.Exists(SelectedSourceOpenPath);
    public bool CanOpenOutputFolder => Directory.Exists(OutputFolder);
    public bool CanChangeExportOptions => ExportUiState.CanChangeExportOptions;
    public bool CanChangeExportPreset => ExportUiState.CanChangeExportPreset;

    public bool MergeAutomaticallyAfterConversion
    {
        get => _settings.MergeAutomaticallyAfterConversion;
        set
        {
            if (_settings.MergeAutomaticallyAfterConversion == value)
                return;

            _settings.MergeAutomaticallyAfterConversion = value;
            OnPropertyChanged();
            SaveSettingsIfReady();
        }
    }

    public bool CanOpenSettings => ExportUiState.CanOpenSettings;
    public bool CanChangeBookMetadata => _isMetadataEditingAvailable && ExportUiState.CanChangeBookMetadata;
    public bool CanRefreshResumeProjects => ExportUiState.CanRefreshResumeProjects;
    public bool CanInspectSelectedResumeProject => ExportUiState.CanInspectSelectedResumeProject;
    public Visibility ExportProgressVisibility => ExportUiState.ExportProgressVisibility;

    public string PipelineStateDebugText => $"Zustand: {_pipelineState}";

    public Visibility PipelineStateDebugVisibility => _settings.ShowDeveloperTab && _settings.ShowPipelineStateDebug
        ? Visibility.Visible
        : Visibility.Collapsed;

    public string ResumeProjectSummary => ResumeProjects.Count == 0
        ? "Keine Projekte im Projektordner"
        : ResumeProjects.Count == 1
            ? "1 Projekt gefunden"
            : $"{ResumeProjects.Count} Projekte gefunden";

    public ProjectIndexItem? SelectedResumeProject
    {
        get => _selectedResumeProject;
        set
        {
            if (ReferenceEquals(_selectedResumeProject, value))
                return;

            _selectedResumeProject = value;
            OnPropertyChanged();
            NotifyExportUiStateChanged();
        }
    }

    public string SelectedFolder
    {
        get => _selectedFolder;
        set
        {
            _selectedFolder = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedSourceDisplayText));
            OnPropertyChanged(nameof(SelectedSourceOpenPath));
            OnPropertyChanged(nameof(CanOpenSelectedFolder));
        }
    }

    public string SelectedSourceDisplayText => !string.IsNullOrWhiteSpace(_selectedSourceDisplayOverride)
        ? _selectedSourceDisplayOverride
        : BuildSelectedSourceDisplayText(SelectedFolder);

    public string SelectedSourceOpenPath => !string.IsNullOrWhiteSpace(_selectedSourceOpenPathOverride)
        ? _selectedSourceOpenPathOverride
        : SelectedFolder;

    public string CurrentProjectTypeGlyph
    {
        get
        {
            if (_loadedResumeProjectIsAudioDisc || _activeAudioDiscManifest is not null)
                return "♪";

            if (_loadedResumeProjectIsMp3Disc || _activeMp3DiscManifest is not null)
                return "◉";

            if (_loadedResumeProjectIsLocal || !string.IsNullOrWhiteSpace(_currentProjectWorkFolder))
                return "▣";

            return string.Empty;
        }
    }

    public Visibility CurrentProjectTypeGlyphVisibility => string.IsNullOrWhiteSpace(CurrentProjectTypeGlyph)
        ? Visibility.Collapsed
        : Visibility.Visible;

    public string StatusText
    {
        get => _statusText;
        set
        {
            _statusText = value;
            OnPropertyChanged();
        }
    }

    public string OutputFolder
    {
        get => _outputFolder;
        set
        {
            _outputFolder = value?.Trim() ?? "";
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanStartExport));
            OnPropertyChanged(nameof(CanOpenOutputFolder));

            _settings.OutputFolder = _outputFolder;
            SaveSettingsIfReady();
        }
    }

    private bool IsTitleAlbumLinked => _settings.KeepAlbumLinkedToTitle;

    public string BookTitle
    {
        get => _bookTitle;
        set
        {
            var previousTitle = _bookTitle;
            _bookTitle = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(MetadataSummaryTitle));
            OnPropertyChanged(nameof(MetadataSummaryDetails));
            OnPropertyChanged(nameof(OutputFileNamePreview));

            if (IsTitleAlbumLinked && !_isSynchronizingTitleAndAlbum)
            {
                _isSynchronizingTitleAndAlbum = true;
                Album = value;
                _isSynchronizingTitleAndAlbum = false;
            }

            OnExportPreviewChanged();
            RefreshActiveAudioDiscPreviewMetadata();
            RefreshActiveMp3DiscPreviewMetadata(previousTitle);
        }
    }

    public string Author
    {
        get => _author;
        set
        {
            _author = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(MetadataSummaryDetails));
            OnPropertyChanged(nameof(OutputFileNamePreview));
            OnExportPreviewChanged();
            RefreshActiveAudioDiscPreviewMetadata();
        }
    }

    public string Narrator
    {
        get => _narrator;
        set
        {
            _narrator = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(MetadataSummaryDetails));
            OnPropertyChanged(nameof(OutputFileNamePreview));
            OnExportPreviewChanged();
        }
    }


    public string Album
    {
        get => _album;
        set
        {
            if (string.Equals(_album, value, StringComparison.Ordinal))
                return;

            _album = value ?? string.Empty;
            OnPropertyChanged();
            OnPropertyChanged(nameof(MetadataSummaryDetails));

            if (IsTitleAlbumLinked && !_isSynchronizingTitleAndAlbum)
            {
                _isSynchronizingTitleAndAlbum = true;
                BookTitle = _album;
                _isSynchronizingTitleAndAlbum = false;
            }
        }
    }

    public bool IsAlbumTabStop => !IsTitleAlbumLinked;

    public string AlbumLinkToggleToolTip => IsTitleAlbumLinked
        ? "Aktuell gekoppelt. Klicken, um Titel und Album getrennt zu bearbeiten."
        : "Aktuell getrennt. Klicken, um Titel und Album automatisch zu koppeln.";

    public string Series
    {
        get => _series;
        set
        {
            if (string.Equals(_series, value, StringComparison.Ordinal))
                return;

            _series = value ?? string.Empty;
            OnPropertyChanged();
            OnPropertyChanged(nameof(MetadataSummaryDetails));
        }
    }

    public string MetadataSummaryTitle => string.IsNullOrWhiteSpace(BookTitle)
        ? "Hörbuch-Metadaten"
        : BookTitle.Trim();

    public string MetadataSummaryDetails
    {
        get
        {
            var parts = new[] { Author, Narrator, Album, Genre }
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(4);
            return string.Join(" · ", parts);
        }
    }

    public string MetadataToggleGlyph => _isMetadataPanelExpanded ? "\uE70E" : "\uE70D";

    public string Genre
    {
        get => _genre;
        set
        {
            var normalized = NormalizeComboBoxValue(value);
            if (GenreListService.IsSeparator(normalized))
                return;

            _genre = normalized;
            OnPropertyChanged();
            OnPropertyChanged(nameof(MetadataSummaryDetails));

            _settings.DefaultGenre = _genre;
            SaveSettingsIfReady();
        }
    }

    public string OutputExtension
    {
        get => _outputExtension;
        set
        {
            _outputExtension = NormalizeComboBoxValue(value);

            if (_outputExtension != ".m4a" && _outputExtension != ".m4b")
                _outputExtension = ".m4a";

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
            _fileNameTemplate = NormalizeComboBoxValue(value);
            OnPropertyChanged();
            OnPropertyChanged(nameof(OutputFileNamePreview));
            OnExportPreviewChanged();

            _settings.DefaultFileNameTemplate = _fileNameTemplate;
            SaveSettingsIfReady();
        }
    }

    public string SelectedExportPreset
    {
        get => _selectedExportPreset;
        set
        {
            var preset = NormalizeComboBoxValue(value);

            if (!ExportPresets.Contains(preset))
                preset = "AAC Stereo 192 kbps";

            _selectedExportPreset = preset;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ExportPreviewCodecSummary));

            RecalculateProcessingActionsForCurrentPreset();
            UpdateManualMergeReviewPresetState();
            if (!_isWaitingForManualMergeReview)
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
        get => _coverPreviewSource;
        set
        {
            _coverPreviewSource = string.IsNullOrWhiteSpace(value)
                ? DefaultCoverPreviewSource
                : value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CoverHintVisibility));
        }
    }

    public Visibility CoverHintVisibility => string.IsNullOrWhiteSpace(_processedCoverPath) && CanChangeBookMetadata
        ? Visibility.Visible
        : Visibility.Collapsed;

    public double ExportProgressPercent
    {
        get => _exportProgressPercent;
        set
        {
            var normalized = Math.Clamp(value, 0, 100);

            if (Math.Abs(_exportProgressPercent - normalized) < 0.05)
                return;

            _exportProgressPercent = normalized;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ExportProgressVisibility));
        }
    }

    public string ExportProgressForeground
    {
        get => _exportProgressForeground;
        private set
        {
            if (string.Equals(_exportProgressForeground, value, StringComparison.OrdinalIgnoreCase))
                return;

            _exportProgressForeground = value;
            OnPropertyChanged();
        }
    }

    public bool IsProgressIndeterminate
    {
        get => _isProgressIndeterminate;
        set
        {
            if (_isProgressIndeterminate == value)
                return;

            _isProgressIndeterminate = value;
            OnPropertyChanged();
        }
    }

    public bool IsDiscSourceAnalysisActive
    {
        get => _isDiscSourceAnalysisActive;
        private set
        {
            if (_isDiscSourceAnalysisActive == value)
                return;

            _isDiscSourceAnalysisActive = value;
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
            if (string.Equals(_discSourceAnalysisText, value, StringComparison.Ordinal))
                return;

            _discSourceAnalysisText = value;
            OnPropertyChanged();
        }
    }

    public Visibility DiscSourceAnalysisVisibility => IsDiscSourceAnalysisActive
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility StandardProgressVisibility => IsDiscSourceAnalysisActive
        ? Visibility.Collapsed
        : Visibility.Visible;

    public string ExportProgressText
    {
        get => _exportProgressText;
        set
        {
            _exportProgressText = value;
            OnPropertyChanged();
        }
    }

    public string ExportPreviewCodecSummary => "Export-Preset:";

    public string OutputFileNamePreview => FileNameTemplateService.BuildOutputFileName(
        BookTitle,
        Author,
        Narrator,
        FileNameTemplate,
        OutputExtension,
        Album,
        Series);

    public string ExportPreviewFileName => Tracks.Count == 0
        ? "Ausgabe: noch keine Ausgabe geplant"
        : $"Ausgabe: {BuildOutputRelativePathPreview()}";

    public string ExportPreviewTrackCount => Tracks.Count == 0
        ? "Tracks: 0"
        : Tracks.Count == 1
            ? "Tracks: 1"
            : $"Tracks: {Tracks.Count}";

    public string ExportPreviewTotalDuration
    {
        get
        {
            if (Tracks.Count == 0)
                return "Gesamtdauer: 00:00:00";

            var durations = Tracks
                .Select(GetPreciseDuration)
                .Where(duration => duration.HasValue)
                .Select(duration => duration!.Value)
                .ToList();

            if (durations.Count == 0)
                return "Gesamtdauer: noch nicht bekannt";

            var total = TimeSpan.FromSeconds(durations.Sum(duration => duration.TotalSeconds));
            var totalText = FormatDuration(total);

            if (durations.Count == Tracks.Count)
                return $"Gesamtdauer: {totalText}";

            return $"Gesamtdauer: {totalText} ({durations.Count}/{Tracks.Count} bekannt)";
        }
    }

    public string ExportPreviewActionSummary => "Aktion: " + BuildProcessingActionSummary();

    public string ExportPreviewChapterSummary
    {
        get
        {
            var chapterCount = CountPlannedChapters();
            return chapterCount == 0
                ? "Kapitel: 0"
                : chapterCount == 1
                    ? "Kapitel: 1 geplant"
                    : $"Kapitel: {chapterCount} geplant";
        }
    }

    private int CountPlannedChapters() => Tracks
        .Where(track => !track.IsExcluded)
        .Sum(track => track.EmbeddedChapters.Count > 0 ? track.EmbeddedChapters.Count : 1);

    public string ExportPreviewIssueSummary => _trackIssueSummaryService
        .Create(Tracks)
        .ToDisplayText();

    private int TrackFileWarningCount => Tracks.Count(track => !string.IsNullOrWhiteSpace(track.DisplayFileWarning));

    private int TrackChapterWarningCount => Tracks.Count(track => !string.IsNullOrWhiteSpace(track.DisplayChapterWarning));

    public string TrackFileWarningSummary
    {
        get
        {
            var count = TrackFileWarningCount;
            return count == 0
                ? string.Empty
                : $"⛔ {count} {Pluralize(count, "Dateiwarnung", "Dateiwarnungen")}";
        }
    }

    public string TrackChapterWarningSummary
    {
        get
        {
            var count = TrackChapterWarningCount;
            return count == 0
                ? string.Empty
                : $"⚠️ {count} {Pluralize(count, "Kapitelwarnung", "Kapitelwarnungen")}";
        }
    }

    public Visibility TrackFileWarningSummaryVisibility => TrackFileWarningCount == 0
        ? Visibility.Collapsed
        : Visibility.Visible;

    public Visibility TrackChapterWarningSummaryVisibility => TrackChapterWarningCount == 0
        ? Visibility.Collapsed
        : Visibility.Visible;

    public Visibility TrackWarningSummarySeparatorVisibility => TrackFileWarningCount > 0 && TrackChapterWarningCount > 0
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility TrackWarningSummaryVisibility => TrackFileWarningCount > 0 || TrackChapterWarningCount > 0
        ? Visibility.Visible
        : Visibility.Collapsed;

    public MainWindow()
    {
        _aacExportProcessingService = new AacExportProcessingService(_ffmpegRunnerService);
        _localProjectLivePreparationService = new LocalProjectLivePreparationService(_localProjectImportService);
        _liveConversionWorkflowService = new LiveConversionWorkflowService(
            _aacExportProcessingService,
            () => _ffmpegStatus.FfmpegPath ?? string.Empty);
        _mp3DiscPresetPreparationWorkflowService = new Mp3DiscPresetPreparationWorkflowService(
            _mp3DiscPreparationService,
            _aacExportProcessingService,
            () => _ffmpegStatus.FfmpegPath ?? string.Empty);
        _mp3DiscImportWorkflowService = new Mp3DiscImportWorkflowService(
            _mp3DiscImportService,
            _mp3DiscProjectService,
            _discDriveService);
        _projectSnapshotService = new ProjectSnapshotService(_mp3DiscProjectService, _audioDiscProjectService, _workManifestService);
        _audioDiscRunPreparationService = new AudioDiscRunPreparationService(_projectSnapshotService);
        _audioDiscRipWorkflowService = new AudioDiscRipWorkflowService(
            _audioDiscProjectService,
            _audioDiscPipelineTimingService);
        _audioDiscRipCompletionWorkflowService = new AudioDiscRipCompletionWorkflowService(
            _audioDiscProjectService);
        _projectResumeLoadWorkflowService = new ProjectResumeLoadWorkflowService(
            _audioDiscProjectService,
            _trackListStateService);
        _discWaitDialogService = new SwitchableDiscWaitDialogService(() => _settings.UseBoxedDiscWaitDialog);
        _mp3DiscPollingService = new Mp3DiscPollingService(_mp3DiscImportService, _discDriveService);
        _mp3DiscWaitDialogService = new Mp3DiscWaitDialogService(_mp3DiscPollingService, _discWaitDialogService);
        _discDriveCandidateProbeService = new DiscDriveCandidateProbeService(_discDriveService);
        _audioDiscPollingService = new AudioDiscPollingService(_audioDiscReaderService, _discDriveService);
        _exportWorkflowService = new ExportWorkflowService(
            _workManifestService,
            _projectSnapshotService,
            _aacExportProcessingService,
            _convertedFileCleanupService,
            new ConvertedTrackPreparationPlanService(
                new ConvertedTrackReconciliationService(_workManifestService)),
            new ConvertedTrackPreparationWorkflowService(
                _workManifestService,
                _aacExportProcessingService),
            _exportChapterService,
            _finalTagService,
            new FinalOutputStorageService());
        _trackPreparedStateRefreshService = new TrackPreparedStateRefreshService(_projectResumePlanService);

        InitializeComponent();
        AddHandler(MouseRightButtonUpEvent, new MouseButtonEventHandler(MainWindow_MouseRightButtonUp), true);

        ConfigureTrackGridColumnLayoutHandlers();

        _trackStateUpdateQueueService = new TrackStateUpdateQueueService(
            () => new TrackStateUpdateContext(
                Tracks,
                _currentProjectWorkFolder,
                SelectedExportPreset,
                IsBusy || IsExporting || IsDiscImporting,
                IsDiscImporting),
            () =>
            {
                TracksGrid.Items.Refresh();
                if (IsBusy || IsExporting || IsDiscImporting)
                    OnExportPreviewChanged();
                else
                    UpdateFinalStatus(Tracks.Count);
            },
            isEnabled: true);

        Tracks.CollectionChanged += Tracks_CollectionChanged;
        Closing += MainWindow_Closing;

        LoadSettings();
        _diagnosticLogService = new DiagnosticLogService(GetProjectFolderStructure().ProjectRootFolder);
        _diagnosticLogService.WriteApplicationEvent("MAIN WINDOW", "Das Hauptfenster wurde initialisiert.");
        _notificationService = new NotificationService(
            new SoundNotificationService(() => _settings),
            new WindowAttentionService(() => _settings, () => this));
        DetectFfmpegTools();
        RefreshResumeProjects(showStatus: false);

        DataContext = this;
        SetMetadataPanelExpanded(false, animate: false);
        SetMetadataEditingAvailable(false);
        _trackStateUpdateQueueService.Start();
    }

    private void SetMetadataEditingAvailable(bool available)
    {
        if (_isMetadataEditingAvailable == available)
            return;

        _isMetadataEditingAvailable = available;
        OnPropertyChanged(nameof(CanChangeBookMetadata));
        OnPropertyChanged(nameof(CoverHintVisibility));
    }

    private void Tracks_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        NotifyExportUiStateChanged();
        OnExportPreviewChanged();
        _trackStateUpdateQueueService.RequestRefresh();
    }

    private void LoadSettings()
    {
        _isLoadingSettings = true;

        _settings = _settingsService.Load();

        Genre = GenreListService.IsSelectableGenre(_settings.DefaultGenre)
            ? _settings.DefaultGenre
            : GenreListService.GetDefaultGenre(_settings.UsePrivateGenreList);

        OutputExtension = _settings.DefaultOutputExtension is ".m4a" or ".m4b"
            ? _settings.DefaultOutputExtension
            : ".m4a";

        FileNameTemplate = string.IsNullOrWhiteSpace(_settings.DefaultFileNameTemplate)
            ? "{Autor} - {Titel}"
            : _settings.DefaultFileNameTemplate;

        SelectedExportPreset = ExportPresets.Contains(_settings.SelectedExportPreset)
            ? _settings.SelectedExportPreset
            : "AAC Stereo 192 kbps";

        SetParallelJobsInput(_settings.SelectedParallelJobs, showMessage: false);

        if (string.IsNullOrWhiteSpace(_settings.WorkingFolder) || IsLegacyDefaultWorkingFolder(_settings.WorkingFolder))
            _settings.WorkingFolder = GetDefaultWorkingFolder();

        _settings.ProjectRetentionDays = ProjectIndexService.NormalizeRetentionDays(_settings.ProjectRetentionDays);
        _settings.DeleteProjectsOlderThanDays = ProjectIndexService.NormalizeDeleteOlderThanDays(_settings.DeleteProjectsOlderThanDays);
        _settings.OutputFolderLayout = OutputFolderLayoutService.NormalizeLayout(_settings.OutputFolderLayout);
        _settings.SoundProfile = SoundSettingsService.NormalizeProfile(_settings.SoundProfile).ToString();
        _settings.FocusProfile = FocusSettingsService.NormalizeProfile(_settings.FocusProfile).ToString();
        _settings.SoundVolumePercent = SoundSettingsService.NormalizeVolumePercent(_settings.SoundVolumePercent);
        if (_settings.ShowPipelineStateDebug || _settings.ForceShowFfmpegSetupButton)
            _settings.ShowDeveloperTab = true;

        if (!string.IsNullOrWhiteSpace(_settings.OutputFolder) && Directory.Exists(_settings.OutputFolder))
            OutputFolder = _settings.OutputFolder;

        if (!string.IsNullOrWhiteSpace(_settings.LastSelectedFolder))
            SelectedFolder = _settings.LastSelectedFolder;

        ApplyTrackGridColumnLayout();
        _isLoadingSettings = false;
    }

    private void DetectFfmpegTools()
    {
        _ffmpegStatus = _ffmpegService.DetectTools(_settings);
        ApplyFfmpegStatusToSettingsAndUi();
    }

    private void ApplyFfmpegStatusToSettingsAndUi()
    {
        if (_ffmpegStatus.FfmpegAvailable)
            _settings.FfmpegPath = _ffmpegStatus.FfmpegPath;

        if (_ffmpegStatus.FfprobeAvailable)
            _settings.FfprobePath = _ffmpegStatus.FfprobePath;

        SaveSettingsIfReady();

        // Die globale Werkzeugerkennung steuert Button und Sperrprofil.
        // Sie überschreibt nicht den fachlichen Projektstatus im Hauptfenster.
        if (StatusText.StartsWith("FFmpeg ", StringComparison.OrdinalIgnoreCase))
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

        _settingsService.Save(_settings);
    }

    private async void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (_allowWindowClose)
        {
            DeleteOldProjects(showUserMessage: false);
            _trackStateUpdateQueueService.Dispose();
            _notificationService.Dispose();
            return;
        }

        var activity = _applicationShutdownService.GetActiveActivity(
            IsDiscImporting,
            IsDiscImporting && _activeAudioDiscManifest is not null,
            IsExporting,
            IsBusy,
            _isPipelinePaused,
            _activeAudioDiscManifest is not null,
            _activeMp3DiscManifest is not null);

        if (activity == ApplicationActivity.None)
        {
            DeleteOldProjects(showUserMessage: false);
            _trackStateUpdateQueueService.Dispose();
            _notificationService.Dispose();
            return;
        }

        e.Cancel = true;

        if (_isShutdownInProgress)
            return;

        var prompt = _applicationShutdownService.CreatePrompt(activity);
        var result = AppDialogService.Show(
            this,
            title: "BookStitch schließen",
            heading: prompt.Heading,
            message: prompt.Message,
            kind: AppDialogKind.Warning,
            buttons: new[]
            {
                new AppDialogButton("Weiterarbeiten", AppDialogResult.Cancel, IsDefault: true, IsCancel: true),
                new AppDialogButton("Abbrechen und schließen", AppDialogResult.Yes, IsDanger: true)
            });

        if (result != AppDialogResult.Yes)
            return;

        _isShutdownInProgress = true;
        StatusText = prompt.ProgressText;
        ExportProgressText = "Laufender Vorgang wird kontrolliert beendet …";
        NotifyExportUiStateChanged();

        TrySaveActiveProjectSnapshot();
        RequestActiveOperationCancellation();

        var stoppedCleanly = await _applicationShutdownService.WaitForIdleAsync(
            () => IsDiscImporting || IsExporting || IsBusy,
            TimeSpan.FromSeconds(30));

        TrySaveActiveProjectSnapshot();

        if (stoppedCleanly)
        {
            CleanupCurrentProjectPartFiles();
        }
        else
        {
            StatusText = "BookStitch wird beendet. Verbleibende temporäre Dateien werden beim nächsten Start bereinigt.";
        }

        _allowWindowClose = true;
        Close();
    }

    public void PrepareForSessionEnding()
    {
        if (_allowWindowClose)
            return;

        _isShutdownInProgress = true;
        _allowWindowClose = true;
        TrySaveActiveProjectSnapshot();
        RequestActiveOperationCancellation();
    }

    private void RequestActiveOperationCancellation()
    {
        if (_discImportCancellation is { IsCancellationRequested: false })
            _discImportCancellation.Cancel();

        if (_exportCancellation is { IsCancellationRequested: false })
            _exportCancellation.Cancel();
    }

    private async Task RequestPipelinePauseAsync()
    {
        if (_pipelineState is not (ProjectPipelineState.AcquiringSources or ProjectPipelineState.Converting))
            return;

        _pauseRequested = true;
        StatusText = "Pause wird vorbereitet …";
        ExportProgressText = "Laufende Vorgänge werden kontrolliert beendet …";

        var discCancellation = IsDiscImporting ? _discImportCancellation : null;
        var exportCancellation = IsExporting ? _exportCancellation : null;

        if (discCancellation is not null && !discCancellation.IsCancellationRequested)
            await discCancellation.CancelAsync();

        if (exportCancellation is not null && !exportCancellation.IsCancellationRequested)
            await exportCancellation.CancelAsync();
    }

    private void TrySaveActiveProjectSnapshot()
    {
        if (_activeMp3DiscManifest is not null)
            TrySaveCurrentMp3DiscProjectSnapshot(_activeMp3DiscManifest);

        if (_activeAudioDiscManifest is not null)
            TrySaveCurrentAudioDiscProjectSnapshot(_activeAudioDiscManifest, force: true);
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
            _convertedFileCleanupService.DeletePartFiles(_currentProjectWorkFolder);
        }
        catch
        {
            // Beim nächsten Start beziehungsweise Export werden verbliebene .part-Dateien erneut bereinigt.
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

        RefreshResumeProjects(showStatus: false);

        var dialog = new ResumeProjectDialog(
            GetWorkingRootFolder(),
            _projectIndexService,
            _projectResumePlanService,
            _settings.ProjectRetentionDays,
            SelectedResumeProject)
        {
            Owner = this
        };

        var result = dialog.ShowDialog();

        if (dialog.SelectedProject is not null)
            SelectedResumeProject = dialog.SelectedProject;

        if (result == true && dialog.SelectedResumePlan is not null)
            await LoadResumeProjectIntoWorkspaceAsync(dialog.SelectedResumePlan);
    }

    private void SetPipelineState(ProjectPipelineState state)
    {
        if (_pipelineState == state)
            return;

        _pipelineState = state;
        if (state is ProjectPipelineState.Preparing or ProjectPipelineState.ReviewBeforeMerge or ProjectPipelineState.Merging or ProjectPipelineState.Completed)
        {
            _isPipelinePaused = false;
            _pauseRequested = false;
        }
        OnPropertyChanged(nameof(PipelineStateDebugText));
        PersistDiscPipelineState(state);
        NotifyExportUiStateChanged();
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
            if (mp3Manifest is null && _loadedResumeProjectIsMp3Disc &&
                !string.IsNullOrWhiteSpace(_currentProjectWorkFolder))
            {
                mp3Manifest = _mp3DiscProjectService.TryLoad(_currentProjectWorkFolder);
            }

            if (mp3Manifest is not null)
            {
                mp3Manifest.PipelineState = state.ToManifestValue();
                _mp3DiscProjectService.Save(mp3Manifest);
            }
        }
        catch
        {
            // Der zentrale UI-Zustand darf nicht an einer zusätzlichen Manifest-Synchronisierung scheitern.
        }
    }

    private void ResetLoadedResumeProjectState()
    {
        _loadedResumeProjectNeedsDiscImport = false;
        _loadedResumeProjectIsMp3Disc = false;
        _loadedResumeProjectIsAudioDisc = false;
        _loadedResumeProjectIsLocal = false;
        _activeAudioDiscManifest = null;
        _pendingAudioDisc = null;
        _isAudioDiscProjectAwaitingRip = false;
        SetPipelineState(ProjectPipelineState.Preparing);
        ResetManualMergeReviewState();
        NotifyExportUiStateChanged();
    }

    private void ResetManualMergeReviewState()
    {
        var hadManualMergeReviewState = _isWaitingForManualMergeReview ||
                                        _manualMergeReviewNeedsReconversion ||
                                        !string.IsNullOrWhiteSpace(_manualMergeReviewPreparedPreset);

        _isWaitingForManualMergeReview = false;
        _manualMergeReviewPreparedPreset = "";
        _manualMergeReviewNeedsReconversion = false;
        _isCurrentProjectCompleted = false;

        if (!hadManualMergeReviewState)
            return;

        SetTrackActionDisplayOverride("");
        NotifyExportUiStateChanged();
    }

    private void UpdateManualMergeReviewPresetState(bool forceRefresh = false, bool updateStatusText = true)
    {
        if (!_isWaitingForManualMergeReview)
            return;

        var includedTracks = Tracks.Where(track => !track.IsExcluded).ToList();
        var hasCompletePreparedPreset = includedTracks.Count > 0 &&
                                        includedTracks.All(track =>
                                            _trackPreparedStateRefreshService.IsReusablePreparedConvertedTrack(track, _selectedExportPreset));
        var needsReconversion = !hasCompletePreparedPreset;

        if (!forceRefresh && _manualMergeReviewNeedsReconversion == needsReconversion)
            return;

        _manualMergeReviewNeedsReconversion = needsReconversion;
        SetTrackActionDisplayOverride(needsReconversion ? "Neu konvertieren" : "Zusammenfügen");
        NotifyExportUiStateChanged();

        if (!updateStatusText)
            return;

        if (needsReconversion && (_loadedResumeProjectIsLocal || _loadedResumeProjectIsMp3Disc))
        {
            var preset = ExportPreset.Parse(SelectedExportPreset);
            var total = includedTracks.Count;
            var projectKind = _loadedResumeProjectIsMp3Disc
                ? WorkflowProjectKind.Mp3Disc
                : WorkflowProjectKind.Folder;
            var totalDiscs = _loadedResumeProjectIsMp3Disc
                ? Math.Max(1, includedTracks.Select(track => track.DiscNumber ?? 1).DefaultIfEmpty(1).Max())
                : 0;

            ApplyWorkflowStatusViewState(_workflowStatusFormatter.Format(new WorkflowStatusSnapshot
            {
                ProjectId = _currentProjectWorkFolder,
                ProjectKind = projectKind,
                ProjectState = _pipelineState,
                IsPresetChangePending = true,
                TotalSourceItems = total,
                TotalChapters = total,
                SourceProgress = _loadedResumeProjectIsMp3Disc
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
                    0,
                    total,
                    0,
                    Array.Empty<int>(),
                    preset.BitrateKbps,
                    preset.Channels == 1,
                    IsLive: false)
            }));
        }
        else if (needsReconversion)
        {
            StatusText = "Das Export-Preset wurde geändert. Die Tracks müssen für das neue Preset neu konvertiert werden.";
            ExportProgressText = "Neues Preset gewählt. Klicke auf „Neu konvertieren“ und prüfe die Tracks danach erneut.";
        }
        else
        {
            StatusText = "Die Tracks sind für das gewählte Preset vorbereitet und können zusammengefügt werden.";
            ExportProgressText = "Konvertierung abgeschlossen. Trackliste prüfen und anschließend zusammenfügen.";
        }
    }

    private void SetTrackActionDisplayOverride(string value)
    {
        foreach (var track in Tracks)
            track.DisplayProcessingActionOverride = track.IsExcluded ? "Ausgeschlossen" : value;

        TracksGrid?.Items.Refresh();
    }

    private async Task LoadResumeProjectIntoWorkspaceAsync(ProjectResumePlan plan)
    {
        IsBusy = true;

        try
        {
            var loadResult = _projectResumeLoadWorkflowService.Prepare(plan);
            if (!loadResult.Success)
            {
                AppDialogService.Error(
                    this,
                    "Audio-CD-Projekt konnte nicht geladen werden",
                    loadResult.ErrorMessage);
                ResetLoadedResumeProjectState();
                _currentProjectWorkFolder = string.Empty;
                _currentFolderPath = string.Empty;
                return;
            }

            _currentProjectWorkFolder = plan.ProjectFolder;
            _pendingDiscProjectSourceFolder = string.Empty;
            _pendingAudioDisc = null;
            _loadedResumeProjectIsMp3Disc = loadResult.IsMp3DiscProject;
            _loadedResumeProjectIsAudioDisc = loadResult.IsAudioDiscProject;
            _loadedResumeProjectIsLocal = !loadResult.IsMp3DiscProject && !loadResult.IsAudioDiscProject;
            _loadedResumeProjectNeedsDiscImport = loadResult.NeedsMp3DiscImport;
            _activeAudioDiscManifest = loadResult.AudioDiscManifest;
            _isAudioDiscProjectAwaitingRip = loadResult.IsAudioDiscProjectAwaitingRip;
            _isWaitingForManualMergeReview = loadResult.IsWaitingForManualMergeReview;
            _isCurrentProjectCompleted = loadResult.IsCompletedProject;
            SetPipelineState(loadResult.PipelineState);
            SetMetadataEditingAvailable(true);
            _currentFolderPath = loadResult.CurrentFolderPath;
            SelectedFolder = loadResult.SelectedFolder;
            SetSelectedSourceDisplayOverride(
                SelectedResumeProject?.ListDisplayName ?? plan.DisplayName,
                loadResult.SelectedFolder);
            if (loadResult.IsAudioDiscProject)
                SetAudioDiscSourceDisplayOverride(loadResult.AudioDiscManifest);

            if (!string.IsNullOrWhiteSpace(plan.OutputFolder))
                OutputFolder = plan.OutputFolder;

            if (!string.IsNullOrWhiteSpace(plan.OutputExtension))
                OutputExtension = plan.OutputExtension;

            if (!string.IsNullOrWhiteSpace(plan.FileNameTemplate))
                FileNameTemplate = plan.FileNameTemplate;

            if (!string.IsNullOrWhiteSpace(plan.SelectedPreset))
                SetSelectedExportPresetSilently(plan.SelectedPreset);

            if (!string.IsNullOrWhiteSpace(plan.ParallelJobs))
                SetParallelJobsInput(plan.ParallelJobs, showMessage: false);

            ApplyLoadedTitleAndAlbum(plan.BookTitle, plan.Album);
            Author = plan.Author;
            Narrator = plan.Narrator;

            if (!string.IsNullOrWhiteSpace(plan.Genre))
                Genre = plan.Genre;

            LoadResumeCover(plan);
            Tracks.Clear();
            foreach (var track in loadResult.Tracks)
                Tracks.Add(track);
            UpdateIndexes();

            if (_ffmpegStatus.IsComplete)
                await EnrichResumeTracksWithFfprobeAsync();

            RecalculateProcessingActionsForCurrentPreset();
            UpdateIndexes();
            UpdateFinalStatus(Tracks.Count);
            ExportProgressPercent = 0;
            ExportProgressText = string.IsNullOrWhiteSpace(loadResult.ProgressText)
                ? BuildIdleExportProgressText()
                : loadResult.ProgressText;
            StatusText = loadResult.StatusText;
            UpdateManualMergeReviewPresetState(forceRefresh: true, updateStatusText: false);
            PublishLoadedProjectStatus(loadResult);

            NotifyExportUiStateChanged();
            OnPropertyChanged(nameof(CoverHintVisibility));
            OnExportPreviewChanged();
            AutoFitTrackColumnsAfterRender();
            RefreshResumeProjects(showStatus: false);
        }
        finally
        {
            IsBusy = false;
            NotifyExportUiStateChanged();
        }
    }

    private void ApplyLoadedTitleAndAlbum(string? title, string? album)
    {
        var state = TitleAlbumLoadPolicy.Resolve(title, album, _settings.KeepAlbumLinkedToTitle);
        _isSynchronizingTitleAndAlbum = true;
        BookTitle = state.Title;
        Album = state.Album;
        _isSynchronizingTitleAndAlbum = false;

        OnPropertyChanged(nameof(AlbumLinkToggleToolTip));
        OnPropertyChanged(nameof(IsAlbumTabStop));
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
        _coverSourcePath = plan.CoverSourcePath ?? "";
        _processedCoverPath = plan.ProcessedCoverPath ?? "";

        if (!string.IsNullOrWhiteSpace(_processedCoverPath) && File.Exists(_processedCoverPath))
        {
            CoverPreviewSource = _processedCoverPath;
            return;
        }

        if (!string.IsNullOrWhiteSpace(_coverSourcePath) && File.Exists(_coverSourcePath))
        {
            try
            {
                SetCoverFromFile(_coverSourcePath);
                return;
            }
            catch
            {
                // Wenn das alte Cover nicht mehr geladen werden kann, bleibt das Projekt trotzdem nutzbar.
            }
        }

        _coverSourcePath = "";
        _processedCoverPath = "";
        CoverPreviewSource = "";
    }

    private void LoadResumeTracks(ProjectResumePlan plan)
    {
        var loadResult = _projectResumeLoadWorkflowService.Prepare(plan);
        if (!loadResult.Success)
            return;

        Tracks.Clear();
        foreach (var track in loadResult.Tracks)
            Tracks.Add(track);
        UpdateIndexes();
    }

    private void RefreshLoadedProjectTrackStateFromPersistedProject(string projectFolder)
    {
        var statusText = StatusText;
        var progressText = ExportProgressText;
        var plan = _projectResumePlanService.BuildFromProjectFolder(projectFolder);
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
        TracksGrid.Items.Refresh();
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

    private async Task EnrichResumeTracksWithFfprobeAsync()
    {
        if (string.IsNullOrWhiteSpace(_ffmpegStatus.FfprobePath))
            return;

        var trackSnapshot = Tracks.ToList();
        var total = trackSnapshot.Count;
        var processed = 0;
        var failed = 0;
        var preset = ExportPreset.Parse(SelectedExportPreset);

        foreach (var track in trackSnapshot)
        {
            processed++;
            StatusText = $"Projekt wird geladen. Technische Audiodaten werden geprüft … {processed}/{total}";

            var trackPath = !string.IsNullOrWhiteSpace(track.FilePath)
                ? track.FilePath
                : TrackPathService.GetTrackPath(_currentFolderPath, track);

            if (string.IsNullOrWhiteSpace(trackPath) || !File.Exists(trackPath))
            {
                if (_isAudioDiscProjectAwaitingRip &&
                    string.Equals(track.ProcessingAction, "FLAC rippen", StringComparison.OrdinalIgnoreCase))
                {
                    track.Warning = string.Empty;
                }

                continue;
            }

            var probeInfo = await _audioInfoService.ProbeAsync(trackPath, _ffmpegStatus.FfprobePath);

            if (!probeInfo.Success)
            {
                failed++;
                if (string.IsNullOrWhiteSpace(track.Warning))
                    SetTrackValue(track, "Warning", "Audiodaten konnten nicht geprüft werden");

                if (processed % 10 == 0 || processed == total)
                    OnExportPreviewChanged();

                continue;
            }

            SetTrackDuration(track, probeInfo.Duration);
            track.EmbeddedChapters = probeInfo.Chapters.ToList();
            SetTrackValue(track, "BitrateKbps", probeInfo.BitrateKbps);
            SetTrackValue(track, "Channels", probeInfo.Channels);
            SetTrackValue(track, "ChannelLayout", AudioProcessingService.FormatChannelLayout(probeInfo.Channels));
            SetTrackValue(track, "Codec", AudioProcessingService.NormalizeCodecName(probeInfo.CodecName));
            SetTrackValue(track, "ProcessingAction", AudioProcessingService.DetermineProcessingAction(probeInfo, preset));

            if (processed % 10 == 0 || processed == total)
                OnExportPreviewChanged();
        }

        _trackListWarningService.Apply(Tracks);
        TracksGrid.Items.Refresh();
        OnExportPreviewChanged();

        if (failed > 0)
        {
            StatusText = failed == 1
                ? "Projekt geladen. 1 Datei konnte nicht vollständig geprüft werden."
                : $"Projekt geladen. {failed} Dateien konnten nicht vollständig geprüft werden.";
        }
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

        var plan = _projectResumePlanService.BuildFromProjectFolder(SelectedResumeProject.ProjectFolder);
        if (plan is null)
        {
            AppDialogService.Warning(
                this,
                "Projekt nicht gelesen",
                "Das ausgewählte Projekt konnte nicht mehr gelesen werden. Aktualisiere die Projektliste und prüfe den Projektordner.");
            RefreshResumeProjects(showStatus: true);
            return;
        }

        var details = new List<string>
        {
            "Projekt: " + plan.DisplayName,
            "Typ: " + plan.ProjectType,
            "Status: " + plan.Status,
            "Tracks im Plan: " + plan.Tracks.Count.ToString(CultureInfo.InvariantCulture),
            "Trackliste bearbeitbar: " + (plan.CanEditTrackOrder ? "Ja" : "Nein"),
            "Disc-Import fortsetzbar: " + (plan.CanContinueDiscImport ? "Ja" : "Nein")
        };

        if (plan.NextMissingDiscNumber.HasValue)
            details.Add($"Nächste fehlende CD: {plan.NextMissingDiscNumber.Value} von {plan.TotalDiscs}");

        if (!string.IsNullOrWhiteSpace(plan.SourceFolder))
            details.Add("Quelle: " + plan.SourceFolder);

        if (!string.IsNullOrWhiteSpace(plan.OutputFolder))
            details.Add("Ausgabe: " + plan.OutputFolder);

        AppDialogService.Show(
            this,
            title: "Resume-Projekt erkannt",
            heading: "Resume-Projekt erkannt",
            message: "BookStitch kann dieses Projekt grundsätzlich wieder öffnen. Das tatsächliche Laden in die Oberfläche kommt im nächsten Schritt.",
            kind: AppDialogKind.Information,
            details: details,
            buttons: new[]
            {
                new AppDialogButton("OK", AppDialogResult.Ok, IsPrimary: true, IsDefault: true, IsCancel: true)
            });
    }

    private void RefreshResumeProjects(bool showStatus)
    {
        var selectedProjectFolder = SelectedResumeProject?.ProjectFolder ?? "";

        ResumeProjects.Clear();

        foreach (var project in _projectIndexService.ScanSelectableProjects(
                     GetWorkingRootFolder(),
                     _settings.ProjectRetentionDays))
        {
            ResumeProjects.Add(project);
        }

        SelectedResumeProject = ResumeProjects.FirstOrDefault(project =>
            string.Equals(project.ProjectFolder, selectedProjectFolder, StringComparison.OrdinalIgnoreCase))
            ?? ResumeProjects.FirstOrDefault();

        OnPropertyChanged(nameof(ResumeProjectSummary));
        OnPropertyChanged(nameof(CanInspectSelectedResumeProject));

        if (showStatus)
            StatusText = ResumeProjects.Count == 0
                ? "Keine vollständigen Projekte gefunden."
                : $"{ResumeProjects.Count} vollständige Projekt(e) gefunden.";
    }


    private void RefreshActiveMp3DiscPreviewMetadata(string? previousTitle)
    {
        var manifest = _activeMp3DiscManifest;
        if (manifest is null)
            return;

        _projectSnapshotService.UpdateMp3DiscProjectSnapshot(manifest, CreateProjectSnapshotFromUi());

        if (_trackListActionService.UpdateGeneratedChapterTitles(
                Tracks,
                previousTitle,
                BookTitle,
                _settings.UseLeadingZerosInChapterSuggestions) == 0)
            return;

        TracksGrid.Items.Refresh();
        PersistTrackListState();
    }

    private void RefreshActiveAudioDiscPreviewMetadata()
    {
        var manifest = _activeAudioDiscManifest;
        if (manifest is null)
            return;

        _projectSnapshotService.UpdateAudioDiscProjectSnapshot(manifest, CreateProjectSnapshotFromUi());

        var manifestTracks = manifest.Discs
            .SelectMany(disc => disc.Tracks)
            .ToDictionary(track => track.GlobalIndex);
        foreach (var track in Tracks)
        {
            if (!manifestTracks.TryGetValue(track.Index, out var manifestTrack))
                continue;

            track.Artist = manifest.Author;
            track.ChapterTitle = manifestTrack.ChapterTitle;
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
            OutputFolder,
            FileNameTemplate,
            BookTitle,
            Author,
            Album,
            Narrator,
            Genre,
            _coverSourcePath,
            _processedCoverPath,
            OutputFileNamePreview);
    }

    private void SaveCurrentMp3DiscProjectSnapshot(Mp3DiscProjectManifest manifest)
    {
        _projectSnapshotService.SaveMp3DiscProjectSnapshot(manifest, CreateProjectSnapshotFromUi());
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
        return _projectSnapshotService.SaveAudioDiscProjectSnapshot(
            manifest,
            CreateProjectSnapshotFromUi(),
            force);
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
        _loadedResumeProjectNeedsDiscImport = true;
        EnterPipelinePause(progressText, preserveWorkflowStatus);
    }


    private void CancelMp3DiscImportAndShowResumeDialog(
        Mp3DiscProjectManifest manifest,
        string progressText,
        bool cleanupPartFiles = false,
        bool preserveWorkflowStatus = false)
    {
        TrySaveCurrentMp3DiscProjectSnapshot(manifest);

        if (cleanupPartFiles)
        {
            _convertedFileCleanupService.DeletePartFiles(manifest.ProjectFolder);
        }

        RefreshLoadedProjectTrackStateFromPersistedProject(manifest.ProjectFolder);
        MarkMp3DiscImportCanceled(progressText, preserveWorkflowStatus);
    }

    private void ShowUserCanceledProjectDialog()
    {
        if (_isShutdownInProgress)
            return;

        AppDialogService.Info(
            this,
            "Projekt abgebrochen",
            "Der Vorgang wurde durch den Benutzer abgebrochen.\n\n" +
            "Bereits vorbereitete Dateien bleiben erhalten.\n" +
            "Das Projekt kann über „Projekte“ fortgesetzt werden.",
            title: "Projekt abgebrochen");
    }

    private Mp3DiscProjectManifest? TryLoadCurrentMp3DiscProjectManifest(bool resetStateWhenFolderMissing)
    {
        if (string.IsNullOrWhiteSpace(_currentProjectWorkFolder) || !Directory.Exists(_currentProjectWorkFolder))
        {
            AppDialogService.Warning(
                this,
                "Projekt nicht gefunden",
                "Der Projektordner konnte nicht gefunden werden. Öffne das Projekt bitte erneut über „Projekte“.");

            if (resetStateWhenFolderMissing)
                ResetLoadedResumeProjectState();

            return null;
        }

        var manifest = _mp3DiscProjectService.TryLoad(_currentProjectWorkFolder);
        if (manifest is null)
        {
            AppDialogService.Warning(
                this,
                "Projekt nicht gelesen",
                "Die MP3-CD-Projektdatei konnte nicht gelesen werden. Öffne das Projekt bitte erneut über „Projekte“.");
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
            "Export-Preset prüfen",
            "Gewähltes Preset ist höher als die Quelldateien",
            $"Die höchste erkannte Quell-Bitrate liegt bei {maxSourceBitrate.Value} kbps.\n" +
            $"Ausgewählt ist {requested.BitrateKbps} kbps. Hochkonvertieren macht die Datei größer, aber nicht hörbar besser.",
            AppDialogKind.Warning,
            details: new[]
            {
                $"Empfohlen: {recommended.DisplayName}",
                $"Gewählt: {requested.DisplayName}"
            },
            buttons: new[]
            {
                new AppDialogButton($"Auf {recommended.DisplayName} stellen", AppDialogResult.Yes, IsPrimary: true, IsDefault: true),
                new AppDialogButton("Trotzdem verwenden", AppDialogResult.No),
                new AppDialogButton("Abbrechen", AppDialogResult.Cancel, IsCancel: true)
            });

        return result switch
        {
            AppDialogResult.Yes => recommendedPreset,
            AppDialogResult.No => requestedPreset,
            _ => _selectedExportPreset
        };
    }

    private int? GetMaxTrackBitrateKbps()
    {
        var values = Tracks
            .Select(track => track.BitrateKbps)
            .Where(value => value is > 0)
            .Select(value => value!.Value)
            .ToList();

        return values.Count == 0 ? null : values.Max();
    }

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
        if (TracksGrid is null || TracksGrid.SelectedItems.Count == 0)
            return;

        if (e.OriginalSource is not DependencyObject source)
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

    private void ClearTrackSelection()
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
            "FFmpeg einrichten",
            "FFmpeg einrichten",
            "BookStitch benötigt FFmpeg und FFprobe zum Konvertieren und Prüfen von Audiodateien.\n\n" +
            "Automatische Reparatur:\nVorhandenes WinGet-Paket wird entfernt und FFmpeg anschließend vollständig neu installiert.",
            AppDialogKind.Question,
            null,
            new[]
            {
                new AppDialogButton("Automatisch installieren", AppDialogResult.Yes, IsPrimary: true, IsDefault: true),
                new AppDialogButton("Manuell auswählen", AppDialogResult.No),
                new AppDialogButton("Abbrechen", AppDialogResult.Cancel, IsCancel: true)
            });

        if (result == AppDialogResult.Cancel)
            return;

        if (result == AppDialogResult.Yes)
        {
            StatusText = "FFmpeg Installation über winget wurde gestartet. Bitte CMD Fenster beachten.";

            var exitCode = await _ffmpegService.InstallWithWingetAsync();

            _ffmpegStatus = _ffmpegService.DetectTools(_settings);
            ApplyFfmpegStatusToSettingsAndUi();

            if (_ffmpegStatus.IsComplete)
            {
                AppDialogService.Info(
                    this,
                    "FFmpeg bereit",
                    "FFmpeg wurde gefunden und ist bereit.");
            }
            else
            {
                AppDialogService.Warning(
                    this,
                    "FFmpeg noch nicht gefunden",
                    "FFmpeg wurde nach der Installation noch nicht gefunden.\n\n" +
                    "Falls die Installation erfolgreich war, starte Visual Studio oder BookStitch einmal neu.\n\n" +
                    $"Installationsprozess ExitCode: {(exitCode?.ToString() ?? "unbekannt")}");
            }

            return;
        }

        SelectFfmpegManually();
    }

    private void SelectFfmpegManually()
    {
        var dialog = new OpenFileDialog
        {
            Title = "ffmpeg.exe auswählen",
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

        _ffmpegStatus = _ffmpegService.DetectToolsFromFfmpegPath(dialog.FileName);
        ApplyFfmpegStatusToSettingsAndUi();

        if (_ffmpegStatus.IsComplete)
        {
            AppDialogService.Info(
                this,
                "FFmpeg bereit",
                "FFmpeg und FFprobe wurden gefunden und gespeichert.");
        }
        else
        {
            AppDialogService.Warning(
                this,
                "FFmpeg unvollständig",
                "ffmpeg.exe wurde ausgewählt, aber ffprobe.exe wurde im selben Ordner nicht gefunden oder konnte nicht gestartet werden.\n\n" +
                "Bitte wähle den Ordner aus, in dem beide Dateien liegen:\nffmpeg.exe und ffprobe.exe");
        }
    }

    private async void SelectFolder_Click(object sender, RoutedEventArgs e)
    {
        if (IsBusy)
            return;

        var dialog = new OpenFolderDialog
        {
            Title = "Hörbuch-Ordner auswählen",
            Multiselect = false,
            InitialDirectory = GetExistingFolderOrDefault(_settings.LastLocalProjectFolder, GetDesktopFolder())
        };

        if (dialog.ShowDialog(this) != true)
            return;

        if (IsOpticalDriveSelection(dialog.FolderName))
        {
            AppDialogService.Warning(
                this,
                "CD über den CD-Button öffnen",
                "Ein optisches Laufwerk kann nicht als lokales Ordnerprojekt geöffnet werden. Verwende dafür bitte oben den Button für MP3- oder Audio-CD-Projekte.");
            return;
        }

        ClearSelectedSourceDisplayOverride();
        ResetLoadedResumeProjectState();
        ResetMetadataForNewFolderProject();
        Tracks.Clear();
        UpdateIndexes();

        SelectedFolder = dialog.FolderName;

        _settings.LastSelectedFolder = dialog.FolderName;
        _settings.LastLocalProjectFolder = dialog.FolderName;
        SaveSettingsIfReady();

        await LoadFolderAsync(dialog.FolderName);

        if (Tracks.Count == 0)
            return;

        var setupDialog = CreateProjectSetupDialog(new ProjectSetupDialogRequest(
            SourceKind: ProjectSetupSourceKind.Folder,
            WindowTitle: "Ordnerprojekt vorbereiten",
            SourceInformation: $"Die Auswahl enthält {Tracks.Count} Audiodateien mit einer Gesamtdauer von {BuildProjectSetupDurationText(Tracks)}.",
            Instruction: "Prüfe die Projektdaten und ergänze sie bei Bedarf.",
            DefaultDiscCount: 1,
            MinimumDiscs: 1,
            MaximumDiscs: 1,
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
            KeepAlbumLinkedToTitle: _settings.KeepAlbumLinkedToTitle,
            UsePrivateGenreList: _settings.UsePrivateGenreList,
            SourceFolder: dialog.FolderName,
            MaxSourceBitrateKbps: GetMaxTrackBitrateKbps(),
            LastCoverFolder: ResolveProjectSetupCoverInitialDirectory(ProjectSetupSourceKind.Folder, dialog.FolderName)));

        SetMetadataEditingAvailable(true);
        var startProject = setupDialog.ShowDialog() == true;
        ApplyDiscProjectSetup(setupDialog.Result);
        if (!startProject)
            return;

        _pauseBeforeMergeOverride = !setupDialog.Result.AutoMergeAfterConversion;
        try
        {
            await RunCurrentExportPlanAsync();
        }
        finally
        {
            _pauseBeforeMergeOverride = null;
        }
    }

    private async void SelectDiscSource_Click(object sender, RoutedEventArgs e)
    {
        if (IsBusy)
            return;

        var selectedPath = ShowDiscSourceSelectionDialog();
        if (string.IsNullOrWhiteSpace(selectedPath))
            return;

        selectedPath = ResolveSelectedDiscSource(selectedPath);
        if (string.IsNullOrWhiteSpace(selectedPath))
            return;

        ClearSelectedSourceDisplayOverride();
        SelectedFolder = selectedPath;

        _settings.LastDiscSourceFolder = selectedPath;
        SaveSettingsIfReady();

        ResetLoadedResumeProjectState();
        await StartMp3DiscProjectAsync(selectedPath);
    }

    private string ResolveSelectedDiscSource(string selectedPath)
    {
        var currentPath = selectedPath;

        while (true)
        {
            if (!_discDriveService.IsCdDrivePath(currentPath))
                return currentPath;

            if (_discDriveService.IsDiscSourceReady(currentPath))
                return currentPath;

            _discDriveService.TryEjectDisc(currentPath);
            var driveLetter = (Path.GetPathRoot(currentPath) ?? currentPath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            var result = AppDialogService.Show(
                this,
                title: "CD-Laufwerk",
                heading: $"Bitte CD in Laufwerk {driveLetter} einlegen",
                message: $"Das ausgewählte Laufwerk {driveLetter} ist leer. Lege eine CD ein und klicke anschließend auf „Erneut prüfen“.\n\nDu kannst alternativ zur vollständigen Laufwerksauswahl zurückkehren.",
                kind: AppDialogKind.Information,
                buttons:
                [
                    new AppDialogButton("Erneut prüfen", AppDialogResult.Yes, IsPrimary: true, IsDefault: true),
                    new AppDialogButton("Anderes Laufwerk wählen", AppDialogResult.No),
                    new AppDialogButton("Abbrechen", AppDialogResult.Cancel, IsCancel: true)
                ]);

            if (result == AppDialogResult.Yes)
                continue;

            if (result == AppDialogResult.No)
            {
                currentPath = ShowDiscSourceSelectionDialog();
                if (string.IsNullOrWhiteSpace(currentPath))
                    return string.Empty;

                continue;
            }

            return string.Empty;
        }
    }

    private string ShowDiscSourceSelectionDialog()
    {
        var dialog = new DiscSourceSelectionDialog(
            _discDriveService.GetCdDriveShells(),
            GetExistingFolderOrDefault(_settings.LastDiscSourceFolder, GetDesktopFolder()),
            _settings.LastSelectedOpticalDrive)
        {
            Owner = this
        };

        if (dialog.ShowDialog() != true)
            return string.Empty;

        if (_discDriveService.IsCdDrivePath(dialog.SelectedPath))
        {
            _settings.LastSelectedOpticalDrive = Path.GetPathRoot(dialog.SelectedPath) ?? dialog.SelectedPath;
            SaveSettingsIfReady();
        }

        return dialog.SelectedPath;
    }

    private string GetDiscSourceInitialDirectory()
    {
        if (!string.IsNullOrWhiteSpace(_settings.LastDiscSourceFolder) &&
            Directory.Exists(_settings.LastDiscSourceFolder))
        {
            return _settings.LastDiscSourceFolder;
        }

        var preferredDiscFolder = _discDriveService.GetPreferredDiscInitialDirectory();
        if (!string.IsNullOrWhiteSpace(preferredDiscFolder) &&
            Directory.Exists(preferredDiscFolder))
        {
            return preferredDiscFolder;
        }

        return Directory.Exists(@"C:\") ? @"C:\" : GetDesktopFolder();
    }

    private async Task StartMp3DiscProjectAsync(string sourceFolder)
    {
        SelectedFolder = sourceFolder;
        ClearPendingMp3DiscPreparation();

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
                "CD nicht gelesen",
                "Das ausgewählte CD-Laufwerk konnte nicht vollständig gelesen werden.",
                details: new[] { ex.Message });
            StatusText = "CD-Analyse fehlgeschlagen.";
            ExportProgressText = BuildIdleExportProgressText();
            return;
        }

        if (!analysis.IsSupportedDataDisc)
        {
            if (analysis.IsProbablyAudioCd && await TryShowAudioDiscContentsAsync(sourceFolder))
                return;

            ShowUnsupportedDiscSourceMessage(analysis);
            StatusText = "Keine unterstützte MP3-CD / Daten-CD erkannt.";
            ExportProgressText = BuildIdleExportProgressText();
            return;
        }

        // CD 1 sofort vor dem Kopieren anzeigen, damit Titel, Autor und Trackliste direkt sichtbar sind.
        try
        {
            AppendDiscPreviewTracks(previewTracks, discNumber: 1, clearExistingTracks: true);
            TryApplyFirstEmbeddedCover(previewTracks);
            SuggestMetadataFromTracksAndFolder(sourceFolder, sourceFolder);
            UpdateFinalStatus(Tracks.Count);
            _pendingDiscProjectSourceFolder = sourceFolder;
            ExportProgressText = $"MP3-CD erkannt: {Tracks.Count} Tracks. Bereit zum Kopieren.";
            AutoFitTrackColumnsAfterRender();
        }
        catch (Exception ex)
        {
            AppDialogService.Error(
                this,
                "MP3-CD nicht gelesen",
                "Die Audiodateien auf der CD konnten nicht vollständig gelesen werden.",
                details: new[] { ex.Message });
            StatusText = "MP3-CD-Analyse fehlgeschlagen.";
            ExportProgressText = "MP3-CD-Analyse fehlgeschlagen.";
            return;
        }

        var setupDialog = CreateProjectSetupDialog(new ProjectSetupDialogRequest(
            SourceKind: ProjectSetupSourceKind.Mp3Disc,
            WindowTitle: "MP3-CD-Projekt vorbereiten",
            SourceInformation: $"Die eingelegte MP3-CD enthält {analysis.SupportedAudioFiles.Count} Audiodateien mit einer Gesamtdauer von {BuildProjectSetupDurationText(previewTracks)}.",
            Instruction: "Gib die Gesamtzahl der CDs an, prüfe die Projektdaten und ergänze sie bei Bedarf.",
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
            KeepAlbumLinkedToTitle: _settings.KeepAlbumLinkedToTitle,
            UsePrivateGenreList: _settings.UsePrivateGenreList,
            SourceFolder: sourceFolder,
            MaxSourceBitrateKbps: GetMaxTrackBitrateKbps(),
            LastCoverFolder: ResolveProjectSetupCoverInitialDirectory(ProjectSetupSourceKind.Mp3Disc, sourceFolder)));

        SetMetadataEditingAvailable(true);
        var startProject = setupDialog.ShowDialog() == true;
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

    private DiscProjectSetupResult CreateMp3DiscSetupFromCurrentUi(DiscProjectSetupResult baseSetup) =>
        baseSetup with
        {
            SelectedExportPreset = SelectedExportPreset,
            ParallelJobs = ParallelJobsInput,
            OutputExtension = OutputExtension,
            OutputFolder = OutputFolder,
            BookTitle = BookTitle,
            Album = Album,
            Author = Author,
            Narrator = Narrator,
            Genre = Genre,
            FileNameTemplate = FileNameTemplate,
            CoverSourcePath = GetCoverSourcePathForProjectSetup(),
            ProcessedCoverPath = _processedCoverPath,
            AutoMergeAfterConversion = _settings.MergeAutomaticallyAfterConversion
        };

    private async Task StartConfirmedMp3DiscProjectAsync(
        string sourceFolder,
        DiscProjectSetupResult setup,
        DiscSourceQuickIdentity sourceQuickIdentity,
        string sourceStructureSignature)
    {
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
        ClearPendingMp3DiscPreparation();
        _settings.LastDiscSourceFolder = sourceFolder;
        SaveSettingsIfReady();
        var projectFolder = _mp3DiscImportService.CreateDiscProjectFolder(sourceFolder, GetProjectFolderStructure().Mp3DiscProjectsFolder);
        _currentProjectWorkFolder = projectFolder;

        var sourceDriveInfo = await Task.Run(() =>
            _discDriveService.GetDriveDiagnosticsForPath(sourceFolder));
        var projectManifest = _mp3DiscProjectService.LoadOrCreate(
            projectFolder,
            sourceFolder,
            totalDiscs,
            setup.SelectedExportPreset,
            setup.ParallelJobs,
            setup.OutputExtension,
            setup.OutputFolder,
            setup.FileNameTemplate,
            sourceDriveInfo);

        SaveCurrentMp3DiscProjectSnapshot(projectManifest);

        await ImportMp3DiscProjectAsync(
            projectFolder,
            sourceFolder,
            projectManifest,
            startDiscNumber: 1,
            firstDiscAlreadyReady: true,
            autoExportWhenComplete: true,
            pauseBeforeMergeOverride: setup.AutoMergeAfterConversion ? false : true);
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
                _mp3DiscImportService.CheckQuickIdentity(candidateSourceFolder, expectedQuickIdentity));

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
                ? $"CD in Laufwerk {driveLetter} nicht verfügbar"
                : "Andere oder veränderte CD erkannt";
            var message = quickCheck.Status == DiscSourceQuickCheckStatus.Unavailable
                ? $"Die zuvor eingelesene CD in Laufwerk {driveLetter} wurde entfernt oder ist noch nicht lesbar.\n\n" +
                  "Lege die CD wieder ein und klicke auf „Erneut prüfen“. Es wurde noch kein Projektordner angelegt."
                : "Die aktuell eingelegte CD entspricht nicht eindeutig der zuvor eingelesenen CD.\n\n" +
                  "Lege die ursprüngliche CD wieder ein oder wähle das Laufwerk aus, in dem sie eingelegt ist.";

            var result = AppDialogService.Show(
                this,
                title: "MP3-CD-Projekt",
                heading: heading,
                message: message,
                kind: AppDialogKind.Warning,
                buttons:
                [
                    new AppDialogButton("Erneut prüfen", AppDialogResult.Yes, IsPrimary: true, IsDefault: true),
                    new AppDialogButton("Anderes Laufwerk wählen", AppDialogResult.No),
                    new AppDialogButton("Abbrechen", AppDialogResult.Cancel, IsCancel: true)
                ]);

            if (result == AppDialogResult.Yes)
                continue;

            if (result == AppDialogResult.No)
            {
                var selectedSource = ShowDiscSourceSelectionDialog();
                if (string.IsNullOrWhiteSpace(selectedSource))
                    return string.Empty;

                selectedSource = ResolveSelectedDiscSource(selectedSource);
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
        DiscSourceAnalysisText = "CD wird überprüft …";
        var previousStatusText = StatusText;
        var previousExportProgressText = ExportProgressText;
        StatusText = string.Empty;
        ExportProgressPercent = 0;
        ExportProgressText = string.Empty;

        using var slowNoticeCancellation = new CancellationTokenSource();
        var slowNoticeTask = ShowSlowDiscSourceNoticeAsync(slowNoticeCancellation.Token);

        try
        {
            return await Task.Run(() =>
            {
                var analysis = _mp3DiscImportService.AnalyzeSource(sourceFolder);
                if (!analysis.IsSupportedDataDisc)
                    return false;

                var currentSignature = _mp3DiscImportService.CreateDiscStructureSignature(sourceFolder, analysis);
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
        var previousStatusText = StatusText;
        var previousExportProgressText = ExportProgressText;
        StatusText = string.Empty;
        ExportProgressPercent = 0;
        ExportProgressText = string.Empty;

        using var slowNoticeCancellation = new CancellationTokenSource();
        var slowNoticeTask = ShowSlowDiscSourceNoticeAsync(slowNoticeCancellation.Token);

        try
        {
            return await Task.Run(() =>
            {
                var analysis = _mp3DiscImportService.AnalyzeSource(sourceFolder);
                var previewTracks = analysis.IsSupportedDataDisc
                    ? _folderScanner.Scan(sourceFolder, TrackNumberPreference.FileName)
                    : [];
                var quickIdentity = _mp3DiscImportService.CreateQuickIdentity(sourceFolder, analysis);
                var signature = analysis.IsSupportedDataDisc
                    ? _mp3DiscImportService.CreateDiscStructureSignature(sourceFolder, analysis)
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
        IsBusy = true;
        IsProgressIndeterminate = true;
        IsDiscSourceAnalysisActive = true;
        DiscSourceAnalysisText = "Audio-CD wird gelesen …";
        var previousStatusText = StatusText;
        var previousExportProgressText = ExportProgressText;
        StatusText = string.Empty;
        ExportProgressPercent = 0;
        ExportProgressText = string.Empty;

        using var slowNoticeCancellation = new CancellationTokenSource();
        var slowNoticeTask = ShowSlowDiscSourceNoticeAsync(slowNoticeCancellation.Token);

        AudioDiscReadResult result;
        try
        {
            result = await Task.Run(() => _audioDiscReaderService.ReadDisc(sourceFolder));
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

        if (!result.IsAudioDisc || result.Disc is null)
            return false;

        ResetMetadataForNewDiscProject();

        var previewWorkingFormat = AudioDiscSettingsService.NormalizeWorkingFormat(_settings.AudioDiscWorkingFormat);
        Tracks.Clear();
        foreach (var track in _audioDiscProjectService.CreateTrackPreview(result.Disc, BookTitle, Author, previewWorkingFormat))
            Tracks.Add(track);
        UpdateIndexes();
        UpdateFinalStatus(Tracks.Count);
        AutoFitTrackColumnsAfterRender();
        _pendingAudioDisc = result.Disc;
        SelectedFolder = result.Disc.DriveRoot;
        SetSelectedSourceDisplayOverride(
            CreateDiscDriveDisplayName(result.Disc.DriveRoot),
            result.Disc.DriveRoot);

        var setupDialog = CreateProjectSetupDialog(new ProjectSetupDialogRequest(
            SourceKind: ProjectSetupSourceKind.AudioDisc,
            WindowTitle: "Audio-CD-Projekt vorbereiten",
            SourceInformation: $"Die eingelegte Audio-CD enthält {result.Disc.TrackCount} Tracks mit einer Gesamtdauer von {result.Disc.TotalDurationText}.",
            Instruction: "Gib die Gesamtzahl der CDs an, prüfe die Projektdaten und ergänze sie bei Bedarf.",
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
            KeepAlbumLinkedToTitle: _settings.KeepAlbumLinkedToTitle,
            UsePrivateGenreList: _settings.UsePrivateGenreList,
            SourceFolder: sourceFolder,
            LastCoverFolder: ResolveProjectSetupCoverInitialDirectory(ProjectSetupSourceKind.AudioDisc, sourceFolder)));

        SetMetadataEditingAvailable(true);
        var startProject = setupDialog.ShowDialog() == true;
        ApplyDiscProjectSetup(setupDialog.Result);
        _pendingAudioDisc = result.Disc;
        _pendingAudioDiscTotalDiscs = setupDialog.Result.TotalDiscs;
        SelectedFolder = result.Disc.DriveRoot;
        var projectPrepared = false;

        try
        {
            var workingFormat = AudioDiscSettingsService.NormalizeWorkingFormat(_settings.AudioDiscWorkingFormat);
            var projectFolder = _audioDiscProjectService.CreateProjectFolder(
                GetProjectFolderStructure().AudioDiscProjectsFolder,
                setupDialog.Result.BookTitle);
            var sourceDriveInfo = await Task.Run(() =>
                _discDriveService.GetDriveDiagnosticsForPath(result.Disc.DriveRoot));
            var manifest = _audioDiscProjectService.CreateInitialManifest(
                projectFolder,
                result.Disc,
                discNumber: 1,
                setup: setupDialog.Result,
                workingFormat: workingFormat,
                sourceDriveInfo: sourceDriveInfo);

            _audioDiscProjectService.Save(manifest);
            _activeAudioDiscManifest = manifest;
            _loadedResumeProjectIsAudioDisc = true;
            _loadedResumeProjectIsLocal = false;
            _currentProjectWorkFolder = projectFolder;
            _isAudioDiscProjectAwaitingRip = true;

            Tracks.Clear();
            foreach (var track in _audioDiscProjectService.CreateTrackPreview(manifest))
                Tracks.Add(track);

            UpdateIndexes();
            UpdateFinalStatus(Tracks.Count);
            AutoFitTrackColumnsAfterRender();
            NotifyExportUiStateChanged();

            StatusText = "Projekt wird gestartet …";
            ExportProgressText = "0 % | Initialisierung";
            projectPrepared = true;
        }
        catch (Exception ex)
        {
            AppDialogService.Error(
                this,
                "Audio-CD-Projekt nicht angelegt",
                "Das Audio-CD-Projekt konnte nicht vollständig vorbereitet werden.",
                details: new[] { ex.Message });
            StatusText = "Audio-CD-Projekt konnte nicht angelegt werden.";
            ExportProgressText = BuildIdleExportProgressText();
        }

        if (projectPrepared && !startProject)
        {
            StatusText = $"Audio-CD vorbereitet: {result.Disc.TrackCount} Tracks. Trackliste kann geprüft und sortiert werden.";
            ExportProgressText = "Zum Starten des Rippings auf „Projekt starten“ klicken.";
        }

        if (projectPrepared && startProject)
        {
            _pauseBeforeMergeOverride = setupDialog.Result.AutoMergeAfterConversion ? false : true;
            try
            {
                await RunPreparedAudioDiscRipAsync();
            }
            finally
            {
                _pauseBeforeMergeOverride = null;
            }
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

        var previousStatusText = StatusText;
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

    private async void AddProjectSources_Click(object sender, RoutedEventArgs e)
    {
        if (IsBusy)
            return;

        if (_loadedResumeProjectIsLocal)
        {
            await AddMoreLocalSourcesAsync();
            return;
        }

        if (_loadedResumeProjectIsAudioDisc)
        {
            await AddMoreAudioDiscsAsync();
            return;
        }

        if (!_loadedResumeProjectIsMp3Disc)
            return;

        var manifest = TryLoadCurrentMp3DiscProjectManifest(resetStateWhenFolderMissing: false);
        if (manifest is null)
            return;

        var additionalImportPlan = _mp3DiscProjectService.BuildAdditionalImportPlan(manifest);
        var defaultSourceFolder = _discDriveService.ResolveResumeDiscSource(
            _currentProjectWorkFolder,
            manifest.SourceFolder,
            _settings.LastDiscSourceFolder,
            SelectedFolder);

        var dialog = new AddMoreDiscsDialog(
            additionalImportPlan.CompletedDiscCount,
            additionalImportPlan.CurrentTotalDiscs,
            additionalImportPlan.DefaultTotalDiscs,
            additionalImportPlan.MinimumTotalDiscs,
            additionalImportPlan.MaximumTotalDiscs,
            _discDriveService.GetCdDrives(),
            preferredDriveRoot: defaultSourceFolder,
            sourceDialogInitialDirectory: GetDiscSourceInitialDirectory())
        {
            Owner = this
        };

        if (dialog.ShowDialog() != true)
            return;

        var addMoreOptions = dialog.Result;
        var sourceFolder = addMoreOptions.SourceFolder.Trim();

        if (!_discDriveService.IsValidResumeDiscSource(sourceFolder, _currentProjectWorkFolder))
        {
            AppDialogService.Warning(
                this,
                "CD-Laufwerk nicht verwendbar",
                "Bitte wähle ein CD-Laufwerk oder einen Ordner außerhalb des Projektordners aus.");
            return;
        }

        _settings.LastDiscSourceFolder = sourceFolder;
        SaveSettingsIfReady();

        var extensionSnapshot = _projectExtensionRollbackService.Capture(manifest.ProjectFolder);
        var returnState = _pipelineState;
        var returnCompletedState = _isCurrentProjectCompleted;

        try
        {
            _mp3DiscProjectService.IncreaseTotalDiscsForAdditionalImport(manifest, addMoreOptions.TotalDiscs);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            AppDialogService.Warning(
                this,
                "CD-Anzahl nicht übernommen",
                ex.Message);
            return;
        }

        manifest.SourceFolder = sourceFolder;
        SaveCurrentMp3DiscProjectSnapshot(manifest);

        var nextMissingDisc = _mp3DiscProjectService.GetNextMissingDiscNumber(manifest, manifest.TotalDiscs);
        if (nextMissingDisc is null)
        {
            StatusText = "Alle eingestellten CDs sind bereits importiert.";
            ExportProgressText = "Keine neue CD zum Importieren gefunden.";
            return;
        }

        _loadedResumeProjectNeedsDiscImport = true;
        _loadedResumeProjectIsMp3Disc = true;
        NotifyExportUiStateChanged();

        StatusText = $"MP3-CD-Projekt wird erweitert. Warte auf CD {nextMissingDisc.Value} von {manifest.TotalDiscs}.";
        ExportProgressText = "Weitere CDs werden importiert und für das aktuelle Preset konvertiert. Danach bleibt die Trackliste zur Prüfung offen.";
        _isCurrentProjectCompleted = false;

        var isFirstExtensionRun = true;

        async Task<bool> RunMp3ExtensionAsync()
        {
            var firstDiscAlreadyReady = !isFirstExtensionRun;
            isFirstExtensionRun = false;
            return await ImportMp3DiscProjectAsync(
                manifest.ProjectFolder,
                sourceFolder,
                manifest,
                startDiscNumber: _mp3DiscProjectService.GetNextMissingDiscNumber(manifest, manifest.TotalDiscs) ?? nextMissingDisc.Value,
                firstDiscAlreadyReady: firstDiscAlreadyReady,
                autoExportWhenComplete: false,
                extensionSnapshot: extensionSnapshot,
                extensionReturnState: returnState,
                extensionReturnCompletedState: returnCompletedState);
        }

        async Task RunAndFinalizeMp3ExtensionAsync()
        {
            var extensionCanceled = await RunMp3ExtensionAsync();
            if (extensionCanceled)
                return;

            ClearProjectExtensionPauseContext();

            var updatedManifest = TryLoadCurrentMp3DiscProjectManifest(resetStateWhenFolderMissing: false);
            if (updatedManifest is not null &&
                _mp3DiscProjectService.GetNextMissingDiscNumber(updatedManifest, updatedManifest.TotalDiscs) is null &&
                !_loadedResumeProjectNeedsDiscImport)
            {
                PauseBeforeMergeForManualReview();
            }
        }

        BeginProjectExtensionPauseContext(
            RunAndFinalizeMp3ExtensionAsync,
            () =>
            {
                _projectExtensionRollbackService.Rollback(extensionSnapshot);
                _convertedFileCleanupService.DeletePartFiles(manifest.ProjectFolder);
                RefreshLoadedProjectTrackStateFromPersistedProject(manifest.ProjectFolder);
                _loadedResumeProjectNeedsDiscImport = false;
                _loadedResumeProjectIsMp3Disc = true;
                _isCurrentProjectCompleted = returnCompletedState;
                _isWaitingForManualMergeReview = returnState is ProjectPipelineState.ReviewBeforeMerge or ProjectPipelineState.Completed;
                SetPipelineState(returnState);
                StatusText = "Hinzufügen weiterer CDs wurde gestoppt. Das bisherige Projekt blieb unverändert.";
                ExportProgressText = "Alle Dateien des Erweiterungsversuchs wurden entfernt.";
                NotifyExportUiStateChanged();
                RefreshResumeProjects(showStatus: false);
            });

        await RunAndFinalizeMp3ExtensionAsync();
    }

    private async Task AddMoreLocalSourcesAsync()
    {
        if (string.IsNullOrWhiteSpace(_currentProjectWorkFolder) ||
            !Directory.Exists(_currentProjectWorkFolder))
        {
            AppDialogService.Warning(
                this,
                "Projektordner nicht gefunden",
                "Das lokale Projekt kann nicht erweitert werden, weil sein Projektordner nicht gefunden wurde.");
            return;
        }

        var selection = AppDialogService.Show(
            this,
            "Lokale Quellen hinzufügen",
            "Lokale Quellen hinzufügen",
            "Wähle, ob du einen vollständigen Ordner oder einzelne Audiodateien zum geöffneten Projekt hinzufügen möchtest.",
            AppDialogKind.Question,
            null,
            new[]
            {
                new AppDialogButton("Ordner hinzufügen", AppDialogResult.Yes, IsPrimary: true, IsDefault: true),
                new AppDialogButton("Dateien hinzufügen", AppDialogResult.No),
                new AppDialogButton("Abbrechen", AppDialogResult.Cancel, IsCancel: true)
            });

        if (selection == AppDialogResult.Cancel)
            return;

        string scanRoot;
        IReadOnlyCollection<string> selectedFiles;

        if (selection == AppDialogResult.Yes)
        {
            var folderDialog = new OpenFolderDialog
            {
                Title = "Ordner zum Projekt hinzufügen",
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
                    "CD nicht als lokalen Ordner hinzufügen",
                    "Ein optisches Laufwerk kann einem lokalen Projekt nicht als Ordnerquelle hinzugefügt werden. Verwende für CDs bitte ein MP3- oder Audio-CD-Projekt.");
                return;
            }

            scanRoot = Path.GetFullPath(folderDialog.FolderName);
            selectedFiles = _folderScanner
                .Scan(scanRoot)
                .Select(track => TrackPathService.GetTrackPath(scanRoot, track))
                .ToArray();

            _settings.LastLocalProjectFolder = scanRoot;
        }
        else
        {
            var fileDialog = new OpenFileDialog
            {
                Title = "Audiodateien zum Projekt hinzufügen",
                Filter = "Audiodateien|*.mp3;*.m4a;*.m4b;*.aac;*.wav;*.flac;*.wma|Alle Dateien|*.*",
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
                    "Dateien nicht übernommen",
                    "Bitte wähle die Dateien gemeinsam aus einem einzigen Ordner aus.");
                return;
            }

            if (IsOpticalDriveSelection(sourceDirectories[0]))
            {
                AppDialogService.Warning(
                    this,
                    "CD nicht als lokale Dateien hinzufügen",
                    "Audiodateien von einem optischen Laufwerk können einem lokalen Projekt nicht direkt hinzugefügt werden. Verwende für CDs bitte ein MP3- oder Audio-CD-Projekt.");
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
                "Keine Audiodateien gefunden",
                "In der Auswahl wurden keine unterstützten Audiodateien gefunden.");
            return;
        }

        var projectFolder = Path.GetFullPath(_currentProjectWorkFolder);
        var originalsFolder = ProjectFolderLayout.GetOriginalsFolder(projectFolder);
        var importRoot = Directory.GetParent(scanRoot)?.FullName ?? scanRoot;
        ProjectFolderLayout.EnsureProjectFolders(projectFolder);
        var manifestPath = ProjectFolderLayout.GetWorkManifestPath(projectFolder);
        var preset = ExportPreset.Parse(SelectedExportPreset);
        var convertedFolder = ProjectFolderLayout.GetConvertedPresetFolder(projectFolder, preset.GetFolderName());
        Directory.CreateDirectory(convertedFolder);

        var manifest = _workManifestService.LoadOrCreate(
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
                "Keine neuen Quellen",
                "Alle ausgewählten Audiodateien sind bereits Bestandteil des Projekts.");
            return;
        }

        var selectedPathSet = candidates
            .Select(item => item.SourcePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var newTracks = _folderScanner
            .Scan(scanRoot, includeFile: path => selectedPathSet.Contains(Path.GetFullPath(path)))
            .ToList();

        if (newTracks.Count == 0)
        {
            AppDialogService.Info(
                this,
                "Keine neuen Quellen",
                "Die ausgewählten Dateien konnten nicht als unterstützte Audiotracks eingelesen werden.");
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

        var snapshot = _projectExtensionRollbackService.Capture(projectFolder);
        var returnState = _pipelineState;
        var returnCompletedState = _isCurrentProjectCompleted;
        var previousTrackCount = Tracks.Count;
        var manifestSyncRoot = new object();
        var sourceAcquisitionCompleted = false;

        async Task RunLocalExtensionAsync()
        {
        _exportCancellation = new CancellationTokenSource();

        var initialPipelineState = _localProjectExtensionStateService.ResolveInitialState(
            candidates.Select(item => item.TargetPath));
        sourceAcquisitionCompleted = initialPipelineState == ProjectPipelineState.Converting;

        SetPipelineState(initialPipelineState);
        IsBusy = true;
        IsExporting = true;
        _isCurrentProjectCompleted = false;
        var workflowOperationId = BeginWorkflowStatusOperation(projectFolder);
        PublishWorkflowStatus(
            workflowOperationId,
            _localWorkflowStatusAdapter.CreateRunningSnapshot(
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
                existingConvertedCount: previousTrackCount,
                conversionTotalOverride: previousTrackCount + candidates.Length,
                activeTrackNumberOffset: previousTrackCount));

        try
        {
            manifest.State.Status = initialPipelineState == ProjectPipelineState.Converting
                ? ProjectManifestStatuses.Converting
                : ProjectManifestStatuses.AcquiringSources;
            _workManifestService.Save(manifestPath, manifest);

            var result = await _localProjectLivePreparationService.RunAsync(
                new LocalProjectLivePreparationRequest(
                    importRoot,
                    candidates.Select(item => item.SourcePath).ToArray(),
                    projectFolder,
                    ResolveParallelJobCount()),
                async (copiedFile, trackPreparationProgress, token) =>
                {
                    var sourcePath = Path.GetFullPath(copiedFile.SourceFile);
                    if (!trackBySourcePath.TryGetValue(sourcePath, out var mappedTrack))
                    {
                        throw new InvalidOperationException(
                            $"Die hinzugefügte Quelldatei konnte keinem Track zugeordnet werden: {copiedFile.SourceFile}");
                    }

                    var trackIndex = previousTrackCount + mappedTrack.Index;
                    var convertedPath = ConvertedTrackPathService.GetConvertedTrackPath(
                        convertedFolder,
                        copiedFile.TargetFile,
                        mappedTrack.Track);

                    var canReuseConvertedTrack = false;
                    lock (manifestSyncRoot)
                    {
                        canReuseConvertedTrack = _workManifestService.CanReuseConvertedTrack(
                            manifest,
                            trackIndex,
                            mappedTrack.Track,
                            copiedFile.TargetFile,
                            convertedPath,
                            preset);
                    }

                    if (!canReuseConvertedTrack)
                    {
                        var durationTicks = TrackDurationService.GetEffectiveDurationTicks(mappedTrack.Track);
                        await _aacExportProcessingService.PrepareTrackForExportAsync(
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

                        lock (manifestSyncRoot)
                        {
                            _workManifestService.UpdateTrack(
                                manifest,
                                trackIndex,
                                mappedTrack.Track,
                                copiedFile.TargetFile,
                                convertedPath,
                                preset);
                            _workManifestService.Save(manifestPath, manifest);
                        }
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
                            _workManifestService.Save(manifestPath, manifest);
                        }
                        SetPipelineState(ProjectPipelineState.Converting);
                    }

                    PublishWorkflowStatus(
                        workflowOperationId,
                        _localWorkflowStatusAdapter.CreateRunningSnapshot(
                            projectFolder,
                            sourceAcquisitionCompleted
                                ? ProjectPipelineState.Converting
                                : ProjectPipelineState.AcquiringSources,
                            progress,
                            preset,
                            isExtension: true,
                            existingConvertedCount: previousTrackCount,
                            conversionTotalOverride: previousTrackCount + candidates.Length,
                            activeTrackNumberOffset: previousTrackCount));
                }),
                _exportCancellation.Token);

            if (result.ImportResult.WasCanceled || _exportCancellation.IsCancellationRequested)
            {
                if (_pauseRequested)
                {
                    PublishWorkflowStatus(
                        workflowOperationId,
                        _localWorkflowStatusAdapter.CreateRunningSnapshot(
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
                            existingConvertedCount: previousTrackCount,
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

            _workManifestService.MarkConversionCompleted(manifest, preset.DisplayName);
            _workManifestService.Save(manifestPath, manifest);

            RefreshLoadedProjectTrackStateFromPersistedProject(projectFolder);
            _currentFolderPath = originalsFolder;
            SelectedFolder = scanRoot;
            _loadedResumeProjectIsLocal = true;
            _isWaitingForManualMergeReview = true;
            _isCurrentProjectCompleted = false;
            SetPipelineState(ProjectPipelineState.ReviewBeforeMerge);
            PublishWorkflowStatus(
                workflowOperationId,
                _localWorkflowStatusAdapter.CreateReadySnapshot(
                    projectFolder,
                    previousTrackCount + candidates.Length,
                    preset));
            EndWorkflowStatusOperation(workflowOperationId);
            ClearProjectExtensionPauseContext();
            NotifyExportUiStateChanged();
            RefreshResumeProjects(showStatus: false);
        }
        catch (OperationCanceledException) when (_exportCancellation?.IsCancellationRequested == true)
        {
            if (_pauseRequested)
            {
                var last = _workflowStatusCoordinator.Snapshot;
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
                "Lokale Erweiterung fehlgeschlagen",
                "Die zusätzlichen lokalen Quellen konnten nicht vollständig übernommen werden.",
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
        _projectExtensionRollbackService.Rollback(snapshot);
        ClearProjectExtensionPauseContext();
        _convertedFileCleanupService.DeletePartFiles(projectFolder);
        RefreshLoadedProjectTrackStateFromPersistedProject(projectFolder);

        _loadedResumeProjectIsLocal = true;
        _loadedResumeProjectIsMp3Disc = false;
        _loadedResumeProjectIsAudioDisc = false;
        _isCurrentProjectCompleted = returnCompletedState;
        _isWaitingForManualMergeReview = returnState == ProjectPipelineState.ReviewBeforeMerge;
        SetPipelineState(returnState);
        StatusText = "Hinzufügen lokaler Quellen wurde abgebrochen. Das bisherige Projekt blieb unverändert.";
        ExportProgressText = "Neu hinzugefügte Originaldateien und Konvertierungen wurden entfernt.";
        NotifyExportUiStateChanged();
        RefreshResumeProjects(showStatus: false);
    }

    private async Task AddMoreAudioDiscsAsync()
    {
        var manifest = _activeAudioDiscManifest;
        if (manifest is null && !string.IsNullOrWhiteSpace(_currentProjectWorkFolder))
            manifest = _audioDiscProjectService.TryLoad(_currentProjectWorkFolder);
        if (manifest is null)
            return;

        var completedDiscs = _audioDiscProjectService.CountCompletedDiscs(manifest);
        var minimumTotal = Math.Max(manifest.TotalDiscs + 1, completedDiscs + 1);
        var dialog = new AddMoreDiscsDialog(
            completedDiscs,
            manifest.TotalDiscs,
            minimumTotal,
            minimumTotal,
            99,
            _discDriveService.GetCdDrives(),
            preferredDriveRoot: manifest.SourceDriveRoot,
            sourceDialogInitialDirectory: GetDiscSourceInitialDirectory())
        {
            Owner = this
        };

        if (dialog.ShowDialog() != true)
            return;

        var snapshot = _projectExtensionRollbackService.Capture(manifest.ProjectFolder);
        var returnState = _pipelineState;
        var returnCompletedState = _isCurrentProjectCompleted;

        try
        {
            _audioDiscProjectService.IncreaseTotalDiscsForAdditionalRip(manifest, dialog.Result.TotalDiscs);
            manifest.SourceDriveRoot = dialog.Result.SourceFolder.Trim();
            _activeAudioDiscManifest = manifest;
            _loadedResumeProjectIsAudioDisc = true;
            _isAudioDiscProjectAwaitingRip = true;
            _isWaitingForManualMergeReview = false;
            _isCurrentProjectCompleted = false;
            SaveCurrentAudioDiscProjectSnapshot(manifest, force: true);
            BeginProjectExtensionPauseContext(
                () => RunPreparedAudioDiscRipAsync(snapshot, returnState, returnCompletedState),
                () =>
                {
                    _projectExtensionRollbackService.Rollback(snapshot);
                    _convertedFileCleanupService.DeletePartFiles(manifest.ProjectFolder);
                    _activeAudioDiscManifest = _audioDiscProjectService.TryLoad(manifest.ProjectFolder);
                    _isAudioDiscProjectAwaitingRip = false;
                    _isCurrentProjectCompleted = returnCompletedState;
                    _isWaitingForManualMergeReview = returnState is ProjectPipelineState.ReviewBeforeMerge or ProjectPipelineState.Completed;
                    SetPipelineState(returnState);
                    RefreshLoadedProjectTrackStateFromPersistedProject(manifest.ProjectFolder);
                    StatusText = "Hinzufügen weiterer Audio-CDs wurde gestoppt. Das bisherige Projekt blieb unverändert.";
                    ExportProgressText = "Alle Dateien des Erweiterungsversuchs wurden entfernt.";
                    NotifyExportUiStateChanged();
                    RefreshResumeProjects(showStatus: false);
                });
            await RunPreparedAudioDiscRipAsync(snapshot, returnState, returnCompletedState);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            AppDialogService.Warning(this, "CD-Anzahl nicht übernommen", ex.Message);
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

        _coverSourcePath = setup.CoverSourcePath;
        _processedCoverPath = !string.IsNullOrWhiteSpace(setup.ProcessedCoverPath) &&
                              File.Exists(setup.ProcessedCoverPath)
            ? setup.ProcessedCoverPath
            : "";
        CoverPreviewSource = _processedCoverPath;

        OnExportPreviewChanged();
    }

    private async Task ContinueLoadedMp3DiscProjectAsync()
    {
        var manifest = TryLoadCurrentMp3DiscProjectManifest(resetStateWhenFolderMissing: true);
        if (manifest is null)
            return;

        var resumePlan = _mp3DiscProjectService.BuildResumePlan(manifest);
        var sourceFolder = _discDriveService.ResolveResumeDiscSource(
            _currentProjectWorkFolder,
            manifest.SourceFolder,
            _settings.LastDiscSourceFolder,
            SelectedFolder);

        if (string.IsNullOrWhiteSpace(sourceFolder))
        {
            sourceFolder = ShowDiscSourceSelectionDialog();
            if (string.IsNullOrWhiteSpace(sourceFolder))
                return;

            sourceFolder = ResolveSelectedDiscSource(sourceFolder);
            if (string.IsNullOrWhiteSpace(sourceFolder))
                return;
        }

        var setupDialog = CreateProjectSetupDialog(new ProjectSetupDialogRequest(
            SourceKind: ProjectSetupSourceKind.Mp3Disc,
            WindowTitle: "MP3-CD-Projekt fortsetzen",
            SourceInformation: resumePlan.SetupMessage,
            Instruction: "Prüfe die Projektdaten und ergänze sie bei Bedarf.",
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
            KeepAlbumLinkedToTitle: _settings.KeepAlbumLinkedToTitle,
            UsePrivateGenreList: _settings.UsePrivateGenreList,
            SourceFolder: sourceFolder,
            MaxSourceBitrateKbps: GetMaxTrackBitrateKbps(),
            LastCoverFolder: ResolveProjectSetupCoverInitialDirectory(ProjectSetupSourceKind.Mp3Disc, sourceFolder)));

        SetMetadataEditingAvailable(true);
        var startProject = setupDialog.ShowDialog() == true;
        var setup = setupDialog.Result;
        ApplyDiscProjectSetup(setup);
        if (!startProject)
            return;

        _mp3DiscProjectService.UpdateResumeDiscPlan(manifest, setup.TotalDiscs, sourceFolder);
        SaveCurrentMp3DiscProjectSnapshot(manifest);

        resumePlan = _mp3DiscProjectService.BuildResumePlan(manifest);

        if (resumePlan.NextMissingDiscNumber is null)
        {
            if (!await PrepareCompletedMp3DiscProjectForCurrentPresetAsync(manifest.ProjectFolder))
                return;

            _loadedResumeProjectNeedsDiscImport = false;
            _loadedResumeProjectIsMp3Disc = true;
            OnPropertyChanged(nameof(CanStartExport));
            OnPropertyChanged(nameof(ExportButtonText));
            RefreshResumeProjects(showStatus: false);
            await RunCurrentExportPlanAsync(manifest.ProjectFolder, ProjectManifestTypes.Mp3DiscProject);
            return;
        }

        _loadedResumeProjectNeedsDiscImport = true;
        NotifyExportUiStateChanged();

        await ImportMp3DiscProjectAsync(
            manifest.ProjectFolder,
            sourceFolder,
            manifest,
            startDiscNumber: resumePlan.NextMissingDiscNumber.Value,
            firstDiscAlreadyReady: false,
            autoExportWhenComplete: false);
    }

    private async Task<bool> ImportMp3DiscProjectAsync(
        string projectFolder,
        string sourceFolder,
        Mp3DiscProjectManifest projectManifest,
        int startDiscNumber,
        bool firstDiscAlreadyReady,
        bool autoExportWhenComplete,
        bool? pauseBeforeMergeOverride = null,
        ProjectExtensionRollbackSnapshot? extensionSnapshot = null,
        ProjectPipelineState? extensionReturnState = null,
        bool extensionReturnCompletedState = false,
        bool isDeveloperShortTest = false,
        string? expectedFirstDiscSignature = null)
    {
        var totalDiscs = projectManifest.TotalDiscs;
        _currentProjectWorkFolder = projectFolder;
        _currentFolderPath = projectFolder;

        // Sobald ein MP3-CD-Projekt angelegt wurde, muss seine Identität auch nach
        // automatischem Import und Export in der aktuellen Sitzung erhalten bleiben.
        // Andernfalls würde ein anschließender Presetwechsel den lokalen Projektpfad verwenden.
        _loadedResumeProjectIsMp3Disc = true;
        NotifyExportUiStateChanged();

        var livePreset = ExportPreset.Parse(SelectedExportPreset);
        var liveConvertedFolder = ProjectFolderLayout.GetConvertedPresetFolder(projectFolder, livePreset.GetFolderName());
        Directory.CreateDirectory(liveConvertedFolder);
        _convertedFileCleanupService.DeletePartFiles(projectFolder);

        var liveConversionQueue = new LiveConversionQueueService();
        using var liveConversionSemaphore = new SemaphoreSlim(liveConversionQueue.GetLiveWorkerLimit(ParallelJobsInput));
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
        var totalCopiedFiles = _mp3DiscImportWorkflowService.CountCompletedCopiedFiles(projectManifest);
        var latestStatusDiscNumber = startDiscNumber;
        var latestCopiedBeforeCurrentDisc = totalCopiedFiles;
        var latestCopiedCurrentDisc = 0;
        var latestTotalCurrentDisc = 0;
        var latestCurrentDiscFinished = false;
        var latestAllDiscsFinished = false;
        var workflowOperationId = BeginWorkflowStatusOperation(projectFolder);

        var workManifest = _workManifestService.LoadOrCreate(
            ProjectFolderLayout.ResolveWorkManifestPath(projectFolder),
            ProjectManifestTypes.Mp3DiscProject,
            projectFolder,
            ProjectFolderLayout.GetOriginalsFolder(projectFolder),
            livePreset.DisplayName);
        var existingConvertedCount = Math.Max(
            _workManifestService.CountReusableConvertedTracks(workManifest, livePreset),
            DeveloperMp3DiscTestProjectService.CountReusablePreparedConvertedFiles(projectFolder, livePreset));
        var importedDiscSignatures = _mp3DiscImportWorkflowService.BuildCompletedDiscSignatureSet(projectManifest);

        RefreshExistingMp3DiscTrackStateBeforeImport(
            projectFolder,
            sourceFolder,
            totalDiscs);

        SetPipelineState(ProjectPipelineState.AcquiringSources);
        IsBusy = true;
        IsDiscImporting = true;
        _activeMp3DiscManifest = projectManifest;
        _discImportCancellation = new CancellationTokenSource();

        async Task<bool> HandleImportCancellationAsync(string progressText, bool cleanupPartFiles = false)
        {
            Interlocked.Exchange(ref _mp3DiscBackgroundPresetPreparationActive, 0);
            await LiveConversionWorkflowService.WaitForTasksAsync(
                liveConversionTasks,
                liveConversionTasksLock,
                () => SaveCurrentMp3DiscProjectSnapshot(projectManifest),
                ProjectSnapshotInterval);

            if (extensionSnapshot is null)
            {
                var last = _workflowStatusCoordinator.Snapshot;
                PublishWorkflowStatus(workflowOperationId, last with { IsPaused = true });
                EndWorkflowStatusOperation(workflowOperationId);
                CancelMp3DiscImportAndShowResumeDialog(
                    projectManifest,
                    progressText,
                    cleanupPartFiles,
                    preserveWorkflowStatus: true);
                return true;
            }

            if (_pauseRequested)
            {
                _convertedFileCleanupService.DeletePartFiles(projectFolder);
                var last = _workflowStatusCoordinator.Snapshot;
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

            _projectExtensionRollbackService.Rollback(extensionSnapshot, generatedConvertedFiles);
            ClearProjectExtensionPauseContext();
            _convertedFileCleanupService.DeletePartFiles(projectFolder);
            RefreshLoadedProjectTrackStateFromPersistedProject(projectFolder);

            _loadedResumeProjectNeedsDiscImport = false;
            _loadedResumeProjectIsMp3Disc = true;
            _isCurrentProjectCompleted = extensionReturnCompletedState;
            _isWaitingForManualMergeReview = extensionReturnState == ProjectPipelineState.ReviewBeforeMerge;
            SetPipelineState(extensionReturnState ?? ProjectPipelineState.ReviewBeforeMerge);
            StatusText = "Hinzufügen weiterer CDs wurde abgebrochen. Das bisherige Projekt blieb unverändert.";
            ExportProgressText = "Neu hinzugefügte Dateien und Konvertierungen wurden entfernt.";
            NotifyExportUiStateChanged();
            RefreshResumeProjects(showStatus: false);

            if (!_isShutdownInProgress)
            {
                AppDialogService.Info(
                    this,
                    "Erweiterung abgebrochen",
                    "Das Hinzufügen weiterer CDs wurde abgebrochen.\n\n" +
                    "Alle Dateien und Konvertierungen dieses Erweiterungsversuchs wurden entfernt. " +
                    "Das zuvor vollständige Projekt steht weiterhin unverändert zur Verfügung.",
                    title: "Erweiterung abgebrochen");
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
                    _mp3DiscWorkflowStatusAdapter.CreateRunningSnapshot(
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
                        allDiscsFinished: statusAllDiscsFinished));
            }

            for (var discNumber = startDiscNumber; discNumber <= totalDiscs; discNumber++)
            {
                lock (mp3StatusProgressLock)
                    latestCopiedBeforeCurrentDisc = totalCopiedFiles;

                if (token.IsCancellationRequested)
                {
                    return await HandleImportCancellationAsync(
                        $"Benutzerabbruch: {totalCopiedFiles} Datei(en) kopiert");
                }

                var shouldWaitForDisc = discNumber != startDiscNumber || !firstDiscAlreadyReady;
                if (shouldWaitForDisc)
                {
                    if (!isDeveloperShortTest)
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
                            token);
                    }

                    PublishMp3Status(
                        discNumber,
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
                        discNumber == startDiscNumber ? expectedFirstDiscSignature : null,
                        acceptedSource => selectedSourceFolder = acceptedSource);
                    sourceFolder = selectedSourceFolder;
                    projectManifest.SourceFolder = sourceFolder;
                    _settings.LastDiscSourceFolder = sourceFolder;
                    SaveSettingsIfReady();
                    Interlocked.Exchange(ref _mp3DiscBackgroundPresetPreparationActive, 0);
                    if (waitOutcome == DiscWaitDialogOutcome.Deferred)
                    {
                        _pauseRequested = true;
                        return await HandleImportCancellationAsync(
                            $"Projekt pausiert • MP3-CD {discNumber} von {totalDiscs} ausstehend");
                    }

                    try
                    {
                        AppendDiscPreviewTracks(sourceFolder, discNumber, clearExistingTracks: false);
                        TracksGrid.Items.Refresh();
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
                    catch (Exception ex)
                    {
                        ShowMp3DiscAnalysisFailed(discNumber, ex);
                        return false;
                    }
                }

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

                var importResult = await _mp3DiscImportWorkflowService.ImportDiscAsync(
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
                        totalFilesOnDisc = precheck.TotalFiles;
                        PublishMp3Status(
                            discNumber,
                            copiedBeforeCurrentDisc: totalCopiedFiles,
                            copiedCurrentDisc: 0,
                            totalCurrentDisc: totalFilesOnDisc,
                            currentDiscFinished: false,
                            allDiscsFinished: false);
                    },
                    token);

                progress.Flush();

                if (importResult.IsAlreadyImported)
                {
                    ShowAlreadyImportedMp3DiscWarning(discNumber, totalDiscs);
                    if (extensionSnapshot is not null)
                        return await HandleImportCancellationAsync("Bereits importierte CD erkannt.");
                    return false;
                }

                if (importResult.WasCanceled)
                {
                    PublishMp3Status(
                        discNumber,
                        copiedBeforeCurrentDisc: totalCopiedFiles,
                        copiedCurrentDisc: importResult.CopiedFiles,
                        totalCurrentDisc: totalFilesOnDisc,
                        isPaused: true);
                    return await HandleImportCancellationAsync(
                        $"Benutzerabbruch: {importResult.CopiedFiles}/{totalFilesOnDisc} auf CD {discNumber} kopiert");
                }

                SaveCurrentMp3DiscProjectSnapshot(projectManifest);

                PublishMp3Status(
                    discNumber,
                    copiedBeforeCurrentDisc: totalCopiedFiles,
                    copiedCurrentDisc: importResult.CopiedFiles,
                    totalCurrentDisc: totalFilesOnDisc,
                    currentDiscFinished: discNumber < totalDiscs,
                    allDiscsFinished: discNumber >= totalDiscs);

                totalCopiedFiles += importResult.CopiedFiles;

                await RefreshMp3DiscTrackListAfterDiscImportAsync(projectFolder, sourceFolder);
                UpdateMp3DiscImportContinuationState(discNumber, totalDiscs);
                WarnIfMp3DiscEjectFailed(sourceFolder, discNumber, totalDiscs);
                if (discNumber < totalDiscs)
                    _notificationService.Notify(NotificationEvent.DiscChangeRequired);
            }
        }
        catch (OperationCanceledException)
        {
            return await HandleImportCancellationAsync(
                $"Benutzerabbruch: {totalCopiedFiles} Datei(en) kopiert",
                cleanupPartFiles: true);
        }
        catch (Exception) when (_discImportCancellation?.IsCancellationRequested == true)
        {
            return await HandleImportCancellationAsync(
                $"Benutzerabbruch: {totalCopiedFiles} Datei(en) kopiert",
                cleanupPartFiles: true);
        }
        catch (Exception ex)
        {
            MarkMp3DiscImportFailed(projectManifest, projectFolder, ex);
            return false;
        }
        finally
        {
            Interlocked.Exchange(ref _mp3DiscBackgroundPresetPreparationActive, 0);
            if (_isShutdownInProgress)
                TrySaveCurrentMp3DiscProjectSnapshot(projectManifest);

            _discImportCancellation?.Dispose();
            _discImportCancellation = null;
            _activeMp3DiscManifest = null;
            IsDiscImporting = false;
            IsBusy = false;
            _trackStateUpdateQueueService.RequestRefresh();
        }

        await WaitForLiveMp3DiscPreparationAsync(
            projectManifest,
            projectFolder,
            liveConversionTasks,
            liveConversionTasksLock);

        if (!isDeveloperShortTest &&
            !await PrepareCompletedMp3DiscProjectForCurrentPresetAsync(projectFolder))
        {
            return false;
        }

        _convertedFileCleanupService.DeletePartFiles(projectFolder);
        if (autoExportWhenComplete)
        {
            EndWorkflowStatusOperation(workflowOperationId);
            await RunFinalMp3DiscExportAsync(projectFolder, pauseBeforeMergeOverride);
            return false;
        }

        SetPipelineState(ProjectPipelineState.ReviewBeforeMerge);
        _isWaitingForManualMergeReview = true;
        _isCurrentProjectCompleted = false;
        _loadedResumeProjectNeedsDiscImport = false;
        _loadedResumeProjectIsMp3Disc = true;
        PublishWorkflowStatus(
            workflowOperationId,
            _mp3DiscWorkflowStatusAdapter.CreateReadySnapshot(
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

        _convertedFileCleanupService.DeletePartFiles(projectFolder);
    }

    private void ApplyMp3DiscUiStatus(Mp3DiscUiStatus status)
    {
        StatusText = status.StatusText;
        ExportProgressText = status.ExportProgressText;

        if (status.ExportProgressPercent.HasValue)
            ExportProgressPercent = status.ExportProgressPercent.Value;
    }

    private async Task RunFinalMp3DiscExportAsync(string projectFolder, bool? pauseBeforeMergeOverride)
    {
        _pauseBeforeMergeOverride = pauseBeforeMergeOverride;
        try
        {
            await RunCurrentExportPlanAsync(projectFolder, ProjectManifestTypes.Mp3DiscProject);
        }
        finally
        {
            _pauseBeforeMergeOverride = null;
        }
    }

    private void RefreshExistingMp3DiscTrackStateBeforeImport(
        string projectFolder,
        string sourceFolder,
        int totalDiscs)
    {
        _mp3DiscTrackReconciliationService.ReconcileImportedTrackPathsForExistingDiscs(
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

        TracksGrid.Items.Refresh();
        UpdateFinalStatus(Tracks.Count);
    }

    private async Task RefreshMp3DiscTrackListAfterDiscImportAsync(string projectFolder, string sourceFolder)
    {
        var statusText = StatusText;
        var progressText = ExportProgressText;
        var excludedPaths = new List<string>();

        SelectedFolder = sourceFolder;

        await LoadFolderAsync(
            projectFolder,
            metadataSourceFolder: sourceFolder,
            updateMetadata: false,
            removeGeneratedOrWorkFiles: false,
            trackNumberPreference: TrackNumberPreference.FileName,
            includeFile: path =>
            {
                var excluded = _trackWorkspaceFilterService.IsMp3DiscGeneratedPath(projectFolder, path);
                if (excluded)
                    excludedPaths.Add(path);
                return !excluded;
            },
            publishWorkflowStatus: false);

        _diagnosticLogService.WriteTrackScan(
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

        _loadedResumeProjectNeedsDiscImport = true;
        NotifyExportUiStateChanged();
    }

    private void WarnIfMp3DiscEjectFailed(string sourceFolder, int discNumber, int totalDiscs)
    {
        var ejected = _discDriveService.TryEjectDisc(sourceFolder);
        if (ejected)
            return;

        var message = discNumber < totalDiscs
            ? "BookStitch konnte das CD-Laufwerk nicht automatisch auswerfen.\n\nBitte die aktuelle CD manuell auswerfen und danach die nächste CD einlegen."
            : "BookStitch konnte das CD-Laufwerk nach der letzten CD nicht automatisch auswerfen.\n\nBitte die CD bei Bedarf manuell auswerfen.";

        AppDialogService.Warning(
            this,
            "CD-Laufwerk nicht ausgeworfen",
            message);
    }

    private void ShowMp3DiscAnalysisFailed(int discNumber, Exception ex)
    {
        AppDialogService.Error(
            this,
            $"CD {discNumber} nicht gelesen",
            "Die Audiodateien auf der eingelegten CD konnten nicht vollständig gelesen werden.",
            details: new[] { ex.Message });
        ApplyMp3DiscUiStatus(_mp3DiscUiStatusService.CreateAnalysisFailed(discNumber));
    }

    private void ShowAlreadyImportedMp3DiscWarning(int discNumber, int totalDiscs)
    {
        AppDialogService.Warning(
            this,
            $"CD {discNumber} wurde nicht importiert",
            $"Im Laufwerk liegt offenbar eine CD, die bereits importiert wurde.\n\nBitte CD {discNumber} von {totalDiscs} einlegen und den Import danach erneut starten bzw. fortsetzen.");
        ApplyMp3DiscUiStatus(_mp3DiscUiStatusService.CreateAlreadyImported(discNumber, totalDiscs));
    }

    private void MarkMp3DiscImportFailed(
        Mp3DiscProjectManifest manifest,
        string projectFolder,
        Exception ex)
    {
        TrySaveCurrentMp3DiscProjectSnapshot(manifest);

        _convertedFileCleanupService.DeletePartFiles(projectFolder);

        _notificationService.Notify(NotificationEvent.Error);
        AppDialogService.Error(
            this,
            "MP3-CD nicht importiert",
            "Beim Kopieren der MP3-CD in den lokalen Arbeitsordner ist ein Fehler aufgetreten.",
            details: new[] { ex.Message });
        ApplyMp3DiscUiStatus(_mp3DiscUiStatusService.CreateImportFailed());
    }

    private void QueueLiveConversionForCopiedFile(
        DiscCopiedFile copiedFile,
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
        var liveTrack = _mp3DiscPreparationService.BuildLiveConversionTrack(copiedFile);
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

        var conversionTask = _liveConversionWorkflowService.RunAsync(
            liveTrack,
            queuedItem,
            livePreset,
            liveConversionQueue,
            liveConversionSemaphore,
            MarkActiveTrack,
            () =>
            {
                onCompleted();
                QueueMp3DiscConvertedTrackUiUpdate(copiedFile, convertedPath, livePreset.DisplayName);
            },
            onFailed,
            token);

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
            var track = _mp3DiscTrackReconciliationService.FindTrackForCopiedFile(Tracks, copiedFile);
            if (track is null)
                return;

            track.FilePath = copiedFile.ImportedFile;
            track.FileName = Path.GetFileName(copiedFile.ImportedFile);
            track.RelativeFolder = _mp3DiscTrackReconciliationService.GetImportedRelativeFolder(copiedFile);

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
            var track = _mp3DiscTrackReconciliationService.FindTrackForCopiedFile(Tracks, copiedFile);
            if (track is null || !File.Exists(convertedPath))
                return;

            var file = new FileInfo(convertedPath);
            if (file.Length <= 0)
                return;

            track.PreparedConvertedPath = convertedPath;
            track.PreparedConvertedPreset = presetName;
            track.HasReusableConvertedFile = true;
            track.ConvertedSizeAvailable = true;
            track.ConvertedSizeMb = Math.Round(file.Length / 1024d / 1024d, 2);

        });
    }

    private async Task<bool> PrepareCompletedMp3DiscProjectForCurrentPresetAsync(string projectFolder)
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
                updateProgress: true);

            return true;
        }
        catch (Exception ex)
        {
            AppDialogService.Error(
                this,
                "Preset-Vorbereitung fehlgeschlagen",
                "Bereits importierte Dateien konnten nicht vollständig für das aktuelle Export-Preset vorbereitet werden.",
                details: ExportFailureDetailsService.SplitMessageLines(ex.Message));
            StatusText = "Preset-Vorbereitung fehlgeschlagen.";
            ExportProgressText = "Projekt ist noch nicht exportbereit.";
            return false;
        }
        finally
        {
            IsBusy = wasBusy;
        }
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
        CancellationToken token)
    {
        _mp3DiscPresetPreparationWorkflowService.StartBackgroundPreparation(
            Tracks,
            projectFolder,
            preset,
            convertedFolder,
            (sourcePath, convertedPath) => PreparedConvertedTrackReuseService.CanReuseForDiscProject(ProjectManifestTypes.Mp3DiscProject, sourcePath, convertedPath),
            sharedSemaphore,
            taskList,
            taskListLock,
            () => Volatile.Read(ref _mp3DiscBackgroundPresetPreparationActive) == 1,
            Math.Clamp(ResolveParallelJobCount() / 2, 1, Math.Max(1, ResolveParallelJobCount())),
            onCompleted,
            onFailed,
            token);
    }

    private async Task<int> PrepareMissingMp3DiscConvertedTracksForCurrentPresetAsync(
        string projectFolder,
        CancellationToken token,
        int? maxParallelOverride = null,
        bool updateProgress = false)
    {
        var preset = ExportPreset.Parse(SelectedExportPreset);
        var convertedFolder = ProjectFolderLayout.GetConvertedPresetFolder(projectFolder, preset.GetFolderName());
        Directory.CreateDirectory(convertedFolder);
        _convertedFileCleanupService.DeletePartFiles(convertedFolder);

        try
        {
            return await _mp3DiscPresetPreparationWorkflowService.PrepareMissingTracksAsync(
                Tracks,
                projectFolder,
                preset,
                convertedFolder,
                (sourcePath, convertedPath) => PreparedConvertedTrackReuseService.CanReuseForDiscProject(ProjectManifestTypes.Mp3DiscProject, sourcePath, convertedPath),
                Math.Clamp(maxParallelOverride ?? ResolveParallelJobCount(), 1, 40),
                updateProgress
                    ? ReportMp3DiscPresetPreparationProgress
                    : null,
                token);
        }
        finally
        {
            _convertedFileCleanupService.DeletePartFiles(convertedFolder);
        }
    }

    private void ReportMp3DiscPresetPreparationProgress(int completed, int total)
    {
        Dispatcher.BeginInvoke(() =>
        {
            var percent = total <= 0 ? 100 : Math.Clamp(completed * 100.0 / total, 0, 100);
            ApplyWorkflowStatusViewState(_workflowStatusFormatter.Format(
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
        string? expectedDiscSignature = null,
        Action<string>? acceptedSourceFolderChanged = null)
    {
        if (await TrySelectNextMp3DiscFromDriveRoundAsync(
                sourceFolder,
                discNumber,
                totalDiscs,
                importedDiscSignatures,
                token,
                expectedDiscSignature,
                acceptedSourceFolderChanged))
        {
            return DiscWaitDialogOutcome.Ready;
        }

        if (_settings.ExperimentalDriveRoundEnabled && string.IsNullOrWhiteSpace(expectedDiscSignature))
        {
            return await WaitForNextMp3DiscWithDriveRoundDialogAsync(
                sourceFolder,
                discNumber,
                totalDiscs,
                importedDiscSignatures,
                token,
                acceptedSourceFolderChanged);
        }

        return await _mp3DiscWaitDialogService.WaitForNextDiscAsync(
            this,
            sourceFolder,
            discNumber,
            totalDiscs,
            importedDiscSignatures,
            _ => { },
            _ => { },
            token,
            NotifyDiscPollingState,
            expectedDiscSignature);
    }

    private async Task<DiscWaitDialogOutcome> WaitForNextMp3DiscWithDriveRoundDialogAsync(
        string sourceFolder,
        int discNumber,
        int totalDiscs,
        HashSet<string> importedDiscSignatures,
        CancellationToken token,
        Action<string>? acceptedSourceFolderChanged)
    {
        string? acceptedSourceFolder = null;
        var request = new DiscWaitDialogRequest(
            discNumber,
            totalDiscs,
            "MP3-CD",
            $"Bitte CD {discNumber} von {totalDiscs} einlegen. Die Laufwerksrunde prüft automatisch alle aktiven Laufwerke in Reihenfolge.",
            "Bereits importierte CDs werden erkannt, übersprungen und nach Möglichkeit wieder ausgeworfen.",
            CreateDiscDriveDisplayName(sourceFolder),
            NotifyDiscPollingState);

        var accepted = await _discWaitDialogService.WaitForDiscAsync(
            this,
            request,
            async cancellationToken =>
            {
                var result = await CheckMp3DriveRoundForWaitDialogAsync(
                    sourceFolder,
                    discNumber,
                    totalDiscs,
                    importedDiscSignatures,
                    cancellationToken);
                if (!string.IsNullOrWhiteSpace(result.AcceptedSourceFolder))
                    acceptedSourceFolder = result.AcceptedSourceFolder;
                return result.PollingResult;
            },
            _ => { },
            _ => { },
            token);

        if (accepted == DiscWaitDialogOutcome.Ready && !string.IsNullOrWhiteSpace(acceptedSourceFolder))
            acceptedSourceFolderChanged?.Invoke(acceptedSourceFolder);

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
        CancellationToken token)
    {
        var round = BuildDriveRound(lastProcessedSourceFolder);
        if (round.Count == 0)
        {
            return new Mp3DriveRoundWaitResult(new DiscPollingResult(
                false,
                $"Bitte CD {discNumber} von {totalDiscs} einlegen.\n\nDie Laufwerksrunde ist aktiv, aber aktuell ist kein aktives Laufwerk verfügbar.",
                $"Warte auf CD {discNumber}: kein aktives Laufwerk verfügbar.",
                "Laufwerksrunde aktiv: kein aktives Laufwerk verfügbar."));
        }

        DiscPollingResult? lastProblem = null;
        foreach (var driveRoot in round)
        {
            token.ThrowIfCancellationRequested();
            var driveLetter = FormatDriveLetter(driveRoot);
            ExportProgressText = $"Laufwerksrunde aktiv: prüfe Laufwerk {driveLetter} …";

            var typeProbe = await Task.Run(
                () => _discDriveCandidateProbeService.ProbeType(driveRoot, DiscMediaKind.Mp3Disc),
                token);
            if (!typeProbe.IsAccepted)
            {
                WriteDriveRoundSkip("MP3-CD", discNumber, typeProbe);
                lastProblem = CreateDriveRoundWaitingResult(
                    discNumber,
                    totalDiscs,
                    "MP3-CD",
                    driveLetter,
                    typeProbe);
                continue;
            }

            Mp3DiscImportPrecheckResult precheck;
            try
            {
                precheck = await Task.Run(
                    () => _mp3DiscImportWorkflowService.AnalyzeDiscForImport(typeProbe.DriveRoot, importedDiscSignatures),
                    token);
            }
            catch (Exception ex)
            {
                _diagnosticLogService.WriteError($"Laufwerksrunde MP3-CD {discNumber}: {driveRoot} konnte nicht geprüft werden", ex);
                lastProblem = new DiscPollingResult(
                    false,
                    $"Laufwerk {driveLetter} konnte gerade nicht gelesen werden.\n\nBookStitch prüft gleich automatisch weiter.",
                    $"Warte auf CD {discNumber}: Laufwerk {driveLetter} konnte gerade nicht gelesen werden.",
                    $"Laufwerksrunde aktiv: Laufwerk {driveLetter} konnte gerade nicht gelesen werden.");
                continue;
            }

            if (precheck.IsAlreadyImported)
            {
                var duplicate = _discDriveCandidateProbeService.MarkDuplicate(typeProbe.DriveRoot, typeProbe.MediaKind);
                WriteDriveRoundSkip("MP3-CD", discNumber, duplicate);
                _discDriveService.TryEjectDisc(typeProbe.DriveRoot);
                lastProblem = new DiscPollingResult(
                    false,
                    $"Laufwerk {driveLetter}: Diese CD wurde bereits importiert und wurde nach Möglichkeit wieder ausgeworfen.\n\nDie Laufwerksrunde prüft automatisch weiter.",
                    $"Warte auf CD {discNumber}: Laufwerk {driveLetter} enthielt ein Duplikat.",
                    $"Laufwerksrunde aktiv: Laufwerk {driveLetter} Duplikat übersprungen.",
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
                lastProblem = CreateDriveRoundWaitingResult(
                    discNumber,
                    totalDiscs,
                    "MP3-CD",
                    driveLetter,
                    noFiles);
                continue;
            }

            SelectedFolder = typeProbe.DriveRoot;
            _diagnosticLogService.WriteApplicationEvent(
                "DRIVE ROUND",
                $"MP3-CD {discNumber}/{totalDiscs}: {typeProbe.DriveRoot} akzeptiert.");
            return new Mp3DriveRoundWaitResult(
                new DiscPollingResult(
                    true,
                    $"Neue MP3-CD in Laufwerk {driveLetter} erkannt. CD {discNumber} von {totalDiscs} wird vorbereitet …",
                    $"CD {discNumber} von {totalDiscs} in Laufwerk {driveLetter} erkannt. Import startet …",
                    $"Laufwerksrunde: Laufwerk {driveLetter} erkannt. Import startet …",
                    DiscPollingDisplayState.Ready),
                typeProbe.DriveRoot);
        }

        return new Mp3DriveRoundWaitResult(lastProblem ?? new DiscPollingResult(
            false,
            $"Bitte CD {discNumber} von {totalDiscs} einlegen.\n\nDie Laufwerksrunde prüft automatisch alle aktiven Laufwerke in Reihenfolge.",
            $"Warte auf CD {discNumber}: Laufwerksrunde aktiv.",
            "Laufwerksrunde aktiv: keine passende MP3-CD gefunden. Nächste Prüfung läuft automatisch …"));
    }


    private async Task<bool> TrySelectNextMp3DiscFromDriveRoundAsync(
        string lastProcessedSourceFolder,
        int discNumber,
        int totalDiscs,
        HashSet<string> importedDiscSignatures,
        CancellationToken token,
        string? expectedDiscSignature,
        Action<string>? acceptedSourceFolderChanged)
    {
        if (!_settings.ExperimentalDriveRoundEnabled || !string.IsNullOrWhiteSpace(expectedDiscSignature))
            return false;

        var round = BuildDriveRound(lastProcessedSourceFolder);
        if (round.Count == 0)
            return false;

        _diagnosticLogService.WriteApplicationEvent(
            "DRIVE ROUND",
            $"MP3-CD {discNumber}/{totalDiscs}: prüfe {string.Join(", ", round)} nach {lastProcessedSourceFolder}.");

        foreach (var driveRoot in round)
        {
            token.ThrowIfCancellationRequested();

            var typeProbe = await Task.Run(
                () => _discDriveCandidateProbeService.ProbeType(driveRoot, DiscMediaKind.Mp3Disc),
                token);
            if (!typeProbe.IsAccepted)
            {
                WriteDriveRoundSkip("MP3-CD", discNumber, typeProbe);
                continue;
            }

            Mp3DiscImportPrecheckResult precheck;
            try
            {
                precheck = await Task.Run(
                    () => _mp3DiscImportWorkflowService.AnalyzeDiscForImport(typeProbe.DriveRoot, importedDiscSignatures),
                    token);
            }
            catch (Exception ex)
            {
                _diagnosticLogService.WriteError($"Laufwerksrunde MP3-CD {discNumber}: {driveRoot} konnte nicht geprüft werden", ex);
                continue;
            }

            if (precheck.IsAlreadyImported)
            {
                var duplicate = _discDriveCandidateProbeService.MarkDuplicate(typeProbe.DriveRoot, typeProbe.MediaKind);
                WriteDriveRoundSkip("MP3-CD", discNumber, duplicate);
                _discDriveService.TryEjectDisc(typeProbe.DriveRoot);
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
            StatusText = $"Laufwerksrunde: MP3-CD {discNumber} von {totalDiscs} in Laufwerk {FormatDriveLetter(typeProbe.DriveRoot)} erkannt.";
            ExportProgressText = "Laufwerksrunde aktiv: Import wird fortgesetzt …";
            _diagnosticLogService.WriteApplicationEvent(
                "DRIVE ROUND",
                $"MP3-CD {discNumber}/{totalDiscs}: {typeProbe.DriveRoot} akzeptiert.");
            return true;
        }

        _diagnosticLogService.WriteApplicationEvent(
            "DRIVE ROUND",
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
            DiscDriveCandidateStatus.Unavailable => $"Laufwerk {driveLetter} ist nicht verbunden.",
            DiscDriveCandidateStatus.Empty => $"Laufwerk {driveLetter} ist leer.",
            DiscDriveCandidateStatus.WrongType => $"Laufwerk {driveLetter} enthält keinen passenden Datenträger.",
            DiscDriveCandidateStatus.Duplicate => $"Laufwerk {driveLetter} enthält ein Duplikat.",
            _ => $"Laufwerk {driveLetter} wurde übersprungen."
        };

        var displayState = result.Status == DiscDriveCandidateStatus.Duplicate
            ? DiscPollingDisplayState.Duplicate
            : result.Status == DiscDriveCandidateStatus.WrongType
                ? DiscPollingDisplayState.Unsupported
                : DiscPollingDisplayState.Waiting;

        return new DiscPollingResult(
            false,
            $"{message}\n\nBitte {mediaName} {discNumber} von {totalDiscs} einlegen. Die Laufwerksrunde prüft automatisch weiter.",
            $"Warte auf {mediaName} {discNumber}: {message}",
            $"Laufwerksrunde aktiv: {driveLetter} übersprungen – {message}",
            displayState);
    }

    private IReadOnlyList<string> BuildDriveRound(string lastProcessedSourceFolder)
    {
        IReadOnlyList<DiscDriveInfo> detectedDrives;
        try
        {
            detectedDrives = _discDriveService.GetCdDrives();
        }
        catch
        {
            detectedDrives = [];
        }

        var configured = _discDriveConfigurationService.Synchronize(_settings, detectedDrives);
        SaveSettingsIfReady();
        return _discDriveRotationService.BuildRound(configured, lastProcessedSourceFolder);
    }

    private void WriteDriveRoundSkip(string projectKind, int discNumber, DiscDriveCandidateResult result)
    {
        _diagnosticLogService.WriteApplicationEvent(
            "DRIVE ROUND",
            $"{projectKind} {discNumber}: {result.DriveRoot} übersprungen ({result.Status}, {result.MediaKind}) {result.Message}".Trim());
    }

    private static string FormatDriveLetter(string driveRoot)
    {
        try
        {
            var root = Path.GetPathRoot(driveRoot) ?? driveRoot;
            return root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return driveRoot;
        }
    }


    private void AppendDiscPreviewTracks(string sourceFolder, int discNumber, bool clearExistingTracks)
    {
        var sourceTracks = _folderScanner.Scan(sourceFolder, TrackNumberPreference.FileName);
        AppendDiscPreviewTracks(sourceTracks, sourceFolder, discNumber, clearExistingTracks);
    }

    private void AppendDiscPreviewTracks(
        IEnumerable<TrackInfo> sourceTracks,
        int discNumber,
        bool clearExistingTracks)
    {
        AppendDiscPreviewTracks(sourceTracks, string.Empty, discNumber, clearExistingTracks);
    }

    private void AppendDiscPreviewTracks(
        IEnumerable<TrackInfo> sourceTracks,
        string sourceFolder,
        int discNumber,
        bool clearExistingTracks)
    {
        _mp3DiscTrackReconciliationService.AppendMissingPreviewTracks(
            Tracks,
            sourceTracks,
            sourceFolder,
            discNumber,
            clearExistingTracks);

        UpdateIndexes();
    }

    private void ShowUnsupportedDiscSourceMessage(DiscSourceAnalysis analysis)
    {
        if (analysis.IsProbablyAudioCd)
        {
            AppDialogService.Warning(
                this,
                "Audio-CD noch nicht unterstützt",
                "Die ausgewählte Quelle sieht nach einer Audio-CD aus. Echte Audio-CDs haben keine normalen MP3-Dateien und brauchen später eine eigene Ripping-Pipeline.\n\n" +
                "Aktuell ist nur MP3-CD / Daten-CD mit normalen Audiodateien aktiv.");
            return;
        }

        AppDialogService.Warning(
            this,
            "Keine MP3-CD erkannt",
            "Auf der ausgewählten Quelle wurden keine unterstützten Audiodateien gefunden.\n\n" +
            "Unterstützt werden aktuell: MP3, AAC, M4A, M4B, WAV und FLAC.");
    }

    private void ResetMetadataForNewFolderProject()
    {
        ResetMetadataForNewDiscProject();
    }

    private void ResetMetadataForNewDiscProject()
    {
        _currentProjectWorkFolder = "";
        _pendingDiscProjectSourceFolder = "";
        _loadedResumeProjectNeedsDiscImport = false;
        _loadedResumeProjectIsMp3Disc = false;
        _loadedResumeProjectIsAudioDisc = false;
        _loadedResumeProjectIsLocal = false;
        _isAudioDiscProjectAwaitingRip = false;
        OnPropertyChanged(nameof(CanStartExport));
        OnPropertyChanged(nameof(ExportButtonText));
        OnPropertyChanged(nameof(AlbumLinkToggleToolTip));
        BookTitle = "";
        Album = "";
        Series = "";
        Author = "";
        Narrator = "";
        _coverSourcePath = "";
        _processedCoverPath = "";
        CoverPreviewSource = "";
        OnExportPreviewChanged();
    }

    private void Cover_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!CanChangeBookMetadata || _isCoverDialogOpen)
            return;

        _isCoverDialogOpen = true;
        try
        {
            var dialog = new OpenFileDialog
            {
                Title = "Coverbild auswählen",
                Filter = "Bilddateien (*.jpg;*.jpeg;*.png;*.webp)|*.jpg;*.jpeg;*.png;*.webp|Alle Dateien (*.*)|*.*",
                Multiselect = false,
                InitialDirectory = ResolveMainCoverDialogInitialDirectory()
            };

            if (dialog.ShowDialog(this) != true)
                return;

            SetCoverFromFile(dialog.FileName);
        }
        finally
        {
            _isCoverDialogOpen = false;
            e.Handled = true;
        }
    }

    private void Cover_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (IsExporting || string.IsNullOrWhiteSpace(_processedCoverPath))
            e.Handled = true;
    }

    private void RemoveCover_Click(object sender, RoutedEventArgs e)
    {
        if (!CanChangeBookMetadata || string.IsNullOrWhiteSpace(_processedCoverPath))
            return;

        _coverSourcePath = "";
        _processedCoverPath = "";
        CoverPreviewSource = "";
        StatusText = "Cover entfernt.";
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
                "Cover nicht übernommen",
                "Bitte eine Bilddatei ablegen. Unterstützt werden JPG, PNG und WebP.");
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

    private string ResolveMainCoverDialogInitialDirectory()
    {
        if (IsLikelyLocalSourceFolderForCoverDialog(_currentFolderPath))
            return _currentFolderPath;

        return GetLastExternalCoverFolderOrDesktop();
    }

    private string ResolveProjectSetupCoverInitialDirectory(ProjectSetupSourceKind sourceKind, string sourceFolder)
    {
        if (sourceKind == ProjectSetupSourceKind.Folder && IsValidExternalCoverFolder(sourceFolder))
            return sourceFolder;

        return GetLastExternalCoverFolderOrDesktop();
    }

    private string GetCoverSourcePathForProjectSetup()
    {
        return IsInternalBookStitchPath(_coverSourcePath)
            ? string.Empty
            : _coverSourcePath;
    }

    private string GetLastExternalCoverFolderOrDesktop()
    {
        return IsValidExternalCoverFolder(_settings.LastCoverFolder)
            ? _settings.LastCoverFolder!
            : GetDesktopFolder();
    }

    private bool IsLikelyLocalSourceFolderForCoverDialog(string? folderPath)
    {
        if (!IsValidExternalCoverFolder(folderPath))
            return false;

        if (_loadedResumeProjectIsMp3Disc || _loadedResumeProjectIsAudioDisc || _isAudioDiscProjectAwaitingRip)
            return false;

        return !IsOpticalDriveSelection(folderPath);
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
        if (!string.IsNullOrWhiteSpace(_processedCoverPath) && File.Exists(_processedCoverPath))
            return;

        try
        {
            var coverFolder = GetProjectFolderStructure().CoversFolder;
            var embeddedCoverPath = _embeddedCoverService.ExtractFirstValidCover(
                tracks.Select(track => track.FilePath),
                coverFolder);

            if (string.IsNullOrWhiteSpace(embeddedCoverPath))
                return;

            SetCoverFromFile(embeddedCoverPath);
        }
        catch (Exception ex)
        {
            _diagnosticLogService.WriteError("Eingebettetes Cover konnte nicht übernommen werden", ex);
        }
    }

    private void SetCoverFromFile(string filePath)
    {
        if (!IsSupportedCoverFile(filePath))
        {
            AppDialogService.Warning(
                this,
                "Cover nicht übernommen",
                "Dieses Bildformat wird nicht unterstützt. Bitte JPG, PNG oder WebP verwenden.");
            return;
        }

        try
        {
            var coverFolder = GetProjectFolderStructure().CoversFolder;
            var result = _coverImageService.CreateProcessedCover(filePath, coverFolder);

            _coverSourcePath = result.SourcePath;
            _processedCoverPath = result.ProcessedJpegPath;
            CoverPreviewSource = result.ProcessedJpegPath;

            var selectedCoverFolder = Path.GetDirectoryName(result.SourcePath);
            if (IsValidExternalCoverFolder(selectedCoverFolder))
            {
                _settings.LastCoverFolder = selectedCoverFolder;
                SaveSettingsIfReady();
            }

            if (!string.IsNullOrWhiteSpace(result.Warning))
            {
                AppDialogService.Warning(
                    this,
                    "Cover übernommen",
                    result.Warning);
            }
            else
            {
                StatusText = "Cover übernommen und auf 2000 × 2000 vorbereitet.";
            }
        }
        catch (Exception ex)
        {
            AppDialogService.Error(
                this,
                "Cover konnte nicht übernommen werden",
                "Das Coverbild konnte nicht verarbeitet werden.",
                new[] { ex.Message });
        }
    }

    private void MetadataHeader_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        SetMetadataPanelExpanded(!_isMetadataPanelExpanded, animate: true);
        e.Handled = true;
    }


    private bool IsOpticalDriveSelection(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        var root = Path.GetPathRoot(Path.GetFullPath(path));
        return !string.IsNullOrWhiteSpace(root) && _discDriveService.IsCdDrivePath(root);
    }

    private void SetMetadataPanelExpanded(bool expanded, bool animate)
    {
        _isMetadataPanelExpanded = expanded;
        OnPropertyChanged(nameof(MetadataToggleGlyph));

        if (MetadataDetailsBorder is null)
            return;

        const double expandedHeight = 98;
        var targetHeight = expanded ? expandedHeight : 0;
        var targetOpacity = expanded ? 1d : 0d;

        if (!animate)
        {
            MetadataDetailsBorder.Height = targetHeight;
            MetadataDetailsBorder.Opacity = targetOpacity;
            return;
        }

        var animationMilliseconds = Math.Clamp(_settings.MetadataPanelAnimationMilliseconds, 0, 2000);
        var duration = TimeSpan.FromMilliseconds(animationMilliseconds);
        var easing = new CubicEase { EasingMode = EasingMode.EaseInOut };
        MetadataDetailsBorder.BeginAnimation(HeightProperty, new DoubleAnimation(targetHeight, duration) { EasingFunction = easing });
        MetadataDetailsBorder.BeginAnimation(OpacityProperty, new DoubleAnimation(targetOpacity, duration) { EasingFunction = easing });
    }


    private void AlbumLabel_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _settings.KeepAlbumLinkedToTitle = !_settings.KeepAlbumLinkedToTitle;
        SaveSettingsIfReady();

        if (IsTitleAlbumLinked)
            Album = BookTitle;

        OnPropertyChanged(nameof(AlbumLinkToggleToolTip));
        OnPropertyChanged(nameof(IsAlbumTabStop));
        e.Handled = true;
    }

    private void GenreComboBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Tab || !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            return;

        AlbumTextBox?.Focus();
        e.Handled = true;
    }

    private void OpenTaskManager_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("taskmgr.exe") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppDialogService.Warning(this, "Task-Manager konnte nicht geöffnet werden", ex.Message);
        }
    }

    private void ChooseOutputFolder_Click(object sender, RoutedEventArgs e)
    {
        ChooseOutputFolder();
    }

    private void OutputFolderPath_Click(object sender, MouseButtonEventArgs e)
    {
        ChooseOutputFolder();
        e.Handled = true;
    }

    private void ChooseOutputFolder()
    {
        if (IsBusy)
            return;

        var dialog = new OpenFolderDialog
        {
            Title = "Ausgabeordner auswählen",
            Multiselect = false
        };

        dialog.InitialDirectory = !string.IsNullOrWhiteSpace(OutputFolder) && Directory.Exists(OutputFolder)
            ? OutputFolder
            : GetDesktopFolder();

        if (dialog.ShowDialog(this) != true)
            return;

        OutputFolder = dialog.FolderName;
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

        return _discDriveService.IsCdDrivePath(SelectedFolder)
            ? SelectedFolder
            : string.Empty;
    }

    private string BuildSelectedSourceDisplayText(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) ||
            string.Equals(path, "Noch kein Ordner ausgewählt.", StringComparison.Ordinal))
        {
            return path ?? string.Empty;
        }

        if (_discDriveService.IsCdDrivePath(path))
        {
            var root = Path.GetPathRoot(path) ?? path;
            var letter = root.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar, ':');
            return string.IsNullOrWhiteSpace(letter) ? "CD-Laufwerk" : $"CD-Laufwerk {letter}";
        }

        try
        {
            var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var folderName = Path.GetFileName(trimmed);
            return string.IsNullOrWhiteSpace(folderName) ? path : folderName;
        }
        catch
        {
            return path;
        }
    }

    private void OpenSelectedFolder_Click(object sender, RoutedEventArgs e)
    {
        OpenFolderInExplorer(SelectedSourceOpenPath, "Quelle oder Projekt");
    }

    private void AutoMergeLabel_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        MergeAutomaticallyAfterConversion = !MergeAutomaticallyAfterConversion;
    }

    private void OpenOutputFolder_Click(object sender, RoutedEventArgs e)
    {
        OpenFolderInExplorer(OutputFolder, "Ausgabeordner");
    }

    private void OpenWorkFolder_Click(object sender, RoutedEventArgs e)
    {
        OpenFolderInExplorer(GetBestWorkFolderToOpen(), "Projektordner");
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
                "FFmpeg nicht gefunden",
                "Die aktuelle FFmpeg-Installation wurde nicht gefunden. Bitte richte FFmpeg über „FFmpeg einrichten …“ ein.");
            return;
        }

        OpenFolderInExplorer(Path.GetDirectoryName(ffmpegPath) ?? string.Empty, "FFmpeg-Ordner");
    }

    private async void BookStitchLogo_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2 || !_settings.ShowDeveloperTab || IsBusy)
            return;

        e.Handled = true;
        await StartDeveloperAudioDiscTestAsync();
    }

    private void MainWindow_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!IsBookStitchLogoSource(e.OriginalSource as DependencyObject))
            return;

        e.Handled = true;
        var menu = CreateDeveloperLogoContextMenu();
        menu.PlacementTarget = this;
        menu.Placement = PlacementMode.MousePoint;
        menu.IsOpen = true;
    }

    private static bool IsBookStitchLogoSource(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is Image image && IsBookStitchLogoImage(image))
                return true;

            source = VisualTreeHelper.GetParent(source);
        }

        return false;
    }

    private static bool IsBookStitchLogoImage(Image image)
    {
        var sourceText = image.Source?.ToString() ?? string.Empty;
        return Math.Abs(image.Width - 38d) < 0.1d &&
               Math.Abs(image.Height - 38d) < 0.1d &&
               sourceText.Contains("BookStitchLogo-Round.png", StringComparison.OrdinalIgnoreCase);
    }

    private ContextMenu CreateDeveloperLogoContextMenu()
    {
        var menu = new ContextMenu
        {
            Style = TryFindResource("BookStitchContextMenuStyle") as Style
        };

        menu.Items.Add(CreateDeveloperCheckMenuItem(
            "Entwicklermodus",
            _settings.ShowDeveloperTab,
            () => SetDeveloperMode(!_settings.ShowDeveloperTab)));

        menu.Items.Add(new Separator());

        menu.Items.Add(CreateDeveloperCheckMenuItem(
            "Trackzustand im Hauptfenster anzeigen",
            _settings.ShowPipelineStateDebug,
            () => SetPipelineStateDebugVisible(!_settings.ShowPipelineStateDebug)));

        menu.Items.Add(CreateDeveloperCheckMenuItem(
            "FFmpeg-Einstellungsbutton immer anzeigen",
            _settings.ForceShowFfmpegSetupButton,
            () => SetForceShowFfmpegSetupButton(!_settings.ForceShowFfmpegSetupButton)));

        menu.Items.Add(new Separator());

        menu.Items.Add(CreateDeveloperActionMenuItem(
            "Audio-CD-Kurztest starten",
            async () =>
            {
                EnsureDeveloperModeEnabled();
                await StartDeveloperAudioDiscTestAsync();
            }));

        menu.Items.Add(new Separator());

        menu.Items.Add(CreateDeveloperActionMenuItem(
            "Entwickler-Tab zeigen",
            () =>
            {
                EnsureDeveloperModeEnabled();
                ShowSettingsDialog(this, openDeveloperTab: true);
                return Task.CompletedTask;
            }));

        return menu;
    }

    private MenuItem CreateDeveloperCheckMenuItem(string text, bool isChecked, Action action)
    {
        var item = CreateDeveloperMenuItem($"{(isChecked ? "✓" : "□")} {text}");
        item.StaysOpenOnClick = false;
        item.Click += (_, _) => action();
        return item;
    }

    private MenuItem CreateDeveloperActionMenuItem(string text, Func<Task> action)
    {
        var item = CreateDeveloperMenuItem(text);
        item.Click += async (_, _) => await action();
        return item;
    }

    private MenuItem CreateDeveloperMenuItem(string text)
    {
        var item = new MenuItem
        {
            Header = text,
            Style = TryFindResource("BookStitchContextMenuItemStyle") as Style
        };
        return item;
    }

    private void SetDeveloperMode(bool enabled)
    {
        _settings.ShowDeveloperTab = enabled;
        if (!enabled)
        {
            _settings.ShowPipelineStateDebug = false;
            _settings.ForceShowFfmpegSetupButton = false;
        }

        SaveSettingsIfReady();
        NotifyDeveloperModeSettingsChanged();
    }

    private void EnsureDeveloperModeEnabled()
    {
        if (_settings.ShowDeveloperTab)
            return;

        _settings.ShowDeveloperTab = true;
        SaveSettingsIfReady();
        NotifyDeveloperModeSettingsChanged();
    }

    private void SetPipelineStateDebugVisible(bool visible)
    {
        if (visible)
            _settings.ShowDeveloperTab = true;

        _settings.ShowPipelineStateDebug = visible;
        SaveSettingsIfReady();
        NotifyDeveloperModeSettingsChanged();
    }

    private void SetForceShowFfmpegSetupButton(bool visible)
    {
        if (visible)
            _settings.ShowDeveloperTab = true;

        _settings.ForceShowFfmpegSetupButton = visible;
        SaveSettingsIfReady();
        NotifyDeveloperModeSettingsChanged();
    }

    private void NotifyDeveloperModeSettingsChanged()
    {
        OnPropertyChanged(nameof(PipelineStateDebugVisibility));
        OnPropertyChanged(nameof(PipelineStateDebugText));
        OnPropertyChanged(nameof(FfmpegSetupButtonVisibility));
    }

    private async Task StartDeveloperAudioDiscTestAsync()
    {
        if (IsBusy)
            return;

        var drives = await Task.Run(() => _discDriveService.GetCdDrives());
        var initialAudioTemplateFolder = Directory.Exists(_settings.LastDeveloperAudioDiscTestFolder)
            ? _settings.LastDeveloperAudioDiscTestFolder
            : GetDesktopFolder();
        var initialMp3TemplateFolder = Directory.Exists(_settings.LastDeveloperMp3DiscTestFolder)
            ? _settings.LastDeveloperMp3DiscTestFolder
            : GetDesktopFolder();
        var initiallySelectMp3Disc = string.Equals(_settings.LastDeveloperDiscTestType, "Mp3Disc", StringComparison.OrdinalIgnoreCase);
        var dialog = new DeveloperAudioDiscTestDialog(drives, initialAudioTemplateFolder, initialMp3TemplateFolder, initiallySelectMp3Disc)
        {
            Owner = this
        };
        if (dialog.ShowDialog() != true || dialog.SelectedDrive is null)
            return;

        _settings.LastDeveloperDiscTestType = dialog.IsMp3DiscTest ? "Mp3Disc" : "AudioCd";
        SaveSettingsIfReady();

        try
        {
            if (dialog.IsMp3DiscTest)
            {
                await StartDeveloperMp3DiscTestAsync(dialog);
                return;
            }

            var preparation = new DeveloperAudioDiscTestProjectService(
                _audioDiscProjectService,
                _workManifestService).Prepare(
                    dialog.ProjectFolder,
                    GetProjectFolderStructure().AudioDiscProjectsFolder,
                    dialog.DiscNumber,
                    dialog.ResetTrackCount,
                    dialog.TotalDiscs,
                    dialog.SelectedDrive);

            _settings.LastDeveloperAudioDiscTestFolder = preparation.TemplateProjectFolder;
            SaveSettingsIfReady();

            var manifest = preparation.Manifest;
            _activeAudioDiscManifest = manifest;
            _loadedResumeProjectIsAudioDisc = true;
            _loadedResumeProjectIsMp3Disc = false;
            _loadedResumeProjectIsLocal = false;
            _currentProjectWorkFolder = manifest.ProjectFolder;
            _currentFolderPath = ProjectFolderLayout.GetOriginalsFolder(manifest.ProjectFolder);
            SelectedFolder = dialog.SelectedDrive.RootPath;
            SetAudioDiscSourceDisplayOverride(manifest);
            _isAudioDiscProjectAwaitingRip = true;
            _isWaitingForManualMergeReview = false;
            _isCurrentProjectCompleted = false;
            SetPipelineState(ProjectPipelineState.AcquiringSources);

            void ApplyShortTestProjectSettings()
            {
                ApplyLoadedTitleAndAlbum(preparation.Title, preparation.Album);
                Author = preparation.Author;
                Narrator = preparation.Narrator;
                Genre = preparation.Genre;
                SetMetadataEditingAvailable(true);
                if (!string.IsNullOrWhiteSpace(preparation.SelectedPreset))
                    SetSelectedExportPresetSilently(preparation.SelectedPreset);
                if (!string.IsNullOrWhiteSpace(preparation.ParallelJobs))
                    SetParallelJobsInput(preparation.ParallelJobs, showMessage: false);
            }

            ApplyShortTestProjectSettings();
            if (!string.IsNullOrWhiteSpace(manifest.OutputFolder))
                OutputFolder = manifest.OutputFolder;
            if (!string.IsNullOrWhiteSpace(manifest.OutputExtension))
                OutputExtension = manifest.OutputExtension;
            if (!string.IsNullOrWhiteSpace(manifest.FileNameTemplate))
                FileNameTemplate = manifest.FileNameTemplate;
            if (!string.IsNullOrWhiteSpace(preparation.CoverFilePath))
                SetCoverFromFile(preparation.CoverFilePath);
            else
            {
                _coverSourcePath = string.Empty;
                _processedCoverPath = string.Empty;
                CoverPreviewSource = string.Empty;
            }

            RefreshActiveAudioDiscPreviewMetadata();
            SaveCurrentAudioDiscProjectSnapshot(manifest);

            Tracks.Clear();
            foreach (var track in _audioDiscProjectService.CreateTrackPreview(manifest))
                Tracks.Add(track);
            UpdateIndexes();
            TracksGrid.Items.Refresh();
            AutoFitTrackColumnsAfterRender();
            NotifyExportUiStateChanged();

            StatusText = $"Audio-CD-Kurztest-Arbeitskopie vorbereitet • CD {preparation.DiscNumber} • Tracks {preparation.FirstResetTrackNumber:00} bis {preparation.FirstResetTrackNumber + preparation.ResetTrackCount - 1:00}";
            ExportProgressText = "Der Audio-CD-Testlauf wird gestartet …";
            _developerAudioDiscTestDriveRoot = dialog.SelectedDrive.RootPath;
            try
            {
                await RunPreparedAudioDiscRipAsync(
                    restoreUiState: ApplyShortTestProjectSettings,
                    queueExistingRippedTracks: false);
            }
            finally
            {
                _developerAudioDiscTestDriveRoot = null;
            }
        }
        catch (Exception ex)
        {
            AppDialogService.Error(this, dialog.IsMp3DiscTest
                ? "MP3-CD-Kurztest konnte nicht vorbereitet werden"
                : "Audio-CD-Kurztest konnte nicht vorbereitet werden", ex.Message);
        }
    }


    private async Task StartDeveloperMp3DiscTestAsync(DeveloperAudioDiscTestDialog dialog)
    {
        if (dialog.SelectedDrive is null)
            return;

        var preparation = new DeveloperMp3DiscTestProjectService(
            _mp3DiscProjectService,
            _workManifestService).Prepare(
                dialog.ProjectFolder,
                GetProjectFolderStructure().Mp3DiscProjectsFolder,
                dialog.DiscNumber,
                dialog.ResetTrackCount,
                dialog.TotalDiscs,
                dialog.SelectedDrive);

        _settings.LastDeveloperMp3DiscTestFolder = preparation.TemplateProjectFolder;
        SaveSettingsIfReady();

        var manifest = preparation.Manifest;
        _activeMp3DiscManifest = manifest;
        _activeAudioDiscManifest = null;
        _loadedResumeProjectIsMp3Disc = true;
        _loadedResumeProjectIsAudioDisc = false;
        _loadedResumeProjectIsLocal = false;
        _loadedResumeProjectNeedsDiscImport = true;
        _currentProjectWorkFolder = manifest.ProjectFolder;
        _currentFolderPath = manifest.ProjectFolder;
        SelectedFolder = dialog.SelectedDrive.RootPath;
        _isWaitingForManualMergeReview = false;
        _isCurrentProjectCompleted = false;
        SetPipelineState(ProjectPipelineState.AcquiringSources);

        ApplyLoadedTitleAndAlbum(preparation.Title, preparation.Album);
        Author = preparation.Author;
        Narrator = preparation.Narrator;
        Genre = preparation.Genre;
        SetMetadataEditingAvailable(true);
        if (!string.IsNullOrWhiteSpace(preparation.SelectedPreset))
            SetSelectedExportPresetSilently(preparation.SelectedPreset);
        if (!string.IsNullOrWhiteSpace(manifest.OutputFolder))
            OutputFolder = manifest.OutputFolder;
        if (!string.IsNullOrWhiteSpace(manifest.OutputExtension))
            OutputExtension = manifest.OutputExtension;
        if (!string.IsNullOrWhiteSpace(manifest.FileNameTemplate))
            FileNameTemplate = manifest.FileNameTemplate;
        if (!string.IsNullOrWhiteSpace(preparation.ParallelJobs))
            SetParallelJobsInput(preparation.ParallelJobs, showMessage: false);

        if (!string.IsNullOrWhiteSpace(preparation.CoverFilePath))
            SetCoverFromFile(preparation.CoverFilePath);
        else
        {
            _coverSourcePath = string.Empty;
            _processedCoverPath = string.Empty;
            CoverPreviewSource = string.Empty;
        }

        SaveCurrentMp3DiscProjectSnapshot(manifest);
        NotifyExportUiStateChanged();
        StatusText = $"MP3-CD-Kurztest-Arbeitskopie vorbereitet • CD {preparation.DiscNumber} • Dateien {preparation.FirstResetTrackNumber:00} bis {preparation.FirstResetTrackNumber + preparation.ResetTrackCount - 1:00}";
        ExportProgressText = "Der MP3-CD-Testlauf wird gestartet …";

        await ImportMp3DiscProjectAsync(
            manifest.ProjectFolder,
            dialog.SelectedDrive.RootPath,
            manifest,
            startDiscNumber: preparation.DiscNumber,
            firstDiscAlreadyReady: false,
            autoExportWhenComplete: false,
            isDeveloperShortTest: true,
            expectedFirstDiscSignature: preparation.ExpectedDiscSignature);
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        if (!CanOpenSettings)
            return;

        ShowSettingsDialog(this);
    }

    private void ShowSettingsDialog(Window owner, bool openDeveloperTab = false)
    {
        var previousUseLeadingZerosInChapterSuggestions = _settings.UseLeadingZerosInChapterSuggestions;

        if (openDeveloperTab)
            EnsureDeveloperModeEnabled();

        var settingsWindow = new SettingsWindow(
            _settings,
            _settingsService,
            _notificationService,
            days => DeleteOldProjects(days, showUserMessage: false),
            _isMetadataPanelExpanded,
            expanded => SetMetadataPanelExpanded(expanded, animate: true),
            enabled => MergeAutomaticallyAfterConversion = enabled,
            _ =>
            {
                OnPropertyChanged(nameof(PipelineStateDebugVisibility));
                OnPropertyChanged(nameof(PipelineStateDebugText));
            },
            PreviewDiscSourceAnalysisAsync,
            StartDeveloperAudioDiscTestAsync,
            openDeveloperTab)
        {
            Owner = owner
        };

        settingsWindow.ShowDialog();

        if (previousUseLeadingZerosInChapterSuggestions != _settings.UseLeadingZerosInChapterSuggestions)
        {
            UpdateIndexes();
            PersistTrackListState();
        }

        OutputExtension = _settings.DefaultOutputExtension;
        FileNameTemplate = _settings.DefaultFileNameTemplate;
        if (IsTitleAlbumLinked)
            Album = BookTitle;

        OnPropertyChanged(nameof(AlbumLinkToggleToolTip));
        OnPropertyChanged(nameof(IsAlbumTabStop));
        OnPropertyChanged(nameof(FfmpegSetupButtonVisibility));
        OnPropertyChanged(nameof(MergeAutomaticallyAfterConversion));
        OnPropertyChanged(nameof(PipelineStateDebugVisibility));
        OnPropertyChanged(nameof(PipelineStateDebugText));
        OnPropertyChanged(nameof(CanStartNewProject));
        OnPropertyChanged(nameof(ExportPreviewFileName));
        RefreshResumeProjects(showStatus: false);
        owner.Activate();
        owner.Focus();
    }

    private DiscProjectSetupDialog CreateProjectSetupDialog(ProjectSetupDialogRequest request)
    {
        var dialog = new DiscProjectSetupDialog(
            request,
            openAdvancedSettings: owner =>
            {
                ShowSettingsDialog(owner);
                return new ProjectSetupDialogGlobalSettings(
                    _settings.MergeAutomaticallyAfterConversion,
                    _settings.KeepAlbumLinkedToTitle,
                    _settings.DefaultOutputExtension,
                    _settings.DefaultFileNameTemplate);
            },
            setAlbumLink: linked =>
            {
                _settings.KeepAlbumLinkedToTitle = linked;
                SaveSettingsIfReady();
                OnPropertyChanged(nameof(AlbumLinkToggleToolTip));
                OnPropertyChanged(nameof(IsAlbumTabStop));
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
            previewCoverChanged: (sourcePath, processedPath) =>
            {
                _coverSourcePath = sourcePath;
                _processedCoverPath = processedPath;
                CoverPreviewSource = processedPath;
            })
        {
            Owner = this
        };

        return dialog;
    }

    private static string BuildProjectSetupDurationText(IEnumerable<TrackInfo> tracks)
    {
        var totalTicks = tracks.Where(track => track.DurationTicks.HasValue).Sum(track => track.DurationTicks!.Value);
        if (totalTicks <= 0)
            return "00:00:00";

        var duration = TimeSpan.FromTicks(totalTicks);
        return $"{(int)duration.TotalHours}:{duration.Minutes:00}:{duration.Seconds:00}";
    }

    private ProjectCleanupResult DeleteOldProjects(bool showUserMessage)
    {
        return DeleteOldProjects(_settings.ProjectRetentionDays, showUserMessage);
    }

    private ProjectCleanupResult DeleteOldProjects(int olderThanDays, bool showUserMessage)
    {
        ProjectCleanupResult result;

        try
        {
            var nowUtc = DateTime.UtcNow;
            result = _projectIndexService.DeleteProjectsOlderThan(
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
            result = new ProjectCleanupResult(0, 0, [$"Projektbereinigung fehlgeschlagen: {ex.Message}"]);
        }

        if (result.DeletedCount > 0)
            RefreshResumeProjects(showStatus: false);

        if (showUserMessage && result.DeletedCount > 0)
            StatusText = result.DeletedCount == 1
                ? "1 altes Projekt wurde gelöscht."
                : $"{result.DeletedCount} alte Projekte wurden gelöscht.";

        return result;
    }

    private IReadOnlyList<string> DeleteOldUnreferencedCoverFiles(DateTime nowUtc, int olderThanDays)
    {
        var failures = new List<string>();
        var coversFolder = GetProjectFolderStructure().CoversFolder;
        if (!Directory.Exists(coversFolder))
            return failures;

        var normalizedDays = ProjectIndexService.NormalizeRetentionDays(olderThanDays);
        var cutoffUtc = normalizedDays == 0
            ? nowUtc
            : nowUtc.AddDays(-normalizedDays);
        var referencedCovers = CollectReferencedCoverPaths(GetWorkingRootFolder(), coversFolder);

        IEnumerable<string> coverFiles;
        try
        {
            coverFiles = Directory.EnumerateFiles(coversFolder, "*", SearchOption.AllDirectories).ToList();
        }
        catch (Exception ex)
        {
            failures.Add($"Cover-Bereinigung fehlgeschlagen: {ex.Message}");
            return failures;
        }

        foreach (var coverFile in coverFiles)
        {
            try
            {
                var fullCoverPath = Path.GetFullPath(coverFile);
                if (referencedCovers.Contains(fullCoverPath))
                    continue;

                if (File.GetLastWriteTimeUtc(fullCoverPath) > cutoffUtc)
                    continue;

                File.SetAttributes(fullCoverPath, FileAttributes.Normal);
                File.Delete(fullCoverPath);
            }
            catch (Exception ex)
            {
                failures.Add($"Cover {Path.GetFileName(coverFile)}: {ex.Message}");
            }
        }

        return failures;
    }

    private static HashSet<string> CollectReferencedCoverPaths(string workingRootFolder, string coversFolder)
    {
        var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(workingRootFolder))
            return referenced;

        IEnumerable<string> manifestFiles;
        try
        {
            manifestFiles = Directory.EnumerateFiles(workingRootFolder, "*.json", SearchOption.AllDirectories).ToList();
        }
        catch
        {
            return referenced;
        }

        foreach (var manifestFile in manifestFiles)
        {
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(manifestFile));
                CollectReferencedCoverPaths(document.RootElement, coversFolder, referenced);
            }
            catch
            {
                // Beschädigte Projektdateien dürfen die Cover-Bereinigung nicht blockieren.
            }
        }

        return referenced;
    }

    private static void CollectReferencedCoverPaths(JsonElement element, string coversFolder, ISet<string> referenced)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                    CollectReferencedCoverPaths(property.Value, coversFolder, referenced);
                break;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                    CollectReferencedCoverPaths(item, coversFolder, referenced);
                break;

            case JsonValueKind.String:
                var value = element.GetString();
                if (IsPathInsideFolder(value, coversFolder, out var fullPath))
                    referenced.Add(fullPath);
                break;
        }
    }

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

    private void OpenFolderInExplorer(string folderPath, string displayName)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
        {
            AppDialogService.Warning(
                this,
                "Ordner nicht gefunden",
                $"Der {displayName} wurde nicht gefunden.\n\n{folderPath}");
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
                this,
                "Explorer konnte nicht geöffnet werden",
                $"Der {displayName} konnte nicht geöffnet werden.\n\n{ex.Message}");
        }
    }

    private async Task LoadFolderAsync(
        string folderPath,
        string? metadataSourceFolder = null,
        bool updateMetadata = true,
        bool removeGeneratedOrWorkFiles = true,
        TrackNumberPreference trackNumberPreference = TrackNumberPreference.EmbeddedTag,
        Func<string, bool>? includeFile = null,
        bool publishWorkflowStatus = true)
    {
        IsBusy = true;

        try
        {
            _loadedResumeProjectNeedsDiscImport = false;
            OnPropertyChanged(nameof(CanStartExport));
            OnPropertyChanged(nameof(ExportButtonText));
            _currentFolderPath = folderPath;
            ExportProgressPercent = 0;
            ExportProgressText = BuildIdleExportProgressText();

            if (string.IsNullOrWhiteSpace(OutputFolder) || !Directory.Exists(OutputFolder))
                OutputFolder = folderPath;

            Tracks.Clear();

            StatusText = "Ordner wird geprüft …";

            var tracks = _folderScanner.Scan(folderPath, trackNumberPreference, includeFile);

            foreach (var track in tracks)
                Tracks.Add(track);

            UpdateIndexes();

            TryApplyFirstEmbeddedCover(tracks);

            if (updateMetadata)
                SuggestMetadataFromTracksAndFolder(folderPath, metadataSourceFolder ?? folderPath);

            if (removeGeneratedOrWorkFiles)
                RemoveGeneratedOrWorkFilesFromTrackList(folderPath);

            UpdateIndexes();

            if (_ffmpegStatus.IsComplete)
            {
                await EnrichTracksWithFfprobeAsync(folderPath, publishWorkflowStatus);
            }
            else
            {
                UpdateFinalStatus(Tracks.Count);
                if (!IsExporting)
                    ExportProgressText = BuildIdleExportProgressText();
            }
            AutoFitTrackColumnsAfterRender();
        }
        catch (Exception ex)
        {
            StatusText = "Fehler beim Einlesen des Ordners.";

            _notificationService.Notify(NotificationEvent.Error);
            AppDialogService.Error(
                this,
                "Fehler beim Einlesen",
                "Der Ordner konnte nicht vollständig eingelesen werden.\n\n" + ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task EnrichTracksWithFfprobeAsync(string folderPath, bool publishWorkflowStatus = true)
    {
        if (string.IsNullOrWhiteSpace(_ffmpegStatus.FfprobePath))
            return;

        var trackSnapshot = Tracks.ToList();
        var total = trackSnapshot.Count;
        var processed = 0;
        var failed = 0;
        var workflowOperationId = publishWorkflowStatus
            ? BeginWorkflowStatusOperation(folderPath)
            : Guid.Empty;

        foreach (var track in trackSnapshot)
        {
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
            var probeInfo = await _audioInfoService.ProbeAsync(trackPath, _ffmpegStatus.FfprobePath);
            var decodeConfirmed = probeInfo.HasPlausibleAudioProperties ||
                                  await _audioInfoService.CanDecodeAudioAsync(trackPath, _ffmpegStatus.FfmpegPath);

            if (!decodeConfirmed)
            {
                failed++;
                track.AudioValidationPassed = false;
                track.Duration = "";
                track.DurationTicks = null;
                track.BitrateKbps = null;
                track.Channels = null;
                track.ChannelLayout = "";
                track.Codec = "Ungültig";
                track.ProcessingAction = "Ungültig";
                track.Warning = "Keine gültige Audiodatei erkannt.";

                if (processed % 10 == 0 || processed == total)
                    OnExportPreviewChanged();

                continue;
            }

            track.AudioValidationPassed = true;
            SetTrackDuration(track, probeInfo.Duration);
            track.EmbeddedChapters = probeInfo.Chapters.ToList();
            SetTrackValue(track, "BitrateKbps", probeInfo.BitrateKbps);
            SetTrackValue(track, "Channels", probeInfo.Channels);
            SetTrackValue(track, "ChannelLayout", AudioProcessingService.FormatChannelLayout(probeInfo.Channels));
            SetTrackValue(track, "Codec", AudioProcessingService.NormalizeCodecName(probeInfo.CodecName));

            var processingAction = AudioProcessingService.DetermineProcessingAction(probeInfo, ExportPreset.Parse(SelectedExportPreset));
            if (processingAction == "Prüfen")
                processingAction = "Konvertieren";

            SetTrackValue(track, "ProcessingAction", processingAction);

            if (track.Warning == "Keine gültige Audiodatei erkannt.")
                SetTrackValue(track, "Warning", "");

            if (processed % 10 == 0 || processed == total)
                OnExportPreviewChanged();
        }

        TracksGrid.Items.Refresh();
        OnExportPreviewChanged();

        if (failed > 0)
        {
            if (publishWorkflowStatus)
            {
                EndWorkflowStatusOperation(workflowOperationId);
                StatusText = failed == 1
                    ? "Technische Audiodaten geprüft. 1 Datei konnte nicht geprüft werden."
                    : $"Technische Audiodaten geprüft. {failed} Dateien konnten nicht geprüft werden.";
            }
            return;
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
            EndWorkflowStatusOperation(workflowOperationId);
        }
    }

    private void UpdateFinalStatus(int trackCount)
    {
        var issueSummary = _trackIssueSummaryService.Create(Tracks);

        var types = Tracks
            .GroupBy(track => track.Extension)
            .OrderBy(group => group.Key)
            .Select(group => $"{group.Count()} {group.Key}");

        var typeText = string.Join(", ", types);

        var actionText = BuildProcessingActionSummary();

        var issueText = issueSummary.ToDisplayText();

        StatusText = trackCount == 1
            ? $"1 Audiodatei gefunden. {typeText}. Aktion: {actionText}. {issueText}."
            : $"{trackCount} Audiodateien gefunden. {typeText}. Aktion: {actionText}. {issueText}.";

        OnExportPreviewChanged();
    }

    private void SuggestMetadataFromTracksAndFolder(string scannedFolderPath, string displayFolderPath)
    {
        if (Tracks.Count == 0)
            return;

        var suggestion = _bookMetadataService.GuessFromFolder(scannedFolderPath, displayFolderPath, Tracks);

        if (string.IsNullOrWhiteSpace(BookTitle) && !string.IsNullOrWhiteSpace(suggestion.Title))
            BookTitle = suggestion.Title;

        if (string.IsNullOrWhiteSpace(Author) && !string.IsNullOrWhiteSpace(suggestion.Author))
            Author = suggestion.Author;

        if (string.IsNullOrWhiteSpace(Narrator) && !string.IsNullOrWhiteSpace(suggestion.Narrator))
            Narrator = suggestion.Narrator;
    }

    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        if (IsBusy)
        {
            if (_pipelineState is ProjectPipelineState.AcquiringSources or ProjectPipelineState.Converting)
                await RequestPipelinePauseAsync();

            return;
        }

        if (_isPipelineFailed)
        {
            await DeletePausedProjectAsync();
            return;
        }

        if (_isPipelinePaused)
        {
            await ContinuePausedPipelineAsync();
            return;
        }

        if (_isAudioDiscProjectAwaitingRip)
        {
            await RunPreparedAudioDiscRipAsync();
            return;
        }

        if (_loadedResumeProjectNeedsDiscImport)
        {
            await ContinueLoadedMp3DiscProjectAsync();
            return;
        }

        if (_loadedResumeProjectIsMp3Disc)
        {
            await RunLoadedMp3DiscProjectExportAsync();
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
            await RunCurrentExportPlanAsync(_currentProjectWorkFolder, ProjectManifestTypes.AudioCdProject);
            return;
        }

        await RunCurrentExportPlanAsync();
    }


    private async Task RunPreparedAudioDiscRipAsync(
        ProjectExtensionRollbackSnapshot? extensionSnapshot = null,
        ProjectPipelineState? extensionReturnState = null,
        bool extensionReturnCompletedState = false,
        Action? restoreUiState = null,
        bool queueExistingRippedTracks = true)
    {
        var manifest = _activeAudioDiscManifest;
        if (manifest is null || manifest.Discs.Count == 0)
        {
            AppDialogService.Warning(
                this,
                "Audio-CD-Projekt nicht bereit",
                "Das vorbereitete Audio-CD-Projekt konnte nicht geladen werden.");
            return;
        }

        if (!_ffmpegStatus.FfmpegAvailable || string.IsNullOrWhiteSpace(_ffmpegStatus.FfmpegPath))
        {
            AppDialogService.Warning(
                this,
                "FFmpeg nicht bereit",
                "Für das FLAC-Ripping muss FFmpeg eingerichtet sein.");
            return;
        }

        AudioDiscRunPreparation runPreparation = null!;
        AudioDiscLiveConversionSession liveConversionSession;
        var audioStatusOperationId = BeginWorkflowStatusOperation(manifest.ProjectFolder);
        var statusDiscNumber = Math.Max(1, _audioDiscProjectService.GetNextRequiredDiscNumber(manifest) ?? 1);
        var statusCurrentCompleted = 0;
        var statusCurrentTotal = 0;
        var statusCurrentDiscFinished = false;
        var statusAllDiscsFinished = false;

        void PublishAudioDiscStatus(AudioDiscLiveConversionSessionSnapshot conversionSnapshot, bool isPaused = false)
        {
            var totalKnownTracks = manifest.Discs.Sum(disc => Math.Max(disc.TrackCount, disc.Tracks.Count));
            var totalRippedTracks = manifest.Discs.Sum(disc => disc.Tracks.Count(track =>
                string.Equals(track.Status, AudioDiscTrackStatus.Ripped, StringComparison.OrdinalIgnoreCase)));
            var convertedTracks = Math.Min(totalKnownTracks, conversionSnapshot.ConvertedCount);
            var format = AudioDiscSettingsService.NormalizeWorkingFormat(manifest.WorkingFormat) switch
            {
                AudioDiscWorkingFormat.Aac256 => "AAC 256 kbps",
                AudioDiscWorkingFormat.Wma => "WMA",
                _ => "FLAC"
            };

            PublishWorkflowStatus(
                audioStatusOperationId,
                _audioDiscWorkflowStatusAdapter.CreateRunningSnapshot(
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
                    extensionSnapshot is not null));
        }

        try
        {
            restoreUiState?.Invoke();
            runPreparation = _audioDiscRunPreparationService.Prepare(
                manifest,
                CreateProjectSnapshotFromUi(),
                ResolveParallelJobCount());
            restoreUiState?.Invoke();
            var workManifest = _workManifestService.LoadOrCreate(
                ProjectFolderLayout.ResolveWorkManifestPath(manifest.ProjectFolder),
                ProjectManifestTypes.AudioCdProject,
                manifest.ProjectFolder,
                ProjectFolderLayout.GetOriginalsFolder(manifest.ProjectFolder),
                runPreparation.Preset.DisplayName);
            var existingConvertedCount = _workManifestService.CountReusableConvertedTracks(
                workManifest,
                runPreparation.Preset);

            liveConversionSession = new AudioDiscLiveConversionSession(
                _audioDiscLiveConversionService,
                _liveConversionWorkflowService,
                _workManifestService,
                manifest,
                runPreparation.Preset,
                runPreparation.ParallelJobs,
                snapshot => PublishAudioDiscStatus(snapshot),
                existingConvertedCount);

            var initialDisc = manifest.Discs
                .OrderBy(disc => disc.DiscNumber)
                .FirstOrDefault(disc => disc.DiscNumber == statusDiscNumber)
                ?? manifest.Discs.OrderBy(disc => disc.DiscNumber).First();
            statusDiscNumber = initialDisc.DiscNumber;
            statusCurrentTotal = Math.Max(initialDisc.TrackCount, initialDisc.Tracks.Count);
            statusCurrentCompleted = initialDisc.Tracks.Count(track =>
                string.Equals(track.Status, AudioDiscTrackStatus.Ripped, StringComparison.OrdinalIgnoreCase));
            PublishAudioDiscStatus(liveConversionSession.GetSnapshot());
        }
        catch (Exception ex)
        {
            EndWorkflowStatusOperation(audioStatusOperationId);
            AppDialogService.Error(
                this,
                "Audio-CD-Fortsetzung konnte nicht vorbereitet werden",
                "Das gewählte Export-Preset und der aktuelle Projektzustand konnten nicht sicher gespeichert werden.",
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

        try
        {
            var workflowResult = await _audioDiscRipWorkflowService.RunAsync(
                new AudioDiscRipWorkflowRequest(
                    manifest,
                    _discImportCancellation.Token,
                    (discNumber, token) => WaitForNextAudioDiscAsync(manifest, discNumber, token),
                    (disc, token) => WaitForRequiredAudioDiscAsync(manifest, disc, token),
                    (disc, token) => ConfirmRequiredAudioDiscStillAvailableAsync(manifest, disc, token),
                    (disc, progress, token) => Task.Run(
                        () => _audioDiscRipService.RipDiscToFlacAsync(
                            manifest,
                            disc,
                            _ffmpegStatus.FfmpegPath,
                            token,
                            progress,
                            liveConversionSession.QueueAsync),
                        token),
                    disc => _discDriveService.TryEjectDisc(disc.SourceDriveRoot),
                    () => queueExistingRippedTracks
                        ? liveConversionSession.QueueExistingRippedTracksAsync(
                            manifest,
                            _discImportCancellation.Token)
                        : Task.CompletedTask,
                    WaitForLiveConversionsAsync,
                    () => SaveCurrentAudioDiscProjectSnapshot(manifest, force: true),
                    liveConversionSession.MarkManifestCanceled,
                    liveConversionSession.MarkManifestFailed,
                    (disc, pollingResult) => _audioDiscProjectService.UpdateDiscSourceDrive(
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
                        _notificationService.Notify(NotificationEvent.DiscChangeRequired);
                        statusDiscNumber = disc.DiscNumber;
                        statusCurrentTotal = Math.Max(disc.TrackCount, disc.Tracks.Count);
                        statusCurrentCompleted = statusCurrentTotal;
                        statusCurrentDiscFinished = disc.DiscNumber < manifest.TotalDiscs;
                        statusAllDiscsFinished = disc.DiscNumber >= manifest.TotalDiscs;
                        PublishAudioDiscStatus(liveConversionSession.GetSnapshot());
                    }));

            switch (workflowResult.Outcome)
            {
                case AudioDiscRipWorkflowOutcome.Completed:
                    var completion = await _audioDiscRipCompletionWorkflowService.RunAsync(
                        new AudioDiscRipCompletionWorkflowRequest(
                            manifest,
                            Tracks,
                            liveConversionSession.GetSnapshot(),
                            rippedFolder => LoadFolderAsync(
                                rippedFolder,
                                metadataSourceFolder: manifest.ProjectFolder,
                                updateMetadata: false,
                                removeGeneratedOrWorkFiles: false),
                            ApplyPersistedTrackListState,
                            UpdateIndexes,
                            () => TracksGrid.Items.Refresh(),
                            OnExportPreviewChanged,
                            NotifyExportUiStateChanged));

                    _isAudioDiscProjectAwaitingRip = false;
                    _currentProjectWorkFolder = manifest.ProjectFolder;
                    _activeAudioDiscManifest = manifest;
                    _loadedResumeProjectIsAudioDisc = true;
                    SelectedFolder = string.IsNullOrWhiteSpace(manifest.SourceDriveRoot)
                        ? SelectedFolder
                        : manifest.SourceDriveRoot;
                    SetAudioDiscSourceDisplayOverride(manifest);
                    statusAllDiscsFinished = true;
                    statusCurrentDiscFinished = true;
                    var completedTotal = manifest.Discs.Sum(disc => Math.Max(disc.TrackCount, disc.Tracks.Count));
                    PublishWorkflowStatus(
                        audioStatusOperationId,
                        _audioDiscWorkflowStatusAdapter.CreateReadySnapshot(
                            manifest.ProjectFolder,
                            manifest.TotalDiscs,
                            completedTotal,
                            runPreparation.Preset,
                            AudioDiscSettingsService.NormalizeWorkingFormat(manifest.WorkingFormat).ToString().ToUpperInvariant()));
                    continueWithExport = true;
                    break;

                case AudioDiscRipWorkflowOutcome.Canceled:
                    extensionCanceled = extensionSnapshot is not null && !_pauseRequested;
                    if (_pauseRequested)
                    {
                        PauseAudioDiscElapsedTimer();
                        PublishAudioDiscStatus(liveConversionSession.GetSnapshot(), isPaused: true);
                        EnterPipelinePause(string.Empty, preserveWorkflowStatus: true);
                    }
                    else
                    {
                        StatusText = $"Audio-CD-Ripping abgebrochen. {workflowResult.CompletedTracks} Tracks bleiben erhalten.";
                        ExportProgressText = "Der Vorgang wurde abgebrochen.";
                    }
                    break;

                case AudioDiscRipWorkflowOutcome.WaitingForDisc:
                    extensionCanceled = false;
                    PauseAudioDiscElapsedTimer();
                    PublishAudioDiscStatus(liveConversionSession.GetSnapshot(), isPaused: true);
                    EnterPipelinePause(string.Empty, preserveWorkflowStatus: true);
                    break;

                case AudioDiscRipWorkflowOutcome.Failed:
                    AppDialogService.Error(
                        this,
                        "Audio-CD konnte nicht vollständig gerippt werden",
                        workflowResult.Message);
                    StatusText = "Audio-CD-Ripping fehlgeschlagen.";
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            extensionCanceled = extensionSnapshot is not null && !_pauseRequested;
            _audioDiscProjectService.MarkProjectCanceled(manifest);
            SaveCurrentAudioDiscProjectSnapshot(manifest, force: true);
            liveConversionSession.MarkManifestCanceled("Audio-CD-Ripping und AAC-Vorbereitung wurden abgebrochen.");
            if (_pauseRequested)
            {
                PauseAudioDiscElapsedTimer();
                PublishAudioDiscStatus(liveConversionSession.GetSnapshot(), isPaused: true);
                EnterPipelinePause(string.Empty, preserveWorkflowStatus: true);
            }
            else
            {
                StatusText = "Audio-CD-Ripping wurde abgebrochen.";
                ExportProgressText = "Bereits vollständig gerippte Tracks bleiben erhalten.";
            }
        }
        catch (Exception ex)
        {
            SaveCurrentAudioDiscProjectSnapshot(manifest, force: true);
            liveConversionSession.MarkManifestFailed(ex.Message);
            _notificationService.Notify(NotificationEvent.Error);
            AppDialogService.Error(
                this,
                "Fehler beim Audio-CD-Ripping",
                "Die Audio-CD konnte nicht vollständig nach FLAC gerippt werden.",
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
            _projectExtensionRollbackService.Rollback(extensionSnapshot);
            ClearProjectExtensionPauseContext();
            var restoredManifest = _audioDiscProjectService.TryLoad(manifest.ProjectFolder);
            _activeAudioDiscManifest = restoredManifest;
            _isAudioDiscProjectAwaitingRip = false;
            _isCurrentProjectCompleted = extensionReturnCompletedState;
            _isWaitingForManualMergeReview = extensionReturnState == ProjectPipelineState.ReviewBeforeMerge || extensionReturnState == ProjectPipelineState.Completed;
            SetPipelineState(extensionReturnState ?? ProjectPipelineState.ReviewBeforeMerge);
            RefreshLoadedProjectTrackStateFromPersistedProject(manifest.ProjectFolder);
            StatusText = "Hinzufügen weiterer Audio-CDs wurde abgebrochen. Das bisherige Projekt blieb unverändert.";
            ExportProgressText = "Neu gerippte Dateien und Konvertierungen wurden entfernt.";
            NotifyExportUiStateChanged();
            return;
        }

        if (continueWithExport)
        {
            if (extensionSnapshot is not null)
            {
                ClearProjectExtensionPauseContext();
                _pauseBeforeMergeOverride = true;
            }

            try
            {
                await RunCurrentExportPlanAsync(
                    manifest.ProjectFolder,
                    ProjectManifestTypes.AudioCdProject);
            }
            finally
            {
                if (extensionSnapshot is not null)
                    _pauseBeforeMergeOverride = null;
            }
        }
    }

    private void NotifyDiscPollingState(DiscPollingDisplayState state)
    {
        if (state is DiscPollingDisplayState.Unsupported or DiscPollingDisplayState.Duplicate)
            _notificationService.Notify(NotificationEvent.Warning);
    }

    private static string CreateDiscDriveDisplayName(string sourceFolder)
    {
        var root = Path.GetPathRoot(sourceFolder)?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar, Path.VolumeSeparatorChar);
        return string.IsNullOrWhiteSpace(root) ? string.Empty : $"CD-Laufwerk {root}";
    }

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

            var sourceReady = await Task.Run(
                () => _discDriveService.IsDiscSourceReady(disc.SourceDriveRoot),
                token);
            if (!sourceReady)
                return false;

            var check = await Task.Run(
                () => _audioDiscPollingService.CheckRequiredDisc(
                    disc.DiscIdentity,
                    disc.DiscNumber,
                    manifest.TotalDiscs,
                    _developerAudioDiscTestDriveRoot),
                token);
            if (!check.PollingResult.CanImport || check.Disc is null)
                return false;
        }

        return true;
    }

    private async Task<AudioDiscPollingResult?> WaitForRequiredAudioDiscAsync(
        AudioDiscProjectManifest manifest,
        AudioDiscProjectManifestDisc disc,
        CancellationToken token)
    {
        var immediateResult = await Task.Run(
            () => _audioDiscPollingService.CheckRequiredDisc(
                disc.DiscIdentity,
                disc.DiscNumber,
                manifest.TotalDiscs,
                _developerAudioDiscTestDriveRoot),
            token);
        if (immediateResult.PollingResult.CanImport && immediateResult.Disc is not null)
            return immediateResult;

        AudioDiscPollingResult? acceptedResult = null;
        var request = new DiscWaitDialogRequest(
            disc.DiscNumber,
            manifest.TotalDiscs,
            "Audio-CD",
            $"Bitte Audio-CD {disc.DiscNumber} von {manifest.TotalDiscs} einlegen. BookStitch prüft automatisch alle optischen Laufwerke.",
            "Die benötigte Disc wird anhand ihrer Disc-Identität erkannt.");

        var accepted = await _discWaitDialogService.WaitForDiscAsync(
            this,
            request,
            async cancellationToken =>
            {
                acceptedResult = await Task.Run(
                    () => _audioDiscPollingService.CheckRequiredDisc(
                        disc.DiscIdentity,
                        disc.DiscNumber,
                        manifest.TotalDiscs,
                        _developerAudioDiscTestDriveRoot),
                    cancellationToken);
                return acceptedResult.PollingResult;
            },
            value => StatusText = value,
            value => ExportProgressText = value,
            token);

        if (accepted == DiscWaitDialogOutcome.Deferred)
            _pauseRequested = true;

        return accepted == DiscWaitDialogOutcome.Ready && acceptedResult?.Disc is not null
            ? acceptedResult
            : null;
    }

    private async Task<AudioDiscProjectManifestDisc?> WaitForNextAudioDiscAsync(
        AudioDiscProjectManifest manifest,
        int discNumber,
        CancellationToken token)
    {
        var sourceFolder = manifest.SourceDriveRoot;
        var importedIdentities = manifest.Discs
            .Select(item => item.DiscIdentity)
            .Where(identity => !string.IsNullOrWhiteSpace(identity))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        AudioDiscPollingResult? acceptedResult = await TrySelectNextAudioDiscFromDriveRoundAsync(
            manifest,
            sourceFolder,
            discNumber,
            importedIdentities,
            token);

        if (acceptedResult?.Disc is not null)
            return AddAcceptedAudioDiscFromPollingResult(manifest, acceptedResult, discNumber);

        acceptedResult = null;

        var request = new DiscWaitDialogRequest(
            discNumber,
            manifest.TotalDiscs,
            "Audio-CD",
            _settings.ExperimentalDriveRoundEnabled
                ? $"Bitte Audio-CD {discNumber} von {manifest.TotalDiscs} einlegen. Die Laufwerksrunde prüft automatisch alle aktiven Laufwerke in Reihenfolge."
                : $"Bitte Audio-CD {discNumber} von {manifest.TotalDiscs} einlegen. BookStitch prüft automatisch und startet, sobald eine neue Audio-CD erkannt wurde.",
            "Bereits verwendete Audio-CDs werden anhand ihrer Disc-Identität erkannt, nicht erneut aufgenommen und nach Möglichkeit wieder ausgeworfen.",
            CreateDiscDriveDisplayName(sourceFolder),
            NotifyDiscPollingState);

        var accepted = await _discWaitDialogService.WaitForDiscAsync(
            this,
            request,
            async cancellationToken =>
            {
                if (_settings.ExperimentalDriveRoundEnabled)
                {
                    var driveRoundResult = await CheckAudioDriveRoundForWaitDialogAsync(
                        manifest,
                        sourceFolder,
                        discNumber,
                        importedIdentities,
                        cancellationToken);
                    acceptedResult = driveRoundResult.AcceptedResult;
                    return driveRoundResult.PollingResult;
                }

                acceptedResult = await Task.Run(
                    () => _audioDiscPollingService.CheckNextDisc(
                        sourceFolder,
                        discNumber,
                        manifest.TotalDiscs,
                        importedIdentities),
                    cancellationToken);
                return acceptedResult.PollingResult;
            },
            _ => { },
            _ => { },
            token);

        if (accepted == DiscWaitDialogOutcome.Deferred)
        {
            _pauseRequested = true;
            return null;
        }

        if (acceptedResult?.Disc is null)
            return null;

        return AddAcceptedAudioDiscFromPollingResult(manifest, acceptedResult, discNumber);
    }

    private sealed record AudioDriveRoundWaitResult(
        DiscPollingResult PollingResult,
        AudioDiscPollingResult? AcceptedResult = null);

    private async Task<AudioDriveRoundWaitResult> CheckAudioDriveRoundForWaitDialogAsync(
        AudioDiscProjectManifest manifest,
        string lastProcessedSourceFolder,
        int discNumber,
        ISet<string> importedDiscIdentities,
        CancellationToken token)
    {
        var round = BuildDriveRound(lastProcessedSourceFolder);
        if (round.Count == 0)
        {
            return new AudioDriveRoundWaitResult(new DiscPollingResult(
                false,
                $"Bitte Audio-CD {discNumber} von {manifest.TotalDiscs} einlegen.\n\nDie Laufwerksrunde ist aktiv, aber aktuell ist kein aktives Laufwerk verfügbar.",
                $"Warte auf Audio-CD {discNumber}: kein aktives Laufwerk verfügbar.",
                "Laufwerksrunde aktiv: kein aktives Laufwerk verfügbar."));
        }

        DiscPollingResult? lastProblem = null;
        foreach (var driveRoot in round)
        {
            token.ThrowIfCancellationRequested();
            var driveLetter = FormatDriveLetter(driveRoot);
            ExportProgressText = $"Laufwerksrunde aktiv: prüfe Laufwerk {driveLetter} …";

            var typeProbe = await Task.Run(
                () => _discDriveCandidateProbeService.ProbeType(driveRoot, DiscMediaKind.AudioCd),
                token);
            if (!typeProbe.IsAccepted)
            {
                WriteDriveRoundSkip("Audio-CD", discNumber, typeProbe);
                lastProblem = CreateDriveRoundWaitingResult(
                    discNumber,
                    manifest.TotalDiscs,
                    "Audio-CD",
                    driveLetter,
                    typeProbe);
                continue;
            }

            AudioDiscPollingResult pollingResult;
            try
            {
                pollingResult = await Task.Run(
                    () => _audioDiscPollingService.CheckNextDisc(
                        typeProbe.DriveRoot,
                        discNumber,
                        manifest.TotalDiscs,
                        importedDiscIdentities),
                    token);
            }
            catch (Exception ex)
            {
                _diagnosticLogService.WriteError($"Laufwerksrunde Audio-CD {discNumber}: {driveRoot} konnte nicht geprüft werden", ex);
                lastProblem = new DiscPollingResult(
                    false,
                    $"Laufwerk {driveLetter} konnte gerade nicht gelesen werden.\n\nBookStitch prüft gleich automatisch weiter.",
                    $"Warte auf Audio-CD {discNumber}: Laufwerk {driveLetter} konnte gerade nicht gelesen werden.",
                    $"Laufwerksrunde aktiv: Laufwerk {driveLetter} konnte gerade nicht gelesen werden.");
                continue;
            }

            if (pollingResult.PollingResult.CanImport && pollingResult.Disc is not null)
            {
                _diagnosticLogService.WriteApplicationEvent(
                    "DRIVE ROUND",
                    $"Audio-CD {discNumber}/{manifest.TotalDiscs}: {typeProbe.DriveRoot} akzeptiert.");
                return new AudioDriveRoundWaitResult(new DiscPollingResult(
                    true,
                    $"Neue Audio-CD in Laufwerk {driveLetter} erkannt. CD {discNumber} von {manifest.TotalDiscs} wird vorbereitet …",
                    $"Audio-CD {discNumber} von {manifest.TotalDiscs} in Laufwerk {driveLetter} erkannt. Ripping startet …",
                    $"Laufwerksrunde: Laufwerk {driveLetter} erkannt. Ripping startet …",
                    DiscPollingDisplayState.Ready), pollingResult);
            }

            lastProblem = pollingResult.PollingResult with
            {
                ProgressText = $"Laufwerksrunde aktiv: Laufwerk {driveLetter} übersprungen – {pollingResult.PollingResult.ProgressText}",
                StatusText = $"Warte auf Audio-CD {discNumber}: Laufwerk {driveLetter} übersprungen."
            };
        }

        return new AudioDriveRoundWaitResult(lastProblem ?? new DiscPollingResult(
            false,
            $"Bitte Audio-CD {discNumber} von {manifest.TotalDiscs} einlegen.\n\nDie Laufwerksrunde prüft automatisch alle aktiven Laufwerke in Reihenfolge.",
            $"Warte auf Audio-CD {discNumber}: Laufwerksrunde aktiv.",
            "Laufwerksrunde aktiv: keine passende Audio-CD gefunden. Nächste Prüfung läuft automatisch …"));
    }

    private async Task<AudioDiscPollingResult?> TrySelectNextAudioDiscFromDriveRoundAsync(
        AudioDiscProjectManifest manifest,
        string lastProcessedSourceFolder,
        int discNumber,
        ISet<string> importedDiscIdentities,
        CancellationToken token)
    {
        if (!_settings.ExperimentalDriveRoundEnabled)
            return null;

        var round = BuildDriveRound(lastProcessedSourceFolder);
        if (round.Count == 0)
            return null;

        _diagnosticLogService.WriteApplicationEvent(
            "DRIVE ROUND",
            $"Audio-CD {discNumber}/{manifest.TotalDiscs}: prüfe {string.Join(", ", round)} nach {lastProcessedSourceFolder}.");

        foreach (var driveRoot in round)
        {
            token.ThrowIfCancellationRequested();

            var typeProbe = await Task.Run(
                () => _discDriveCandidateProbeService.ProbeType(driveRoot, DiscMediaKind.AudioCd),
                token);
            if (!typeProbe.IsAccepted)
            {
                WriteDriveRoundSkip("Audio-CD", discNumber, typeProbe);
                continue;
            }

            AudioDiscPollingResult pollingResult;
            try
            {
                pollingResult = await Task.Run(
                    () => _audioDiscPollingService.CheckNextDisc(
                        typeProbe.DriveRoot,
                        discNumber,
                        manifest.TotalDiscs,
                        importedDiscIdentities),
                    token);
            }
            catch (Exception ex)
            {
                _diagnosticLogService.WriteError($"Laufwerksrunde Audio-CD {discNumber}: {driveRoot} konnte nicht geprüft werden", ex);
                continue;
            }

            if (pollingResult.PollingResult.CanImport && pollingResult.Disc is not null)
            {
                StatusText = $"Laufwerksrunde: Audio-CD {discNumber} von {manifest.TotalDiscs} in Laufwerk {FormatDriveLetter(typeProbe.DriveRoot)} erkannt.";
                ExportProgressText = "Laufwerksrunde aktiv: Ripping wird fortgesetzt …";
                _diagnosticLogService.WriteApplicationEvent(
                    "DRIVE ROUND",
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

        _diagnosticLogService.WriteApplicationEvent(
            "DRIVE ROUND",
            $"Audio-CD {discNumber}/{manifest.TotalDiscs}: keine passende neue Disc gefunden.");
        return null;
    }

    private AudioDiscProjectManifestDisc AddAcceptedAudioDiscFromPollingResult(
        AudioDiscProjectManifest manifest,
        AudioDiscPollingResult acceptedResult,
        int discNumber)
    {
        SaveCurrentAudioDiscProjectSnapshot(manifest);

        var addedDisc = _audioDiscProjectService.AddDisc(
            manifest,
            acceptedResult.Disc!,
            discNumber,
            acceptedResult.DriveInfo);
        SaveCurrentAudioDiscProjectSnapshot(manifest, force: true);

        Tracks.Clear();
        foreach (var track in _audioDiscProjectService.CreateTrackPreview(manifest))
            Tracks.Add(track);

        UpdateIndexes();
        TracksGrid.Items.Refresh();
        AutoFitTrackColumnsAfterRender();
        NotifyExportUiStateChanged();

        return addedDisc;
    }


    private async Task RunLoadedMp3DiscProjectExportAsync()
    {
        if (string.IsNullOrWhiteSpace(_currentProjectWorkFolder) || !Directory.Exists(_currentProjectWorkFolder))
        {
            AppDialogService.Warning(
                this,
                "Projekt nicht gefunden",
                "Der MP3-CD-Projektordner konnte nicht gefunden werden. Öffne das Projekt bitte erneut über „Projekte“.");
            ResetLoadedResumeProjectState();
            return;
        }

        _loadedResumeProjectNeedsDiscImport = false;
        _loadedResumeProjectIsMp3Disc = true;
        NotifyExportUiStateChanged();

        // Geladene MP3-CD-Projekte laufen auch bei einem Presetwechsel direkt durch den
        // normalen Export-Workflow. Dadurch gelten derselbe Converting-Zustand,
        // dieselbe Pause-/Fortsetzen-Logik und dieselben aktiven Jobmeldungen wie bei
        // allen anderen Exportpfaden. Eine separate Vorabkonvertierung würde den
        // fachlichen Pipelinezustand umgehen und die falsche Buttongruppe stehen lassen.
        await RunCurrentExportPlanAsync(_currentProjectWorkFolder, ProjectManifestTypes.Mp3DiscProject);
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

        _activeAudioDiscManifest = _audioDiscProjectService.TryLoad(_currentProjectWorkFolder);
    }

    private string ResolveCurrentProjectType(string requestedProjectType)
    {
        RestoreActiveAudioDiscManifestFromCurrentProject();

        if (_activeAudioDiscManifest is not null)
            return ProjectManifestTypes.AudioCdProject;

        if (_loadedResumeProjectIsMp3Disc)
            return ProjectManifestTypes.Mp3DiscProject;

        return requestedProjectType;
    }

    private async Task RunCurrentExportPlanAsync(
        string? projectWorkFolderOverride = null,
        string projectType = ProjectManifestTypes.FolderProject)
    {
        _isPipelineFailed = false;
        projectType = ResolveCurrentProjectType(projectType);

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
            projectType);
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

        return TrackPathService.PathEquals(_currentFolderPath, originalsFolder);
    }

    private void EnterPreparedProjectReviewStateAfterPreparation()
    {
        _isWaitingForManualMergeReview = true;
        _manualMergeReviewPreparedPreset = SelectedExportPreset;
        _manualMergeReviewNeedsReconversion = false;
        _isCurrentProjectCompleted = false;
        SetPipelineState(ProjectPipelineState.ReviewBeforeMerge);
        SetTrackActionDisplayOverride("Zusammenfügen");
        NotifyExportUiStateChanged();
    }

    private void RestoreReviewStateAfterPreExportValidationStopped()
    {
        if (IsBusy ||
            _pipelineState is ProjectPipelineState.ReviewBeforeMerge or ProjectPipelineState.Completed or ProjectPipelineState.Merging ||
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
                "FFmpeg fehlt",
                "FFmpeg wurde nicht gefunden. Bitte prüfe die FFmpeg-Einstellungen und starte das Projekt erneut.");
            return false;
        }

        var sourceFolder = Path.GetFullPath(_currentFolderPath);
        var trackSnapshot = Tracks.ToList();
        var sourceFiles = trackSnapshot
            .Select(track => TrackPathService.GetTrackPath(sourceFolder, track))
            .ToArray();

        var projectFolder = !string.IsNullOrWhiteSpace(_currentProjectWorkFolder) &&
                            Directory.Exists(_currentProjectWorkFolder)
            ? _currentProjectWorkFolder
            : _localProjectImportService.CreateProjectFolder(
                GetProjectFolderStructure().LocalProjectsFolder,
                sourceFolder);

        var originalsFolder = Path.Combine(
            projectFolder,
            LocalProjectImportService.OriginalsFolderName);
        var preset = ExportPreset.Parse(SelectedExportPreset);
        var exportPlan = _exportPlanService.Create(new ExportPlanRequest(
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

        var manifest = _workManifestService.LoadOrCreate(
            exportPlan.ManifestPath,
            ProjectManifestTypes.FolderProject,
            projectFolder,
            originalsFolder,
            preset.DisplayName);
        var manifestSyncRoot = new object();
        var sourceAcquisitionCompleted = false;

        _currentProjectWorkFolder = projectFolder;
        manifest.State.Status = ProjectManifestStatuses.AcquiringSources;
        _workManifestService.Save(exportPlan.ManifestPath, manifest);
        SetPipelineState(ProjectPipelineState.AcquiringSources);
        IsBusy = true;
        IsExporting = true;
        _exportCancellation = new CancellationTokenSource();
        var workflowOperationId = BeginWorkflowStatusOperation(projectFolder);
        PublishWorkflowStatus(
            workflowOperationId,
            _localWorkflowStatusAdapter.CreateRunningSnapshot(
                projectFolder,
                ProjectPipelineState.AcquiringSources,
                new LocalProjectLivePreparationProgress(0, sourceFiles.Length, 0, string.Empty, [], []),
                preset));

        try
        {
            var result = await _localProjectLivePreparationService.RunAsync(
                new LocalProjectLivePreparationRequest(
                    sourceFolder,
                    sourceFiles,
                    projectFolder,
                    ResolveParallelJobCount()),
                async (copiedFile, trackPreparationProgress, token) =>
                {
                    var sourcePath = Path.GetFullPath(copiedFile.SourceFile);
                    if (!trackBySourcePath.TryGetValue(sourcePath, out var mappedTrack))
                    {
                        throw new InvalidOperationException(
                            $"Die kopierte Quelldatei konnte keinem Track zugeordnet werden: {copiedFile.SourceFile}");
                    }

                    var convertedPath = ConvertedTrackPathService.GetConvertedTrackPath(
                        exportPlan.ConvertedFolder,
                        copiedFile.TargetFile,
                        mappedTrack.Track);
                    var canReuseConvertedTrack = false;

                    lock (manifestSyncRoot)
                    {
                        canReuseConvertedTrack = _workManifestService.CanReuseConvertedTrack(
                            manifest,
                            mappedTrack.Index,
                            mappedTrack.Track,
                            copiedFile.TargetFile,
                            convertedPath,
                            preset);
                    }

                    if (!canReuseConvertedTrack)
                    {
                        var durationTicks = TrackDurationService.GetEffectiveDurationTicks(mappedTrack.Track);
                        await _aacExportProcessingService.PrepareTrackForExportAsync(
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

                        lock (manifestSyncRoot)
                        {
                            _workManifestService.UpdateTrack(
                                manifest,
                                mappedTrack.Index,
                                mappedTrack.Track,
                                copiedFile.TargetFile,
                                convertedPath,
                                preset);
                            _workManifestService.Save(exportPlan.ManifestPath, manifest);
                        }
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
                            _workManifestService.Save(exportPlan.ManifestPath, manifest);
                        }
                        SetPipelineState(ProjectPipelineState.Converting);
                    }

                    PublishWorkflowStatus(
                        workflowOperationId,
                        _localWorkflowStatusAdapter.CreateRunningSnapshot(
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
                    _localWorkflowStatusAdapter.CreateRunningSnapshot(
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
                    track.ConvertedSizeMb = new FileInfo(track.PreparedConvertedPath).Length / 1024d / 1024d;
                    track.ConvertedSizeAvailable = true;
                }
            }

            _currentFolderPath = originalsFolder;
            SelectedFolder = sourceFolder;
            _loadedResumeProjectIsLocal = true;
            _loadedResumeProjectIsMp3Disc = false;
            _loadedResumeProjectIsAudioDisc = false;
            SetSelectedSourceDisplayOverride(
                $"Ordnerprojekt: {new DirectoryInfo(sourceFolder).Name}",
                sourceFolder);
            TracksGrid.Items.Refresh();
            OnExportPreviewChanged();
            PublishWorkflowStatus(
                workflowOperationId,
                _localWorkflowStatusAdapter.CreateReadySnapshot(
                    projectFolder,
                    trackSnapshot.Count,
                    preset));
            EndWorkflowStatusOperation(workflowOperationId);
            return true;
        }
        catch (OperationCanceledException)
        {
            var last = _workflowStatusCoordinator.Snapshot;
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
                "Lokale Projektvorbereitung fehlgeschlagen.",
                "Lokale Vorbereitung fehlgeschlagen.");

            var errorText = ex.Message;
            var dialogResult = ShowHardWorkflowErrorDialog(
                "Lokale Vorbereitung fehlgeschlagen",
                "Lokale Vorbereitung fehlgeschlagen",
                "Die lokalen Originaldateien konnten nicht vollständig übernommen und konvertiert werden.",
                errorText);

            if (dialogResult == AppDialogResult.Yes)
                await DeletePausedProjectAsync();

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
            _convertedFileCleanupService.DeletePartFiles(_currentProjectWorkFolder);

        TrySaveActiveProjectSnapshot();
        PersistTrackListState();
        _isPipelineFailed = true;
        _isPipelinePaused = false;
        _pauseRequested = false;
        IsBusy = false;
        IsExporting = false;
        IsDiscImporting = false;
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

        return AppDialogService.Show(
            this,
            title: title,
            heading: heading,
            message: message + "\n\nDu kannst die Fehlermeldung kopieren, das Projekt prüfen oder das unvollständige Projekt abbrechen.",
            kind: AppDialogKind.Error,
            details: detailItems,
            buttons: new[]
            {
                new AppDialogButton("Fehlermeldung kopieren", AppDialogResult.None, ClipboardText: clipboardText, ClosesDialog: false),
                new AppDialogButton("Projekt abbrechen", AppDialogResult.Yes, IsDanger: true),
                new AppDialogButton("OK", AppDialogResult.Ok, IsPrimary: true, IsDefault: true, IsCancel: true)
            },
            width: 740,
            height: 420);
    }


    private async void CancelExport_Click(object sender, RoutedEventArgs e)
    {
        if (_isPipelineFailed)
        {
            await DeletePausedProjectAsync();
            return;
        }

        if (_isPipelinePaused)
        {
            if (_isProjectExtensionRun)
                await StopPausedProjectExtensionAsync();
            else
                await DeletePausedProjectAsync();
            return;
        }

        if (!IsBusy && _pipelineState is ProjectPipelineState.ReviewBeforeMerge or ProjectPipelineState.Completed)
        {
            CloseCurrentProject();
            return;
        }

        if (!IsBusy &&
            _pipelineState is ProjectPipelineState.AcquiringSources or ProjectPipelineState.Converting &&
            !IsDiscImporting &&
            !IsExporting)
        {
            RestoreReviewStateAfterPreExportValidationStopped();
            if (_pipelineState is ProjectPipelineState.ReviewBeforeMerge or ProjectPipelineState.Completed)
                return;
        }

        if (_pipelineState is ProjectPipelineState.AcquiringSources or ProjectPipelineState.Converting)
        {
            await RequestPipelinePauseAsync();
            return;
        }

        if (IsDiscImporting && _discImportCancellation is not null && !_discImportCancellation.IsCancellationRequested)
        {
            StatusText = _isAudioDiscProjectAwaitingRip
                ? "Audio-CD-Ripping wird abgebrochen …"
                : "MP3-CD-Import wird abgebrochen …";
            ExportProgressText = "Abbruch wird angefordert …";
            _discImportCancellation.Cancel();
            return;
        }

        if (!IsExporting || _exportCancellation is null || _exportCancellation.IsCancellationRequested)
            return;

        StatusText = "Export wird abgebrochen …";
        ExportProgressText = "Abbruch wird angefordert …";
        _exportCancellation.Cancel();
    }

    private void PauseAudioDiscElapsedTimer()
    {
        _audioDiscElapsedBeforeCurrentRun += _audioDiscCurrentRunElapsed;
        _audioDiscCurrentRunElapsed = TimeSpan.Zero;
    }

    private Guid BeginWorkflowStatusOperation(string? projectId)
    {
        _workflowStatusOperationId = _workflowStatusCoordinator.BeginOperation(projectId);
        return _workflowStatusOperationId;
    }

    private void PublishWorkflowStatus(Guid operationId, WorkflowStatusSnapshot snapshot)
    {
        if (!_workflowStatusCoordinator.Publish(operationId, snapshot))
            return;

        ApplyWorkflowStatusViewState(_workflowStatusCoordinator.CurrentViewState);
    }

    private void ApplyWorkflowStatusViewState(WorkflowStatusViewState viewState)
    {
        void Apply()
        {
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
        _workflowStatusCoordinator.EndOperation(operationId);
        if (_workflowStatusOperationId == operationId)
            _workflowStatusOperationId = Guid.Empty;
    }

    private void EnterPipelinePause(string progressText, bool preserveWorkflowStatus = false)
    {
        if (!string.IsNullOrWhiteSpace(_currentProjectWorkFolder))
            _convertedFileCleanupService.DeletePartFiles(_currentProjectWorkFolder);

        TrySaveActiveProjectSnapshot();
        PersistTrackListState();
        _isPipelinePaused = true;
        _isPipelineFailed = false;
        _pauseRequested = false;
        IsBusy = false;
        IsExporting = false;
        IsDiscImporting = false;
        if (!preserveWorkflowStatus)
        {
            StatusText = "Projekt pausiert.";
            ExportProgressText = progressText;
        }
        NotifyExportUiStateChanged();
    }

    private async Task ContinuePausedPipelineAsync()
    {
        if (!_isPipelinePaused || IsBusy)
            return;

        _isPipelinePaused = false;
        _isPipelineFailed = false;
        _pauseRequested = false;
        StatusText = "Projekt wird fortgesetzt …";
        NotifyExportUiStateChanged();

        if (_isProjectExtensionRun && _pausedProjectExtensionContinuation is not null)
        {
            await _pausedProjectExtensionContinuation();
            return;
        }

        var continuationKind = _pausedPipelineContinuationService.Resolve(
            new PausedPipelineContinuationInput(
                _pipelineState,
                _isAudioDiscProjectAwaitingRip,
                _loadedResumeProjectIsAudioDisc,
                _loadedResumeProjectNeedsDiscImport,
                _loadedResumeProjectIsMp3Disc));

        if (continuationKind == PausedPipelineContinuationKind.AudioDiscRip)
        {
            await RunPreparedAudioDiscRipAsync();
            return;
        }

        if (continuationKind == PausedPipelineContinuationKind.Mp3DiscImport)
        {
            await ContinuePausedMp3DiscProjectAsync();
            return;
        }

        var projectType = _loadedResumeProjectIsMp3Disc
            ? ProjectManifestTypes.Mp3DiscProject
            : _loadedResumeProjectIsAudioDisc
                ? ProjectManifestTypes.AudioCdProject
                : ProjectManifestTypes.FolderProject;

        await RunCurrentExportPlanAsync(_currentProjectWorkFolder, projectType);
    }

    private async Task ContinuePausedMp3DiscProjectAsync()
    {
        var manifest = TryLoadCurrentMp3DiscProjectManifest(resetStateWhenFolderMissing: false);
        if (manifest is null)
        {
            EnterPipelinePause("Fortsetzen nicht möglich. Das MP3-CD-Projekt konnte nicht geladen werden.");
            return;
        }

        var resumePlan = _mp3DiscProjectService.BuildResumePlan(manifest);
        if (resumePlan.NextMissingDiscNumber is null)
        {
            _loadedResumeProjectNeedsDiscImport = false;
            _loadedResumeProjectIsMp3Disc = true;
            await RunCurrentExportPlanAsync(manifest.ProjectFolder, ProjectManifestTypes.Mp3DiscProject);
            return;
        }

        var sourceFolder = _discDriveService.ResolveResumeDiscSource(
            manifest.ProjectFolder,
            manifest.SourceFolder,
            _settings.LastDiscSourceFolder,
            SelectedFolder);

        if (string.IsNullOrWhiteSpace(sourceFolder))
        {
            sourceFolder = ShowDiscSourceSelectionDialog();
            if (!string.IsNullOrWhiteSpace(sourceFolder))
                sourceFolder = ResolveSelectedDiscSource(sourceFolder);
        }

        if (string.IsNullOrWhiteSpace(sourceFolder))
        {
            EnterPipelinePause($"Pausiert. Bitte lege CD {resumePlan.NextMissingDiscNumber.Value} wieder ein und klicke anschließend auf Weiter.");
            return;
        }

        SelectedFolder = sourceFolder;
        _settings.LastDiscSourceFolder = sourceFolder;
        SaveSettingsIfReady();
        _loadedResumeProjectNeedsDiscImport = true;
        _loadedResumeProjectIsMp3Disc = true;

        await ImportMp3DiscProjectAsync(
            manifest.ProjectFolder,
            sourceFolder,
            manifest,
            startDiscNumber: resumePlan.NextMissingDiscNumber.Value,
            firstDiscAlreadyReady: true,
            autoExportWhenComplete: true,
            pauseBeforeMergeOverride: _settings.MergeAutomaticallyAfterConversion ? false : true);
    }

    private void BeginProjectExtensionPauseContext(Func<Task> continuation, Action stopAction)
    {
        _isProjectExtensionRun = true;
        _pausedProjectExtensionContinuation = continuation;
        _pausedProjectExtensionStopAction = stopAction;
        NotifyExportUiStateChanged();
    }

    private void ClearProjectExtensionPauseContext()
    {
        _isProjectExtensionRun = false;
        _pausedProjectExtensionContinuation = null;
        _pausedProjectExtensionStopAction = null;
        NotifyExportUiStateChanged();
    }

    private async Task StopPausedProjectExtensionAsync()
    {
        if (!_isPipelinePaused || !_isProjectExtensionRun)
            return;

        await Task.Yield();
        var stopAction = _pausedProjectExtensionStopAction;
        _isPipelinePaused = false;
        _isPipelineFailed = false;
        _pauseRequested = false;
        ClearProjectExtensionPauseContext();
        stopAction?.Invoke();
    }

    private async Task DeletePausedProjectAsync()
    {
        if (!_isPipelinePaused && !_isPipelineFailed)
            return;

        var wasFailed = _isPipelineFailed;
        var keepProjectButtonText = wasFailed ? "OK" : "Weiter";
        var result = AppDialogService.Show(
            this,
            title: "Projekt abbrechen",
            heading: "Unvollständiges Projekt endgültig löschen?",
            message:
                "Das aktuelle Projekt wird vollständig gelöscht. Dazu gehören alle bereits kopierten, gerippten und konvertierten Dateien. Dieser Vorgang kann nicht rückgängig gemacht werden.",
            kind: AppDialogKind.Warning,
            buttons: new[]
            {
                new AppDialogButton(keepProjectButtonText, AppDialogResult.Cancel, IsPrimary: true, IsDefault: true, IsCancel: true),
                new AppDialogButton("Projekt löschen", AppDialogResult.Yes, IsDanger: true)
            });

        if (result != AppDialogResult.Yes)
        {
            if (!wasFailed)
                await ContinuePausedPipelineAsync();
            return;
        }

        await Task.Yield();
        var projectFolder = _currentProjectWorkFolder;
        if (!string.IsNullOrWhiteSpace(projectFolder))
        {
            var deletion = _projectIndexService.DeleteProject(GetWorkingRootFolder(), projectFolder);
            if (!deletion.Deleted)
            {
                AppDialogService.Error(
                    this,
                    "Projekt konnte nicht gelöscht werden",
                    deletion.ErrorMessage ?? "Der Projektordner konnte nicht vollständig entfernt werden.");
                return;
            }
        }

        _isPipelinePaused = false;
        _isPipelineFailed = false;
        _pauseRequested = false;
        ClearCurrentProjectAfterDeletion();
        ApplyWorkflowStatusViewState(_workflowStatusFormatter.Format(WorkflowStatusSnapshot.Empty));
        RefreshResumeProjects(showStatus: false);
    }

    private void ClearCurrentProjectAfterDeletion()
    {
        _isPipelineFailed = false;
        ClearSelectedSourceDisplayOverride();
        ResetLoadedResumeProjectState();
        ResetMetadataForNewFolderProject();
        SetMetadataEditingAvailable(false);
        SetMetadataPanelExpanded(false, animate: false);
        _currentFolderPath = string.Empty;
        _currentProjectWorkFolder = string.Empty;
        SelectedFolder = "Noch kein Ordner ausgewählt.";
        Tracks.Clear();
        UpdateIndexes();
        ExportProgressPercent = 0;
        SetPipelineState(ProjectPipelineState.Preparing);
        NotifyExportUiStateChanged();
        OnExportPreviewChanged();
    }

    private void CloseCurrentProject()
    {
        _isPipelinePaused = false;
        _isPipelineFailed = false;
        _pauseRequested = false;
        TrySaveActiveProjectSnapshot();
        PersistTrackListState();
        ClearSelectedSourceDisplayOverride();
        ResetLoadedResumeProjectState();
        ResetMetadataForNewFolderProject();
        SetMetadataEditingAvailable(false);
        SetMetadataPanelExpanded(false, animate: false);
        _currentFolderPath = string.Empty;
        _currentProjectWorkFolder = string.Empty;
        SelectedFolder = "Noch kein Ordner ausgewählt.";
        Tracks.Clear();
        UpdateIndexes();
        ExportProgressPercent = 0;
        ExportProgressText = BuildIdleExportProgressText();
        StatusText = "Bereit für ein neues Projekt.";
        NotifyExportUiStateChanged();
        OnExportPreviewChanged();
    }

    private ExportCheckResult ValidateExportPlan()
    {
        return _exportValidationService.Validate(
            Tracks,
            _ffmpegStatus,
            _currentFolderPath,
            OutputFolder,
            OutputFileNamePreview,
            OutputExtension,
            GetWorkingRootFolder(),
            finalOutputPathOverride: BuildFinalOutputPath());
    }

    private string BuildFinalOutputPath()
    {
        return _outputFolderLayoutService.BuildOutputPath(
            OutputFolder,
            Author,
            BookTitle,
            OutputFileNamePreview,
            _settings.OutputFolderLayout,
            Album,
            Series);
    }

    private string BuildOutputRelativePathPreview()
    {
        return _outputFolderLayoutService.BuildRelativeOutputPath(
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
            title: "Export prüfen",
            heading: "Export prüfen",
            message:
                "Vor dem Export sind ein paar Hinweise aufgefallen.\n" +
                "Du kannst trotzdem fortfahren. Du solltest die Liste aber kurz prüfen.\n\n" +
                "Trotzdem exportieren?",
            kind: AppDialogKind.Warning,
            details: AppDialogService.LimitDetails(warnings, 80),
            buttons: new[]
            {
                new AppDialogButton("Exportieren", AppDialogResult.Yes, IsPrimary: true, IsDefault: true),
                new AppDialogButton("Abbrechen", AppDialogResult.Cancel, IsCancel: true)
            });

        return result == AppDialogResult.Yes;
    }

    private void ShowExportBlockingErrors(IReadOnlyList<string> errors)
    {
        var containsInvalidAudio = errors.Any(error =>
            error.Contains("Keine gültige Audiodatei:", StringComparison.OrdinalIgnoreCase));

        AppDialogService.Show(
            this,
            title: containsInvalidAudio ? "Audiodateien prüfen" : "Export prüfen",
            heading: containsInvalidAudio ? "Ungültige Audiodateien gefunden" : "Export nicht möglich",
            message: containsInvalidAudio
                ? "BookStitch kann die unten aufgeführten Dateien nicht als Audio lesen. " +
                  "Sie sind möglicherweise beschädigt oder wurden nur umbenannt.\n\n" +
                  "Entferne oder ersetze diese Dateien und starte den Export erneut."
                : "Vor dem Export müssen noch ein paar Probleme behoben werden.\n\n" +
                  "Bitte korrigieren und erneut starten.",
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
        string projectType = ProjectManifestTypes.FolderProject)
    {
        var ffmpegPath = _ffmpegStatus.FfmpegPath ?? string.Empty;
        if (string.IsNullOrWhiteSpace(ffmpegPath))
        {
            AppDialogService.Error(
                this,
                "FFmpeg fehlt",
                "FFmpeg wurde nicht gefunden. Bitte prüfe die FFmpeg-Einstellungen und starte den Export erneut.");
            StatusText = "Export nicht möglich: FFmpeg fehlt.";
            return;
        }

        var exportPlan = _exportPlanService.Create(new ExportPlanRequest(
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

        var audioDiscManifest = string.Equals(
                projectType,
                ProjectManifestTypes.AudioCdProject,
                StringComparison.OrdinalIgnoreCase)
            ? _activeAudioDiscManifest
            : null;

        if (audioDiscManifest is not null)
        {
            _audioDiscProjectService.MarkExportStarted(audioDiscManifest);
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
        SetPipelineState(ProjectPipelineState.Converting);
        ExportProgressPercent = 0;
        if (!useWorkflowStatus)
            ExportProgressText = "Export wird vorbereitet …";

        _exportCancellation = new CancellationTokenSource();
        var refreshTrackStateAfterRun = false;
        var exportStatusOperationId = useWorkflowStatus
            ? BeginWorkflowStatusOperation(exportPlan.ProjectWorkFolder)
            : Guid.Empty;
        var exportPreset = ExportPreset.Parse(SelectedExportPreset);

        if (useWorkflowStatus)
        {
            PublishWorkflowStatus(exportStatusOperationId, CreateExportStatusSnapshot(
                workflowProjectKind,
                exportPlan.ProjectWorkFolder,
                trackSnapshot.Count,
                0,
                Array.Empty<int>(),
                exportPreset,
                totalDiscCount: audioDiscCount));
        }

        try
        {
            var continuingFromManualMergeReview =
                _isWaitingForManualMergeReview && !_manualMergeReviewNeedsReconversion;
            var request = new ExportWorkflowRequest(
                exportPlan,
                _currentFolderPath,
                ffmpegPath,
                CreateProjectSnapshotFromUi(),
                new FinalAudioTagData
                {
                    Title = BookTitle,
                    Album = Album,
                    Author = Author,
                    Narrator = Narrator,
                    Genre = Genre,
                    CoverPath = _processedCoverPath
                },
                ShouldPauseBeforeMerge(continuingFromManualMergeReview));

            var callbacks = new ExportWorkflowCallbacks
            {
                SetStatusText = useWorkflowStatus ? null : text => Dispatcher.Invoke(() => StatusText = text),
                SetProgressText = useWorkflowStatus ? null : text => Dispatcher.Invoke(() => ExportProgressText = text),
                SetProgressPercent = useWorkflowStatus ? null : percent => Dispatcher.Invoke(() => ExportProgressPercent = percent),
                SetPipelineState = state => Dispatcher.Invoke(() => SetPipelineState(state)),
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
                ResolveFinalOutputConflict = ResolveFinalOutputConflict,
                ResolveFinalOutputFailure = ResolveFinalOutputFailure
            };

            var result = await _exportWorkflowService.RunAsync(
                request,
                callbacks,
                _exportCancellation.Token);

            switch (result.Status)
            {
                case ExportWorkflowResultStatus.PausedBeforeMerge:
                    if (audioDiscManifest is not null)
                    {
                        _audioDiscProjectService.MarkExportPausedBeforeMerge(audioDiscManifest);
                        TrySaveCurrentAudioDiscProjectSnapshot(audioDiscManifest, force: true);
                    }

                    SetPipelineState(ProjectPipelineState.ReviewBeforeMerge);
                    if (useWorkflowStatus)
                        EndWorkflowStatusOperation(exportStatusOperationId);
                    PauseBeforeMergeForManualReview();
                    return;

                case ExportWorkflowResultStatus.Completed:
                    if (audioDiscManifest is not null)
                    {
                        _audioDiscProjectService.MarkExportCompleted(audioDiscManifest, result.OutputPath);
                        TrySaveCurrentAudioDiscProjectSnapshot(audioDiscManifest, force: true);
                    }

                    _isWaitingForManualMergeReview = true;
                    _manualMergeReviewPreparedPreset = SelectedExportPreset;
                    _manualMergeReviewNeedsReconversion = false;
                    _isCurrentProjectCompleted = true;
                    SetPipelineState(ProjectPipelineState.Completed);
                    SetTrackActionDisplayOverride("Zusammenfügen");
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
                                TotalChapters = trackSnapshot.Count,
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
                        ExportProgressText = "Ausgabedatei vollständig zusammengefügt und mit Tags und Cover gespeichert.";
                        StatusText = "Hörbuch erfolgreich erstellt.";
                    }
                    _notificationService.Notify(NotificationEvent.ProjectCompleted);

                    var exportFinishedResult = AppDialogService.Show(
                        this,
                        title: "Export fertig",
                        heading: "Export abgeschlossen",
                        message: result.OutputPath,
                        kind: AppDialogKind.Information,
                        buttons: new[]
                        {
                            new AppDialogButton("Ordner öffnen", AppDialogResult.Yes, IsPrimary: true),
                            new AppDialogButton("OK", AppDialogResult.Ok, IsDefault: true, IsCancel: true)
                        });

                    if (exportFinishedResult == AppDialogResult.Yes)
                        OpenFolderInExplorer(Path.GetDirectoryName(result.OutputPath) ?? OutputFolder, "Ausgabeordner");

                    return;

                case ExportWorkflowResultStatus.FinalOutputDiscarded:
                    refreshTrackStateAfterRun = true;
                    if (audioDiscManifest is not null)
                    {
                        _audioDiscProjectService.MarkExportCanceled(audioDiscManifest);
                        TrySaveCurrentAudioDiscProjectSnapshot(audioDiscManifest, force: true);
                    }

                    ReturnToReviewAfterInterruptedExport();
                    ExportProgressText = "Fertige Datei wurde verworfen.";
                    StatusText = "Export abgebrochen. Die vorhandene Ausgabedatei wurde nicht verändert.";
                    return;

                case ExportWorkflowResultStatus.Canceled:
                    refreshTrackStateAfterRun = true;
                    if (audioDiscManifest is not null)
                    {
                        _audioDiscProjectService.MarkExportCanceled(audioDiscManifest);
                        TrySaveCurrentAudioDiscProjectSnapshot(audioDiscManifest, force: true);
                    }

                    if (_pauseRequested && _pipelineState == ProjectPipelineState.Converting)
                    {
                        if (useWorkflowStatus)
                        {
                            var last = _workflowStatusCoordinator.Snapshot;
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
                            EnterPipelinePause("Konvertierung pausiert. Unvollständige Dateien wurden entfernt.");
                        }
                        return;
                    }

                    var canceledDuringMerge = _pipelineState == ProjectPipelineState.Merging;
                    var lastStatus = useWorkflowStatus ? _workflowStatusCoordinator.Snapshot : null;
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
                            title: "Zusammenfügen abgebrochen",
                            heading: "Zusammenfügen abgebrochen",
                            message: "Die vorbereiteten Tracks bleiben erhalten. Das Projekt ist weiterhin bereit zum Zusammenfügen.",
                            kind: AppDialogKind.Information,
                            buttons: new[]
                            {
                                new AppDialogButton("OK", AppDialogResult.Ok, IsPrimary: true, IsDefault: true, IsCancel: true)
                            });
                    }
                    else
                    {
                        ExportProgressText = "Benutzerabbruch.";
                        StatusText = "Export abgebrochen. Das quellenvollständige Projekt bleibt geöffnet.";
                        ShowUserCanceledProjectDialog();
                    }
                    return;

                case ExportWorkflowResultStatus.Failed:
                    refreshTrackStateAfterRun = true;
                    if (audioDiscManifest is not null)
                    {
                        _audioDiscProjectService.MarkExportFailed(
                            audioDiscManifest,
                            result.Error?.Message);
                        TrySaveCurrentAudioDiscProjectSnapshot(audioDiscManifest, force: true);
                    }

                    var failedStatus = useWorkflowStatus ? _workflowStatusCoordinator.Snapshot : null;
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
                                Error = _workflowExportFailureStatusService.Create(result.Error)
                            });
                        EndWorkflowStatusOperation(exportStatusOperationId);
                    }
                    else
                    {
                        ExportProgressText = "Export fehlgeschlagen.";
                        StatusText = "Export fehlgeschlagen. Das quellenvollständige Projekt bleibt geöffnet.";
                    }

                    _isPipelineFailed = true;
                    _isPipelinePaused = false;
                    NotifyExportUiStateChanged();

                    _notificationService.Notify(NotificationEvent.Error);
                    var failureDetails = _exportFailureDetailsService.BuildExportFailureDetails(
                        result.Error ?? new InvalidOperationException("Unbekannter Exportfehler."),
                        result.ConvertedFolder);
                    var failureText = string.Join(Environment.NewLine, failureDetails);
                    var failureDialogResult = ShowHardWorkflowErrorDialog(
                        "Export fehlgeschlagen",
                        "Export fehlgeschlagen",
                        "Beim Export ist ein Fehler aufgetreten.\n\nBetroffene Datei und technische Details:",
                        failureText,
                        failureDetails);

                    if (failureDialogResult == AppDialogResult.Yes)
                        await DeletePausedProjectAsync();

                    return;
            }
        }
        finally
        {
            if (refreshTrackStateAfterRun)
                RefreshLoadedProjectTrackStateFromPersistedProject(exportPlan.ProjectWorkFolder);

            if (useLocalWorkflowStatus)
                EndWorkflowStatusOperation(exportStatusOperationId);

            _exportCancellation?.Dispose();
            _exportCancellation = null;
            IsExporting = false;
            IsBusy = false;
        }
    }


    private void ReturnToReviewAfterInterruptedExport()
    {
        _isWaitingForManualMergeReview = true;
        _manualMergeReviewPreparedPreset = SelectedExportPreset;
        SetPipelineState(ProjectPipelineState.ReviewBeforeMerge);
        _trackPreparedStateRefreshService.RefreshForCurrentPreset(Tracks, _currentProjectWorkFolder, SelectedExportPreset);
        UpdateManualMergeReviewPresetState();
    }

    private FinalOutputConflictAction ResolveFinalOutputConflict(string outputPath, string renamedOutputPath)
    {
        if (!Dispatcher.CheckAccess())
            return Dispatcher.Invoke(() => ResolveFinalOutputConflict(outputPath, renamedOutputPath));

        if (_settings.OverwriteFinalOutputWithoutAsking)
            return FinalOutputConflictAction.Overwrite;

        var result = AppDialogService.Show(
            this,
            "Ausgabedatei existiert bereits",
            "Fertige Datei speichern",
            "Die Konvertierung und das Zusammenfügen sind abgeschlossen. Im Ausgabeordner existiert bereits eine Datei mit diesem Namen.\n\n" +
            outputPath +
            "\n\nBei ‚Umbenennen‘ wird die neue Datei gespeichert als:\n" +
            Path.GetFileName(renamedOutputPath) +
            "\n\nBei ‚Abbrechen‘ wird die neu zusammengefügte Datei verworfen. Die vorbereiteten Tracks bleiben im Projekt erhalten.",
            AppDialogKind.Question,
            null,
            new[]
            {
                new AppDialogButton("Überschreiben", AppDialogResult.Yes, IsPrimary: true, IsDefault: true),
                new AppDialogButton("Umbenennen", AppDialogResult.No),
                new AppDialogButton("Abbrechen", AppDialogResult.Cancel, IsCancel: true)
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
            title: "Ausgabedatei konnte nicht gespeichert werden",
            heading: "Fertiges Hörbuch auf dem Desktop speichern?",
            message:
                "Das Hörbuch wurde vollständig zusammengefügt, konnte aber nicht am gewählten Ausgabeort gespeichert werden. " +
                "Der Ordner ist möglicherweise nicht mehr erreichbar oder die vorhandene Datei wird gerade von einem anderen Programm verwendet.\n\n" +
                "Gewähltes Ziel:\n" + outputPath + "\n\n" +
                "BookStitch kann die fertige Datei stattdessen auf dem Desktop speichern als:\n" +
                Path.GetFileName(desktopOutputPath) + "\n\n" +
                "Bei ‚Abbrechen‘ wird die fertige zusammengefügte Datei verworfen. Die vorbereiteten Tracks bleiben im Projekt erhalten.",
            kind: AppDialogKind.Warning,
            details: new[] { error.Message },
            buttons: new[]
            {
                new AppDialogButton("Auf Desktop speichern", AppDialogResult.Yes, IsPrimary: true, IsDefault: true),
                new AppDialogButton("Abbrechen", AppDialogResult.Cancel, IsCancel: true)
            },
            width: 760,
            height: 460);

        return result == AppDialogResult.Yes
            ? FinalOutputFailureAction.SaveToDesktop
            : FinalOutputFailureAction.Discard;
    }


    private bool ShouldPauseBeforeMerge(bool continuingFromManualMergeReview)
    {
        if (continuingFromManualMergeReview)
            return false;

        if (_pauseBeforeMergeOverride.HasValue)
            return _pauseBeforeMergeOverride.Value;

        return !_settings.MergeAutomaticallyAfterConversion;
    }

    private void PauseBeforeMergeForManualReview()
    {
        _isCurrentProjectCompleted = false;
        _isWaitingForManualMergeReview = true;
        SetPipelineState(ProjectPipelineState.ReviewBeforeMerge);
        _manualMergeReviewPreparedPreset = SelectedExportPreset;
        _manualMergeReviewNeedsReconversion = false;
        SetTrackActionDisplayOverride("Zusammenfügen");
        NotifyExportUiStateChanged();
        if (_loadedResumeProjectIsMp3Disc)
        {
            var operationId = BeginWorkflowStatusOperation(_currentProjectWorkFolder);
            var totalDiscs = Math.Max(1, Tracks.Select(track => track.DiscNumber ?? 1).DefaultIfEmpty(1).Max());
            PublishWorkflowStatus(
                operationId,
                _mp3DiscWorkflowStatusAdapter.CreateReadySnapshot(
                    _currentProjectWorkFolder,
                    totalDiscs,
                    Tracks.Count,
                    ExportPreset.Parse(SelectedExportPreset)));
            EndWorkflowStatusOperation(operationId);
        }
        else if (_loadedResumeProjectIsLocal || !_loadedResumeProjectIsAudioDisc)
        {
            var operationId = BeginWorkflowStatusOperation(_currentProjectWorkFolder);
            PublishWorkflowStatus(
                operationId,
                _localWorkflowStatusAdapter.CreateReadySnapshot(
                    _currentProjectWorkFolder,
                    Tracks.Count,
                    ExportPreset.Parse(SelectedExportPreset)));
            EndWorkflowStatusOperation(operationId);
        }
        else
        {
            ExportProgressPercent = 100;
            ExportProgressText = "Konvertierung abgeschlossen. Trackliste prüfen und anschließend zusammenfügen.";
            StatusText = "Konvertierung abgeschlossen. Du kannst die Trackliste jetzt prüfen, sortieren oder Tracks ausschließen. Danach auf „Zusammenfügen“ klicken.";
        }

        _notificationService.Notify(NotificationEvent.UserActionRequired);

        AppDialogService.Show(
            this,
            title: "Vor dem Zusammenfügen stoppen",
            heading: "Konvertierung abgeschlossen",
            message:
                "BookStitch hat alle Tracks vorbereitet und stoppt vor dem finalen Zusammenfügen.\n\n" +
                "Du kannst die Trackliste jetzt noch einmal prüfen, sortieren oder einzelne Tracks entfernen.\n\n" +
                "Wenn alles passt, klicke auf „Zusammenfügen“. Änderst du das Export-Preset, werden die Tracks zuerst für das neue Preset konvertiert und danach erneut zur Prüfung angehalten.",
            kind: AppDialogKind.Information,
            buttons: new[]
            {
                new AppDialogButton("OK", AppDialogResult.Ok, IsPrimary: true, IsDefault: true, IsCancel: true)
            });
    }

    private void WriteFinalTags(string finalAudioPath)
    {
        _finalTagService.WriteFinalTags(
            finalAudioPath,
            new FinalAudioTagData
            {
                Title = BookTitle,
                Album = Album,
                Author = Author,
                Narrator = Narrator,
                Genre = Genre,
                CoverPath = _processedCoverPath
            });
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
            ? $" | aktiv: {string.Join(", ", activeTrackNumbers)}"
            : "";

        Dispatcher.InvokeAsync(() =>
        {
            ExportProgressPercent = percent;
            ExportProgressText = $"{percent:0.0}% | {completedCount}/{totalCount} fertig{activeText}";
        });
    }

    private string BuildIdleExportProgressText()
    {
        var total = Tracks.Count;
        return $"0,0 % | 0/{total} fertig";
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
                    "Ungültige Parallelität",
                    "Bitte bei Parallel entweder Auto oder eine Zahl von 1 bis 40 eingeben.");
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
                    "Ungültige Parallelität",
                    "Die Parallelität darf nur zwischen 1 und 40 liegen.");
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
        var configured = _settings.WorkingFolder;

        if (string.IsNullOrWhiteSpace(configured) || IsLegacyDefaultWorkingFolder(configured))
        {
            configured = GetDefaultWorkingFolder();
            _settings.WorkingFolder = configured;
            SaveSettingsIfReady();
        }

        return _workFolderStructureService.Ensure(configured);
    }

    private string GetWorkingRootFolder()
    {
        return GetProjectFolderStructure().ProjectRootFolder;
    }

    private static string GetDefaultWorkingFolder()
    {
        return Path.Combine(GetMusicFolder(), "BookStitchProjects");
    }

    private static bool IsLegacyDefaultWorkingFolder(string? folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
            return false;

        var legacyFolder = Path.Combine(GetMusicFolder(), "BookStitch", "Work");

        try
        {
            return string.Equals(
                Path.GetFullPath(folderPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                Path.GetFullPath(legacyFolder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static string GetMusicFolder()
    {
        var musicFolder = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);

        if (string.IsNullOrWhiteSpace(musicFolder))
            musicFolder = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        return musicFolder;
    }

    private string BuildProjectWorkFolderName()
    {
        return _exportPlanService.BuildProjectWorkFolderName(_currentFolderPath, Author, BookTitle);
    }

    private void RemoveGeneratedOrWorkFilesFromTrackList(string folderPath)
    {
        if (Tracks.Count == 0)
            return;

        var expectedOutputInSourceFolder = Path.Combine(folderPath, OutputFileNamePreview);
        var workRoot = _settings.WorkingFolder;
        var removeList = _trackWorkspaceFilterService.GetGeneratedOrWorkTracks(folderPath, Tracks, expectedOutputInSourceFolder, workRoot);

        foreach (var track in removeList)
            Tracks.Remove(track);
    }

    private void RecalculateProcessingActionsForCurrentPreset()
    {
        if (Tracks.Count == 0)
            return;

        _trackPreparedStateRefreshService.RefreshForCurrentPreset(Tracks, _currentProjectWorkFolder, SelectedExportPreset);
        var preset = ExportPreset.Parse(SelectedExportPreset);

        foreach (var track in Tracks)
        {
            track.HasReusableConvertedFile = _trackPreparedStateRefreshService.IsReusablePreparedConvertedTrack(track, SelectedExportPreset);
            if (_isAudioDiscProjectAwaitingRip &&
                !string.IsNullOrWhiteSpace(track.FilePath) &&
                !File.Exists(track.FilePath))
            {
                track.ProcessingAction = "FLAC rippen";
                continue;
            }

            track.ProcessingAction = AudioProcessingService.DetermineProcessingAction(track, preset);
        }

        TracksGrid?.Items.Refresh();
        OnExportPreviewChanged();
        _trackStateUpdateQueueService.RequestRefresh();
    }



    private string BuildProcessingActionSummary()
    {
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

        var selected = TracksGrid
            .SelectedItems
            .OfType<TrackInfo>()
            .ToList();

        var sortKey = e.Column.SortMemberPath;

        if (string.IsNullOrWhiteSpace(sortKey))
            sortKey = e.Column.Header?.ToString() ?? "";

        var currentOrder = Tracks.ToList();
        var direction = GetNextTrackGridSortDirection(sortKey, currentOrder);
        var sortedTracks = _trackListActionService.Sort(currentOrder, sortKey, direction);

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
        var ascending = _trackListActionService.Sort(currentOrder, sortKey, ListSortDirection.Ascending);
        var descending = _trackListActionService.Sort(currentOrder, sortKey, ListSortDirection.Descending);

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

        SaveTrackGridColumnLayout();
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

    private void TracksGrid_ColumnReordered(object sender, DataGridColumnEventArgs e)
    {
        if (_isApplyingTrackGridColumnLayout)
            return;

        SaveTrackGridColumnLayout();
    }

    private void BuildTrackGridRowContextMenu(ContextMenu menu)
    {
        menu.Items.Clear();
        menu.Items.Add(CreateTrackGridMenuItem(
            "Ausgewählte Tracks ausschließen",
            (_, _) => RemoveSelectedTracks_Click(this, new RoutedEventArgs())));
        menu.Items.Add(CreateTrackGridMenuItem(
            "Ausgewählte Tracks wiederherstellen",
            (_, _) => RestoreSelectedTracks_Click(this, new RoutedEventArgs())));
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
            CreateTrackGridCheckedMenuHeader("AutoFit verwenden", _settings.TrackGridAutoFitEnabled),
            (_, _) => ToggleTrackGridAutoFit()));
        menu.Items.Add(CreateTrackGridMenuItem(
            "Standardlayout wiederherstellen",
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
            column.Header?.ToString() ?? GetTrackGridColumnKey(column),
            column.Visibility == Visibility.Visible);
    }

    private static FrameworkElement CreateTrackGridCheckedMenuHeader(string text, bool isChecked)
    {
        var panel = new Grid();
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var check = new TextBlock
        {
            Text = isChecked ? "✓" : "",
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

    private void ToggleTrackGridColumnVisibility(DataGridColumn column)
    {
        column.Visibility = column.Visibility == Visibility.Visible
            ? Visibility.Collapsed
            : Visibility.Visible;

        SaveTrackGridColumnLayout();
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
        }
        finally
        {
            _isApplyingTrackGridColumnLayout = false;
        }

        SaveSettingsIfReady();
        AutoFitTrackColumnsAfterRender();
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

        var ordered = TracksGrid.Columns
            .Select((column, originalIndex) => new
            {
                Column = column,
                OriginalIndex = originalIndex,
                Saved = saved.GetValueOrDefault(GetTrackGridColumnKey(column))
            })
            .OrderBy(item => item.Saved?.DisplayIndex ?? int.MaxValue)
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
            }
        }
        finally
        {
            _isApplyingTrackGridColumnLayout = false;
        }
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
        return column.SortMemberPath
               ?? column.Header?.ToString()
               ?? "Column";
    }

    private static double? GetTrackGridColumnWidth(DataGridColumn column)
    {
        var width = column.ActualWidth > 0
            ? column.ActualWidth
            : column.Width.IsAbsolute
                ? column.Width.Value
                : 0;

        return width > 0
            ? Math.Round(width, 1)
            : null;
    }

    private static Visibility GetDefaultTrackGridColumnVisibility(DataGridColumn column)
    {
        return string.Equals(GetTrackGridColumnKey(column), "Extension", StringComparison.Ordinal)
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private static DataGridLength GetDefaultTrackGridColumnWidth(DataGridColumn column)
    {
        return GetTrackGridColumnKey(column) switch
        {
            "#" => DataGridLength.Auto,
            "DiscNumber" => new DataGridLength(55),
            "TrackNumber" => new DataGridLength(60),
            "FileName" => new DataGridLength(200),
            "RelativeFolder" => new DataGridLength(100),
            "TagTitle" => new DataGridLength(220),
            "ChapterTitle" => new DataGridLength(160),
            "Duration" => new DataGridLength(75),
            "BitrateKbps" => new DataGridLength(70),
            "ChannelLayout" => new DataGridLength(95),
            "FileWarningText" => new DataGridLength(150),
            "ChapterWarningText" => new DataGridLength(130),
            "Extension" => new DataGridLength(60),
            "Codec" => new DataGridLength(75),
            "ConvertedSizeMb" => new DataGridLength(90),
            _ => DataGridLength.Auto
        };
    }

    private static T? FindVisualAncestor<T>(DependencyObject? source)
        where T : DependencyObject
    {
        var current = source;
        while (current is not null)
        {
            if (current is T match)
                return match;

            current = VisualTreeHelper.GetParent(current);
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
        HandleTracksGridDeleteKey(e);
    }

    private void TracksGrid_KeyDown(object sender, KeyEventArgs e)
    {
        HandleTracksGridDeleteKey(e);
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
        var result = _trackListActionService.ToggleSelectedForDelete(selected);
        if (result.ChangedCount == 0)
            return false;

        UpdateIndexes();
        PersistTrackListState();
        UpdateFinalStatus(Tracks.Count(track => !track.IsExcluded));
        ExportProgressText = BuildIdleExportProgressText();
        StatusText = result.Action == TrackExclusionToggleAction.Restored
            ? result.ChangedCount == 1 ? "1 Track wurde wiederhergestellt." : $"{result.ChangedCount} Tracks wurden wiederhergestellt."
            : result.ChangedCount == 1 ? "1 Track wurde ausgeschlossen." : $"{result.ChangedCount} Tracks wurden ausgeschlossen.";
        OnPropertyChanged(nameof(CanStartExport));
        OnExportPreviewChanged();
        TracksGrid.Items.Refresh();
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

        var changed = _trackListActionService.ExcludeSelected(selected);
        if (changed == 0)
            return false;

        UpdateIndexes();
        PersistTrackListState();
        UpdateFinalStatus(Tracks.Count(track => !track.IsExcluded));
        ExportProgressText = BuildIdleExportProgressText();
        StatusText = changed == 1 ? "1 Track wurde ausgeschlossen." : $"{changed} Tracks wurden ausgeschlossen.";
        OnPropertyChanged(nameof(CanStartExport));
        OnExportPreviewChanged();
        TracksGrid.Items.Refresh();
        return true;
    }

    private void RestoreSelectedTracks_Click(object sender, RoutedEventArgs e)
    {
        if (!CanEditTrackOrder)
            return;

        var visibleTracks = GetVisibleTracks();
        var selected = GetSelectedTracksInVisibleOrder(visibleTracks);
        var changed = _trackListActionService.RestoreSelected(selected);
        if (changed == 0)
            return;

        UpdateIndexes();
        PersistTrackListState();
        UpdateFinalStatus(Tracks.Count(track => !track.IsExcluded));
        ExportProgressText = BuildIdleExportProgressText();
        StatusText = changed == 1 ? "1 Track wurde wiederhergestellt." : $"{changed} Tracks wurden wiederhergestellt.";
        OnPropertyChanged(nameof(CanStartExport));
        OnExportPreviewChanged();
        TracksGrid.Items.Refresh();
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

        var newOrder = _trackListActionService.MoveSelectedUp(visibleTracks, selected);

        ReplaceTrackList(newOrder);
        RestoreSelection(selected);
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

        var newOrder = _trackListActionService.MoveSelectedDown(visibleTracks, selected);

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

        var newOrder = _trackListActionService.MoveSelectedToTop(visibleTracks, selected);

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

        var newOrder = _trackListActionService.MoveSelectedToBottom(visibleTracks, selected);

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
        Tracks.Clear();

        foreach (var track in newOrder)
            Tracks.Add(track);

        UpdateIndexes();
        PersistTrackListState();
        AutoFitTrackColumnsAfterRender();
    }

    private void PersistTrackListState()
    {
        var folder = !string.IsNullOrWhiteSpace(_currentProjectWorkFolder)
            ? _currentProjectWorkFolder
            : string.Empty;
        if (!string.IsNullOrWhiteSpace(folder))
            _trackListStateService.Save(folder, Tracks.ToList());
    }

    private void ApplyPersistedTrackListState(string projectFolder)
    {
        var ordered = _trackListStateService.Apply(projectFolder, Tracks);
        Tracks.Clear();
        foreach (var track in ordered)
            Tracks.Add(track);
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
        _trackListActionService.Renumber(Tracks, _settings.UseLeadingZerosInChapterSuggestions);
        _trackListWarningService.Apply(Tracks);

        TracksGrid?.Items.Refresh();
        OnExportPreviewChanged();
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
            {
                if (IsFixedTrackColumnWidth(column))
                    continue;

                column.Width = DataGridLength.Auto;
            }

            TracksGrid.UpdateLayout();

            foreach (var column in TracksGrid.Columns)
            {
                var header = column.Header?.ToString() ?? "";

                if (IsFixedTrackColumnWidth(column))
                    continue;

                var width = Math.Max(column.ActualWidth + 12, EstimateTrackColumnWidth(header, allTracks));

                width = header switch
                {
                    "#" => Math.Max(width, 45),
                    "Disc" => Math.Max(width, 55),
                    "Track" => Math.Max(width, 60),
                    "Datei" => Math.Clamp(width, 160, 420),
                    "Ordner" => Math.Clamp(width, 70, 360),
                    "Tag-Titel" => Math.Clamp(width, 120, 520),
                    "Kapitelvorschlag" => Math.Clamp(width, 120, 480),
                    "Dauer" => Math.Max(width, 75),
                    "Bitrate" => Math.Max(width, 70),
                    "Kanäle" => Math.Max(width, 65),
                    "Mono/Stereo" => Math.Max(width, 95),
                    "Warnung" => Math.Clamp(width, 75, 420),
                    "Dateiwarnung" => Math.Clamp(width, 175, 420),
                    "Kapitelwarnung" => Math.Clamp(width, 145, 360),
                    "Typ" => Math.Max(width, 60),
                    "Codec" => Math.Max(width, 75),
                    "Aktion" => Math.Max(width, 110),
                    "Quellgröße MB" => Math.Max(width, 105),
                    "AAC-Größe MB" => Math.Max(width, 105),
                    "Output MB" => Math.Max(width, 90),
                    _ => width
                };

                column.Width = new DataGridLength(width);
            }
        });
    }

    private static bool IsFixedTrackColumnWidth(DataGridColumn column)
    {
        var header = column.Header?.ToString() ?? string.Empty;
        return string.Equals(header, "Dateiwarnung", StringComparison.Ordinal) ||
               string.Equals(header, "Kapitelwarnung", StringComparison.Ordinal);
    }

    private static double EstimateTrackColumnWidth(string header, IReadOnlyList<TrackInfo> tracks)
    {
        var maxTextLength = EstimateTextWidth(header) + 28;

        foreach (var track in tracks)
        {
            var text = header switch
            {
                "#" => track.Index.ToString(CultureInfo.InvariantCulture),
                "Disc" => track.DiscNumber?.ToString(CultureInfo.InvariantCulture) ?? "",
                "Track" => track.TrackNumber?.ToString(CultureInfo.InvariantCulture) ?? "",
                "Datei" => track.FileName,
                "Ordner" => track.RelativeFolder,
                "Tag-Titel" => track.TagTitle,
                "Kapitelvorschlag" => track.ChapterTitle,
                "Dauer" => track.Duration,
                "Bitrate" => track.BitrateKbps?.ToString(CultureInfo.InvariantCulture) ?? "",
                "Kanäle" => track.ChannelLayout,
                "Mono/Stereo" => track.ChannelLayout,
                "Warnung" => track.Warning,
                "Dateiwarnung" => track.DisplayFileWarning,
                "Kapitelwarnung" => track.DisplayChapterWarning,
                "Typ" => track.Extension,
                "Codec" => track.Codec,
                "Aktion" => track.ProcessingAction,
                "Quellgröße MB" => track.SizeMb > 0 ? track.SizeMb.ToString("0.00", CultureInfo.InvariantCulture) : "",
                "AAC-Größe MB" => track.DisplayOutputSizeMb,
                "Output MB" => track.DisplayOutputSizeMb,
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

    private static void SetTrackDuration(TrackInfo track, TimeSpan? duration)
    {
        if (duration is null)
            return;

        var property = typeof(TrackInfo).GetProperty("Duration");

        if (property is null || !property.CanWrite)
            return;

        var targetType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;

        if (targetType == typeof(string))
        {
            property.SetValue(track, FormatDuration(duration.Value));
            track.DurationTicks = duration.Value.Ticks;
            return;
        }

        if (targetType == typeof(TimeSpan))
        {
            property.SetValue(track, duration.Value);
            track.DurationTicks = duration.Value.Ticks;
        }
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

    private void NotifyExportUiStateChanged()
    {
        OnPropertyChanged(nameof(CanSelectFolder));
        OnPropertyChanged(nameof(CanStartNewProject));
        OnPropertyChanged(nameof(CanConfigureFfmpeg));
        OnPropertyChanged(nameof(CanEditTrackOrder));
        OnPropertyChanged(nameof(CanStartExport));
        OnPropertyChanged(nameof(ExportButtonText));
        OnPropertyChanged(nameof(SecondaryButtonText));
        OnPropertyChanged(nameof(SecondaryButtonVisibility));
        OnPropertyChanged(nameof(CanCancelExport));
        OnPropertyChanged(nameof(CanAddProjectSources));
        OnPropertyChanged(nameof(AddProjectSourcesVisibility));
        OnPropertyChanged(nameof(AddProjectSourcesButtonText));
        OnPropertyChanged(nameof(CanChooseOutputFolder));
        OnPropertyChanged(nameof(CanChangeExportOptions));
        OnPropertyChanged(nameof(CanChangeExportPreset));
        OnPropertyChanged(nameof(CanOpenSettings));
        OnPropertyChanged(nameof(CanChangeBookMetadata));
        OnPropertyChanged(nameof(CoverHintVisibility));
        OnPropertyChanged(nameof(CanRefreshResumeProjects));
        OnPropertyChanged(nameof(CanInspectSelectedResumeProject));
        OnPropertyChanged(nameof(ExportProgressVisibility));

        if (_isWaitingForManualMergeReview && !_isMetadataPanelExpanded)
            SetMetadataPanelExpanded(true, animate: true);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}