using BookStitch.Controls;
using BookStitch.Models;
using BookStitch.Presentation;
using BookStitch.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Text;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace BookStitch.Dialog;

public partial class MetadataDialog : Window, INotifyPropertyChanged
{
    private const string CompatibilityFieldTechnicalName = "Compatibility Field";
    private const string DescriptionOutputPreviewTechnicalName = "Description Output Preview";
    private readonly Action<Window> _openSettings;
    private readonly GlobalSettingsManager? _genreSettingsManager;
    private readonly Action _refreshUi;
    private readonly ProjectMetadataState _metadataState;
    private readonly AppSettings _appSettings;
    private readonly Action _appSettingsChanged;
    private readonly MetadataPreferences _metadataPreferences;
    private readonly Action<MetadataPreferences> _metadataPreferencesChanged;
    private readonly Action<ProjectMetadata> _saveMetadata;
    private readonly Func<string?> _projectFolderProvider;
    private readonly Func<IReadOnlyList<SourceMetadataSnapshot>> _sourceSnapshotsProvider;
    private readonly SourceMetadataAlternativeService _candidateService;
    private readonly Func<FinalAudioTagData> _compatibilityPreviewProvider;
    private readonly Func<bool> _compatibilityDescriptionEnabledProvider;
    private readonly Action<bool> _compatibilityDescriptionEnabledChanged;
    private readonly Action _refreshChapterTimelinePresentation;
    private readonly Action _chapterSettingsChanged;
    private readonly ChapterSettingsEditorService _chapterSettingsEditorService = new();
    private readonly ChapterGridColumnLayoutState _chapterGridColumnLayoutState;
    private readonly CoverImageService _coverImageService;
    private readonly CoverPickerService? _coverPickerService;
    private CoverProcessedAssetInfo? _coverProcessedAssetInfo;
    private readonly DependencyPropertyDescriptor _chapterColumnWidthDescriptor = DependencyPropertyDescriptor.FromProperty(DataGridColumn.WidthProperty, typeof(DataGridColumn))!;
    private bool _isApplyingChapterColumnLayout;
    private readonly ConceptCandidateSessionService _candidateSession;
    private readonly MetadataNormalizationService _normalization;
    private readonly CancellationTokenSource _candidateLoadCancellation = new();
    private int _candidateLoadVersion;
    private bool _candidateRefreshQueued;
    private bool _closeRequested;
    private string? _candidateProjectKey;
    private readonly Func<IReadOnlyList<MetadataFieldDecisionDiagnostic>> _metadataFieldDecisionsProvider;
    private readonly bool _canChooseCover;
    private readonly Action<Window> _chooseCover;
    private readonly Func<IDataObject, Window, Task>? _dropCover;
    private readonly Action _removeCover;
    private string _conceptSearchText = string.Empty;
    private string _preSearchFamilyKey = ConceptMetadataGroupIds.QuickAccess;
    private FrameworkElement? _activeConceptSearchSurface;
    private bool _isApplyingConceptSearch;
    private readonly List<ConceptSearchFieldPlacement> _conceptSearchFieldPlacements = [];
    private readonly List<FrameworkElement> _conceptSearchTransientFields = [];
    private readonly ObservableCollection<TrackInfo> _chapterTracks;
    private readonly ObservableCollection<MetadataChapterPresentationRow> _chapterPresentationRows = [];
    private ScrollViewer? _chapterGridScrollViewer;
    private bool _isSyncingChapterVerticalScrollBar;
    private bool _isSyncingChapterHorizontalScrollBar;
    private bool _isRefreshingChapterSettingsEditor;
    private bool _isRefreshingCoverWarningThreshold;
    private readonly DispatcherTimer _chapterAutoFitTimer = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private string? _chapterAutoFitSignature;
    private double _lastChapterAutoFitAvailableWidth = double.NaN;
    private bool _chapterAutoFitForcePending;
    private const double ChapterAutoFitHysteresis = 10d;
    private const double ChapterAutoFitResizeThreshold = 12d;

    public ICollectionView ChapterTracksView { get; }
    public ConceptSpecialFieldDraftState SpecialDrafts { get; } = new();

    public MetadataDialog(
        bool usePrivateGenreList,
        Action<Window> openSettings,
        ProjectMetadataState metadataState,
        AppSettings appSettings,
        bool canChooseCover,
        Action<Window> chooseCover,
        Action removeCover,
        Action? refreshUi = null,
        Action? appSettingsChanged = null,
        MetadataPreferences? metadataPreferences = null,
        Action<MetadataPreferences>? metadataPreferencesChanged = null,
        Func<string?>? projectFolderProvider = null,
        Func<IReadOnlyList<SourceMetadataSnapshot>>? sourceSnapshotsProvider = null,
        Func<IReadOnlyList<MetadataFieldDecisionDiagnostic>>? metadataFieldDecisionsProvider = null,
        Action<ProjectMetadata>? saveMetadata = null,
        SourceMetadataAlternativeService? candidateService = null,
        ScalarCandidatePresentationService? scalarCandidatePresentation = null,
        Func<FinalAudioTagData>? compatibilityPreviewProvider = null,
        Func<bool>? compatibilityDescriptionEnabledProvider = null,
        Action<bool>? compatibilityDescriptionEnabledChanged = null,
        ObservableCollection<TrackInfo>? tracks = null,
        Action? refreshChapterTimelinePresentation = null,
        Action? chapterSettingsChanged = null,
        ChapterGridColumnLayoutState? chapterGridColumnLayoutState = null,
        CoverImageService? coverImageService = null,
        CoverPickerService? coverPickerService = null,
        GlobalSettingsManager? genreSettingsManager = null,
        Func<IDataObject, Window, Task>? dropCover = null)
    {
        ArgumentNullException.ThrowIfNull(openSettings);
        ArgumentNullException.ThrowIfNull(metadataState);
        ArgumentNullException.ThrowIfNull(appSettings);
        ArgumentNullException.ThrowIfNull(chooseCover);
        ArgumentNullException.ThrowIfNull(removeCover);

        InitializeComponent();

        _openSettings = openSettings;
        _genreSettingsManager = genreSettingsManager;
        _refreshUi = refreshUi ?? (() => { });
        _metadataState = metadataState;
        _appSettings = appSettings;
        _appSettingsChanged = appSettingsChanged ?? (() => { });
        _metadataPreferences = metadataPreferences ?? new MetadataPreferences();
        MetadataPreferencesService.Normalize(_metadataPreferences);
        _metadataPreferencesChanged = metadataPreferencesChanged ?? (_ => { });
        _saveMetadata = saveMetadata ?? (_ => { });
        _projectFolderProvider = projectFolderProvider ?? (() => null);
        _sourceSnapshotsProvider = sourceSnapshotsProvider ?? (() => []);
        _metadataFieldDecisionsProvider = metadataFieldDecisionsProvider ?? (() => []);
        _candidateService = candidateService ?? new SourceMetadataAlternativeService();
        _compatibilityPreviewProvider = compatibilityPreviewProvider ?? (() => new FinalAudioTagData());
        _compatibilityDescriptionEnabledProvider = compatibilityDescriptionEnabledProvider ?? (() => true);
        _compatibilityDescriptionEnabledChanged = compatibilityDescriptionEnabledChanged ?? (_ => { });
        _refreshChapterTimelinePresentation = refreshChapterTimelinePresentation ?? (() => { });
        _chapterSettingsChanged = chapterSettingsChanged ?? (() => { });
        _chapterGridColumnLayoutState = chapterGridColumnLayoutState ?? new ChapterGridColumnLayoutState();
        _coverImageService = coverImageService ?? new CoverImageService();
        _coverPickerService = coverPickerService;
        if (_coverPickerService is not null)
            _coverPickerService.PropertyChanged += CoverPickerService_PropertyChanged;
        _chapterTracks = tracks ?? new ObservableCollection<TrackInfo>();
        ChapterTracksView = new ListCollectionView(_chapterPresentationRows);
        RebuildChapterPresentationRows();
        _normalization = new MetadataNormalizationService();
        InitializeHelpDemoField();
        _candidateSession = new ConceptCandidateSessionService(
            _normalization,
            scalarCandidatePresentation ?? new ScalarCandidatePresentationService(_normalization));
        WorkTypeFieldState = CreateSpecialFieldState(ProjectMetadataField.AudioWorkType);
        AbridgedStatusFieldState = CreateSpecialFieldState(ProjectMetadataField.AbridgedStatus);
        YearFieldState = CreateSpecialFieldState(ProjectMetadataField.Year);
        SpecialDrafts.Attach(YearFieldState, WorkTypeFieldState, AbridgedStatusFieldState);
        InitializeConceptFieldStates();
        WorkTypeFieldState.PropertyChanged += WorkTypeFieldState_PropertyChanged;
        SeriesFieldState.PropertyChanged += SeriesFieldState_PropertyChanged;
        DescriptionFieldState.PropertyChanged += DescriptionFieldState_PropertyChanged;
        NarratorComposerBinding = CreateConceptFieldBinding(
            ProjectMetadataFieldBindingCatalog.NarratorsToComposers,
            NarratorsField,
            ComposersField,
            () =>
            {
                OnPropertyChanged(nameof(UseNarratorsAsComposers));
                OnPropertyChanged(nameof(CompatibilityDescriptionOutputPreview));
            });
        BindStateManagedConceptPlacementViews();
        SynchronizeActiveBindings();
        foreach (var field in _conceptFieldStates)
            field.SetTechnicalFieldNameMode(_metadataPreferences.ShowTechnicalConceptFieldNames);
        SeriesFieldState.SetTechnicalFieldNameMode(_metadataPreferences.ShowTechnicalConceptFieldNames);
        WorkTypeFieldState.SetTechnicalFieldNameMode(_metadataPreferences.ShowTechnicalConceptFieldNames);
        AbridgedStatusFieldState.SetTechnicalFieldNameMode(_metadataPreferences.ShowTechnicalConceptFieldNames);
        YearFieldState.SetTechnicalFieldNameMode(_metadataPreferences.ShowTechnicalConceptFieldNames);
        _isWorkTypeIncluded =
            MetadataPreferencesService.GetPolicy(_metadataPreferences, ProjectMetadataField.AudioWorkType) == OutputFieldPolicy.Enabled;
        _isAbridgedStatusIncluded =
            MetadataPreferencesService.GetPolicy(_metadataPreferences, ProjectMetadataField.AbridgedStatus) == OutputFieldPolicy.Enabled;
        _isReleaseDateIncluded =
            MetadataPreferencesService.GetPolicy(_metadataPreferences, ProjectMetadataField.ReleaseDate) == OutputFieldPolicy.Enabled;
        _isYearIncluded =
            MetadataPreferencesService.GetPolicy(_metadataPreferences, ProjectMetadataField.Year) == OutputFieldPolicy.Enabled;
        _isTrackNumberIncluded =
            MetadataPreferencesService.GetPolicy(_metadataPreferences, ProjectMetadataField.OutputTrackNumber) == OutputFieldPolicy.Enabled;
        _isTrackCountIncluded =
            MetadataPreferencesService.GetPolicy(_metadataPreferences, ProjectMetadataField.OutputTrackTotal) == OutputFieldPolicy.Enabled;
        _isTrackNumberLocked = _metadataPreferences.OutputTrackNumberPreferenceLocked;
        _isTrackCountLocked = _metadataPreferences.OutputTrackTotalPreferenceLocked;
        _canChooseCover = canChooseCover;
        _chooseCover = chooseCover;
        _dropCover = dropCover;
        _removeCover = removeCover;
        // Read the shared settings when composing choices; never rewrite existing metadata.
        DataContext = this;
        RefreshCoverProcessedAssetInfo();
        _metadataState.PropertyChanged += ProjectMetadataState_PropertyChanged;
        Closed += MetadataDialog_Closed;
        if (_genreSettingsManager is not null)
            _genreSettingsManager.GenreOptionsChanged += SharedGenreOptionsChanged;
        WeakEventManager<DeveloperAppearancePreview, EventArgs>.AddHandler(DeveloperToolsCommands.AppearancePreview,
            nameof(DeveloperAppearancePreview.Changed), PreviewAppearanceChanged);
        Closing += MetadataDialog_Closing;
        _chapterAutoFitTimer.Tick += ChapterAutoFitTimer_Tick;
        ConfigureChapterSettingsEditor();
        RefreshChapterSettingsEditorFromSharedState();
        ConfigureCoverWarningThresholdEditor();
        RefreshCoverWarningThresholdFromSharedState();
        AttachChapterColumnWidthObservers();
        RefreshCanonicalSpecialFieldProjections();
        RefreshConceptFieldStates();
        _ = RefreshMetadataCandidatesAsync();
        SelectConceptFamily(
            MetadataPreferencesService.GetInitialConceptGroupKey(_metadataPreferences),
            BuildConceptSearchFamilies());
        ConceptRememberLastGroupCheckBox.IsChecked = _metadataPreferences.RememberLastConceptGroup;
    }

    private ConceptSpecialMetadataFieldState CreateSpecialFieldState(ProjectMetadataField field) =>
        new(
            _metadataState,
            _metadataPreferences,
            field,
            _saveMetadata,
            _metadataPreferencesChanged,
            _candidateSession);

    public event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyList<string> Genres => GenreListService.GetGenres(_appSettings);

    public ObservableCollection<ConceptMetadataFieldViewModel> PublicationAdditionalFields { get; } = [];
    public ObservableCollection<ConceptMetadataFieldViewModel> LanguageFields { get; } = [];
    public ObservableCollection<ConceptMetadataFieldViewModel> CatalogDataFields { get; } = [];
    public ObservableCollection<ConceptMetadataFieldViewModel> SortingFields { get; } = [];
    public ObservableCollection<ConceptMetadataFieldViewModel> DescriptionAdditionalFields { get; } = [];
    public ObservableCollection<ConceptMetadataFieldViewModel> TechnicalFields { get; } = [];
    public ObservableCollection<ConceptMetadataFieldViewModel> PassThroughFields { get; } = [];
    public ConceptMetadataFieldViewModel TitleField { get; private set; } = null!;
    public ConceptMetadataFieldViewModel AlbumField { get; private set; } = null!;
    public ConceptMetadataFieldViewModel SubtitleField { get; private set; } = null!;
    public ConceptMetadataFieldViewModel VolumeField { get; private set; } = null!;
    public ConceptMetadataFieldViewModel EditionField { get; private set; } = null!;
    public ConceptMetadataFieldViewModel PublisherField { get; private set; } = null!;
    public ConceptMetadataFieldViewModel LicenseField { get; private set; } = null!;
    public ConceptMetadataFieldViewModel AuthorsField { get; private set; } = null!;
    public ConceptMetadataFieldViewModel NarratorsField { get; private set; } = null!;
    public ConceptMetadataFieldViewModel ComposersField { get; private set; } = null!;
    public ConceptMetadataFieldViewModel TranslatorsField { get; private set; } = null!;
    public ConceptMetadataFieldViewModel GenresField { get; private set; } = null!;
    public ConceptMetadataFieldViewModel DescriptionField { get; private set; } = null!;
    public ConceptSeriesFieldState SeriesFieldState { get; private set; } = null!;
    public ConceptDescriptionFieldState DescriptionFieldState { get; private set; } = null!;
    public ConceptSpecialMetadataFieldState WorkTypeFieldState { get; private set; } = null!;
    public ConceptSpecialMetadataFieldState AbridgedStatusFieldState { get; private set; } = null!;
    public ConceptSpecialMetadataFieldState YearFieldState { get; private set; } = null!;
    private readonly List<ConceptMetadataFieldViewModel> _conceptFieldStates = [];
    private ConceptMetadataFieldComposition _quickAccessFieldComposition = null!;
    private readonly List<ConceptMetadataFieldBindingState> _conceptFieldBindings = [];

    public ObservableCollection<ConceptSingleValue> WorkTypeValues => WorkTypeFieldState.Values;
    public ObservableCollection<ConceptSingleValue> AbridgedStatusValues => AbridgedStatusFieldState.Values;
    public ObservableCollection<ConceptSeriesEntryValue> SeriesValues => SeriesFieldState.SeriesValues;
    public ObservableCollection<ConceptSingleValue> ReleaseDateValues { get; } = [];

    public ObservableCollection<ConceptMetadataCandidate> WorkTypeCandidates => WorkTypeFieldState.Candidates;
    public ObservableCollection<ConceptMetadataCandidate> AbridgedStatusCandidates => AbridgedStatusFieldState.Candidates;
    public ObservableCollection<SeriesSourceMetadataAlternative> SeriesCandidates => SeriesFieldState.SeriesCandidates;
    public ObservableCollection<ConceptSeriesEntryCandidate> SeriesEntryCandidates => SeriesFieldState.SeriesEntryCandidates;
    public ObservableCollection<ConceptMetadataCandidate> ReleaseDateCandidates { get; } = [];

    public ObservableCollection<ConceptSingleValue> YearValues => YearFieldState.Values;

    public ObservableCollection<ConceptSingleValue> TrackNumberValues { get; } = [];

    public ObservableCollection<ConceptSingleValue> TrackCountValues { get; } = [];

    public ConceptMetadataFieldBindingState NarratorComposerBinding { get; private set; } = null!;
    public ConceptMetadataFieldBindingState TitleAlbumBinding { get; private set; } = null!;

    private readonly ProjectMetadata _helpDemoMetadata = new();
    private readonly MetadataPreferences _helpDemoPreferences = new();
    private bool _wasHelpSelected;
    public ConceptMetadataFieldViewModel HelpDemoField { get; private set; } = null!;

    public bool UseNarratorsAsComposers
    {
        get => NarratorComposerBinding?.IsActive ?? MetadataFieldBindingPreferencesService.GetIsEnabled(_metadataPreferences, ProjectMetadataFieldBindingCatalog.NarratorsToComposers);
        set
        {
            if (NarratorComposerBinding is null)
                return;
            NarratorComposerBinding.IsActive = value;
        }
    }

    private bool _isReleaseDateIncluded = true;
    private bool _isWorkTypeIncluded = true;
    private bool _isAbridgedStatusIncluded = true;
    private bool _isYearIncluded = true;
    private bool _isTrackNumberIncluded = true;
    private bool _isTrackCountIncluded = true;
    private bool _isTrackNumberLocked;
    private bool _isTrackCountLocked;
    private bool _isDescriptionIncluded = true;

    public bool ShowTechnicalFieldNames
    {
        get => _metadataPreferences.ShowTechnicalConceptFieldNames;
        set
        {
            if (!MetadataPreferencesService.SetShowTechnicalConceptFieldNames(_metadataPreferences, value))
                return;

            foreach (var metadataField in _conceptFieldStates)
                metadataField.SetTechnicalFieldNameMode(value);
            SeriesFieldState.SetTechnicalFieldNameMode(value);
            WorkTypeFieldState.SetTechnicalFieldNameMode(value);
            AbridgedStatusFieldState.SetTechnicalFieldNameMode(value);
            YearFieldState.SetTechnicalFieldNameMode(value);

            _metadataPreferencesChanged(_metadataPreferences);
            OnPropertyChanged();
            OnPropertyChanged(nameof(YearLabel));
            OnPropertyChanged(nameof(SeriesLabel));
            OnPropertyChanged(nameof(SeriesPartLabel));
            OnPropertyChanged(nameof(WorkTypeLabel));
            OnPropertyChanged(nameof(AbridgedStatusLabel));
            OnPropertyChanged(nameof(TrackNumberLabel));
            OnPropertyChanged(nameof(TrackCountLabel));
            OnPropertyChanged(nameof(DescriptionLabel));
            OnPropertyChanged(nameof(ReleaseDateLabel));
            OnPropertyChanged(nameof(CompatibilityFieldLabel));
            OnPropertyChanged(nameof(CompatibilityDescriptionOutputPreviewLabel));
            OnPropertyChanged(nameof(ContextAuthorLabel));
            OnPropertyChanged(nameof(ContextNarratorLabel));
        }
    }

