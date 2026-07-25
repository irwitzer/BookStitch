namespace BookStitch.Models;

public enum WorkflowProjectKind
{
    None,
    Folder,
    Mp3Disc,
    AudioDisc
}

public enum WorkflowActivity
{
    PreparingProject,
    AnalyzingTracks,
    AnalyzingConvertedTracks,
    ReadingChapters,
    CopyingSources,
    Ripping,
    Converting,
    WaitingForDisc,
    Merging,
    WritingMetadata,
    RollingBack
}

public enum SourceAcquisitionKind
{
    None,
    Copying,
    Ripping
}

public enum WorkflowAnalysisKind
{
    None,
    SourceTracks,
    ConvertedTracks,
    Chapters
}

public enum WorkflowRollbackPhase
{
    None,
    Running,
    Completed
}

public sealed record SourceAcquisitionProgress(
    SourceAcquisitionKind Kind,
    int CompletedCurrentSource,
    int TotalCurrentSource,
    int CompletedProject,
    int TotalProject,
    int CurrentDisc = 0,
    int TotalDiscs = 0,
    int Percent = 0,
    string? WorkingFormat = null,
    bool CurrentSourceFinished = false,
    bool AllSourcesFinished = false);

public sealed record ConversionActivityProgress(
    int Completed,
    int Total,
    int Percent,
    IReadOnlyList<int>? ActiveTrackNumbers = null,
    int BitrateKbps = 128,
    bool IsMono = false,
    bool IsLive = true);

public sealed record AnalysisProgress(
    WorkflowAnalysisKind Kind,
    int Completed,
    int Total,
    int Percent);

public sealed record MergeProgress(
    int CurrentFile,
    int TotalFiles,
    int Percent,
    bool IsWritingMetadata = false);

public sealed record WorkflowErrorStatus(
    string Message,
    int? FailedTrackOrFileNumber = null);

public sealed record WorkflowWarningStatus(string Message);

public sealed record WorkflowRollbackStatus(WorkflowRollbackPhase Phase);

public sealed record WorkflowStatusSnapshot
{
    public static WorkflowStatusSnapshot Empty { get; } = new();

    public string? ProjectId { get; init; }
    public WorkflowProjectKind ProjectKind { get; init; }
    public ProjectPipelineState ProjectState { get; init; } = ProjectPipelineState.Preparing;
    public IReadOnlySet<WorkflowActivity> ActiveActivities { get; init; } = new HashSet<WorkflowActivity>();
    public SourceAcquisitionProgress? SourceProgress { get; init; }
    public ConversionActivityProgress? ConversionProgress { get; init; }
    public AnalysisProgress? AnalysisProgress { get; init; }
    public MergeProgress? MergeProgress { get; init; }
    public WorkflowErrorStatus? Error { get; init; }
    public WorkflowWarningStatus? Warning { get; init; }
    public WorkflowRollbackStatus? Rollback { get; init; }
    public bool IsPaused { get; init; }
    public bool IsExtension { get; init; }
    public bool IsLoadedProject { get; init; }
    public bool IsReadyToMerge { get; init; }
    public bool IsSuccessfulExport { get; init; }
    public bool IsProjectIncomplete { get; init; }
    public bool IsExportAborted { get; init; }
    public bool IsMergeAborted { get; init; }
    public bool IsProjectPrepared { get; init; }
    public bool IsPresetChangePending { get; init; }
    public int TotalSourceItems { get; init; }
    public int TotalChapters { get; init; }
    public long? OutputFileSizeBytes { get; init; }
}

public enum WorkflowProgressVisualKind
{
    Source,
    Conversion,
    Merge
}

public sealed record WorkflowStatusViewState(
    string TeletextText,
    string ProgressText,
    int ProgressPercent,
    bool IsProgressIndeterminate = false,
    WorkflowProgressVisualKind ProgressVisualKind = WorkflowProgressVisualKind.Source);
