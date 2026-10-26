namespace BookStitch.Services;

public enum PipelineUiTransitionKind { Pausing, Stopping, Discarding }
public enum PipelineUiTransitionTarget { Primary, Secondary, DiscStopSave, DeleteNewProject }

/// <summary>Transient presentation only; cancellation and project durability retain their existing owners.</summary>
public sealed record PipelineUiTransition(PipelineUiTransitionKind Kind, PipelineUiTransitionTarget Target)
{
    public string ButtonText => LocalizationText.Get(Kind == PipelineUiTransitionKind.Pausing
        ? "Pipeline.Transition.Pausing" : Kind == PipelineUiTransitionKind.Discarding
            ? "Pipeline.Transition.Discarding" : "Pipeline.Transition.Stopping");
    public string StatusText => LocalizationText.Get(Kind == PipelineUiTransitionKind.Pausing
        ? "Pipeline.Pause.Preparing" : Kind == PipelineUiTransitionKind.Discarding
            ? "Pipeline.Transition.Discarding" : Target == PipelineUiTransitionTarget.DiscStopSave
            ? "Pipeline.Transition.StopSaving" : "Pipeline.Transition.StoppingStatus");
}