    public bool WarnBeforeDiscardingConceptDrafts
    {
        get => _metadataPreferences.WarnBeforeDiscardingConceptDrafts;
        set
        {
            if (_metadataPreferences.WarnBeforeDiscardingConceptDrafts == value)
                return;

            _metadataPreferences.WarnBeforeDiscardingConceptDrafts = value;
            _metadataPreferencesChanged(_metadataPreferences);
            OnPropertyChanged();
        }
    }

    public string YearLabel => YearFieldState.Label;
    public string SeriesLabel => SeriesFieldState.SeriesLabel;
    public string SeriesPartLabel => SeriesFieldState.SeriesPartLabel;
    public string WorkTypeLabel => WorkTypeFieldState.Label;
    public string AbridgedStatusLabel => AbridgedStatusFieldState.Label;
    public string TrackNumberLabel => GetFieldLabel(ProjectMetadataField.OutputTrackNumber);
    public string TrackCountLabel => GetFieldLabel(ProjectMetadataField.OutputTrackTotal);
    public string DescriptionLabel => GetFieldLabel(ProjectMetadataField.Description);
    public string ReleaseDateLabel => GetFieldLabel(ProjectMetadataField.ReleaseDate);

    public bool IsReleaseDateIncluded
    {
        get => _isReleaseDateIncluded;
        set => SetPersistentOutputState(
            ref _isReleaseDateIncluded,
            value,
            ProjectMetadataField.ReleaseDate,
            nameof(IsReleaseDateIncluded),
            ConceptPublicationReleaseDateComboBox);
    }

    public bool IsWorkTypeIncluded
    {
        get => WorkTypeFieldState.IsOutputEnabled;
        set
        {
            WorkTypeFieldState.IsOutputEnabled = value;
            _isWorkTypeIncluded = value;
            OnPropertyChanged();
        }
    }

    public bool IsAbridgedStatusIncluded
    {
        get => AbridgedStatusFieldState.IsOutputEnabled;
        set
        {
            AbridgedStatusFieldState.IsOutputEnabled = value;
            _isAbridgedStatusIncluded = value;
            OnPropertyChanged();
        }
    }

