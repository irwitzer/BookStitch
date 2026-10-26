namespace BookStitch.Services;

public sealed class MainWindowWorkflowComposition
{
    private MainWindowWorkflowComposition(
        AacExportProcessingService aacExportProcessingService,
        LocalProjectLivePreparationService localProjectLivePreparationService,
        LiveConversionWorkflowService liveConversionWorkflowService,
        Mp3DiscPresetPreparationWorkflowService mp3DiscPresetPreparationWorkflowService,
        Mp3DiscImportWorkflowService mp3DiscImportWorkflowService,
        ProjectSnapshotService projectSnapshotService,
        AudioDiscRunPreparationService audioDiscRunPreparationService,
        AudioDiscRipWorkflowService audioDiscRipWorkflowService,
        AudioDiscRipCompletionWorkflowService audioDiscRipCompletionWorkflowService,
        ProjectResumeLoadWorkflowService projectResumeLoadWorkflowService,
        IDiscWaitDialogService discWaitDialogService,
        Mp3DiscPollingService mp3DiscPollingService,
        Mp3DiscWaitDialogService mp3DiscWaitDialogService,
        DiscDriveCandidateProbeService discDriveCandidateProbeService,
        AudioDiscPollingService audioDiscPollingService,
        ExportWorkflowService exportWorkflowService,
        TrackPreparedStateRefreshService trackPreparedStateRefreshService)
    {
        AacExportProcessingService = aacExportProcessingService;
        LocalProjectLivePreparationService = localProjectLivePreparationService;
        LiveConversionWorkflowService = liveConversionWorkflowService;
        Mp3DiscPresetPreparationWorkflowService = mp3DiscPresetPreparationWorkflowService;
        Mp3DiscImportWorkflowService = mp3DiscImportWorkflowService;
        ProjectSnapshotService = projectSnapshotService;
        AudioDiscRunPreparationService = audioDiscRunPreparationService;
        AudioDiscRipWorkflowService = audioDiscRipWorkflowService;
        AudioDiscRipCompletionWorkflowService = audioDiscRipCompletionWorkflowService;
        ProjectResumeLoadWorkflowService = projectResumeLoadWorkflowService;
        DiscWaitDialogService = discWaitDialogService;
        Mp3DiscPollingService = mp3DiscPollingService;
        Mp3DiscWaitDialogService = mp3DiscWaitDialogService;
        DiscDriveCandidateProbeService = discDriveCandidateProbeService;
        AudioDiscPollingService = audioDiscPollingService;
        ExportWorkflowService = exportWorkflowService;
        TrackPreparedStateRefreshService = trackPreparedStateRefreshService;
    }

    public AacExportProcessingService AacExportProcessingService { get; }
    public LocalProjectLivePreparationService LocalProjectLivePreparationService { get; }
    public LiveConversionWorkflowService LiveConversionWorkflowService { get; }
    public Mp3DiscPresetPreparationWorkflowService Mp3DiscPresetPreparationWorkflowService { get; }
    public Mp3DiscImportWorkflowService Mp3DiscImportWorkflowService { get; }
    public ProjectSnapshotService ProjectSnapshotService { get; }
    public AudioDiscRunPreparationService AudioDiscRunPreparationService { get; }
    public AudioDiscRipWorkflowService AudioDiscRipWorkflowService { get; }
    public AudioDiscRipCompletionWorkflowService AudioDiscRipCompletionWorkflowService { get; }
    public ProjectResumeLoadWorkflowService ProjectResumeLoadWorkflowService { get; }
    public IDiscWaitDialogService DiscWaitDialogService { get; }
    public Mp3DiscPollingService Mp3DiscPollingService { get; }
    public Mp3DiscWaitDialogService Mp3DiscWaitDialogService { get; }
    public DiscDriveCandidateProbeService DiscDriveCandidateProbeService { get; }
    public AudioDiscPollingService AudioDiscPollingService { get; }
    public ExportWorkflowService ExportWorkflowService { get; }
    public TrackPreparedStateRefreshService TrackPreparedStateRefreshService { get; }

    public static MainWindowWorkflowComposition Create(
        MainWindowFoundationComposition foundation,
        FfmpegRunnerService ffmpegRunnerService,
        LocalProjectImportService localProjectImportService,
        Func<string> getFfmpegPath,
        Mp3DiscPreparationService mp3DiscPreparationService,
        Mp3DiscImportService mp3DiscImportService,
        DiscDriveService discDriveService,
        AudioDiscPipelineTimingService audioDiscPipelineTimingService,
        Func<bool> useBoxedDiscWaitDialog,
        AudioDiscReaderService audioDiscReaderService)
    {
        ArgumentNullException.ThrowIfNull(foundation);
        ArgumentNullException.ThrowIfNull(getFfmpegPath);
        ArgumentNullException.ThrowIfNull(useBoxedDiscWaitDialog);

        var aacExportProcessingService = new AacExportProcessingService(ffmpegRunnerService);
        var projectSnapshotService = new ProjectSnapshotService(
            foundation.Mp3DiscProjectService,
            foundation.AudioDiscProjectService,
            foundation.WorkManifestService);
        var discWaitDialogService = new SwitchableDiscWaitDialogService(useBoxedDiscWaitDialog);
        var mp3DiscPollingService = new Mp3DiscPollingService(mp3DiscImportService, discDriveService);

        return new MainWindowWorkflowComposition(
            aacExportProcessingService,
            new LocalProjectLivePreparationService(localProjectImportService),
            new LiveConversionWorkflowService(aacExportProcessingService, getFfmpegPath),
            new Mp3DiscPresetPreparationWorkflowService(
                mp3DiscPreparationService,
                aacExportProcessingService,
                getFfmpegPath),
            new Mp3DiscImportWorkflowService(
                mp3DiscImportService,
                foundation.Mp3DiscProjectService,
                discDriveService),
            projectSnapshotService,
            new AudioDiscRunPreparationService(projectSnapshotService),
            new AudioDiscRipWorkflowService(
                foundation.AudioDiscProjectService,
                audioDiscPipelineTimingService),
            new AudioDiscRipCompletionWorkflowService(foundation.AudioDiscProjectService),
            new ProjectResumeLoadWorkflowService(
                foundation.AudioDiscProjectService,
                foundation.TrackListStateService),
            discWaitDialogService,
            mp3DiscPollingService,
            new Mp3DiscWaitDialogService(mp3DiscPollingService, discWaitDialogService),
            new DiscDriveCandidateProbeService(discDriveService),
            new AudioDiscPollingService(audioDiscReaderService, discDriveService),
            new ExportWorkflowService(
                foundation.WorkManifestService,
                projectSnapshotService,
                aacExportProcessingService,
                foundation.ConvertedFileCleanupService,
                new ConvertedTrackPreparationPlanService(
                    new ConvertedTrackReconciliationService(foundation.WorkManifestService)),
                new ConvertedTrackPreparationWorkflowService(
                    foundation.WorkManifestService,
                    aacExportProcessingService),
                foundation.ExportChapterService,
                foundation.FinalTagService,
                new FinalOutputStorageService(),
                new FinalizedOutputValidationService(),
                foundation.ChapterAutoEngineService,
                foundation.ChapterModeEngineService),
            new TrackPreparedStateRefreshService());
    }
}
