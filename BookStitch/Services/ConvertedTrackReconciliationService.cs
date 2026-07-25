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
        ExportPreset preset)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(track);
        ArgumentNullException.ThrowIfNull(preset);

        var canReuse = _workManifestService.CanReuseConvertedTrack(
                           manifest,
                           index,
                           track,
                           sourcePath,
                           convertedPath,
                           preset) ||
                       PreparedConvertedTrackReuseService.CanReuseForDiscProject(
                           projectType,
                           sourcePath,
                           convertedPath);

        if (canReuse)
        {
            // A reusable file is authoritative evidence for the current cache entry.
            // Rebuild the entry so stale disc-project manifests and reordered track
            // indices are normalized before conversion resume continues.
            _workManifestService.UpdateTrack(
                manifest,
                index,
                track,
                sourcePath,
                convertedPath,
                preset);

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