    public bool IsSeriesIncluded
    {
        get => SeriesFieldState.IsSeriesIncluded;
        set
        {
            SeriesFieldState.IsSeriesIncluded = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsSeriesEditorVisible));
        }
    }

    public bool IsSeriesEditorVisible => SeriesFieldState.IsSeriesEditorVisible;

    public string SeriesNameDraft
    {
        get => SeriesFieldState.SeriesNameDraft;
        set
        {
            SeriesFieldState.SeriesNameDraft = value;
            OnPropertyChanged();
        }
    }

    public string SeriesPartDraft
    {
        get => SeriesFieldState.SeriesPartDraft;
        set
        {
            SeriesFieldState.SeriesPartDraft = value;
            OnPropertyChanged();
        }
    }


    public bool IsYearIncluded
    {
        get => YearFieldState.IsOutputEnabled;
        set
        {
            YearFieldState.IsOutputEnabled = value;
            _isYearIncluded = value;
            OnPropertyChanged();
        }
    }

    public bool IsTrackNumberIncluded
    {
        get => _isTrackNumberIncluded;
        set
        {
            if (!SetPersistentOutputState(
                    ref _isTrackNumberIncluded,
                    value,
                    ProjectMetadataField.OutputTrackNumber,
                    nameof(IsTrackNumberIncluded),
                    ConceptWorkTrackNumberTextBox))
            {
                return;
            }

            OnPropertyChanged(nameof(IsTrackNumberEditorVisible));
            OnPropertyChanged(nameof(CanEditTrackNumber));
        }
    }

    public bool IsTrackCountIncluded
    {
        get => _isTrackCountIncluded;
        set
        {
            if (!SetPersistentOutputState(
                    ref _isTrackCountIncluded,
                    value,
                    ProjectMetadataField.OutputTrackTotal,
                    nameof(IsTrackCountIncluded),
                    ConceptWorkTrackCountTextBox))
            {
                return;
            }

            OnPropertyChanged(nameof(IsTrackCountEditorVisible));
            OnPropertyChanged(nameof(CanEditTrackCount));
        }
    }

    public bool IsTrackNumberLocked
    {
        get => _isTrackNumberLocked;
        set
        {
            if (_isTrackNumberLocked == value)
                return;

            if (value)
            {
                var current = ReadCanonicalInteger(ProjectMetadataField.OutputTrackNumber, TrackNumberValues);
                if (!current.HasValue)
                    return;

                _isTrackNumberLocked = true;
                _metadataPreferences.PreferredOutputTrackNumber = current;
                _metadataPreferences.OutputTrackNumberPreferenceLocked = true;
                CommitCanonicalInteger(ProjectMetadataField.OutputTrackNumber, current, MetadataOrigin.GlobalPreference);
            }
            else
            {
                _isTrackNumberLocked = false;
                _metadataPreferences.OutputTrackNumberPreferenceLocked = false;
            }

            _metadataPreferencesChanged(_metadataPreferences);
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsTrackNumberEditorVisible));
            OnPropertyChanged(nameof(CanEditTrackNumber));
            OnPropertyChanged(nameof(CanRemoveTrackNumber));
        }
    }

    public bool IsTrackCountLocked
    {
        get => _isTrackCountLocked;
        set
        {
            if (_isTrackCountLocked == value)
                return;

            if (value)
            {
                var current = ReadCanonicalInteger(ProjectMetadataField.OutputTrackTotal, TrackCountValues);
                if (!current.HasValue)
                    return;

                _isTrackCountLocked = true;
                _metadataPreferences.PreferredOutputTrackTotal = current;
                _metadataPreferences.OutputTrackTotalPreferenceLocked = true;
                CommitCanonicalInteger(ProjectMetadataField.OutputTrackTotal, current, MetadataOrigin.GlobalPreference);
            }
            else
            {
                _isTrackCountLocked = false;
                _metadataPreferences.OutputTrackTotalPreferenceLocked = false;
            }

            _metadataPreferencesChanged(_metadataPreferences);
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsTrackCountEditorVisible));
            OnPropertyChanged(nameof(CanEditTrackCount));
            OnPropertyChanged(nameof(CanRemoveTrackCount));
        }
    }

    public bool IsTrackNumberEditorVisible => IsTrackNumberIncluded && !IsTrackNumberLocked;

    public bool CanEditTrackNumber => IsTrackNumberEditorVisible;

    public bool CanRemoveTrackNumber => !IsTrackNumberLocked;

    public bool IsTrackCountEditorVisible => IsTrackCountIncluded && !IsTrackCountLocked;

    public bool CanEditTrackCount => IsTrackCountEditorVisible;

    public bool CanRemoveTrackCount => !IsTrackCountLocked;


    public bool IsDescriptionIncluded
    {
        get => DescriptionFieldState?.IsDescriptionIncluded ?? _isDescriptionIncluded;
        set
        {
            _isDescriptionIncluded = value;
            if (DescriptionFieldState is not null)
                DescriptionFieldState.IsDescriptionIncluded = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CompatibilityDescriptionOutputPreview));
        }
    }

    public string DescriptionText
    {
        get => DescriptionFieldState?.DescriptionText ?? string.Empty;
        set
        {
            if (DescriptionFieldState is not null)
                DescriptionFieldState.DescriptionText = value ?? string.Empty;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CompatibilityDescriptionOutputPreview));
        }
    }





    public string ReleaseDateOriginLabel =>
        FormatOriginLabel(_metadataState.CanonicalMetadata.GetValue<MetadataDate?>(ProjectMetadataField.ReleaseDate).Origin);



    public string WorkTypeOriginLabel => WorkTypeFieldState.OriginLabel;

    public string AbridgedStatusOriginLabel => AbridgedStatusFieldState.OriginLabel;

    public string SeriesOriginLabel => SeriesFieldState.SeriesOriginLabel;



    public string YearOriginLabel => YearFieldState.OriginLabel;


    public string DescriptionOriginLabel => DescriptionFieldState?.DescriptionOriginLabel ?? string.Empty;
    public string CompatibilityFieldLabel =>
        ShowTechnicalFieldNames ? CompatibilityFieldTechnicalName : LocalizationText.Get("Metadata.Search.CompatibilityField");
    public string CompatibilityDescriptionOutputPreviewLabel =>
        ShowTechnicalFieldNames ? DescriptionOutputPreviewTechnicalName : LocalizationText.Get("Metadata.Search.DescriptionOutputPreview");
    public string CompatibilityFieldText => CompatibilityDescriptionRenderer.Render(_compatibilityPreviewProvider());
    public string CompatibilityDescriptionOutputPreview =>
        IsDescriptionIncluded
            ? CompatibilityDescriptionRenderer.AppendToDescription(DescriptionText, CompatibilityFieldText)
            : ResolveHelpText("MetadataDescriptionOutputDisabledPreview");
    public Visibility CompatibilityDescriptionOutputPreviewVisibility =>
        IsCompatibilityDescriptionEnabled ? Visibility.Visible : Visibility.Collapsed;

    public bool IsCompatibilityDescriptionEnabled
    {
        get => _compatibilityDescriptionEnabledProvider();
        set
        {
            if (value == _compatibilityDescriptionEnabledProvider())
                return;

            _compatibilityDescriptionEnabledChanged(value);
            OnPropertyChanged();
            OnPropertyChanged(nameof(CompatibilityDescriptionOutputPreview));
            OnPropertyChanged(nameof(CompatibilityDescriptionOutputPreviewVisibility));
        }
    }

    public string CoverPreviewSource =>
        CoverPreviewPresentationService.NormalizeSource(
            _metadataState.CoverPreviewSource,
            AppearanceSettingsService.GetEffectiveMode(_appSettings),
            AppearanceSettingsService.GetCoverPlaceholderOverride());

    public string ContextBookTitle => _metadataState.BookTitle;
    public string ContextAuthor => _metadataState.Author;
    public string ContextNarrator => _metadataState.Narrator;
    public string ContextAuthorLabel => GetFieldLabel(ProjectMetadataField.Authors);
    public string ContextNarratorLabel => GetFieldLabel(ProjectMetadataField.Narrators);
    public string CoverDetailsFileName => _coverProcessedAssetInfo?.FileName ?? string.Empty;
    public string CoverDetailsFormat => _coverProcessedAssetInfo?.Format ?? string.Empty;
    public string CoverDetailsDimensions => _coverProcessedAssetInfo is null ? string.Empty : $"{_coverProcessedAssetInfo.Width} × {_coverProcessedAssetInfo.Height} px";
    public string CoverDetailsFileSize => _coverProcessedAssetInfo is null ? string.Empty : FormatFileSize(_coverProcessedAssetInfo.FileSizeBytes);
    public bool RememberLastCoverPickerFolder
    {
        get => _coverPickerService?.RememberLastCoverPickerFolder == true;
        set
        {
            if (_coverPickerService is null || _coverPickerService.RememberLastCoverPickerFolder == value)
                return;

            _coverPickerService.RememberLastCoverPickerFolder = value;
            OnPropertyChanged();
        }
    }
    public Visibility CoverDetailsVisibility => _coverProcessedAssetInfo is null ? Visibility.Collapsed : Visibility.Visible;

    private void RefreshCoverProcessedAssetInfo()
    {
        _coverProcessedAssetInfo = _coverImageService.TryGetProcessedCoverInfo(_metadataState.ProcessedCoverPath);
        OnPropertyChanged(nameof(CoverDetailsFileName));
        OnPropertyChanged(nameof(CoverDetailsFormat));
        OnPropertyChanged(nameof(CoverDetailsDimensions));
        OnPropertyChanged(nameof(CoverDetailsFileSize));
        OnPropertyChanged(nameof(CoverDetailsVisibility));

        if (ConceptMetadataContextCover is not null &&
            ConceptFamilyList is not null &&
            ConceptCoverFamilyItem is not null &&
            !HasConceptSearchText &&
            ReferenceEquals(ConceptFamilyList.SelectedItem, ConceptCoverFamilyItem))
        {
            ConceptMetadataContextCover.Visibility = Visibility.Visible;
        }
    }

    private void ConfigureCoverWarningThresholdEditor()
    {
        ConceptCoverWarningThresholdComboBox.Items.Clear();
        foreach (var option in CoverWarningSettingsPresentation.Options)
        {
            ConceptCoverWarningThresholdComboBox.Items.Add(new ComboBoxItem
            {
                Tag = option.ThresholdPixels,
                Content = TryFindResource(option.ResourceKey)?.ToString() ?? option.ResourceKey
            });
        }
    }

    public void RefreshCoverWarningThresholdFromSharedState()
    {
        _isRefreshingCoverWarningThreshold = true;
        try
        {
            var threshold = CoverWarningSettingsService.NormalizeThresholdPixels(_appSettings.CoverWarningThresholdPixels);
            ConceptCoverWarningThresholdComboBox.SelectedItem = ConceptCoverWarningThresholdComboBox.Items
                .OfType<ComboBoxItem>()
                .FirstOrDefault(item => Convert.ToInt32(item.Tag, CultureInfo.InvariantCulture) == threshold);
        }
        finally { _isRefreshingCoverWarningThreshold = false; }
    }

    private void ConceptCoverWarningThresholdComboBox_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_isRefreshingCoverWarningThreshold || sender is not ComboBox { SelectedItem: ComboBoxItem item })
            return;
        _appSettings.CoverWarningThresholdPixels = CoverWarningSettingsService.NormalizeThresholdPixels(
            Convert.ToInt32(item.Tag, CultureInfo.InvariantCulture));
        _appSettingsChanged();
    }

    private void CoverPickerService_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == nameof(CoverPickerService.RememberLastCoverPickerFolder))
            OnPropertyChanged(nameof(RememberLastCoverPickerFolder));
    }

    private static string FormatFileSize(long bytes)
    {
        if (bytes < 1024)
            return $"{bytes} B";
        if (bytes < 1024 * 1024)
            return $"{Math.Round(bytes / 1024d):N0} KB";
        return $"{bytes / (1024d * 1024d):N1} MB";
    }

    public Visibility CoverHintVisibility =>
        CoverPreviewPresentationService.IsHintVisible(
            _metadataState.ProcessedCoverPath,
            _canChooseCover)
            ? Visibility.Visible
            : Visibility.Collapsed;

    public bool CanRemoveCover => CoverPreviewPresentationService.CanRemove(
        _metadataState.ProcessedCoverPath,
        _canChooseCover);

    private void WorkTypeFieldState_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ConceptSpecialMetadataFieldState.IsOutputEnabled))
            OnPropertyChanged(nameof(IsWorkTypeIncluded));
        if (e.PropertyName == nameof(ConceptSpecialMetadataFieldState.OriginLabel))
            OnPropertyChanged(nameof(WorkTypeOriginLabel));
    }

    private void SeriesFieldState_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ConceptSeriesFieldState.SeriesLabel) or null)
            OnPropertyChanged(nameof(SeriesLabel));
        if (e.PropertyName is nameof(ConceptSeriesFieldState.SeriesPartLabel) or null)
            OnPropertyChanged(nameof(SeriesPartLabel));
        if (e.PropertyName is nameof(ConceptSeriesFieldState.SeriesOriginLabel) or null)
            OnPropertyChanged(nameof(SeriesOriginLabel));
        if (e.PropertyName is nameof(ConceptSeriesFieldState.IsSeriesIncluded) or null)
            OnPropertyChanged(nameof(IsSeriesIncluded));
        if (e.PropertyName is nameof(ConceptSeriesFieldState.IsSeriesEditorVisible) or null)
            OnPropertyChanged(nameof(IsSeriesEditorVisible));
        if (e.PropertyName is nameof(ConceptSeriesFieldState.SeriesNameDraft) or null)
            OnPropertyChanged(nameof(SeriesNameDraft));
        if (e.PropertyName is nameof(ConceptSeriesFieldState.SeriesPartDraft) or null)
            OnPropertyChanged(nameof(SeriesPartDraft));
    }

    private void DescriptionFieldState_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ConceptDescriptionFieldState.DescriptionLabel) or null)
            OnPropertyChanged(nameof(DescriptionLabel));
        if (e.PropertyName is nameof(ConceptDescriptionFieldState.DescriptionOriginLabel) or null)
            OnPropertyChanged(nameof(DescriptionOriginLabel));
        if (e.PropertyName is nameof(ConceptDescriptionFieldState.IsDescriptionIncluded) or null)
        {
            _isDescriptionIncluded = DescriptionFieldState.IsDescriptionIncluded;
            OnPropertyChanged(nameof(IsDescriptionIncluded));
            OnPropertyChanged(nameof(CompatibilityDescriptionOutputPreview));
        }
        if (e.PropertyName is nameof(ConceptDescriptionFieldState.DescriptionText) or null)
        {
            OnPropertyChanged(nameof(DescriptionText));
            OnPropertyChanged(nameof(CompatibilityDescriptionOutputPreview));
        }
    }

    private void ProjectMetadataState_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(CompatibilityFieldText));
        OnPropertyChanged(nameof(CompatibilityDescriptionOutputPreview));
        if (e.PropertyName is nameof(ProjectMetadataState.CanonicalMetadata) or
            nameof(ProjectMetadataState.BookTitle) or
            nameof(ProjectMetadataState.Author) or
            nameof(ProjectMetadataState.Narrator))
        {
            OnPropertyChanged(nameof(ContextBookTitle));
            OnPropertyChanged(nameof(ContextAuthor));
            OnPropertyChanged(nameof(ContextNarrator));
        }

        if (e.PropertyName == nameof(ProjectMetadataState.CanonicalMetadata))
        {
            DiscardConceptDrafts();
            RefreshConceptFieldStates();
            QueueMetadataCandidateRefresh();
            return;
        }

        var stateManagedField = e.PropertyName switch
        {
            nameof(ProjectMetadataState.BookTitle) => ProjectMetadataField.Title,
            nameof(ProjectMetadataState.Album) => ProjectMetadataField.Album,
            nameof(ProjectMetadataState.Subtitle) => ProjectMetadataField.Subtitle,
            nameof(ProjectMetadataState.Volume) => ProjectMetadataField.Volume,
            nameof(ProjectMetadataState.Edition) => ProjectMetadataField.Edition,
            nameof(ProjectMetadataState.Author) => ProjectMetadataField.Authors,
            nameof(ProjectMetadataState.Narrator) => ProjectMetadataField.Narrators,
            nameof(ProjectMetadataState.Translators) => ProjectMetadataField.Translators,
            nameof(ProjectMetadataState.Publisher) => ProjectMetadataField.Publisher,
            nameof(ProjectMetadataState.License) => ProjectMetadataField.License,
            nameof(ProjectMetadataState.Genre) => ProjectMetadataField.Genres,
            _ => (ProjectMetadataField?)null
        };
        if (stateManagedField.HasValue)
        {
            RefreshStateManagedField(stateManagedField.Value);
            return;
        }

        if (e.PropertyName == nameof(ProjectMetadataState.WorkType) ||
            e.PropertyName == nameof(ProjectMetadataState.AbridgedStatus))
        {
            RefreshCanonicalWorkFields();
            QueueMetadataCandidateRefresh();
        }

        if (e.PropertyName == nameof(ProjectMetadataState.Series))
        {
            RefreshCanonicalSeries();
            OnPropertyChanged(nameof(SeriesOriginLabel));
            QueueMetadataCandidateRefresh();
        }

        if (e.PropertyName is nameof(ProjectMetadataState.ReleaseDate) or
            nameof(ProjectMetadataState.Year))
        {
            RefreshCanonicalPublicationFields();
            QueueMetadataCandidateRefresh();
        }

        if (e.PropertyName is nameof(ProjectMetadataState.OutputTrackNumber) or
            nameof(ProjectMetadataState.OutputTrackTotal))
        {
            RefreshCanonicalTrackFields();
        }

        if (e.PropertyName == nameof(ProjectMetadataState.CoverPreviewSource))
            OnPropertyChanged(nameof(CoverPreviewSource));

        if (e.PropertyName == nameof(ProjectMetadataState.ProcessedCoverPath))
        {
            OnPropertyChanged(nameof(CoverHintVisibility));
            OnPropertyChanged(nameof(CanRemoveCover));
            RefreshCoverProcessedAssetInfo();
        }
    }

    private void RefreshStateManagedField(ProjectMetadataField field)
    {
        var state = _conceptFieldStates.FirstOrDefault(item => item.Field == field);
        if (state is null)
            return;

        if (!state.IsList)
            _candidateSession.RetainCurrentScalarWinners(field, state.Candidates);

        state.Refresh();
        QueueMetadataCandidateRefresh();
    }


    private void PreviewAppearanceChanged(object? sender, EventArgs e)
    {
        if (Dispatcher.CheckAccess()) OnPropertyChanged(nameof(CoverPreviewSource));
    }

    private void MetadataDialog_Closed(object? sender, EventArgs e)
    {
        if (_genreSettingsManager is not null)
            _genreSettingsManager.GenreOptionsChanged -= SharedGenreOptionsChanged;
        WeakEventManager<DeveloperAppearancePreview, EventArgs>.RemoveHandler(DeveloperToolsCommands.AppearancePreview,
            nameof(DeveloperAppearancePreview.Changed), PreviewAppearanceChanged);
        WorkTypeFieldState.PropertyChanged -= WorkTypeFieldState_PropertyChanged;
        SeriesFieldState.PropertyChanged -= SeriesFieldState_PropertyChanged;
        DescriptionFieldState.PropertyChanged -= DescriptionFieldState_PropertyChanged;
        _chapterAutoFitTimer.Stop();
        _chapterAutoFitTimer.Tick -= ChapterAutoFitTimer_Tick;
        DetachChapterGridScrollViewer();
        DetachChapterColumnWidthObservers();
        _metadataState.PropertyChanged -= ProjectMetadataState_PropertyChanged;
        _candidateLoadCancellation.Cancel();
        DialogOwnerFocusService.Default.RestoreOwnerAfterModalDialog(Owner);
    }

    private sealed record MetadataDraftDescriptor(
        string Key,
        Func<bool> HasDraft,
        Func<string> Label,
        string FamilyKey,
        Action Discard);

    private void MetadataDialog_Closing(object? sender, CancelEventArgs e)
    {
        var activeDrafts = GetActiveMetadataDrafts();
        if (activeDrafts.Length == 0)
            return;

        if (_metadataPreferences.WarnBeforeDiscardingConceptDrafts)
        {
            var result = AppDialogService.Show(
                this,
                "BookStitch",
                LocalizationText.Get("Metadata.Dialog.UnconfirmedInputTitle"),
                ConceptDraftWarningPresentation.BuildMessage(activeDrafts.Select(draft => draft.Label())),
                AppDialogKind.Warning,
                buttons:
                [
                    new AppDialogButton(
                        LocalizationText.Get("Metadata.Dialog.Back"),
                        AppDialogResult.Cancel,
                        IsDefault: true,
                        IsCancel: true),
                    new AppDialogButton(
                        LocalizationText.Get("Metadata.Dialog.CloseAnyway"),
                        AppDialogResult.Yes,
                        IsDanger: true)
                ]);
            if (result != AppDialogResult.Yes)
            {
                e.Cancel = true;
                NavigateBackToDraftGroup();
                return;
            }
        }

        foreach (var draft in activeDrafts)
            draft.Discard();

        OnPropertyChanged(nameof(DescriptionText));
        OnPropertyChanged(nameof(CompatibilityDescriptionOutputPreview));
    }

    private MetadataDraftDescriptor[] GetActiveMetadataDrafts() =>
        BuildMetadataDraftDescriptors()
            .Where(draft => draft.HasDraft())
            .GroupBy(draft => draft.Key, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToArray();

    private IReadOnlyList<MetadataDraftDescriptor> BuildMetadataDraftDescriptors()
    {
        var drafts = new List<MetadataDraftDescriptor>(_conceptFieldStates.Count + 7);
        foreach (var field in _conceptFieldStates)
        {
            drafts.Add(new MetadataDraftDescriptor(
                field.Field.ToString(),
                () => field.HasDraft,
                () => field.Label,
                GetConceptFamilyKeyForField(field),
                field.DiscardDraft));
        }

        drafts.Add(CreateSpecialDraftDescriptor(
            "ReleaseDate",
            () => HasEditableComboBoxDraft(ConceptPublicationReleaseDateComboBox),
            () => ReleaseDateLabel,
            ConceptMetadataGroupIds.Publication,
            () => ClearEditableComboBoxDraft(ConceptPublicationReleaseDateComboBox)));
        drafts.Add(CreateSpecialDraftDescriptor(
            "AudioWorkType",
            () => SpecialDrafts.WorkType.HasValue,
            () => WorkTypeLabel,
            ConceptMetadataGroupIds.Work,
            () => SpecialDrafts.WorkType = null));
        drafts.Add(CreateSpecialDraftDescriptor(
            "AbridgedStatus",
            () => SpecialDrafts.AbridgedStatus.HasValue,
            () => AbridgedStatusLabel,
            ConceptMetadataGroupIds.Work,
            () => SpecialDrafts.AbridgedStatus = null));
        drafts.Add(CreateSpecialDraftDescriptor(
            "SeriesName",
            () => !string.IsNullOrWhiteSpace(SeriesNameDraft),
            () => SeriesLabel,
            ConceptMetadataGroupIds.Work,
            SeriesFieldState.DiscardDraft));
        drafts.Add(CreateSpecialDraftDescriptor(
            "SeriesPart",
            () => !string.IsNullOrWhiteSpace(SeriesPartDraft),
            () => SeriesPartLabel,
            ConceptMetadataGroupIds.Work,
            SeriesFieldState.DiscardDraft));
        drafts.Add(CreateSpecialDraftDescriptor(
            "Year",
            () => !string.IsNullOrWhiteSpace(SpecialDrafts.Year),
            () => YearLabel,
            ConceptMetadataGroupIds.Publication,
            () => SpecialDrafts.Year = string.Empty));
        drafts.Add(CreateSpecialDraftDescriptor(
            "OutputTrackNumber",
            () => !string.IsNullOrWhiteSpace(SpecialDrafts.TrackNumber),
            () => TrackNumberLabel,
            ConceptMetadataGroupIds.Sorting,
            () => SpecialDrafts.TrackNumber = string.Empty));
        drafts.Add(CreateSpecialDraftDescriptor(
            "OutputTrackTotal",
            () => !string.IsNullOrWhiteSpace(SpecialDrafts.TrackCount),
            () => TrackCountLabel,
            ConceptMetadataGroupIds.Sorting,
            () => SpecialDrafts.TrackCount = string.Empty));
        return drafts;
    }

    private string GetConceptFamilyKeyForField(ConceptMetadataFieldViewModel field) =>
        field switch
        {
            _ when ReferenceEquals(field, SubtitleField) || ReferenceEquals(field, VolumeField) => ConceptMetadataGroupIds.Work,
            _ when ReferenceEquals(field, PublisherField) || ReferenceEquals(field, LicenseField) || ReferenceEquals(field, EditionField) => ConceptMetadataGroupIds.Publication,
            _ when ReferenceEquals(field, AuthorsField) || ReferenceEquals(field, NarratorsField) || ReferenceEquals(field, ComposersField) || ReferenceEquals(field, TranslatorsField) => ConceptMetadataGroupIds.People,
            _ when ReferenceEquals(field, GenresField) || CatalogDataFields.Contains(field) => ConceptMetadataGroupIds.CatalogData,
            _ when LanguageFields.Contains(field) => ConceptMetadataGroupIds.Language,
            _ when SortingFields.Contains(field) => ConceptMetadataGroupIds.Sorting,
            _ when PublicationAdditionalFields.Contains(field) => ConceptMetadataGroupIds.Publication,
            _ when DescriptionAdditionalFields.Contains(field) || ReferenceEquals(field, DescriptionField) => ConceptMetadataGroupIds.Description,
            _ when TechnicalFields.Contains(field) => ConceptMetadataGroupIds.TechnicalData,
            _ when PassThroughFields.Contains(field) => ConceptMetadataGroupIds.PassThrough,
            _ => ConceptMetadataGroupIds.QuickAccess
        };

    private MetadataDraftDescriptor CreateSpecialDraftDescriptor(
        string key,
        Func<bool> hasDraft,
        Func<string> label,
        string familyKey,
        Action discard) =>
        new(key, hasDraft, label, familyKey, discard);

    private static bool HasEditableComboBoxDraft(ComboBox comboBox) =>
        !string.IsNullOrWhiteSpace(comboBox.Text) || comboBox.SelectedItem is not null;

    private static void ClearEditableComboBoxDraft(ComboBox comboBox)
    {
        comboBox.ApplyTemplate();
        ClearEditableComboBox(comboBox, comboBox.Template.FindName("PART_EditableTextBox", comboBox) as TextBox);
    }

    private void NavigateBackToDraftGroup()
    {
        var activeDrafts = GetActiveMetadataDrafts();
        if (activeDrafts.Length == 0)
            return;

        var searchIsActive = HasConceptSearchText;
        var currentFamilyKey = searchIsActive ? null : GetSelectedConceptFamilyKey();
        var targetFamilyKey = ResolveDraftReturnFamily(
            currentFamilyKey,
            activeDrafts.Select(draft => draft.FamilyKey));
        if (targetFamilyKey is null)
            return;

        if (searchIsActive && !string.IsNullOrEmpty(ConceptSearchTextBox.Text))
            ConceptSearchTextBox.Clear();

        SelectConceptFamily(targetFamilyKey, BuildConceptSearchFamilies());
    }

    internal static string? ResolveDraftReturnFamily(
        string? currentFamilyKey,
        IEnumerable<string> activeDraftFamilyKeys)
    {
        var familyKeys = activeDraftFamilyKeys
            .Where(familyKey => !string.IsNullOrWhiteSpace(familyKey))
            .ToArray();

        if (!string.IsNullOrWhiteSpace(currentFamilyKey) &&
            familyKeys.Contains(currentFamilyKey, StringComparer.Ordinal))
        {
            return currentFamilyKey;
        }

        return familyKeys.FirstOrDefault();
    }

    private bool HasConceptDrafts() => BuildMetadataDraftDescriptors().Any(draft => draft.HasDraft());

    private void DiscardConceptDrafts()
    {
        foreach (var draft in BuildMetadataDraftDescriptors().Where(draft => draft.HasDraft()))
            draft.Discard();

        OnPropertyChanged(nameof(DescriptionText));
        OnPropertyChanged(nameof(CompatibilityDescriptionOutputPreview));
    }

    private void ConceptFamilyList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ConceptQuickAccessPanel is null ||
            ConceptWorkOutputPanel is null ||
            ConceptPeoplePanel is null ||
            ConceptPublicationPanel is null ||
            ConceptLanguagePanel is null ||
            ConceptCatalogDataPanel is null ||
            ConceptSortingPanel is null ||
            ConceptDescriptionPanel is null ||
            ConceptCompatibilityPanel is null ||
            ConceptTechnicalDataPanel is null ||
            ConceptPassThroughPanel is null ||
            ConceptCoverPanel is null ||
            ConceptHelpPanel is null ||
            ConceptUtilityFamilyList is null)
        {
            return;
        }

        if (ReferenceEquals(sender, ConceptFamilyList) && ConceptFamilyList.SelectedIndex >= 0)
            ConceptUtilityFamilyList.SelectedIndex = -1;
        else if (ReferenceEquals(sender, ConceptUtilityFamilyList) && ConceptUtilityFamilyList.SelectedIndex >= 0)
            ConceptFamilyList.SelectedIndex = -1;

        if (HasConceptSearchText && !_isApplyingConceptSearch)
        {
            _activeConceptSearchSurface = BuildConceptSearchFamilies()
                .FirstOrDefault(family => ReferenceEquals(family.FamilyList.SelectedItem, family.NavigationItem))
                ?.SearchSurface;
        }

        UpdateConceptFamilyPanelVisibility();
        PersistSelectedConceptGroupIfNeeded();
    }

    private void ConceptRememberLastGroupCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (MetadataPreferencesService.SetRememberLastConceptGroup(
                _metadataPreferences,
                ConceptRememberLastGroupCheckBox.IsChecked == true,
                GetSelectedConceptFamilyKey()))
        {
            _metadataPreferencesChanged(_metadataPreferences);
        }
    }

    private void PersistSelectedConceptGroupIfNeeded()
    {
        if (HasConceptSearchText)
            return;

        if (MetadataPreferencesService.RememberConceptGroupSelection(
                _metadataPreferences,
                GetSelectedConceptFamilyKey()))
        {
            _metadataPreferencesChanged(_metadataPreferences);
        }
    }

    private void UpdateConceptFamilyPanelVisibility()
    {
        ConceptMetadataContextColumn.Visibility = Visibility.Collapsed;
        ConceptMetadataContextAudiobook.Visibility = Visibility.Visible;
        ConceptMetadataContextCover.Visibility = Visibility.Collapsed;
        var isSearchActive = HasConceptSearchText;
        var selectedSearchSurface = isSearchActive ? _activeConceptSearchSurface : null;
        ConceptSearchResultsPanel.Visibility = isSearchActive && selectedSearchSurface is null
            ? Visibility.Visible
            : Visibility.Collapsed;

        if (isSearchActive)
        {
            ConceptQuickAccessPanel.Visibility = Visibility.Collapsed;
            ConceptWorkOutputPanel.Visibility = Visibility.Collapsed;
            ConceptPeoplePanel.Visibility = Visibility.Collapsed;
            ConceptPublicationPanel.Visibility = Visibility.Collapsed;
            ConceptLanguagePanel.Visibility = Visibility.Collapsed;
            ConceptCatalogDataPanel.Visibility = Visibility.Collapsed;
            ConceptSortingPanel.Visibility = Visibility.Collapsed;
            ConceptDescriptionPanel.Visibility = Visibility.Collapsed;
            ConceptCompatibilityPanel.Visibility = Visibility.Collapsed;
            ConceptTechnicalDataPanel.Visibility = Visibility.Collapsed;
            ConceptPassThroughPanel.Visibility = Visibility.Collapsed;
            ConceptChapterPanel.Visibility = Visibility.Collapsed;
            ConceptCoverPanel.Visibility = Visibility.Collapsed;
            ConceptHelpPanel.Visibility = Visibility.Collapsed;
            if (_wasHelpSelected)
            {
                ResetHelpDemo();
                _wasHelpSelected = false;
            }

            if (selectedSearchSurface is not null)
                selectedSearchSurface.Visibility = Visibility.Visible;

            var isInlineSearchSurface = ReferenceEquals(selectedSearchSurface, ConceptCompatibilityPanel) ||
                                        ReferenceEquals(selectedSearchSurface, ConceptHelpPanel);
            ConceptScrollViewer.Visibility = selectedSearchSurface is null || isInlineSearchSurface
                ? Visibility.Visible
                : Visibility.Collapsed;
            ConceptMetadataContextColumn.Visibility = selectedSearchSurface is null || ReferenceEquals(selectedSearchSurface, ConceptCompatibilityPanel)
                ? Visibility.Visible
                : Visibility.Collapsed;
            return;
        }

        ConceptQuickAccessPanel.Visibility = ReferenceEquals(ConceptFamilyList.SelectedItem, ConceptQuickAccessFamilyItem)
            ? Visibility.Visible
            : Visibility.Collapsed;
        ConceptWorkOutputPanel.Visibility = ReferenceEquals(ConceptFamilyList.SelectedItem, ConceptWorkOutputFamilyItem)
            ? Visibility.Visible
            : Visibility.Collapsed;
        ConceptPeoplePanel.Visibility = ReferenceEquals(ConceptFamilyList.SelectedItem, ConceptPeopleFamilyItem)
            ? Visibility.Visible
            : Visibility.Collapsed;
        ConceptPublicationPanel.Visibility = ReferenceEquals(ConceptFamilyList.SelectedItem, ConceptPublicationFamilyItem)
            ? Visibility.Visible
            : Visibility.Collapsed;
        ConceptLanguagePanel.Visibility = ReferenceEquals(ConceptFamilyList.SelectedItem, ConceptLanguageFamilyItem)
            ? Visibility.Visible
            : Visibility.Collapsed;
        ConceptCatalogDataPanel.Visibility = ReferenceEquals(ConceptFamilyList.SelectedItem, ConceptCatalogDataFamilyItem)
            ? Visibility.Visible
            : Visibility.Collapsed;
        ConceptSortingPanel.Visibility = ReferenceEquals(ConceptFamilyList.SelectedItem, ConceptSortingFamilyItem)
            ? Visibility.Visible
            : Visibility.Collapsed;
        ConceptDescriptionPanel.Visibility = ReferenceEquals(ConceptFamilyList.SelectedItem, ConceptDescriptionFamilyItem)
            ? Visibility.Visible
            : Visibility.Collapsed;
        ConceptCompatibilityPanel.Visibility = ReferenceEquals(ConceptFamilyList.SelectedItem, ConceptCompatibilityFamilyItem)
            ? Visibility.Visible
            : Visibility.Collapsed;
        ConceptTechnicalDataPanel.Visibility = ReferenceEquals(ConceptFamilyList.SelectedItem, ConceptTechnicalDataFamilyItem)
            ? Visibility.Visible
            : Visibility.Collapsed;
        ConceptPassThroughPanel.Visibility = ReferenceEquals(ConceptUtilityFamilyList.SelectedItem, ConceptPassThroughFamilyItem)
            ? Visibility.Visible
            : Visibility.Collapsed;
        var isChapterSelected = ReferenceEquals(ConceptUtilityFamilyList.SelectedItem, ConceptChapterFamilyItem);
        ConceptChapterPanel.Visibility = isChapterSelected ? Visibility.Visible : Visibility.Collapsed;
        ConceptScrollViewer.Visibility = isChapterSelected ? Visibility.Collapsed : Visibility.Visible;
        ConceptCoverPanel.Visibility = ReferenceEquals(ConceptFamilyList.SelectedItem, ConceptCoverFamilyItem)
            ? Visibility.Visible
            : Visibility.Collapsed;
        var isHelpSelected = ReferenceEquals(ConceptUtilityFamilyList.SelectedItem, ConceptHelpFamilyItem);
        ConceptHelpPanel.Visibility = isHelpSelected ? Visibility.Visible : Visibility.Collapsed;
        if (_wasHelpSelected && !isHelpSelected)
            ResetHelpDemo();
        _wasHelpSelected = isHelpSelected;

        var isCoverSelected = ReferenceEquals(ConceptFamilyList.SelectedItem, ConceptCoverFamilyItem);
        ConceptMetadataContextColumn.Visibility = isChapterSelected || isHelpSelected
            ? Visibility.Collapsed
            : Visibility.Visible;
        ConceptMetadataContextAudiobook.Visibility = isCoverSelected ? Visibility.Collapsed : Visibility.Visible;
        ConceptMetadataContextCover.Visibility = isCoverSelected ? Visibility.Visible : Visibility.Collapsed;
    }







    private void ConceptSingleValueTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox textBox || e.Key != Key.Enter)
            return;

        var fieldKind = textBox.Tag as string;
        var values = GetSingleValueCollection(fieldKind);
        if (values is null)
            return;

        if (!IsSingleValueFieldIncluded(fieldKind))
        {
            e.Handled = true;
            return;
        }

        var value = TrimInvisibleValuePadding(textBox.Text ?? string.Empty);
        e.Handled = true;

        if (string.IsNullOrEmpty(value))
            return;

        if (string.Equals(fieldKind, "Year", StringComparison.Ordinal) && !IsFourDigitYear(value))
            return;

        if (values.Count > 0 &&
            AreCanonicalScalarValuesEqual(fieldKind, values[0].Value, value))
        {
            FlashDuplicateSingleValue(values[0]);
        }
        else
        {
            values.Clear();
            values.Add(new ConceptSingleValue(value));

            if (fieldKind is "Year" or "TrackNumber" or "TrackCount" &&
                int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var integerValue))
            {
                var field = fieldKind switch
                {
                    "Year" => ProjectMetadataField.Year,
                    "TrackNumber" => ProjectMetadataField.OutputTrackNumber,
                    "TrackCount" => ProjectMetadataField.OutputTrackTotal,
                    _ => throw new InvalidOperationException()
                };
                var origin = fieldKind switch
                {
                    "TrackNumber" when IsTrackNumberLocked => MetadataOrigin.GlobalPreference,
                    "TrackCount" when IsTrackCountLocked => MetadataOrigin.GlobalPreference,
                    _ => MetadataOrigin.User
                };
                CommitCanonicalInteger(field, integerValue, origin);
            }
        }

        textBox.Clear();
        textBox.CaretIndex = 0;
    }

    private static string? GetCandidateDisplayValue(object? candidate) => candidate switch
    {
        ConceptMetadataCandidate presentation => presentation.DisplayValue,
        SourceMetadataAlternative source => source.DisplayValue,
        _ => null
    };

    private void ConceptCandidateComboBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not ComboBox comboBox || e.Key != Key.Enter)
            return;

        var fieldKind = comboBox.Tag as string;
        var values = GetSingleValueCollection(fieldKind);
        e.Handled = true;
        comboBox.IsDropDownOpen = false;

        if (values is null || !IsSingleValueFieldIncluded(fieldKind))
            return;

        comboBox.ApplyTemplate();
        var editableTextBox = comboBox.Template.FindName("PART_EditableTextBox", comboBox) as TextBox;
        var value = TrimInvisibleValuePadding(
            editableTextBox?.Text ??
            comboBox.Text ??
            GetCandidateDisplayValue(comboBox.SelectedItem) ??
            string.Empty);

        if (string.IsNullOrEmpty(value))
            return;

        if (string.Equals(fieldKind, "ReleaseDate", StringComparison.Ordinal))
        {
            if (!MetadataDateParser.TryParse(value, out var releaseDate))
                return;

            value = releaseDate.ToString();
        }

        if (values.Count > 0 &&
            AreCanonicalScalarValuesEqual(fieldKind, values[0].Value, value))
        {
            FlashDuplicateSingleValue(values[0]);
        }
        else
        {
            values.Clear();
            values.Add(new ConceptSingleValue(value));
            CommitCanonicalScalar(fieldKind, value, MetadataOrigin.User);
        }

        ClearEditableComboBox(comboBox, editableTextBox);
    }

    private ObservableCollection<ConceptSingleValue>? GetSingleValueCollection(string? fieldKind) =>
        fieldKind switch
        {
            "WorkType" => WorkTypeValues,
            "AbridgedStatus" => AbridgedStatusValues,
            "ReleaseDate" => ReleaseDateValues,
            "Year" => YearValues,
            "TrackNumber" => TrackNumberValues,
            "TrackCount" => TrackCountValues,
            _ => null
        };

    private bool IsSingleValueFieldIncluded(string? fieldKind) =>
        fieldKind switch
        {
            "WorkType" => IsWorkTypeIncluded,
            "AbridgedStatus" => IsAbridgedStatusIncluded,
            "ReleaseDate" => IsReleaseDateIncluded,
            "Year" => IsYearIncluded,
            "TrackNumber" => IsTrackNumberIncluded,
            "TrackCount" => IsTrackCountIncluded,
            _ => false
        };

    private TextBox? GetActiveSingleValueTextBox(string? fieldKind) =>
        fieldKind switch
        {
            "Year" => ConceptPublicationPanel.Visibility == Visibility.Visible
                ? ConceptPublicationYearTextBox
                : ConceptYearTextBox,
            "TrackNumber" => ConceptWorkTrackNumberTextBox,
            "TrackCount" => ConceptWorkTrackCountTextBox,
            _ => null
        };

    private ComboBox? GetActiveCandidateComboBox(string? fieldKind) =>
        fieldKind switch
        {
            "ReleaseDate" => ConceptPublicationReleaseDateComboBox,
            _ => null
        };

    private bool AreCanonicalScalarValuesEqual(string? fieldKind, string left, string right)
    {
        var field = fieldKind switch
        {
            "ReleaseDate" => ProjectMetadataField.ReleaseDate,
            _ => (ProjectMetadataField?)null
        };

        if (!field.HasValue)
            return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

        return string.Equals(
            _normalization.NormalizeForComparison(field.Value, left),
            _normalization.NormalizeForComparison(field.Value, right),
            StringComparison.Ordinal);
    }


    private ComboBox? GetSingleChoiceComboBox(string? fieldKind) =>
        fieldKind switch
        {
            "WorkType" => ConceptWorkOutputPanel.Visibility == Visibility.Visible
                ? ConceptWorkWorkTypeComboBox
                : ConceptWorkTypeComboBox,
            "AbridgedStatus" => ConceptWorkOutputPanel.Visibility == Visibility.Visible
                ? ConceptWorkAbridgedStatusComboBox
                : ConceptAbridgedStatusComboBox,
            _ => null
        };



    private async void FlashDuplicateSingleValue(ConceptSingleValue fieldValue)
    {
        var flashVersion = ++fieldValue.DuplicateHighlightVersion;
        fieldValue.IsDuplicateHighlighted = true;
        await Task.Delay(450);

        if (fieldValue.DuplicateHighlightVersion == flashVersion)
            fieldValue.IsDuplicateHighlighted = false;
    }

    private void SingleValueBadge_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2 ||
            e.OriginalSource is not DependencyObject source ||
            FindVisualAncestor<Button>(source) is not null)
        {
            return;
        }

        EditSingleValue_Click(sender, e);
    }

    private void EditSingleValue_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element ||
            element.DataContext is not ConceptSingleValue fieldValue)
        {
            return;
        }

        var fieldKind = element.Tag as string;
        var values = GetSingleValueCollection(fieldKind);
        if (values is null || !IsSingleValueFieldIncluded(fieldKind))
            return;
        if ((fieldKind == "TrackNumber" && IsTrackNumberLocked) ||
            (fieldKind == "TrackCount" && IsTrackCountLocked) ||
            (fieldKind == "WorkType" && WorkTypeFieldState.IsLocked))
        {
            return;
        }

        if (GetActiveCandidateComboBox(fieldKind) is { } candidateComboBox)
        {
            values.Remove(fieldValue);
            CommitCanonicalScalar(fieldKind, null, MetadataOrigin.None);
            LoadEditableComboBoxDraft(candidateComboBox, fieldValue.Value);
            e.Handled = true;
            return;
        }

        if (GetActiveSingleValueTextBox(fieldKind) is { } textBox)
        {
            values.Remove(fieldValue);
            textBox.Text = fieldValue.Value;
            textBox.Focus();
            Keyboard.Focus(textBox);
            textBox.SelectionLength = 0;
            textBox.CaretIndex = textBox.Text.Length;
            e.Handled = true;
            return;
        }

        if (GetSingleChoiceComboBox(fieldKind) is not { } comboBox)
            return;

        object? matchingItem = null;
        foreach (var item in comboBox.Items)
        {
            if (!string.Equals(item?.ToString(), fieldValue.Value, StringComparison.OrdinalIgnoreCase))
                continue;

            matchingItem = item;
            break;
        }

        if (matchingItem is null)
            return;

        values.Remove(fieldValue);
        comboBox.SelectedItem = matchingItem;
        comboBox.Focus();
        Keyboard.Focus(comboBox);
        e.Handled = true;
    }

    private void RemoveSingleValue_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element ||
            element.DataContext is not ConceptSingleValue fieldValue)
        {
            return;
        }

        var fieldKind = element.Tag as string;
        if ((fieldKind == "TrackNumber" && IsTrackNumberLocked) ||
            (fieldKind == "TrackCount" && IsTrackCountLocked) ||
            (fieldKind == "WorkType" && WorkTypeFieldState.IsLocked))
        {
            e.Handled = true;
            return;
        }

        if (GetSingleValueCollection(fieldKind)?.Remove(fieldValue) == true)
        {
            if (GetActiveCandidateComboBox(fieldKind) is not null)
                CommitCanonicalScalar(fieldKind, null, MetadataOrigin.None);
            else if (fieldKind is "WorkType" or "AbridgedStatus")
                CommitCanonicalEnum(fieldKind, null, MetadataOrigin.None);
            else if (fieldKind is "Year" or "TrackNumber" or "TrackCount")
            {
                var field = fieldKind switch
                {
                    "Year" => ProjectMetadataField.Year,
                    "TrackNumber" => ProjectMetadataField.OutputTrackNumber,
                    "TrackCount" => ProjectMetadataField.OutputTrackTotal,
                    _ => throw new InvalidOperationException()
                };
                CommitCanonicalInteger(field, null, MetadataOrigin.None);
            }
        }

        e.Handled = true;
    }

    private static bool IsFourDigitYear(string value)
    {
        if (value.Length != 4)
            return false;

        foreach (var character in value)
        {
            if (!char.IsDigit(character))
                return false;
        }

        return true;
    }

    private void ConceptYearTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        foreach (var character in e.Text)
        {
            if (char.IsDigit(character))
                continue;

            e.Handled = true;
            return;
        }
    }

    private void ConceptYearTextBox_Pasting(object sender, DataObjectPastingEventArgs e)
    {
        if (sender is not TextBox textBox ||
            !e.DataObject.GetDataPresent(DataFormats.UnicodeText) ||
            e.DataObject.GetData(DataFormats.UnicodeText) is not string pastedText)
        {
            e.CancelCommand();
            return;
        }

        var candidate = textBox.Text.Remove(textBox.SelectionStart, textBox.SelectionLength)
            .Insert(textBox.SelectionStart, pastedText);

        if (candidate.Length > 4)
        {
            e.CancelCommand();
            return;
        }

        foreach (var character in candidate)
        {
            if (char.IsDigit(character))
                continue;

            e.CancelCommand();
            return;
        }
    }

    private void ConceptNumericTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        foreach (var character in e.Text)
        {
            if (char.IsDigit(character))
                continue;

            e.Handled = true;
            return;
        }
    }

    private void ConceptNumericTextBox_Pasting(object sender, DataObjectPastingEventArgs e)
    {
        if (!e.DataObject.GetDataPresent(DataFormats.UnicodeText) ||
            e.DataObject.GetData(DataFormats.UnicodeText) is not string pastedText)
        {
            e.CancelCommand();
            return;
        }

        foreach (var character in pastedText)
        {
            if (char.IsDigit(character))
                continue;

            e.CancelCommand();
            return;
        }
    }

    private void ConceptEnumCandidateComboBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not ComboBox comboBox || e.Key != Key.Enter)
            return;

        e.Handled = true;
        CommitEnumCandidate(comboBox, comboBox.SelectedItem as ConceptMetadataCandidate);
    }

    private void ConceptEnumCandidateComboBox_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ComboBoxItem item ||
            ItemsControl.ItemsControlFromItemContainer(item) is not ComboBox comboBox ||
            item.DataContext is not ConceptMetadataCandidate candidate)
            return;

        // Let ComboBox finish the mouse selection before clearing the committed draft.
        Dispatcher.BeginInvoke(new Action(() => CommitEnumCandidate(comboBox, candidate)));
    }

    private void CommitEnumCandidate(ComboBox comboBox, ConceptMetadataCandidate? candidate)
    {
        var fieldKind = comboBox.Tag as string;
        if (fieldKind == "WorkType" && WorkTypeFieldState.IsLocked)
            return;
        var values = GetSingleValueCollection(fieldKind);
        comboBox.IsDropDownOpen = false;

        if (values is null || !IsSingleValueFieldIncluded(fieldKind) || candidate is null)
            return;

        if (IsUnknownEnumCandidate(candidate.ValueToCommit))
        {
            values.Clear();
            CommitCanonicalEnum(fieldKind, candidate.ValueToCommit, MetadataOrigin.User);
        }
        else if (values.Count == 1 && string.Equals(values[0].Value, candidate.DisplayValue, StringComparison.OrdinalIgnoreCase))
        {
            FlashDuplicateSingleValue(values[0]);
        }
        else
        {
            values.Clear();
            values.Add(new ConceptSingleValue(candidate.DisplayValue));
            CommitCanonicalEnum(fieldKind, candidate.ValueToCommit, MetadataOrigin.User);
        }

        comboBox.SelectedItem = null;
    }

    private static bool IsUnknownEnumCandidate(object value) => value switch
    {
        AudioWorkType workType => workType == AudioWorkType.Unknown,
        AbridgedStatus status => status == AbridgedStatus.Unknown,
        _ => false
    };

    private static void ClearEditableComboBox(ComboBox comboBox, TextBox? editableTextBox)
    {
        comboBox.SelectedItem = null;
        comboBox.Text = string.Empty;

        if (editableTextBox is null)
            return;

        editableTextBox.Clear();
        editableTextBox.CaretIndex = 0;
    }

    private static void LoadEditableComboBoxDraft(ComboBox comboBox, string value)
    {
        comboBox.ApplyTemplate();
        var editableTextBox = comboBox.Template.FindName("PART_EditableTextBox", comboBox) as TextBox;

        comboBox.SelectedItem = null;
        comboBox.Text = value;

        if (editableTextBox is not null)
        {
            editableTextBox.Text = value;
            editableTextBox.Focus();
            Keyboard.Focus(editableTextBox);
            editableTextBox.SelectionLength = 0;
            editableTextBox.CaretIndex = editableTextBox.Text.Length;
            return;
        }

        comboBox.Focus();
        Keyboard.Focus(comboBox);
    }

    private static string TrimInvisibleValuePadding(string value)
    {
        var start = 0;
        var end = value.Length - 1;

        while (start <= end && IsInvisibleValuePadding(value[start]))
            start++;

        while (end >= start && IsInvisibleValuePadding(value[end]))
            end--;

        return start > end ? string.Empty : value.Substring(start, end - start + 1);
    }

    private static bool IsInvisibleValuePadding(char character)
    {
        return char.IsWhiteSpace(character) ||
               char.IsControl(character) ||
               char.GetUnicodeCategory(character) == UnicodeCategory.Format;
    }

    private bool IsSeriesCandidateComboBox(ComboBox comboBox) =>
        FindVisualAncestor<Controls.ConceptSeriesFieldControl>(comboBox) is not null;

    private static void MoveEditableComboBoxCaretToEnd(ComboBox comboBox)
    {
        comboBox.ApplyTemplate();
        if (comboBox.Template.FindName("PART_EditableTextBox", comboBox) is not TextBox editableTextBox)
            return;

        editableTextBox.SelectionLength = 0;
        editableTextBox.CaretIndex = editableTextBox.Text.Length;
    }

    private static T? FindVisualAncestor<T>(DependencyObject source) where T : DependencyObject
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

    private static T? FindVisualChild<T>(
        DependencyObject parent,
        Func<T, bool>? predicate = null) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);

            if (child is T match && (predicate is null || predicate(match)))
                return match;

            if (FindVisualChild<T>(child, predicate) is { } nested)
                return nested;
        }

        return null;
    }

    private void ConceptSearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isApplyingConceptSearch || sender is not TextBox searchTextBox)
            return;

        var next = searchTextBox.Text ?? string.Empty;
        if (string.Equals(_conceptSearchText, next, StringComparison.Ordinal))
            return;

        if (_conceptSearchText.Length == 0 && next.Length > 0)
            _preSearchFamilyKey = GetSelectedConceptFamilyKey() ?? "quick";

        _conceptSearchText = next;
        OnPropertyChanged(nameof(HasConceptSearchText));
        OnPropertyChanged(nameof(SearchAdornment));
        ApplyConceptSearch();
    }

    public bool HasConceptSearchText => !string.IsNullOrWhiteSpace(_conceptSearchText);

    public string SearchAdornment => HasConceptSearchText ? "×" : "⌕";

    private void ConceptSearchAdornment_Click(object sender, RoutedEventArgs e)
    {
        ConceptSearchTextBox.Clear();
        ConceptSearchTextBox.Focus();
        Keyboard.Focus(ConceptSearchTextBox);
        e.Handled = true;
    }

    private void ApplyConceptSearch()
    {
        if (_isApplyingConceptSearch)
            return;

        _isApplyingConceptSearch = true;
        try
        {
            RestoreConceptSearchResultFields();

            var families = BuildConceptSearchFamilies();
            if (!HasConceptSearchText)
            {
                _activeConceptSearchSurface = null;
                ConceptSearchResultsPanel.Children.Clear();
                foreach (var family in families)
                    SetSearchFamilyState(family, true);

                var restoreTargetKey = string.IsNullOrWhiteSpace(_conceptSearchText)
                    ? _preSearchFamilyKey
                    : GetSelectedConceptFamilyKey();
                SelectConceptFamily(restoreTargetKey, families);
                return;
            }

            var matches = new List<ConceptSearchResultMatch>();
            var matchedFamilyKeys = new HashSet<string>(StringComparer.Ordinal);
            var seenFieldKeys = new HashSet<string>(StringComparer.Ordinal);
            var sequence = 0;

            foreach (var family in families)
            {
                var familyRank = ConceptDialogSearchMatcher.GetRank(family.SearchTerms, _conceptSearchText);
                if (familyRank is not null)
                    matchedFamilyKeys.Add(family.Key);

                foreach (var field in family.Fields)
                {
                    var fieldRank = field.GetRank(_conceptSearchText);
                    if (fieldRank is null)
                        continue;

                    matchedFamilyKeys.Add(family.Key);

                    if (!seenFieldKeys.Add(field.IdentityKey))
                        continue;

                    matches.Add(new ConceptSearchResultMatch(field, fieldRank.Value, sequence++));
                }
            }

            foreach (var family in families)
                SetSearchFamilyState(family, matchedFamilyKeys.Contains(family.Key));

            foreach (var match in matches.OrderBy(item => item.Rank).ThenBy(item => item.Sequence))
                MoveConceptSearchResultField(match.Field);

            var selectedKey = GetSelectedConceptFamilyKey();
            var targetKey = selectedKey is not null && matchedFamilyKeys.Contains(selectedKey)
                ? selectedKey
                : families.FirstOrDefault(family => matchedFamilyKeys.Contains(family.Key))?.Key;
            _activeConceptSearchSurface = families
                .FirstOrDefault(family => string.Equals(family.Key, targetKey, StringComparison.Ordinal))
                ?.SearchSurface;

            SelectConceptFamily(targetKey, families);
            UpdateConceptFamilyPanelVisibility();
        }
        finally
        {
            _isApplyingConceptSearch = false;
        }
    }

    private static void SetSearchFamilyState(ConceptSearchFamilyDescriptor family, bool isMatch)
    {
        family.NavigationItem.Visibility = Visibility.Visible;
        family.NavigationItem.Opacity = isMatch ? 1.0 : 0.35;
        family.NavigationItem.IsHitTestVisible = isMatch;
        family.NavigationItem.Focusable = isMatch;
    }

    private void MoveConceptSearchResultField(ConceptSearchFieldDescriptor field)
    {
        var resultElement = field.GetResultElement();
        if (resultElement is null)
            return;

        if (resultElement.Parent is not Panel parent)
        {
            if (field.SearchMargin is Thickness transientSearchMargin)
                resultElement.Margin = transientSearchMargin;
            ConceptSearchResultsPanel.Children.Add(resultElement);
            _conceptSearchTransientFields.Add(resultElement);
            return;
        }

        var index = parent.Children.IndexOf(resultElement);
        if (index < 0)
            return;

        var originalMargin = resultElement.Margin;
        parent.Children.RemoveAt(index);
        _conceptSearchFieldPlacements.Add(new ConceptSearchFieldPlacement(resultElement, parent, index, originalMargin));
        if (field.SearchMargin is Thickness searchMargin)
            resultElement.Margin = searchMargin;
        ConceptSearchResultsPanel.Children.Add(resultElement);
    }

    private void RestoreConceptSearchResultFields()
    {
        foreach (var transientField in _conceptSearchTransientFields)
        {
            if (transientField is Controls.ConceptMetadataFieldControl fieldControl)
                fieldControl.PrepareForTransientRemoval();
            ConceptSearchResultsPanel.Children.Remove(transientField);
        }
        _conceptSearchTransientFields.Clear();

        for (var index = _conceptSearchFieldPlacements.Count - 1; index >= 0; index--)
        {
            var placement = _conceptSearchFieldPlacements[index];
            ConceptSearchResultsPanel.Children.Remove(placement.Element);
            placement.Element.Margin = placement.OriginalMargin;
            var insertIndex = Math.Min(placement.Index, placement.Parent.Children.Count);
            placement.Parent.Children.Insert(insertIndex, placement.Element);
        }

        _conceptSearchFieldPlacements.Clear();
    }

    private IReadOnlyList<ConceptSearchFamilyDescriptor> BuildConceptSearchFamilies()
    {
        return
        [
            new ConceptSearchFamilyDescriptor(
                "quick",
                [LocalizationText.Get("UiSchnellzugriff")],
                ConceptFamilyList,
                ConceptQuickAccessFamilyItem,
                [
                    new ConceptSearchFieldDescriptor("title", CombineSearchTerms(TitleField.Values.Select(item => item.Value), TitleField.Descriptor.SearchTerms.ToArray()), () => CreateConceptFieldSearchView(TitleField)),
                    new ConceptSearchFieldDescriptor("album", CombineSearchTerms(AlbumField.Values.Select(item => item.Value), AlbumField.Descriptor.SearchTerms.ToArray()), () => CreateConceptFieldSearchView(AlbumField, TitleAlbumBinding)),
                    new ConceptSearchFieldDescriptor("author", CombineSearchTerms(AuthorsField.Values.Select(item => item.Value), AuthorsField.Descriptor.SearchTerms.ToArray()), () => CreateConceptFieldSearchView(AuthorsField)),
                    new ConceptSearchFieldDescriptor("speaker", CombineSearchTerms(NarratorsField.Values.Select(item => item.Value), NarratorsField.Descriptor.SearchTerms.ToArray()), () => CreateConceptFieldSearchView(NarratorsField)),
                    new ConceptSearchFieldDescriptor("genre", CombineSearchTerms(GenresField.Values.Select(item => item.Value), GenresField.Descriptor.SearchTerms.ToArray()), () => CreateConceptFieldSearchView(GenresField)),
                    new ConceptSearchFieldDescriptor("year", CombineSearchTerms(YearValues.Select(item => item.Value), ConceptMetadataFieldCatalog.Get(ProjectMetadataField.Year).SearchTerms.ToArray()), ConceptQuickAccessYearField),
                    new ConceptSearchFieldDescriptor("abridged-status", CombineSearchTerms(AbridgedStatusValues.Select(item => item.Value), ConceptMetadataFieldCatalog.Get(ProjectMetadataField.AbridgedStatus).SearchTerms.ToArray()), ConceptQuickAccessAbridgedStatusField),
                    new ConceptSearchFieldDescriptor("work-type", CombineSearchTerms(WorkTypeValues.Select(item => item.Value), ConceptMetadataFieldCatalog.Get(ProjectMetadataField.AudioWorkType).SearchTerms.ToArray()), ConceptQuickAccessWorkTypeField),
                    new ConceptSearchFieldDescriptor(
                        "description",
                        ConceptMetadataFieldCatalog.Get(ProjectMetadataField.Description).SearchTerms,
                        ConceptQuickAccessDescriptionField,
                        [DescriptionText],
                        allowOrderedSubsequenceForValue: false)
                ]),
            new ConceptSearchFamilyDescriptor(
                "work-output",
                [LocalizationText.Get("UiWerk")],
                ConceptFamilyList,
                ConceptWorkOutputFamilyItem,
                [
                    new ConceptSearchFieldDescriptor("title", CombineSearchTerms(TitleField.Values.Select(item => item.Value), TitleField.Descriptor.SearchTerms.ToArray()), () => CreateConceptFieldSearchView(TitleField)),
                    new ConceptSearchFieldDescriptor("album", CombineSearchTerms(AlbumField.Values.Select(item => item.Value), AlbumField.Descriptor.SearchTerms.ToArray()), () => CreateConceptFieldSearchView(AlbumField, TitleAlbumBinding)),
                    new ConceptSearchFieldDescriptor("subtitle", CombineSearchTerms(SubtitleField.Values.Select(item => item.Value), SubtitleField.Descriptor.SearchTerms.ToArray()), () => CreateConceptFieldSearchView(SubtitleField)),
                    new ConceptSearchFieldDescriptor("work-type", CombineSearchTerms(WorkTypeValues.Select(item => item.Value), ConceptMetadataFieldCatalog.Get(ProjectMetadataField.AudioWorkType).SearchTerms.ToArray()), ConceptWorkWorkTypeField),
                    new ConceptSearchFieldDescriptor("abridged-status", CombineSearchTerms(AbridgedStatusValues.Select(item => item.Value), ConceptMetadataFieldCatalog.Get(ProjectMetadataField.AbridgedStatus).SearchTerms.ToArray()), ConceptWorkAbridgedStatusField),
                    new ConceptSearchFieldDescriptor(
                        "series",
                        CombineSearchTerms(
                            SeriesValues.SelectMany(item => new[] { item.Name, item.Part }),
                            [.. ConceptMetadataFieldCatalog.Get(ProjectMetadataField.Series).SearchTerms,
                             .. ConceptMetadataFieldCatalog.Get(ConceptMetadataFieldCatalog.SeriesPartId).SearchTerms]),
                        ConceptWorkSeriesField),
                    new ConceptSearchFieldDescriptor("volume", CombineSearchTerms(VolumeField.Values.Select(item => item.Value), VolumeField.Descriptor.SearchTerms.ToArray()), () => CreateConceptFieldSearchView(VolumeField))
                ]),
            new ConceptSearchFamilyDescriptor(
                "people",
                [LocalizationText.Get("UiPersonen")],
                ConceptFamilyList,
                ConceptPeopleFamilyItem,
                [
                    new ConceptSearchFieldDescriptor("author", CombineSearchTerms(AuthorsField.Values.Select(item => item.Value), AuthorsField.Descriptor.SearchTerms.ToArray()), () => CreateConceptFieldSearchView(AuthorsField)),
                    new ConceptSearchFieldDescriptor("speaker", CombineSearchTerms(NarratorsField.Values.Select(item => item.Value), NarratorsField.Descriptor.SearchTerms.ToArray()), () => CreateConceptFieldSearchView(NarratorsField)),
                    new ConceptSearchFieldDescriptor("composer", CombineSearchTerms(ComposersField.Values.Select(item => item.Value), ComposersField.Descriptor.SearchTerms.ToArray()), () => CreateConceptFieldSearchView(ComposersField, NarratorComposerBinding)),
                    new ConceptSearchFieldDescriptor("translator", CombineSearchTerms(TranslatorsField.Values.Select(item => item.Value), TranslatorsField.Descriptor.SearchTerms.ToArray()), () => CreateConceptFieldSearchView(TranslatorsField))
                ]),
            new ConceptSearchFamilyDescriptor(
                "publication",
                [LocalizationText.Get("UiVeroeffentlichung")],
                ConceptFamilyList,
                ConceptPublicationFamilyItem,
                new[]
                {
                    new ConceptSearchFieldDescriptor("publisher", CombineSearchTerms(PublisherField.Values.Select(item => item.Value), PublisherField.Descriptor.SearchTerms.ToArray()), () => CreateConceptFieldSearchView(PublisherField)),
                    new ConceptSearchFieldDescriptor("release-date", CombineSearchTerms(ReleaseDateValues.Select(item => item.Value), ConceptMetadataFieldCatalog.Get(ProjectMetadataField.ReleaseDate).SearchTerms.ToArray()), ConceptPublicationReleaseDateField),
                    new ConceptSearchFieldDescriptor("year", CombineSearchTerms(YearValues.Select(item => item.Value), ConceptMetadataFieldCatalog.Get(ProjectMetadataField.Year).SearchTerms.ToArray()), ConceptPublicationYearField),
                    new ConceptSearchFieldDescriptor("license", CombineSearchTerms(LicenseField.Values.Select(item => item.Value), LicenseField.Descriptor.SearchTerms.ToArray()), () => CreateConceptFieldSearchView(LicenseField))
                }.Concat(BuildGenericSearchFields(PublicationAdditionalFields))
                 .Append(new ConceptSearchFieldDescriptor("edition", CombineSearchTerms(EditionField.Values.Select(item => item.Value), EditionField.Descriptor.SearchTerms.ToArray()), () => CreateConceptFieldSearchView(EditionField)))
                 .ToArray()),
            new ConceptSearchFamilyDescriptor("language", [LocalizationText.Get("UiSprache")], ConceptFamilyList, ConceptLanguageFamilyItem,
                BuildGenericSearchFields(LanguageFields)),
            new ConceptSearchFamilyDescriptor(
                "catalog-data",
                [LocalizationText.Get("MetadataGroupCatalogData")],
                ConceptFamilyList,
                ConceptCatalogDataFamilyItem,
                new[] { new ConceptSearchFieldDescriptor("genre", CombineSearchTerms(GenresField.Values.Select(item => item.Value), GenresField.Descriptor.SearchTerms.ToArray()), () => CreateConceptFieldSearchView(GenresField)) }
                    .Concat(BuildGenericSearchFields(CatalogDataFields)).ToArray()),
            new ConceptSearchFamilyDescriptor("sorting", [LocalizationText.Get("UiSortierung")], ConceptFamilyList, ConceptSortingFamilyItem,
                BuildGenericSearchFields(SortingFields)
                    .Concat([
                        new ConceptSearchFieldDescriptor("track-number", CombineSearchTerms(TrackNumberValues.Select(item => item.Value), ConceptMetadataFieldCatalog.Get(ProjectMetadataField.OutputTrackNumber).SearchTerms.ToArray()), ConceptWorkTrackNumberField),
                        new ConceptSearchFieldDescriptor("track-count", CombineSearchTerms(TrackCountValues.Select(item => item.Value), ConceptMetadataFieldCatalog.Get(ProjectMetadataField.OutputTrackTotal).SearchTerms.ToArray()), ConceptWorkTrackCountField)
                    ]).ToArray()),
            new ConceptSearchFamilyDescriptor(
                "description-group",
                ConceptMetadataFieldCatalog.Get(ProjectMetadataField.Description).SearchTerms,
                ConceptFamilyList,
                ConceptDescriptionFamilyItem,
                BuildGenericSearchFields(DescriptionAdditionalFields)
                    .Append(new ConceptSearchFieldDescriptor(
                        "description",
                        ConceptMetadataFieldCatalog.Get(ProjectMetadataField.Description).SearchTerms,
                        ConceptQuickAccessDescriptionField,
                        [DescriptionText],
                        allowOrderedSubsequenceForValue: false))
                    .ToArray()),
            new ConceptSearchFamilyDescriptor(
                "compatibility",
                [LocalizationText.Get("MetadataGroupCompatibility")],
                ConceptFamilyList,
                ConceptCompatibilityFamilyItem,
                [
                    new ConceptSearchFieldDescriptor("composer",
                        CombineSearchTerms(ComposersField.Values.Select(item => item.Value), ComposersField.Descriptor.SearchTerms.ToArray()),
                        () => CreateConceptFieldSearchView(ComposersField, NarratorComposerBinding)),
                    new ConceptSearchFieldDescriptor(
                        "compatibility-field",
                        ConceptMetadataFieldDescriptor.CreateSearchTerms("Metadata.Search.CompatibilityField", CompatibilityFieldTechnicalName),
                        ConceptCompatibilityField,
                        [CompatibilityFieldText]),
                    new ConceptSearchFieldDescriptor(
                        "description-output-preview",
                        ConceptMetadataFieldDescriptor.CreateSearchTerms("Metadata.Search.DescriptionOutputPreview", DescriptionOutputPreviewTechnicalName),
                        ConceptCompatibilityDescriptionPreview,
                        [CompatibilityDescriptionOutputPreview],
                        allowOrderedSubsequenceForValue: false,
                        searchMargin: new Thickness(0, 12, 0, 14))
                ],
                ConceptCompatibilityPanel),
            new ConceptSearchFamilyDescriptor("pass-through", [LocalizationText.Get("UiPassThrough")], ConceptUtilityFamilyList, ConceptPassThroughFamilyItem,
                BuildGenericSearchFields(PassThroughFields)),
            new ConceptSearchFamilyDescriptor(
                "chapter",
                [LocalizationText.Get("UiKapitel"), LocalizationText.Get("UiKapitelvorschlag"), LocalizationText.Get("UiKapitelwarnung"), "chapter"],
                ConceptUtilityFamilyList,
                ConceptChapterFamilyItem,
                [],
                ConceptChapterPanel),
            new ConceptSearchFamilyDescriptor("cover", [LocalizationText.Get("UiCover")], ConceptFamilyList, ConceptCoverFamilyItem,
                [new ConceptSearchFieldDescriptor("cover", [LocalizationText.Get("UiCover")], ConceptCoverWidthWrapper)]),
            new ConceptSearchFamilyDescriptor("help", [LocalizationText.Get("MetadataHelpTitle")], ConceptUtilityFamilyList, ConceptHelpFamilyItem, [], ConceptHelpPanel)
        ];
    }

    private static IEnumerable<string> CombineSearchTerms(
        IEnumerable<string> dynamicValues,
        params string[] fixedValues) =>
        fixedValues.Concat(dynamicValues ?? []);

    private Controls.ConceptMetadataFieldControl CreateConceptFieldSearchView(
        ConceptMetadataFieldViewModel field,
        ConceptMetadataFieldBindingState? fieldBinding = null)
    {
        var control = new Controls.ConceptMetadataFieldControl();
        if (fieldBinding is null)
        {
            control.DataContext = field;
            return control;
        }

        ConfigureBindingFieldView(control, field, fieldBinding);
        return control;
    }

    private IReadOnlyList<ConceptSearchFieldDescriptor> BuildGenericSearchFields(
        IEnumerable<ConceptMetadataFieldViewModel> fields) =>
        fields.Select(field => new ConceptSearchFieldDescriptor(
                "generic-" + field.Field,
                field.Descriptor.SearchTerms,
                () => new Controls.ConceptMetadataFieldControl { DataContext = field },
                field.Values.Select(value => value.Value),
                allowOrderedSubsequenceForValue: field.Field != ProjectMetadataField.Comment))
            .ToArray();

    private string? GetSelectedConceptFamilyKey()
    {
        if (ReferenceEquals(ConceptFamilyList.SelectedItem, ConceptQuickAccessFamilyItem))
            return "quick";
        if (ReferenceEquals(ConceptFamilyList.SelectedItem, ConceptWorkOutputFamilyItem))
            return "work-output";
        if (ReferenceEquals(ConceptFamilyList.SelectedItem, ConceptPeopleFamilyItem))
            return "people";
        if (ReferenceEquals(ConceptFamilyList.SelectedItem, ConceptPublicationFamilyItem))
            return "publication";
        if (ReferenceEquals(ConceptFamilyList.SelectedItem, ConceptLanguageFamilyItem))
            return "language";
        if (ReferenceEquals(ConceptFamilyList.SelectedItem, ConceptCatalogDataFamilyItem))
            return "catalog-data";
        if (ReferenceEquals(ConceptFamilyList.SelectedItem, ConceptSortingFamilyItem))
            return "sorting";
        if (ReferenceEquals(ConceptFamilyList.SelectedItem, ConceptDescriptionFamilyItem))
            return "description-group";
        if (ReferenceEquals(ConceptFamilyList.SelectedItem, ConceptCompatibilityFamilyItem))
            return "compatibility";
        if (ReferenceEquals(ConceptFamilyList.SelectedItem, ConceptTechnicalDataFamilyItem))
            return "technical-data";
        if (ReferenceEquals(ConceptUtilityFamilyList.SelectedItem, ConceptPassThroughFamilyItem))
            return "pass-through";
        if (ReferenceEquals(ConceptUtilityFamilyList.SelectedItem, ConceptChapterFamilyItem))
            return "chapter";
        if (ReferenceEquals(ConceptFamilyList.SelectedItem, ConceptCoverFamilyItem))
            return "cover";
        if (ReferenceEquals(ConceptUtilityFamilyList.SelectedItem, ConceptHelpFamilyItem))
            return "help";

        return null;
    }

    internal static string NormalizeConceptFamilyKey(string? familyKey) =>
        string.Equals(familyKey, "original-data", StringComparison.Ordinal)
            ? ConceptMetadataGroupIds.QuickAccess
            : MetadataPreferencesService.NormalizeConceptGroupKey(familyKey);

    private void SelectConceptFamily(
        string? familyKey,
        IReadOnlyList<ConceptSearchFamilyDescriptor> families)
    {
        familyKey = NormalizeConceptFamilyKey(familyKey);

        var target = families.FirstOrDefault(family =>
            string.Equals(family.Key, familyKey, StringComparison.Ordinal) &&
            family.NavigationItem.Visibility == Visibility.Visible);

        if (target is null)
        {
            ConceptFamilyList.SelectedIndex = -1;
            ConceptUtilityFamilyList.SelectedIndex = -1;
            UpdateConceptFamilyPanelVisibility();
            return;
        }

        target.FamilyList.SelectedItem = target.NavigationItem;
        if (ReferenceEquals(target.FamilyList, ConceptFamilyList))
            ConceptUtilityFamilyList.SelectedIndex = -1;
        else
            ConceptFamilyList.SelectedIndex = -1;

        UpdateConceptFamilyPanelVisibility();
    }

    private sealed class ConceptSearchFamilyDescriptor
    {
        public ConceptSearchFamilyDescriptor(
            string key,
            IReadOnlyList<string> searchTerms,
            ListBox familyList,
            ListBoxItem navigationItem,
            IReadOnlyList<ConceptSearchFieldDescriptor> fields,
            FrameworkElement? searchSurface = null)
        {
            Key = key;
            SearchTerms = searchTerms;
            FamilyList = familyList;
            NavigationItem = navigationItem;
            Fields = fields;
            SearchSurface = searchSurface;
        }

        public string Key { get; }

        public IReadOnlyList<string> SearchTerms { get; }

        public ListBox FamilyList { get; }

        public ListBoxItem NavigationItem { get; }

        public IReadOnlyList<ConceptSearchFieldDescriptor> Fields { get; }

        public FrameworkElement? SearchSurface { get; }
    }

    private sealed class ConceptSearchFieldDescriptor
    {
        public ConceptSearchFieldDescriptor(
            string identityKey,
            IEnumerable<string> searchTerms,
            FrameworkElement resultElement,
            IEnumerable<string>? valueSearchTerms = null,
            bool allowOrderedSubsequenceForValue = true,
            Thickness? searchMargin = null)
            : this(identityKey, searchTerms, () => resultElement, valueSearchTerms, allowOrderedSubsequenceForValue, searchMargin)
        {
        }

        public ConceptSearchFieldDescriptor(
            string identityKey,
            IEnumerable<string> searchTerms,
            Func<FrameworkElement?> resultElementProvider,
            IEnumerable<string>? valueSearchTerms = null,
            bool allowOrderedSubsequenceForValue = true,
            Thickness? searchMargin = null)
        {
            IdentityKey = identityKey;
            SearchTerms = searchTerms.Where(value => !string.IsNullOrWhiteSpace(value)).ToArray();
            ValueSearchTerms = (valueSearchTerms ?? []).Where(value => !string.IsNullOrWhiteSpace(value)).ToArray();
            AllowOrderedSubsequenceForValue = allowOrderedSubsequenceForValue;
            SearchMargin = searchMargin;
            _resultElementProvider = resultElementProvider;
        }

        private readonly Func<FrameworkElement?> _resultElementProvider;

        public string IdentityKey { get; }

        public IReadOnlyList<string> SearchTerms { get; }

        public IReadOnlyList<string> ValueSearchTerms { get; }

        public bool AllowOrderedSubsequenceForValue { get; }

        public Thickness? SearchMargin { get; }

        public int? GetRank(string query)
        {
            var labelRank = ConceptDialogSearchMatcher.GetRank(SearchTerms, query);
            var valueRank = ConceptDialogSearchMatcher.GetRank(
                ValueSearchTerms,
                query,
                AllowOrderedSubsequenceForValue);
            if (labelRank is null)
                return valueRank;
            if (valueRank is null)
                return labelRank;
            return Math.Min(labelRank.Value, valueRank.Value);
        }

        public FrameworkElement? GetResultElement() => _resultElementProvider();
    }

    private sealed record ConceptSearchResultMatch(
        ConceptSearchFieldDescriptor Field,
        int Rank,
        int Sequence);

    private sealed record ConceptSearchFieldPlacement(
        FrameworkElement Element,
        Panel Parent,
        int Index,
        Thickness OriginalMargin);

    private void MetadataDialog_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.F || !Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            return;

        e.Handled = true;
        ConceptSearchTextBox.Focus();
        Keyboard.Focus(ConceptSearchTextBox);
        ConceptSearchTextBox.SelectAll();
    }

    private void ConceptSearchTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || sender is not TextBox searchTextBox || string.IsNullOrEmpty(searchTextBox.Text))
            return;

        searchTextBox.Clear();
        e.Handled = true;
    }

    private void ConceptQuickAccessCoverPreview_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;

        if (!string.IsNullOrEmpty(ConceptSearchTextBox.Text))
            ConceptSearchTextBox.Clear();

        ConceptFamilyList.SelectedItem = ConceptCoverFamilyItem;
        ConceptUtilityFamilyList.SelectedIndex = -1;
        UpdateConceptFamilyPanelVisibility();
    }

    private void ConceptCover_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = _dropCover is not null && _canChooseCover && _coverImageService.CanReadDrop(e.Data)
            ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void ConceptCover_Drop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (_dropCover is not null && _canChooseCover) await _dropCover(e.Data, this);
    }

    private void ConceptCover_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        _chooseCover(this);
    }

    private void ConceptRemoveCover_Click(object sender, RoutedEventArgs e)
    {
        _removeCover();
        e.Handled = true;
    }

    private void ConfigureChapterSettingsEditor()
    {
        ConceptChapterSettingsHeadingTextBlock.Text = ChapterSettingsEditorPresentation.GetText("ChapterSettingsHeading");
        ConceptChapterModeLabelTextBlock.Text = ChapterSettingsEditorPresentation.GetText("Mode");
        ConceptChapterNumberingStyleLabelTextBlock.Text = ChapterSettingsEditorPresentation.GetText("NumberingStyle");
        ConceptChapterCustomHeadingTextBlock.Text = ChapterSettingsEditorPresentation.GetText("CustomRules");

        ConfigureChapterSettingsComboBox(ConceptChapterModeComboBox, ChapterSettingsEditorPresentation.Modes);
        ConfigureChapterSettingsComboBox(ConceptChapterNumberingStyleComboBox, ChapterSettingsEditorPresentation.NumberingStyles);
        ConfigureChapterSettingsComboBox(ConceptChapterSlot1ComboBox, ChapterSettingsEditorPresentation.Slots);
        ConfigureChapterSettingsComboBox(ConceptChapterSeparator1ComboBox, ChapterSettingsEditorPresentation.Separators);
        ConfigureChapterSettingsComboBox(ConceptChapterSlot2ComboBox, ChapterSettingsEditorPresentation.Slots);
        ConfigureChapterSettingsComboBox(ConceptChapterSeparator2ComboBox, ChapterSettingsEditorPresentation.Separators);
        ConfigureChapterSettingsComboBox(ConceptChapterSlot3ComboBox, ChapterSettingsEditorPresentation.Slots);
    }

    public void RefreshChapterSettingsEditorFromSharedState()
    {
        var previousGuard = _isRefreshingChapterSettingsEditor;
        _isRefreshingChapterSettingsEditor = true;
        try
        {
            var state = _chapterSettingsEditorService.Read(_appSettings);
            SelectChapterSettingsComboBoxValue(ConceptChapterModeComboBox, state.Mode);
            SelectChapterSettingsComboBoxValue(ConceptChapterNumberingStyleComboBox, state.NumberingStyle);
            SelectChapterSettingsComboBoxValue(ConceptChapterSlot1ComboBox, state.Slot1);
            SelectChapterSettingsComboBoxValue(ConceptChapterSeparator1ComboBox, state.Separator1);
            SelectChapterSettingsComboBoxValue(ConceptChapterSlot2ComboBox, state.Slot2);
            SelectChapterSettingsComboBoxValue(ConceptChapterSeparator2ComboBox, state.Separator2);
            SelectChapterSettingsComboBoxValue(ConceptChapterSlot3ComboBox, state.Slot3);
            UpdateChapterSettingsEditorVisibility(state.Mode);
            UpdateChapterSlotOptionAvailability(state.NumberingStyle);
        }
        finally
        {
            _isRefreshingChapterSettingsEditor = previousGuard;
        }
    }

    private void ConceptChapterSettingsComboBox_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_isRefreshingChapterSettingsEditor)
            return;

        var current = _chapterSettingsEditorService.Read(_appSettings);
        var updated = current with
        {
            Mode = GetChapterSettingsComboBoxValue(ConceptChapterModeComboBox, current.Mode),
            NumberingStyle = GetChapterSettingsComboBoxValue(ConceptChapterNumberingStyleComboBox, current.NumberingStyle),
            Slot1 = GetChapterSettingsComboBoxValue(ConceptChapterSlot1ComboBox, current.Slot1),
            Separator1 = GetChapterSettingsComboBoxValue(ConceptChapterSeparator1ComboBox, current.Separator1),
            Slot2 = GetChapterSettingsComboBoxValue(ConceptChapterSlot2ComboBox, current.Slot2),
            Separator2 = GetChapterSettingsComboBoxValue(ConceptChapterSeparator2ComboBox, current.Separator2),
            Slot3 = GetChapterSettingsComboBoxValue(ConceptChapterSlot3ComboBox, current.Slot3),
            PreviewDataSource = current.PreviewDataSource
        };

        _chapterSettingsEditorService.Apply(_appSettings, updated);
        RefreshChapterSettingsEditorFromSharedState();
        _chapterSettingsChanged();
        RefreshChapterPresentationView(forceAutoFit: true);
    }

    private void UpdateChapterSettingsEditorVisibility(string mode)
    {
        ConceptChapterNumberingStylePanel.Visibility = ChapterSettingsEditorPresentation.ShowsNumberingStyle(mode)
            ? Visibility.Visible
            : Visibility.Collapsed;
        ConceptChapterCustomRulePanel.Visibility = ChapterSettingsEditorPresentation.ShowsCustomRule(mode)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void UpdateChapterSlotOptionAvailability(string numberingStyle)
    {
        foreach (var comboBox in new[] { ConceptChapterSlot1ComboBox, ConceptChapterSlot2ComboBox, ConceptChapterSlot3ComboBox })
        {
            foreach (var item in comboBox.Items.OfType<ComboBoxItem>())
            {
                var slotValue = item.Tag?.ToString() ?? string.Empty;
                item.IsEnabled = ChapterSettingsEditorPresentation.IsSlotOptionEnabled(slotValue, numberingStyle);
                var helpText = ChapterSettingsEditorPresentation.GetSlotOptionUnavailableHelpText(slotValue, numberingStyle);
                item.ToolTip = helpText;
                ToolTipService.SetShowOnDisabled(item, helpText is not null);
                System.Windows.Automation.AutomationProperties.SetHelpText(item, helpText ?? string.Empty);
            }
        }
    }

    private static void ConfigureChapterSettingsComboBox(
        ComboBox comboBox,
        IReadOnlyList<ChapterSettingsOptionDefinition> options)
    {
        comboBox.Items.Clear();
        foreach (var option in options)
        {
            comboBox.Items.Add(new ComboBoxItem
            {
                Tag = option.Value,
                Content = ChapterSettingsEditorPresentation.GetText(option.ResourceKey)
            });
        }
    }

    private static void SelectChapterSettingsComboBoxValue(ComboBox comboBox, string value)
    {
        foreach (var item in comboBox.Items.OfType<ComboBoxItem>())
        {
            if (!string.Equals(item.Tag?.ToString(), value, StringComparison.Ordinal))
                continue;

            comboBox.SelectedItem = item;
            return;
        }

        comboBox.SelectedIndex = comboBox.Items.Count > 0 ? 0 : -1;
    }

    private static string GetChapterSettingsComboBoxValue(ComboBox comboBox, string fallback) =>
        comboBox.SelectedItem is ComboBoxItem { Tag: not null } item
            ? item.Tag.ToString() ?? fallback
            : fallback;

    public void RefreshChapterPresentationView(bool forceAutoFit = false)
    {
        RebuildChapterPresentationRows();
        ChapterTracksView.Refresh();
        var signature = BuildChapterAutoFitSignature();
        var changed = !string.Equals(signature, _chapterAutoFitSignature, StringComparison.Ordinal);
        _chapterAutoFitSignature = signature;
        if (changed || forceAutoFit || _chapterGridColumnLayoutState.AutoFitPending)
            ScheduleChapterAutoFit(forceAutoFit || _chapterGridColumnLayoutState.AutoFitPending);
    }

    private void AttachChapterColumnWidthObservers()
    {
        foreach (var column in ChapterColumns())
            _chapterColumnWidthDescriptor.AddValueChanged(column, ConceptChapterColumnWidthChanged);
    }

    private void DetachChapterColumnWidthObservers()
    {
        foreach (var column in ChapterColumns())
            _chapterColumnWidthDescriptor.RemoveValueChanged(column, ConceptChapterColumnWidthChanged);
    }

    private DataGridColumn[] ChapterColumns() =>
        [ConceptChapterProposalColumn, ConceptChapterDurationColumn, ConceptChapterWarningColumn];

    private void ConceptChapterColumnWidthChanged(object? sender, EventArgs e)
    {
        if (_isApplyingChapterColumnLayout || sender is not DataGridColumn column || column.ActualWidth <= 0)
            return;

        if (ReferenceEquals(column, ConceptChapterProposalColumn))
            _chapterGridColumnLayoutState.ProposalWidth = column.ActualWidth;
        else if (ReferenceEquals(column, ConceptChapterDurationColumn))
            _chapterGridColumnLayoutState.DurationWidth = column.ActualWidth;
        else if (ReferenceEquals(column, ConceptChapterWarningColumn))
            _chapterGridColumnLayoutState.WarningWidth = column.ActualWidth;
    }

    private void ApplyChapterColumnLayoutForCurrentProject()
    {
        _isApplyingChapterColumnLayout = true;
        try
        {
            if (_chapterGridColumnLayoutState.AutoFitPending)
                ApplyChapterAutoFit(force: true);
            else
                SetChapterColumnWidths(
                    _chapterGridColumnLayoutState.ProposalWidth ?? 180,
                    _chapterGridColumnLayoutState.DurationWidth ?? 110,
                    _chapterGridColumnLayoutState.WarningWidth ?? 260);
        }
        finally
        {
            _isApplyingChapterColumnLayout = false;
        }
    }

    private void ApplyChapterAutoFit(bool force)
    {
        MaterializeEmbeddedChapterPresentationForAutoFit();
        var proposal = ChapterGridLayoutMetrics.CalculateProposalColumnWidth(
            LocalizationText.Get("UiKapitelvorschlag"),
            _chapterTracks.Select(track => track.DisplayChapterProposal),
            _chapterTracks.SelectMany(track => track.EmbeddedChapterRows.Select(row => row.ChapterProposal)),
            MeasureChapterText,
            120,
            Math.Max(260, ConceptChapterGrid.ActualWidth * 0.62));
        var duration = ChapterGridLayoutMetrics.CalculateTextColumnWidth(
            LocalizationText.Get("UiDauer"),
            _chapterTracks.Select(track => track.Duration)
                .Concat(_chapterTracks.SelectMany(track => track.EmbeddedChapterRows.Select(row => row.Duration))),
            MeasureChapterText,
            72,
            150);
        var warning = ChapterGridLayoutMetrics.CalculateTextColumnWidth(
            LocalizationText.Get("UiKapitelwarnung"),
            _chapterTracks.Select(track => track.DisplayChapterWarning),
            MeasureChapterText,
            116,
            320);

        var currentProposal = _chapterGridColumnLayoutState.ProposalWidth ?? ConceptChapterProposalColumn.ActualWidth;
        var currentDuration = _chapterGridColumnLayoutState.DurationWidth ?? ConceptChapterDurationColumn.ActualWidth;
        var currentWarning = _chapterGridColumnLayoutState.WarningWidth ?? ConceptChapterWarningColumn.ActualWidth;
        var nextProposal = ChapterGridColumnLayoutState.ShouldUpdateAutoFitWidth(currentProposal, proposal, force, ChapterAutoFitHysteresis) ? proposal : currentProposal;
        var nextDuration = ChapterGridColumnLayoutState.ShouldUpdateAutoFitWidth(currentDuration, duration, force, ChapterAutoFitHysteresis) ? duration : currentDuration;
        var nextWarning = ChapterGridColumnLayoutState.ShouldUpdateAutoFitWidth(currentWarning, warning, force, ChapterAutoFitHysteresis) ? warning : currentWarning;

        SetChapterColumnWidths(nextProposal, nextDuration, nextWarning);
        _chapterGridColumnLayoutState.ProposalWidth = nextProposal;
        _chapterGridColumnLayoutState.DurationWidth = nextDuration;
        _chapterGridColumnLayoutState.WarningWidth = nextWarning;
        _chapterGridColumnLayoutState.CompleteAutoFit();
    }

    private string BuildChapterAutoFitSignature()
    {
        var builder = new StringBuilder();
        foreach (var row in _chapterPresentationRows)
            builder.Append(row.ChapterProposal).Append('\u001f').Append(row.Duration).Append('\u001f').Append(row.ChapterWarning).Append('\u001e');
        return builder.ToString();
    }

    private void ScheduleChapterAutoFit(bool force)
    {
        if (!ConceptChapterGrid.IsLoaded || _chapterTracks.Count == 0)
            return;
        _chapterAutoFitForcePending |= force;
        _chapterAutoFitTimer.Stop();
        _chapterAutoFitTimer.Start();
    }

    private void ChapterAutoFitTimer_Tick(object? sender, EventArgs e)
    {
        _chapterAutoFitTimer.Stop();
        if (!ConceptChapterGrid.IsLoaded || _chapterTracks.Count == 0)
            return;
        var force = _chapterAutoFitForcePending || _chapterGridColumnLayoutState.AutoFitPending;
        _chapterAutoFitForcePending = false;
        _isApplyingChapterColumnLayout = true;
        try { ApplyChapterAutoFit(force); }
        finally { _isApplyingChapterColumnLayout = false; }
    }

    private void MaterializeEmbeddedChapterPresentationForAutoFit()
    {
        foreach (var track in _chapterTracks.Where(track => track.HasEmbeddedChapterDetails))
            _ = track.EmbeddedChapterRows;
        _refreshChapterTimelinePresentation();
    }

    private double MeasureChapterText(string? value)
    {
        var text = new TextBlock
        {
            Text = value ?? string.Empty,
            FontFamily = ConceptChapterGrid.FontFamily,
            FontSize = ConceptChapterGrid.FontSize
        };
        text.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return text.DesiredSize.Width;
    }

    private void SetChapterColumnWidths(double proposal, double duration, double warning)
    {
        ConceptChapterProposalColumn.Width = new DataGridLength(proposal, DataGridLengthUnitType.Pixel);
        ConceptChapterDurationColumn.Width = new DataGridLength(duration, DataGridLengthUnitType.Pixel);
        ConceptChapterWarningColumn.Width = new DataGridLength(warning, DataGridLengthUnitType.Pixel);
    }

    private void ConceptChapterGrid_Loaded(object sender, RoutedEventArgs e)
    {
        AttachChapterGridScrollViewer();
        _lastChapterAutoFitAvailableWidth = ConceptChapterGrid.ActualWidth;
        ApplyChapterColumnLayoutForCurrentProject();
        if (_chapterGridColumnLayoutState.AutoFitPending)
            ScheduleChapterAutoFit(force: true);
    }

    private void ConceptChapterGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var width = e.NewSize.Width;
        if (width <= 0 || (!double.IsNaN(_lastChapterAutoFitAvailableWidth) && Math.Abs(width - _lastChapterAutoFitAvailableWidth) < ChapterAutoFitResizeThreshold))
            return;
        _lastChapterAutoFitAvailableWidth = width;
        ScheduleChapterAutoFit(force: false);
    }

    private void ConceptChapterGrid_Unloaded(object sender, RoutedEventArgs e)
    {
        DetachChapterGridScrollViewer();
    }

    private void AttachChapterGridScrollViewer()
    {
        DetachChapterGridScrollViewer();
        ConceptChapterGrid.ApplyTemplate();
        var scrollViewer = FindVisualChild<ScrollViewer>(
            ConceptChapterGrid,
            candidate => ReferenceEquals(candidate.TemplatedParent, ConceptChapterGrid));
        if (scrollViewer is null)
            return;

        _chapterGridScrollViewer = scrollViewer;
        _chapterGridScrollViewer.ScrollChanged += ConceptChapterGridScrollViewer_ScrollChanged;
        UpdateConceptChapterVerticalScrollBar();
        UpdateConceptChapterHorizontalScrollBar();
    }

    private void DetachChapterGridScrollViewer()
    {
        if (_chapterGridScrollViewer is not null)
            _chapterGridScrollViewer.ScrollChanged -= ConceptChapterGridScrollViewer_ScrollChanged;

        _chapterGridScrollViewer = null;
        if (ConceptChapterVerticalScrollBar is not null)
        {
            _isSyncingChapterVerticalScrollBar = true;
            try
            {
                ConceptChapterVerticalScrollBar.Value = 0;
                ConceptChapterVerticalScrollBar.Visibility = Visibility.Collapsed;
            }
            finally
            {
                _isSyncingChapterVerticalScrollBar = false;
            }
        }

        if (ConceptChapterHorizontalScrollBar is not null)
        {
            _isSyncingChapterHorizontalScrollBar = true;
            try
            {
                ConceptChapterHorizontalScrollBar.Value = 0;
                ConceptChapterHorizontalScrollBar.Visibility = Visibility.Collapsed;
            }
            finally
            {
                _isSyncingChapterHorizontalScrollBar = false;
            }
        }
    }

    private void ConceptChapterGridScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        UpdateConceptChapterVerticalScrollBar();
        UpdateConceptChapterHorizontalScrollBar();
    }

    private void UpdateConceptChapterVerticalScrollBar()
    {
        if (_chapterGridScrollViewer is null || ConceptChapterVerticalScrollBar is null)
            return;

        _isSyncingChapterVerticalScrollBar = true;
        try
        {
            var maximum = Math.Max(0, _chapterGridScrollViewer.ScrollableHeight);
            var viewport = Math.Max(0, _chapterGridScrollViewer.ViewportHeight);
            ConceptChapterVerticalScrollBar.Minimum = 0;
            ConceptChapterVerticalScrollBar.Maximum = maximum;
            ConceptChapterVerticalScrollBar.ViewportSize = viewport;
            ConceptChapterVerticalScrollBar.LargeChange = viewport > 0 ? viewport : 1;
            ConceptChapterVerticalScrollBar.Value = Math.Clamp(_chapterGridScrollViewer.VerticalOffset, 0, maximum);
            ConceptChapterVerticalScrollBar.Visibility = maximum > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        finally
        {
            _isSyncingChapterVerticalScrollBar = false;
        }
    }

    private void ConceptChapterVerticalScrollBar_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isSyncingChapterVerticalScrollBar || _chapterGridScrollViewer is null)
            return;

        _chapterGridScrollViewer.ScrollToVerticalOffset(e.NewValue);
    }

    private void UpdateConceptChapterHorizontalScrollBar()
    {
        if (_chapterGridScrollViewer is null || ConceptChapterHorizontalScrollBar is null)
            return;

        _isSyncingChapterHorizontalScrollBar = true;
        try
        {
            var maximum = Math.Max(0, _chapterGridScrollViewer.ScrollableWidth);
            var viewport = Math.Max(0, _chapterGridScrollViewer.ViewportWidth);
            ConceptChapterHorizontalScrollBar.Minimum = 0;
            ConceptChapterHorizontalScrollBar.Maximum = maximum;
            ConceptChapterHorizontalScrollBar.ViewportSize = viewport;
            ConceptChapterHorizontalScrollBar.LargeChange = viewport > 0 ? viewport : 1;
            ConceptChapterHorizontalScrollBar.Value = Math.Clamp(_chapterGridScrollViewer.HorizontalOffset, 0, maximum);
            ConceptChapterHorizontalScrollBar.Visibility = maximum > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        finally
        {
            _isSyncingChapterHorizontalScrollBar = false;
        }
    }

    private void ConceptChapterHorizontalScrollBar_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isSyncingChapterHorizontalScrollBar || _chapterGridScrollViewer is null)
            return;

        _chapterGridScrollViewer.ScrollToHorizontalOffset(e.NewValue);
    }

    private void RebuildChapterPresentationRows()
    {
        _chapterPresentationRows.Clear();
        foreach (var track in _chapterTracks)
        {
            _chapterPresentationRows.Add(new MetadataChapterPresentationRow(track));
            if (!track.IsChapterDetailsExpanded || !track.HasEmbeddedChapterDetails)
                continue;

            foreach (var chapter in track.EmbeddedChapterRows)
                _chapterPresentationRows.Add(new MetadataChapterPresentationRow(track, chapter));
        }
    }

    private void ConceptChapterDetailsToggle_Click(object sender, RoutedEventArgs e)
    {
        _refreshChapterTimelinePresentation();
        RefreshChapterPresentationView();
        e.Handled = true;
    }

    public void RefreshForExternalChanges()
    {
        ResetCandidateSessionIfProjectChanged();
        _refreshUi();
        RefreshCanonicalSpecialFieldProjections();
        RefreshConceptFieldStates();
        RefreshChapterSettingsEditorFromSharedState();
        RefreshCoverWarningThresholdFromSharedState();
        RefreshChapterPresentationView();
        _ = RefreshMetadataCandidatesAsync();
    }

    public void NotifyProjectChanged()
    {
        Interlocked.Increment(ref _candidateLoadVersion);
        _candidateProjectKey = null;
        _candidateSession.Reset();
        ClearCandidateCollectionsAndDrafts();
        _refreshUi();
        RefreshCanonicalSpecialFieldProjections();
        RefreshConceptFieldStates();
        RefreshChapterSettingsEditorFromSharedState();
        RefreshCoverWarningThresholdFromSharedState();
        RefreshChapterPresentationView();
    }

    private void SharedGenreOptionsChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(Genres));
        QueueMetadataCandidateRefresh();
    }

    private void OpenSettings_Click(object sender, RoutedEventArgs e)
    {
        _openSettings(this);
        OnPropertyChanged(nameof(Genres));
        QueueMetadataCandidateRefresh();
        RefreshChapterSettingsEditorFromSharedState();
        RefreshCoverWarningThresholdFromSharedState();
        OnPropertyChanged(nameof(CoverPreviewSource));
        e.Handled = true;
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        // Draft warnings run a nested dispatcher while WPF is already closing.
        if (_closeRequested)
            return;

        _closeRequested = true;
        try
        {
            Close();
        }
        finally
        {
            _closeRequested = false;
        }
    }

    private string GetFieldLabel(ProjectMetadataField field) =>
        ConceptMetadataFieldCatalog.Get(field).GetDisplayName(ShowTechnicalFieldNames);

    private string GetSpecialFieldLabel(string id) =>
        ConceptMetadataFieldCatalog.Get(id).GetDisplayName(ShowTechnicalFieldNames);

    private void InitializeConceptFieldStates()
    {
        ConceptMetadataFieldViewModel CreateState(
            ProjectMetadataField field,
            ConceptMetadataFieldDescriptor descriptor)
        {
            var vm = new ConceptMetadataFieldViewModel(
                () => _metadataState.CanonicalMetadata,
                _metadataPreferences,
                _normalization,
                field,
                descriptor,
                metadata =>
                {
                    _saveMetadata(metadata);
                    if (field == ProjectMetadataField.Description)
                        OnPropertyChanged(nameof(DescriptionOriginLabel));
                },
                preferences => _metadataPreferencesChanged(preferences),
                descriptor.IsMultiline,
                descriptor.IsReadOnly,
                candidateRefreshRequested: field == ProjectMetadataField.Description
                    ? null
                    : _ => QueueMetadataCandidateRefresh(),
                candidateValueCommitted: field == ProjectMetadataField.Description
                    ? null
                    : (committedField, value) =>
                    {
                        _candidateSession.RetainUserValue(committedField, value);
                        QueueMetadataCandidateRefresh();
                    });
            _conceptFieldStates.Add(vm);
            return vm;
        }

        ConceptMetadataFieldViewModel AddToGroup(
            ObservableCollection<ConceptMetadataFieldViewModel> group,
            ConceptMetadataFieldDefinition definition)
        {
            var vm = CreateState(
                definition.Field,
                definition.Descriptor);
            group.Add(vm);
            return vm;
        }

        _quickAccessFieldComposition = new ConceptMetadataFieldComposition(
            () => _metadataState.CanonicalMetadata,
            _metadataPreferences,
            _normalization,
            metadata => _saveMetadata(metadata),
            preferences => _metadataPreferencesChanged(preferences),
            candidateRefreshRequested: _ => QueueMetadataCandidateRefresh(),
            candidateValueCommitted: (field, value) =>
            {
                _candidateSession.RetainUserValue(field, value);
                QueueMetadataCandidateRefresh();
            },
            titleAlbumBindingChanged: QueueMetadataCandidateRefresh);
        TitleField = _quickAccessFieldComposition.Title;
        AlbumField = _quickAccessFieldComposition.Album;
        AuthorsField = _quickAccessFieldComposition.Authors;
        NarratorsField = _quickAccessFieldComposition.Narrators;
        GenresField = _quickAccessFieldComposition.Genres;
        _conceptFieldStates.AddRange(_quickAccessFieldComposition.Fields);
        TitleAlbumBinding = _quickAccessFieldComposition.TitleAlbumBinding;
        _conceptFieldBindings.Add(TitleAlbumBinding);

        SubtitleField = CreateState(ProjectMetadataField.Subtitle, ConceptMetadataFieldCatalog.Get(ProjectMetadataField.Subtitle));
        VolumeField = CreateState(ProjectMetadataField.Volume, ConceptMetadataFieldCatalog.Get(ProjectMetadataField.Volume));
        EditionField = CreateState(ProjectMetadataField.Edition, ConceptMetadataFieldCatalog.Get(ProjectMetadataField.Edition));
        PublisherField = CreateState(ProjectMetadataField.Publisher, ConceptMetadataFieldCatalog.Get(ProjectMetadataField.Publisher));
        LicenseField = CreateState(ProjectMetadataField.License, ConceptMetadataFieldCatalog.Get(ProjectMetadataField.License));
        ComposersField = CreateState(ProjectMetadataField.Composers, ConceptMetadataFieldCatalog.Get(ProjectMetadataField.Composers));
        TranslatorsField = CreateState(ProjectMetadataField.Translators, ConceptMetadataFieldCatalog.Get(ProjectMetadataField.Translators));

        foreach (var definition in ConceptMetadataGroupCatalog.PublicationAdditional)
            AddToGroup(PublicationAdditionalFields, definition);
        foreach (var definition in ConceptMetadataGroupCatalog.Language)
            AddToGroup(LanguageFields, definition);
        foreach (var definition in ConceptMetadataGroupCatalog.CatalogData)
            AddToGroup(CatalogDataFields, definition);
        foreach (var definition in ConceptMetadataGroupCatalog.Sorting)
            AddToGroup(SortingFields, definition);
        foreach (var definition in ConceptMetadataGroupCatalog.DescriptionAdditional)
            AddToGroup(DescriptionAdditionalFields, definition);
        foreach (var definition in ConceptMetadataGroupCatalog.Technical)
            AddToGroup(TechnicalFields, definition);
        foreach (var definition in ConceptMetadataGroupCatalog.PassThrough)
            AddToGroup(PassThroughFields, definition);

        DescriptionField = CreateState(
            ConceptMetadataGroupCatalog.Description.Field,
            ConceptMetadataGroupCatalog.Description.Descriptor);
        _isDescriptionIncluded = DescriptionField.IsOutputEnabled;
        SeriesFieldState = new ConceptSeriesFieldState(
            _metadataState,
            _metadataPreferences,
            _normalization,
            _saveMetadata,
            _metadataPreferencesChanged,
            QueueMetadataCandidateRefresh);
        DescriptionFieldState = new ConceptDescriptionFieldState(
            _metadataState,
            DescriptionField,
            _saveMetadata);
    }

    private void BindStateManagedConceptPlacementViews()
    {
        ConceptQuickAccessTitleField.DataContext = TitleField;
        ConceptWorkOutputTitleField.DataContext = TitleField;
        ConfigureBindingFieldView(
            ConceptQuickAccessAlbumField,
            AlbumField,
            TitleAlbumBinding);
        ConfigureBindingFieldView(
            ConceptWorkOutputAlbumField,
            AlbumField,
            TitleAlbumBinding);
        ConceptWorkSubtitleField.DataContext = SubtitleField;
        ConceptWorkVolumeField.DataContext = VolumeField;
        ConceptPublicationEditionField.DataContext = EditionField;
        ConceptPublicationPublisherField.DataContext = PublisherField;
        ConceptPublicationLicenseField.DataContext = LicenseField;
        ConceptQuickAccessAuthorField.DataContext = AuthorsField;
        ConceptPeopleAuthorField.DataContext = AuthorsField;
        ConceptQuickAccessSpeakerField.DataContext = NarratorsField;
        ConceptPeopleSpeakerField.DataContext = NarratorsField;
        ConfigureBindingFieldView(
            ConceptPeopleComposerField,
            ComposersField,
            NarratorComposerBinding);
        ConfigureBindingFieldView(
            ConceptCompatibilityComposerField,
            ComposersField,
            NarratorComposerBinding);
        ConceptPeopleTranslatorField.DataContext = TranslatorsField;
        ConceptHelpDemoField.DataContext = HelpDemoField;
        ConceptQuickAccessGenreField.DataContext = GenresField;
        ConceptCatalogGenreField.DataContext = GenresField;
        ConceptQuickAccessIsbnField.DataContext = CatalogDataFields.Single(field => field.Field == ProjectMetadataField.ISBN);
        ConceptQuickAccessSeriesField.DataContext = SeriesFieldState;
        ConceptWorkSeriesField.DataContext = SeriesFieldState;
        ConceptQuickAccessDescriptionField.DataContext = DescriptionFieldState;
        ConceptDescriptionFullField.DataContext = DescriptionFieldState;
    }

    private ConceptMetadataFieldBindingState CreateConceptFieldBinding(
        ProjectMetadataFieldBindingDefinition definition,
        ConceptMetadataFieldViewModel sourceField,
        ConceptMetadataFieldViewModel targetField,
        Action? bindingChanged = null)
    {
        var binding = new ConceptMetadataFieldBindingState(
            definition,
            sourceField,
            targetField,
            () => MetadataFieldBindingPreferencesService.GetIsEnabled(_metadataPreferences, definition),
            value =>
            {
                if (MetadataFieldBindingPreferencesService.SetIsEnabled(_metadataPreferences, definition, value))
                    _metadataPreferencesChanged(_metadataPreferences);
            },
            () =>
            {
                bindingChanged?.Invoke();
                QueueMetadataCandidateRefresh();
            });
        _conceptFieldBindings.Add(binding);
        return binding;
    }

    private static void ConfigureBindingFieldView(
        Controls.ConceptMetadataFieldControl control,
        ConceptMetadataFieldViewModel targetField,
        ConceptMetadataFieldBindingState bindingState) =>
        Controls.ConceptMetadataFieldBindingAccessory.Configure(control, targetField, bindingState);

    private void SynchronizeActiveBindings()
    {
        foreach (var binding in _conceptFieldBindings)
            binding.SynchronizeActiveBinding();

        OnPropertyChanged(nameof(UseNarratorsAsComposers));
    }

    private void InitializeHelpDemoField()
    {
        var descriptor = new ConceptMetadataFieldDescriptor(
            "help-demo-topics",
            ProjectMetadataField.Genres,
            "MetadataHelpDemoFieldLabel",
            "Help Demo Topics",
            ConceptFieldLockKind.GenrePreference,
            HideEditorWhenLocked: true,
            LockControlsOutputPolicy: false);

        HelpDemoField = new ConceptMetadataFieldViewModel(
            () => _helpDemoMetadata,
            _helpDemoPreferences,
            _normalization,
            ProjectMetadataField.Genres,
            descriptor,
            _ => { },
            _ => { },
            candidateValueCommitted: (_, value) =>
            {
                var key = _normalization.NormalizeForComparison(ProjectMetadataField.Genres, value);
                if (!HelpDemoField.Candidates.Any(candidate => string.Equals(
                        _normalization.NormalizeForComparison(ProjectMetadataField.Genres, candidate.DisplayValue),
                        key,
                        StringComparison.Ordinal)))
                {
                    HelpDemoField.Candidates.Add(new ConceptMetadataCandidate(
                        ProjectMetadataField.Genres, value, value, 0, MetadataOrigin.User));
                }
            });

        ResetHelpDemo();
    }

    private void ResetHelpDemo()
    {
        _helpDemoPreferences.OutputPolicies.Clear();
        _helpDemoPreferences.PreferredGenres.Clear();
        _helpDemoPreferences.GenrePreferenceLocked = false;

        _helpDemoMetadata.SetValue<IReadOnlyList<string>>(
            ProjectMetadataField.Genres,
            [
                ResolveHelpText("MetadataHelpDemoStartAdventure"),
                ResolveHelpText("MetadataHelpDemoStartSea"),
                ResolveHelpText("MetadataHelpDemoStartTravel")
            ],
            MetadataOrigin.User);

        HelpDemoField.Draft = string.Empty;
        HelpDemoField.Refresh();
        HelpDemoField.SetCandidates(
        [
            new ConceptMetadataCandidate(ProjectMetadataField.Genres, ResolveHelpText("MetadataHelpDemoStartAdventure"), ResolveHelpText("MetadataHelpDemoStartAdventure"), 10, MetadataOrigin.SourceAggregation, IsCanonicalWinner: true, IsAutomaticWinner: true),
            new ConceptMetadataCandidate(ProjectMetadataField.Genres, ResolveHelpText("MetadataHelpDemoCandidateContinents"), ResolveHelpText("MetadataHelpDemoCandidateContinents"), 4, MetadataOrigin.SourceAggregation),
            new ConceptMetadataCandidate(ProjectMetadataField.Genres, ResolveHelpText("MetadataHelpDemoStartSea"), ResolveHelpText("MetadataHelpDemoStartSea"), 0, MetadataOrigin.User),
            new ConceptMetadataCandidate(ProjectMetadataField.Genres, ResolveHelpText("MetadataHelpDemoStartTravel"), ResolveHelpText("MetadataHelpDemoStartTravel"), 0, MetadataOrigin.User)
        ]);
    }

    private string ResolveHelpText(string resourceKey) =>
        TryFindResource(resourceKey) as string
        ?? Application.Current?.TryFindResource(resourceKey) as string
        ?? throw new InvalidOperationException($"Help resource '{resourceKey}' was not found or is not text.");

    private void RefreshConceptFieldStates()
    {
        foreach (var field in _conceptFieldStates)
            field.Refresh();

        DescriptionFieldState.RefreshCanonical();
        OnPropertyChanged(nameof(DescriptionText));
        OnPropertyChanged(nameof(DescriptionOriginLabel));
        OnPropertyChanged(nameof(IsDescriptionIncluded));
        OnPropertyChanged(nameof(CompatibilityFieldText));
        OnPropertyChanged(nameof(IsCompatibilityDescriptionEnabled));
        SynchronizeActiveBindings();
    }

    private IReadOnlyList<StateManagedConceptFieldCandidateSet> BuildStateManagedConceptFieldCandidates(
        IReadOnlyList<SourceMetadataSnapshot> snapshots,
        IReadOnlyList<MetadataFieldDecisionDiagnostic> decisions)
    {
        var result = new List<StateManagedConceptFieldCandidateSet>();

        foreach (var field in _conceptFieldStates)
        {
            if (!IsStateManagedCandidateField(field.Field))
                continue;

            var candidates = ConceptMetadataFieldCandidateComposer.Build(
                field,
                _metadataState,
                snapshots,
                decisions,
                Genres,
                _candidateSession,
                _candidateService,
                _conceptFieldBindings,
                _candidateLoadCancellation.Token);
            result.Add(new StateManagedConceptFieldCandidateSet(field, candidates));
        }

        return result;
    }

    private sealed record StateManagedConceptFieldCandidateSet(
        ConceptMetadataFieldViewModel Field,
        IReadOnlyList<ConceptMetadataCandidate> Candidates);

    private bool IsStateManagedCandidateField(ProjectMetadataField field)
    {
        var state = _conceptFieldStates.FirstOrDefault(item => item.Field == field);
        return state is not null &&
               field != ProjectMetadataField.Description &&
               !state.IsReadOnly &&
               state.ValueKind is ProjectMetadataFieldValueKind.String or ProjectMetadataFieldValueKind.StringList;
    }


    private void RefreshCanonicalSpecialFieldProjections()
    {
        RefreshCanonicalWorkFields();
        RefreshCanonicalPublicationFields();
        RefreshCanonicalTrackFields();
        RefreshCanonicalSeries();
    }

    private void RefreshCanonicalWorkFields()
    {
        WorkTypeFieldState.RefreshCanonical();
        AbridgedStatusFieldState.RefreshCanonical();
        OnPropertyChanged(nameof(WorkTypeOriginLabel));
        OnPropertyChanged(nameof(AbridgedStatusOriginLabel));
    }


    private void RefreshCanonicalPublicationFields()
    {
        SyncSingleValueProjection(
            ReleaseDateValues,
            _metadataState.CanonicalMetadata.GetValue<MetadataDate?>(ProjectMetadataField.ReleaseDate).Value?.ToString());
        YearFieldState.RefreshCanonical();
        OnPropertyChanged(nameof(ReleaseDateOriginLabel));
        OnPropertyChanged(nameof(YearOriginLabel));
    }


    private void RefreshCanonicalTrackFields()
    {
        SyncSingleValueProjection(
            TrackNumberValues,
            _metadataState.CanonicalMetadata.GetValue<int?>(ProjectMetadataField.OutputTrackNumber).Value?.ToString(CultureInfo.InvariantCulture));
        SyncSingleValueProjection(
            TrackCountValues,
            _metadataState.CanonicalMetadata.GetValue<int?>(ProjectMetadataField.OutputTrackTotal).Value?.ToString(CultureInfo.InvariantCulture));
    }

    private static void SyncEnumProjection(
        ObservableCollection<ConceptSingleValue> target,
        AudioWorkType? value)
    {
        SyncSingleValueProjection(
            target,
            value is AudioWorkType workType && workType != AudioWorkType.Unknown
                ? MetadataDisplayLabels.AudioWorkType(workType)
                : string.Empty);
    }

    private static void SyncEnumProjection(
        ObservableCollection<ConceptSingleValue> target,
        AbridgedStatus? value)
    {
        SyncSingleValueProjection(
            target,
            value is AbridgedStatus status && status != AbridgedStatus.Unknown
                ? MetadataDisplayLabels.AbridgedStatus(status)
                : string.Empty);
    }

    private void RefreshCanonicalSeries() => SeriesFieldState.RefreshCanonical();

    private void QueueMetadataCandidateRefresh()
    {
        if (_candidateRefreshQueued || _candidateLoadCancellation.IsCancellationRequested)
            return;

        _candidateRefreshQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            _candidateRefreshQueued = false;
            _ = RefreshMetadataCandidatesAsync();
        }));
    }

    private async Task RefreshMetadataCandidatesAsync()
    {
        try
        {
            var projectKey = NormalizeCandidateProjectKey(_projectFolderProvider());
            ResetCandidateSessionIfProjectChanged(projectKey);
            var version = Interlocked.Increment(ref _candidateLoadVersion);

            var liveSnapshots = _sourceSnapshotsProvider();
            IReadOnlyList<SourceMetadataSnapshot> snapshots;
            if (liveSnapshots.Count > 0)
            {
                snapshots = liveSnapshots;
            }
            else
            {
                if (string.IsNullOrWhiteSpace(projectKey) || !Directory.Exists(projectKey))
                {
                    snapshots = [];
                }
                else
                {
                    snapshots = await Task.Run(
                        () => new SourceMetadataSnapshotStore().LoadAll(Path.GetFullPath(projectKey)),
                        _candidateLoadCancellation.Token);
                }
            }

            if (_candidateLoadCancellation.IsCancellationRequested || version != _candidateLoadVersion)
                return;

            var decisions = _metadataFieldDecisionsProvider();
            var allCandidates = _candidateService.Build(snapshots, _candidateLoadCancellation.Token);
            var stateManagedCandidateSets = BuildStateManagedConceptFieldCandidates(snapshots, decisions);
            var releaseDateSourceCandidates = allCandidates.TryGetValue(ProjectMetadataField.ReleaseDate, out var releaseDateSource)
                ? releaseDateSource.OfType<ScalarSourceMetadataAlternative>().ToArray()
                : [];
            var releaseDateCandidates = _candidateSession.PresentValueScalar(
                ProjectMetadataField.ReleaseDate,
                releaseDateSourceCandidates,
                _metadataState.CanonicalMetadata.GetValue<MetadataDate?>(ProjectMetadataField.ReleaseDate));
            var workTypeCandidates = BuildEnumCandidates(
                ProjectMetadataField.AudioWorkType,
                allCandidates,
                ConceptEnumCandidateOrder.AudioWorkTypes);
            var abridgedStatusCandidates = BuildEnumCandidates(
                ProjectMetadataField.AbridgedStatus,
                allCandidates,
                ConceptEnumCandidateOrder.AbridgedStatuses);
            var seriesCandidates = SeriesFieldState.BuildCandidates(allCandidates);
            if (_candidateLoadCancellation.IsCancellationRequested || version != _candidateLoadVersion)
                return;

            ApplyCandidateCollections(
                workTypeCandidates,
                abridgedStatusCandidates,
                releaseDateCandidates,
                stateManagedCandidateSets,
                seriesCandidates);
        }
        catch (OperationCanceledException)
        {
        }
        catch (IOException)
        {
            // Candidate discovery is auxiliary. The canonical editor remains usable.
        }
        catch (UnauthorizedAccessException)
        {
            // Candidate discovery is auxiliary. The canonical editor remains usable.
        }
    }

    private IReadOnlyList<ConceptMetadataCandidate> BuildEnumCandidates<TEnum>(
        ProjectMetadataField field,
        IReadOnlyDictionary<ProjectMetadataField, IReadOnlyList<SourceMetadataAlternative>> sourceCandidates,
        IEnumerable<TEnum> enumValues)
        where TEnum : struct, Enum
    {
        return field switch
        {
            ProjectMetadataField.AudioWorkType => WorkTypeFieldState.BuildCandidates(sourceCandidates),
            ProjectMetadataField.AbridgedStatus => AbridgedStatusFieldState.BuildCandidates(sourceCandidates),
            _ => throw new ArgumentOutOfRangeException(nameof(field), field, "Unsupported enum candidate field.")
        };
    }

    private void ResetCandidateSessionIfProjectChanged()
    {
        ResetCandidateSessionIfProjectChanged(NormalizeCandidateProjectKey(_projectFolderProvider()));
    }

    private void ResetCandidateSessionIfProjectChanged(string? projectKey)
    {
        if (string.Equals(_candidateProjectKey, projectKey, StringComparison.OrdinalIgnoreCase))
            return;

        Interlocked.Increment(ref _candidateLoadVersion);
        _candidateProjectKey = projectKey;
        _candidateSession.Reset();
        ClearCandidateCollectionsAndDrafts();
    }

    private static string? NormalizeCandidateProjectKey(string? projectKey)
    {
        if (string.IsNullOrWhiteSpace(projectKey))
            return null;

        try
        {
            return Path.GetFullPath(projectKey);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return projectKey.Trim();
        }
    }

    private void ClearCandidateCollectionsAndDrafts()
    {
        WorkTypeCandidates.Clear();
        AbridgedStatusCandidates.Clear();
        SeriesFieldState.ClearCandidates();
        ReleaseDateCandidates.Clear();
        foreach (var field in _conceptFieldStates)
        {
            field.Candidates.Clear();
            field.DiscardDraft();
        }

        SeriesFieldState.DiscardDraft();
        SpecialDrafts.Clear();

        foreach (var comboBox in new[]
        {
            ConceptWorkTypeComboBox,
            ConceptWorkWorkTypeComboBox,
            ConceptAbridgedStatusComboBox,
            ConceptWorkAbridgedStatusComboBox,
            ConceptPublicationReleaseDateComboBox
        })
        {
            comboBox.SelectedItem = null;
            comboBox.Text = string.Empty;
        }
    }


    private void ApplyCandidateCollections(
        IReadOnlyList<ConceptMetadataCandidate> workTypeCandidates,
        IReadOnlyList<ConceptMetadataCandidate> abridgedStatusCandidates,
        IReadOnlyList<ConceptMetadataCandidate> releaseDateCandidates,
        IReadOnlyList<StateManagedConceptFieldCandidateSet> stateManagedCandidateSets,
        IReadOnlyList<SeriesSourceMetadataAlternative> seriesCandidates)
    {
        var drafts = CaptureCandidateDrafts();
        var workTypeDraft = SpecialDrafts.WorkType;
        var abridgedStatusDraft = SpecialDrafts.AbridgedStatus;
        ReplaceCandidates(WorkTypeCandidates, workTypeCandidates);
        ReplaceCandidates(AbridgedStatusCandidates, abridgedStatusCandidates);
        ReplaceCandidates(ReleaseDateCandidates, releaseDateCandidates);
        foreach (var candidateSet in stateManagedCandidateSets)
            candidateSet.Field.SetCandidates(candidateSet.Candidates);
        SeriesFieldState.SetCandidates(seriesCandidates);
        RestoreCandidateDrafts(drafts);
        SpecialDrafts.WorkType = workTypeDraft;
        SpecialDrafts.AbridgedStatus = abridgedStatusDraft;
    }


    private IReadOnlyList<CandidateEditorDraftSnapshot> CaptureCandidateDrafts()
    {
        return EnumerateCandidateDraftComboBoxes()
            .Select(CaptureCandidateDraft)
            .ToArray();
    }

    private IEnumerable<ComboBox> EnumerateCandidateDraftComboBoxes()
    {
        return EnumerateVisualDescendants<ComboBox>(this)
            .Where(comboBox => comboBox.IsEditable && !IsSeriesCandidateComboBox(comboBox));
    }

    private CandidateEditorDraftSnapshot CaptureCandidateDraft(ComboBox comboBox)
    {
        if (FindVisualAncestor<Controls.ConceptMetadataFieldControl>(comboBox) is { } genericControl)
        {
            return new CandidateEditorDraftSnapshot(
                comboBox,
                genericControl.CaptureCandidateDraftState(),
                null);
        }

        comboBox.ApplyTemplate();
        var editableTextBox = comboBox.Template.FindName("PART_EditableTextBox", comboBox) as TextBox;
        var text = editableTextBox?.Text ?? comboBox.Text ?? string.Empty;
        var selectionStart = editableTextBox?.SelectionStart ?? text.Length;
        var selectionLength = editableTextBox?.SelectionLength ?? 0;
        var caretIndex = editableTextBox?.CaretIndex ?? selectionStart;
        var selectedCandidate = comboBox.SelectedItem as ConceptMetadataCandidate;

        return new CandidateEditorDraftSnapshot(
            comboBox,
            null,
            new StandardCandidateEditorDraftState(
                text,
                selectionStart,
                selectionLength,
                caretIndex,
                comboBox.IsKeyboardFocusWithin,
                selectedCandidate,
                CandidateComboBoxInteraction.IsAutomaticSelection(comboBox)));
    }

    private void RestoreCandidateDrafts(IReadOnlyList<CandidateEditorDraftSnapshot> drafts)
    {
        foreach (var draft in drafts)
        {
            using var interactionSuppression =
                CandidateComboBoxInteraction.SuppressSelectionFinalization(draft.ComboBox);

            if (draft.GenericState is not null &&
                FindVisualAncestor<Controls.ConceptMetadataFieldControl>(draft.ComboBox) is { } genericControl)
            {
                genericControl.RestoreCandidateDraftState(draft.GenericState);
                continue;
            }

            if (draft.StandardState is not { } standard)
                continue;

            draft.ComboBox.ApplyTemplate();
            var editableTextBox = draft.ComboBox.Template.FindName("PART_EditableTextBox", draft.ComboBox) as TextBox;
            var matchingCandidate = standard.SelectedCandidate is null
                ? null
                : draft.ComboBox.Items
                    .OfType<ConceptMetadataCandidate>()
                    .FirstOrDefault(candidate => CandidateIdentityEquals(candidate, standard.SelectedCandidate));

            draft.ComboBox.SelectedItem = matchingCandidate;
            CandidateComboBoxInteraction.RestoreEditorState(
                draft.ComboBox,
                standard.Text,
                standard.SelectionStart,
                standard.SelectionLength,
                standard.CaretIndex,
                standard.WasAutomaticSelection);

            if (standard.HadKeyboardFocus && editableTextBox is not null)
                editableTextBox.Focus();
        }
    }

    private bool CandidateIdentityEquals(
        ConceptMetadataCandidate left,
        ConceptMetadataCandidate right)
    {
        if (left.Field != right.Field)
            return false;

        if (left.ValueToCommit is string leftText && right.ValueToCommit is string rightText)
        {
            return string.Equals(
                _normalization.NormalizeForComparison(left.Field, leftText),
                _normalization.NormalizeForComparison(right.Field, rightText),
                StringComparison.Ordinal);
        }

        return Equals(left.ValueToCommit, right.ValueToCommit);
    }

    private static IEnumerable<T> EnumerateVisualDescendants<T>(DependencyObject parent)
        where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match)
                yield return match;

            foreach (var nested in EnumerateVisualDescendants<T>(child))
                yield return nested;
        }
    }

    private sealed record CandidateEditorDraftSnapshot(
        ComboBox ComboBox,
        Controls.ConceptMetadataEditorDraftState? GenericState,
        StandardCandidateEditorDraftState? StandardState);

    private sealed record StandardCandidateEditorDraftState(
        string Text,
        int SelectionStart,
        int SelectionLength,
        int CaretIndex,
        bool HadKeyboardFocus,
        ConceptMetadataCandidate? SelectedCandidate,
        bool WasAutomaticSelection);

    private static void ReplaceCandidates<T>(
        ObservableCollection<T> target,
        IReadOnlyList<T> values)
    {
        target.Clear();
        foreach (var value in values)
            target.Add(value);
    }


    private static void SyncSingleValueProjection(
        ObservableCollection<ConceptSingleValue> target,
        string? value)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (target.Count == 1 &&
            string.Equals(target[0].Value, normalized, StringComparison.Ordinal))
        {
            return;
        }

        if (target.Count == 0 && normalized.Length == 0)
            return;

        target.Clear();
        if (normalized.Length > 0)
            target.Add(new ConceptSingleValue(normalized));
    }

    private void CommitCanonicalScalar(string? fieldKind, string? value, MetadataOrigin origin)
    {
        if (!string.Equals(fieldKind, "ReleaseDate", StringComparison.Ordinal))
            return;

        if (!MetadataDateParser.TryParse(value, out var date))
        {
            if (string.IsNullOrWhiteSpace(value))
                _metadataState.CanonicalMetadata.SetValue<MetadataDate?>(ProjectMetadataField.ReleaseDate, null, MetadataOrigin.None);
            else
                return;
        }
        else
        {
            _metadataState.CanonicalMetadata.SetValue<MetadataDate?>(ProjectMetadataField.ReleaseDate, date, origin);
        }

        _saveMetadata(_metadataState.CanonicalMetadata);
        QueueMetadataCandidateRefresh();
    }


    private int? ReadCanonicalInteger(
        ProjectMetadataField field,
        ObservableCollection<ConceptSingleValue> projection)
    {
        var canonical = _metadataState.CanonicalMetadata.GetValue<int?>(field).Value;
        if (canonical.HasValue)
            return canonical;

        if (projection.Count == 1 &&
            int.TryParse(projection[0].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) &&
            parsed >= 0)
        {
            return parsed;
        }

        return null;
    }

    private void CommitCanonicalInteger(
        ProjectMetadataField field,
        int? value,
        MetadataOrigin origin)
    {
        if (field == ProjectMetadataField.Year)
        {
            YearFieldState.CommitValue(value, origin);
            return;
        }

        _metadataState.CanonicalMetadata.SetValue<int?>(field, value, origin);

        if (field == ProjectMetadataField.OutputTrackNumber && IsTrackNumberLocked)
        {
            _metadataPreferences.PreferredOutputTrackNumber = value;
            _metadataPreferences.OutputTrackNumberPreferenceLocked = value.HasValue;
            _metadataPreferencesChanged(_metadataPreferences);
        }
        else if (field == ProjectMetadataField.OutputTrackTotal && IsTrackCountLocked)
        {
            _metadataPreferences.PreferredOutputTrackTotal = value;
            _metadataPreferences.OutputTrackTotalPreferenceLocked = value.HasValue;
            _metadataPreferencesChanged(_metadataPreferences);
        }

        _saveMetadata(_metadataState.CanonicalMetadata);
    }

    private void CommitCanonicalEnum(string? fieldKind, object? value, MetadataOrigin origin)
    {
        if (fieldKind == "WorkType")
            WorkTypeFieldState.CommitValue(value, origin);
        else if (fieldKind == "AbridgedStatus")
            AbridgedStatusFieldState.CommitValue(value, origin);
    }

    private bool SetPersistentOutputState(
        ref bool storage,
        bool value,
        ProjectMetadataField field,
        string propertyName,
        params Control[] draftEditors)
    {
        if (!SetUiState(ref storage, value, propertyName))
            return false;

        if (!value)
        {
            foreach (var draftEditor in draftEditors)
                ClearDraftEditor(draftEditor);
        }

        if (MetadataPreferencesService.SetPolicy(
                _metadataPreferences,
                field,
                value ? OutputFieldPolicy.Enabled : OutputFieldPolicy.Disabled))
        {
            _metadataPreferencesChanged(_metadataPreferences);
        }

        return true;
    }

    private static string FormatOriginLabel(MetadataOrigin origin) => MetadataOriginEvidenceLabels.FormatOptional(origin);

    private bool SetOutputState(
        ref bool storage,
        bool value,
        string propertyName,
        params Control[] draftEditors)
    {
        if (!SetUiState(ref storage, value, propertyName))
            return false;

        if (!value)
        {
            foreach (var draftEditor in draftEditors)
                ClearDraftEditor(draftEditor);
        }

        return true;
    }

    private static void ClearDraftEditor(Control draftEditor)
    {
        switch (draftEditor)
        {
            case TextBox textBox:
                textBox.Clear();
                textBox.CaretIndex = 0;
                break;

            case ComboBox comboBox:
                comboBox.ApplyTemplate();
                var editableTextBox = comboBox.Template.FindName("PART_EditableTextBox", comboBox) as TextBox;
                ClearEditableComboBox(comboBox, editableTextBox);
                break;
        }
    }

    private bool SetUiState(ref bool storage, bool value, [CallerMemberName] string? propertyName = null)
    {
        if (storage == value)
            return false;

        storage = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_coverPickerService is not null)
            _coverPickerService.PropertyChanged -= CoverPickerService_PropertyChanged;
        base.OnClosed(e);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

