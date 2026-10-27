namespace BookStitch.Services;

public sealed class MainWindowMediaComposition
{
    private MainWindowMediaComposition(Func<string?>? getCoverTransparencyMode)
    {
        TagMetadataReader = new TagMetadataReader();
        FileSystemMetadataReader = new FileSystemMetadataReader();
        FolderScanner = new FolderScanner(TagMetadataReader, FileSystemMetadataReader);
        EmbeddedCoverService = new EmbeddedCoverService(TagMetadataReader);
        SourceMetadataSnapshotStore = new SourceMetadataSnapshotStore();
        FfmpegService = new FfmpegService();
        FfprobeRunnerService = new FfprobeRunnerService();
        FfprobeConcurrencyPolicy = new FfprobeConcurrencyPolicy();
        FfprobeCoordinator = new FfprobeCoordinator();
        FfmpegRunnerService = new FfmpegRunnerService();
        AudioInfoService = new AudioInfoService(FfprobeRunnerService, FfmpegRunnerService);
        AudioProbeBatchService = new AudioProbeBatchService(
            AudioInfoService,
            FfprobeConcurrencyPolicy,
            FfprobeCoordinator);
        TrackAudioProbeApplicationService = new TrackAudioProbeApplicationService();
        SourceMetadataCaptureService = new SourceMetadataCaptureService(
            SourceMetadataSnapshotStore,
            TagMetadataReader);
        ProjectMetadataInitializationService = new ProjectMetadataInitializationService();
        ProjectMetadataPreviewAggregationService = new ProjectMetadataPreviewAggregationService(
            ProjectMetadataInitializationService);
        ProjectMetadataAggregationPersistenceService = new ProjectMetadataAggregationPersistenceService(
            SourceMetadataSnapshotStore,
            ProjectMetadataInitializationService);
        CoverImageService = new CoverImageService(getCoverTransparencyMode);
    }

    public TagMetadataReader TagMetadataReader { get; }
    public FileSystemMetadataReader FileSystemMetadataReader { get; }
    public FolderScanner FolderScanner { get; }
    public EmbeddedCoverService EmbeddedCoverService { get; }
    public FfmpegService FfmpegService { get; }
    public AudioInfoService AudioInfoService { get; }
    public AudioProbeBatchService AudioProbeBatchService { get; }
    public TrackAudioProbeApplicationService TrackAudioProbeApplicationService { get; }
    public SourceMetadataSnapshotStore SourceMetadataSnapshotStore { get; }
    public SourceMetadataCaptureService SourceMetadataCaptureService { get; }
    public ProjectMetadataInitializationService ProjectMetadataInitializationService { get; }
    public ProjectMetadataPreviewAggregationService ProjectMetadataPreviewAggregationService { get; }
    public ProjectMetadataAggregationPersistenceService ProjectMetadataAggregationPersistenceService { get; }
    public CoverImageService CoverImageService { get; }
    public FfmpegRunnerService FfmpegRunnerService { get; }
    public FfprobeRunnerService FfprobeRunnerService { get; }
    public FfprobeConcurrencyPolicy FfprobeConcurrencyPolicy { get; }
    public FfprobeCoordinator FfprobeCoordinator { get; }

    public static MainWindowMediaComposition Create(Func<string?>? getCoverTransparencyMode = null) => new(getCoverTransparencyMode);
}
