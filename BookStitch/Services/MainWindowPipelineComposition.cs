namespace BookStitch.Services;

public sealed class MainWindowPipelineComposition
{
    private MainWindowPipelineComposition()
    {
        ExportValidationService = new ExportValidationService();
        OutputFolderLayoutService = new OutputFolderLayoutService();
        ProjectExtensionRollbackService = new ProjectExtensionRollbackService();
        ExportPlanService = new ExportPlanService();
        PipelinePauseRequestService = new PipelinePauseRequestService();
        PipelinePauseTransitionService = new PipelinePauseTransitionService();
        PausedPipelineContinuationService = new PausedPipelineContinuationService();
        LocalProjectImportService = new LocalProjectImportService();
        LocalProjectExtensionStateService = new LocalProjectExtensionStateService();
        LiveConversionQueueFactory = static () => new LiveConversionQueueService();
        ApplicationShutdownService = new ApplicationShutdownService();
    }

    public ExportValidationService ExportValidationService { get; }
    public OutputFolderLayoutService OutputFolderLayoutService { get; }
    public ProjectExtensionRollbackService ProjectExtensionRollbackService { get; }
    public ExportPlanService ExportPlanService { get; }
    public PipelinePauseRequestService PipelinePauseRequestService { get; }
    public PipelinePauseTransitionService PipelinePauseTransitionService { get; }
    public PausedPipelineContinuationService PausedPipelineContinuationService { get; }
    public LocalProjectImportService LocalProjectImportService { get; }
    public LocalProjectExtensionStateService LocalProjectExtensionStateService { get; }
    public Func<LiveConversionQueueService> LiveConversionQueueFactory { get; }
    public ApplicationShutdownService ApplicationShutdownService { get; }

    public static MainWindowPipelineComposition Create() => new();
}