internal static class ConceptEnumCandidateOrder
{
    public static IReadOnlyList<AudioWorkType> AudioWorkTypes { get; } =
    [
        AudioWorkType.Audiobook,
        AudioWorkType.AudioDrama,
        AudioWorkType.Unknown
    ];

    public static IReadOnlyList<AbridgedStatus> AbridgedStatuses { get; } =
    [
        AbridgedStatus.Unabridged,
        AbridgedStatus.Abridged,
        AbridgedStatus.Unknown
    ];
}

internal static class ConceptDialogSearchMatcher
{
    public static int? GetRank(
        IEnumerable<string> values,
        string query,
        bool allowOrderedSubsequence = true)
    {
        var normalizedQuery = Normalize(query);
        if (normalizedQuery.Length == 0)
            return string.IsNullOrWhiteSpace(query) ? 0 : null;

        var normalizedValues = (values ?? [])
            .Select(Normalize)
            .Where(value => value.Length > 0)
            .ToArray();

        if (normalizedValues.Any(value => string.Equals(value, normalizedQuery, StringComparison.Ordinal)))
            return 0;

        if (normalizedValues.Any(value => value.StartsWith(normalizedQuery, StringComparison.Ordinal)))
            return 1;

        var queryLength = normalizedQuery.EnumerateRunes().Count(Rune.IsLetterOrDigit);
        if ((queryLength >= 4 || (queryLength >= 2 && IsCjkQuery(normalizedQuery))) &&
            normalizedValues.Any(value => value.Contains(normalizedQuery, StringComparison.Ordinal)))
        {
            return 2;
        }

        if (allowOrderedSubsequence &&
            queryLength >= 5 &&
            normalizedValues.Any(value => IsOrderedSubsequence(normalizedQuery, value)))
        {
            return 3;
        }

        return null;
    }

