namespace BookStitch.Models;

public enum ExportWorkflowResultStatus
{
    Completed,
    PausedBeforeMerge,
    FinalOutputDiscarded,
    Canceled,
    Failed
}

public sealed record ExportWorkflowResult(
    ExportWorkflowResultStatus Status,
    string OutputPath,
    string ProjectWorkFolder,
    string ConvertedFolder,
    Exception? Error = null,
    ConvertedTrackResumeState? ConversionResumeState = null);
