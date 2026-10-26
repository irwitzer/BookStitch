namespace BookStitch.Services;

public sealed class MainWindowFoundationComposition
{
    private MainWindowFoundationComposition(BookStitchEnvironmentContext environmentContext)
    {
        EnvironmentContext = environmentContext;
        SettingsService = new SettingsService(environmentContext);
        GlobalSettingsManager = new GlobalSettingsManager(SettingsService);
        DebugSettingsService = new DebugSettingsService(SettingsService);
        SettingsSelectionService = new MainWindowSettingsSelectionService();
        TrackListStateService = new TrackListStateService();
        FinalTagService = new FinalTagService();
        Mp3DiscProjectService = new Mp3DiscProjectService();
        AudioDiscProjectService = new AudioDiscProjectService();
        WorkManifestService = new WorkManifestService();
        ConvertedFileCleanupService = new ConvertedFileCleanupService();
        ProjectResumePlanService = new ProjectResumePlanService();
        ExportChapterService = new ExportChapterService();
        ChapterAutoEngineService = new ChapterAutoEngineService(
            new ChapterTimelineService(),
            new ChapterAnalysisSnapshotService(),
            new ChapterResolveService(),
            new ChapterTitleRenderer(),
            new SourceMetadataSnapshotStore(),
            new SourceCopyIdentityStore(),
            Mp3DiscProjectService,
            AudioDiscProjectService);
        ChapterMasterEngineService = new ChapterMasterEngineService();
        ChapterModeEngineService = new ChapterModeEngineService(
            ChapterAutoEngineService,
            ChapterMasterEngineService,
            new ChapterOriginalEngineService(),
            new ChapterCustomEngineService());
        ChapterDebugPresentationService = new ChapterDebugPresentationService();
        MetadataDebugPresentationService = new MetadataDebugPresentationService();
        ChapterTimelinePresentationService = new ChapterTimelinePresentationService();
        WorkFolderStructureService = new WorkFolderStructureService();
        ProjectIndexService = new ProjectIndexService();
        SystemSleepPreventionActivityService = new SystemSleepPreventionActivityService();
        SystemSleepPreventionService = new SystemSleepPreventionService();
    }

    public BookStitchEnvironmentContext EnvironmentContext { get; }
    public SettingsService SettingsService { get; }
    public GlobalSettingsManager GlobalSettingsManager { get; }
    public DebugSettingsService DebugSettingsService { get; }
    public MainWindowSettingsSelectionService SettingsSelectionService { get; }
    public TrackListStateService TrackListStateService { get; }
    public FinalTagService FinalTagService { get; }
    public Mp3DiscProjectService Mp3DiscProjectService { get; }
    public AudioDiscProjectService AudioDiscProjectService { get; }
    public WorkManifestService WorkManifestService { get; }
    public ConvertedFileCleanupService ConvertedFileCleanupService { get; }
    public ProjectResumePlanService ProjectResumePlanService { get; }
    public ExportChapterService ExportChapterService { get; }
    public ChapterAutoEngineService ChapterAutoEngineService { get; }
    public ChapterMasterEngineService ChapterMasterEngineService { get; }
    public ChapterModeEngineService ChapterModeEngineService { get; }
    public ChapterDebugPresentationService ChapterDebugPresentationService { get; }
    public MetadataDebugPresentationService MetadataDebugPresentationService { get; }
    public ChapterTimelinePresentationService ChapterTimelinePresentationService { get; }
    public WorkFolderStructureService WorkFolderStructureService { get; }
    public ProjectIndexService ProjectIndexService { get; }
    public SystemSleepPreventionActivityService SystemSleepPreventionActivityService { get; }
    public SystemSleepPreventionService SystemSleepPreventionService { get; }

    public static MainWindowFoundationComposition Create() =>
        new(new BookStitchEnvironmentResolver().Resolve());

    public static MainWindowFoundationComposition Create(BookStitchEnvironmentContext environmentContext) =>
        new(environmentContext);
}
