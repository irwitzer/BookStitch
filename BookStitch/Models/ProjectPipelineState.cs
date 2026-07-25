namespace BookStitch.Models;

public enum ProjectPipelineState
{
    Preparing,
    AcquiringSources,
    Converting,
    ReviewBeforeMerge,
    Merging,
    Completed
}

public static class ProjectPipelineStateNames
{
    public const string Preparing = nameof(ProjectPipelineState.Preparing);
    public const string AcquiringSources = nameof(ProjectPipelineState.AcquiringSources);
    public const string Converting = nameof(ProjectPipelineState.Converting);
    public const string ReviewBeforeMerge = nameof(ProjectPipelineState.ReviewBeforeMerge);
    public const string Merging = nameof(ProjectPipelineState.Merging);
    public const string Completed = nameof(ProjectPipelineState.Completed);

    public static string ToManifestValue(this ProjectPipelineState state) => state.ToString();

    public static ProjectPipelineState FromManifestValue(
        string? value,
        bool hasSuccessfulExport = false,
        bool sourcesComplete = false)
    {
        if (Enum.TryParse<ProjectPipelineState>(value, ignoreCase: true, out var state))
            return state;

        return value?.Trim() switch
        {
            "Created" or "NotStarted" or "AwaitingRip" => ProjectPipelineState.Preparing,
            "Importing" or "Ripping" or "WaitingForDisc" => ProjectPipelineState.AcquiringSources,
            "Exporting" => ProjectPipelineState.Converting,
            "Ready" or "PausedBeforeMerge" or "RippingCompleted" => ProjectPipelineState.ReviewBeforeMerge,
            "Completed" => ProjectPipelineState.Completed,
            "Canceled" or "Failed" when hasSuccessfulExport => ProjectPipelineState.Completed,
            "Canceled" or "Failed" when sourcesComplete => ProjectPipelineState.ReviewBeforeMerge,
            _ when hasSuccessfulExport => ProjectPipelineState.Completed,
            _ when sourcesComplete => ProjectPipelineState.ReviewBeforeMerge,
            _ => ProjectPipelineState.Preparing
        };
    }
}
