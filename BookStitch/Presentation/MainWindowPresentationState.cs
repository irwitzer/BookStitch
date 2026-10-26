using BookStitch.Services;

namespace BookStitch.Presentation;

public sealed class MainWindowPresentationState
{
    public bool IsBusy { get; set; }

    public bool IsExporting { get; set; }

    public bool IsDiscImporting { get; set; }

    public bool IsPipelinePaused { get; set; }

    public bool IsPipelineFailed { get; set; }

    public bool IsProjectExtensionRun { get; set; }

    public bool PauseRequested { get; set; }

    public PipelineUiTransition? UiTransition { get; private set; }

    public PipelineUiTransition? BeginUiTransition(PipelineUiTransitionKind kind, PipelineUiTransitionTarget target)
    {
        if (UiTransition is not null) return null;
        return UiTransition = new(kind, target);
    }

    public void EndUiTransition(PipelineUiTransition? owner)
    {
        if (owner is not null && ReferenceEquals(UiTransition, owner)) UiTransition = null;
    }

    public void ClearUiTransition() => UiTransition = null;

    public string ProjectStatus(string currentStatus) => UiTransition?.StatusText ??
        (PauseRequested ? LocalizationText.Get("Pipeline.Pause.Preparing") : currentStatus);
}