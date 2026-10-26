namespace BookStitch.Services;

public sealed class MainWindowStatusComposition
{
    private MainWindowStatusComposition()
    {
        ExportUiStateService = new ExportUiStateService();
        ExportFailureDetailsService = new ExportFailureDetailsService();
        WorkflowExportFailureStatusService = new WorkflowExportFailureStatusService();
        WorkflowStatusCoordinator = new WorkflowStatusCoordinator();
        WorkflowStatusFormatter = new WorkflowStatusFormatter();
        LocalWorkflowStatusAdapter = new LocalWorkflowStatusAdapter();
        Mp3DiscWorkflowStatusAdapter = new Mp3DiscWorkflowStatusAdapter();
        AudioDiscWorkflowStatusAdapter = new AudioDiscWorkflowStatusAdapter();
    }

    public ExportUiStateService ExportUiStateService { get; }
    public ExportFailureDetailsService ExportFailureDetailsService { get; }
    public WorkflowExportFailureStatusService WorkflowExportFailureStatusService { get; }
    public WorkflowStatusCoordinator WorkflowStatusCoordinator { get; }
    public WorkflowStatusFormatter WorkflowStatusFormatter { get; }
    public LocalWorkflowStatusAdapter LocalWorkflowStatusAdapter { get; }
    public Mp3DiscWorkflowStatusAdapter Mp3DiscWorkflowStatusAdapter { get; }
    public AudioDiscWorkflowStatusAdapter AudioDiscWorkflowStatusAdapter { get; }

    public static MainWindowStatusComposition Create() => new();
}