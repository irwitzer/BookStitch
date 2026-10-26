using BookStitch.Models;

namespace BookStitch.Services;

public sealed record TrackStateUpdateContext(
    IReadOnlyList<TrackInfo> Tracks,
    string ProjectWorkFolder,
    string SelectedPreset,
    bool IsWorkflowActive,
    bool IsRefreshSuspended = false,
    ProjectPipelineState PipelineState = ProjectPipelineState.Preparing);
