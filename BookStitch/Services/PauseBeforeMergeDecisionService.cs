namespace BookStitch.Services;

public sealed record PauseBeforeMergeDecisionInput(
    bool ManualMergeRequested,
    bool MergeAutomaticallyAfterConversion);

public sealed class PauseBeforeMergeDecisionService
{
    public bool ShouldPauseBeforeMerge(PauseBeforeMergeDecisionInput input)
    {
        // Historical review/completion and cached audio are not authorization.
        // Only an explicit command for this invocation may override the live switch.
        if (input.ManualMergeRequested)
            return false;

        return !input.MergeAutomaticallyAfterConversion;
    }
}
