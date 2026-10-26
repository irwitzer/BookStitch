using BookStitch.Models;

namespace BookStitch.Services;

public sealed record ConvertedTrackPreparationPlanItem(
    int Index,
    TrackInfo Track,
    string SourcePath,
    string ConvertedPath,
    bool CanReuse);

public sealed record ConvertedTrackPreparationPlan(
    IReadOnlyList<ConvertedTrackPreparationPlanItem> Items,
    IReadOnlyList<ConvertedTrackPreparationPlanItem> ReusableItems,
    IReadOnlyList<ConvertedTrackPreparationPlanItem> PendingItems,
    bool ManifestChanged,
    int ReusableCount,
    int PendingCount,
    long ReusableDurationTicks,
    ConvertedTrackPreparationPlanStatus Status,
    ConvertedTrackResumeState ResumeState)
{
    public bool IsResume => ResumeState.IsPartialResume;
}

public sealed class ConvertedTrackPreparationPlanService
{
    private readonly ConvertedTrackReconciliationService _convertedTrackReconciliationService;

    public ConvertedTrackPreparationPlanService(
        ConvertedTrackReconciliationService convertedTrackReconciliationService)
    {
        _convertedTrackReconciliationService = convertedTrackReconciliationService
            ?? throw new ArgumentNullException(nameof(convertedTrackReconciliationService));
    }

    public ConvertedTrackPreparationPlan Build(
        ExportWorkManifest manifest,
        string projectType,
        IReadOnlyList<TrackInfo> tracks,
        string currentFolderPath,
        string convertedFolder,
        ExportPreset preset,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(tracks);
        ArgumentNullException.ThrowIfNull(preset);

        var paths = ConvertedTrackPathService.GetConvertedTrackPaths(convertedFolder, currentFolderPath, tracks);
        var targets = tracks.Select((track, index) =>
        {
            var source = TrackPathService.GetTrackPath(currentFolderPath, track);
            return (Source: System.IO.Path.GetFullPath(source), Target: paths[index],
                Action: AudioProcessingService.NormalizeProcessingAction(track.ProcessingAction));
        }).ToArray();
        if (targets.Select(item => item.Target).Distinct(StringComparer.OrdinalIgnoreCase).Count() != targets.Length)
            throw new InvalidOperationException(LocalizationText.Get("Export.CacheCollision"));

        var items = new List<ConvertedTrackPreparationPlanItem>(tracks.Count);
        var manifestChanged = false;

        for (var index = 0; index < tracks.Count; index++)
        {
            token.ThrowIfCancellationRequested();
            var track = tracks[index];
            var canonical = targets[index];
            var sourcePath = canonical.Source;
            var convertedPath = canonical.Target;

            var reconciliation = _convertedTrackReconciliationService.ReconcileTrack(
                manifest,
                projectType,
                index,
                track,
                sourcePath,
                convertedPath,
                preset, token);

            manifestChanged |= reconciliation.ManifestChanged;

            items.Add(new ConvertedTrackPreparationPlanItem(
                index,
                track,
                sourcePath,
                convertedPath,
                reconciliation.CanReuse));
        }

        var reusableItems = items.Where(item => item.CanReuse).ToArray();
        var pendingItems = items.Where(item => !item.CanReuse).ToArray();
        var reusableCount = reusableItems.Length;
        var pendingCount = pendingItems.Length;
        var reusableDurationTicks = reusableItems.Sum(item =>
            TrackDurationService.GetEffectiveDurationTicks(item.Track));
        var status = pendingCount switch
        {
            0 => ConvertedTrackPreparationPlanStatus.FullyReusable,
            _ when reusableCount == 0 => ConvertedTrackPreparationPlanStatus.RequiresPreparation,
            _ => ConvertedTrackPreparationPlanStatus.PartiallyReusable
        };

        var resumeState = new ConvertedTrackResumeState(
            status,
            items.Count,
            reusableCount,
            pendingCount,
            reusableDurationTicks);

        return new ConvertedTrackPreparationPlan(
            items,
            reusableItems,
            pendingItems,
            manifestChanged,
            reusableCount,
            pendingCount,
            reusableDurationTicks,
            status,
            resumeState);
    }
}
