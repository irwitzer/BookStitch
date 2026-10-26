using BookStitch.Models;

namespace BookStitch.Services;

public sealed record ConvertedTrackReconciliationResult(
    bool CanReuse,
    bool ManifestChanged);

public sealed class ConvertedTrackReconciliationService
{
    private readonly WorkManifestService _workManifestService;

    public ConvertedTrackReconciliationService(WorkManifestService workManifestService)
    {
        _workManifestService = workManifestService ?? throw new ArgumentNullException(nameof(workManifestService));
    }

    public ConvertedTrackReconciliationResult ReconcileTrack(
        ExportWorkManifest manifest,
        string projectType,
        int index,
        TrackInfo track,
        string sourcePath,
        string convertedPath,
        ExportPreset preset,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(track);
        ArgumentNullException.ThrowIfNull(preset);

        if (track.SourceId == Guid.Empty)
            track.SourceId = manifest.Tracks.FirstOrDefault(entry =>
                TrackPathService.PathEquals(entry.SourcePath, sourcePath) && entry.SourceId != Guid.Empty)?.SourceId ?? Guid.NewGuid();
        foreach (var entry in manifest.Tracks.Where(entry => TrackPathService.PathEquals(entry.SourcePath, sourcePath)))
            WorkManifestService.MigratePreparedEntry(entry);

        var canReuse = _workManifestService.CanReuseConvertedTrack(
                           manifest,
                           index,
                           track,
                           sourcePath,
                           convertedPath,
                           preset, token);
        // An untracked file's timestamp cannot establish the preset or processing action.

        if (canReuse)
        {
            // Normalize the verified cache entry after track reordering.
            _workManifestService.UpdateTrack(
                manifest,
                index,
                track,
                sourcePath,
                convertedPath,
                preset, existingContentVerified: true);

            return new ConvertedTrackReconciliationResult(
                CanReuse: true,
                ManifestChanged: true);
        }

        var manifestChanged = _workManifestService.MarkTrackPendingForPreparation(
            manifest,
            index,
            track,
            sourcePath,
            convertedPath,
            preset);

        return new ConvertedTrackReconciliationResult(
            CanReuse: false,
            ManifestChanged: manifestChanged);
    }
}
