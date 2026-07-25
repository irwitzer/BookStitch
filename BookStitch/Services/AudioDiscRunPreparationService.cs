using BookStitch.Models;

namespace BookStitch.Services;

public sealed record AudioDiscRunPreparation(
    ExportPreset Preset,
    int ParallelJobs);

public sealed class AudioDiscRunPreparationService
{
    private readonly ProjectSnapshotService _projectSnapshotService;

    public AudioDiscRunPreparationService(ProjectSnapshotService projectSnapshotService)
    {
        _projectSnapshotService = projectSnapshotService ?? throw new ArgumentNullException(nameof(projectSnapshotService));
    }

    public AudioDiscRunPreparation Prepare(
        AudioDiscProjectManifest manifest,
        ProjectSnapshotUiState snapshot,
        int resolvedParallelJobs)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(snapshot);

        _projectSnapshotService.SaveAudioDiscProjectSnapshot(manifest, snapshot, force: true);

        return new AudioDiscRunPreparation(
            ExportPreset.Parse(manifest.ExportPreset),
            Math.Max(1, resolvedParallelJobs));
    }
}