    public static string Normalize(string value)
    {
        // EnumerateRunes replaces isolated surrogates before Normalize, which rejects invalid UTF-16.
        var validText = string.Concat((value ?? string.Empty).EnumerateRunes().Select(rune => rune.ToString()));
        var result = new StringBuilder(validText.Length);
        var hasBase = false;
        var latinBase = false;
        foreach (var rune in validText.Normalize(NormalizationForm.FormD).EnumerateRunes())
        {
            if (IsMark(rune))
            {
                // Latin accents are optional search input; vowel/voicing marks in other scripts are not.
                if (hasBase && !latinBase)
                    result.Append(rune.ToString());
                continue;
            }

            hasBase = Rune.IsLetterOrDigit(rune);
            latinBase = hasBase && (rune.Value is <= 0x024F or >= 0x1E00 and <= 0x1EFF);
            if (hasBase)
                result.Append(Rune.ToUpperInvariant(rune).ToString());
        }
        return result.ToString();
    }

    private static bool IsMark(Rune rune) => Rune.GetUnicodeCategory(rune) is
        UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark;

    private static bool IsCjkQuery(string query) => query.EnumerateRunes().All(rune =>
        IsMark(rune) || rune.Value is
            >= 0x3040 and <= 0x30FF or // Hiragana and Katakana (including decomposed voicing marks).
            >= 0x31F0 and <= 0x31FF or
            >= 0x3400 and <= 0x4DBF or >= 0x4E00 and <= 0x9FFF or
            >= 0xF900 and <= 0xFAFF or >= 0xFF66 and <= 0xFF9D or
            >= 0x20000 and <= 0x2FA1F or >= 0x30000 and <= 0x323AF);

    private static bool IsOrderedSubsequence(string query, string value)
    {
        var queryRunes = query.EnumerateRunes().ToArray();
        var queryIndex = 0;
        foreach (var character in value.EnumerateRunes())
        {
            if (character != queryRunes[queryIndex])
                continue;

            queryIndex++;
            if (queryIndex == queryRunes.Length)
                return true;
        }

        return false;
    }
}

public sealed class ConceptSingleValue : INotifyPropertyChanged
{
    private bool _isDuplicateHighlighted;
    private bool _isDragging;

    public ConceptSingleValue(string value)
    {
        Value = value ?? string.Empty;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Value { get; }

    internal int DuplicateHighlightVersion { get; set; }

    public bool IsDragging
    {
        get => _isDragging;
        internal set
        {
            if (_isDragging == value)
                return;

            _isDragging = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsDragging)));
        }
    }

    public bool IsDuplicateHighlighted
    {
        get => _isDuplicateHighlighted;
        internal set
        {
            if (_isDuplicateHighlighted == value)
                return;

            _isDuplicateHighlighted = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsDuplicateHighlighted)));
        }
    }
}
