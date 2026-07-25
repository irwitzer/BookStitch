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
        ExportPreset preset)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(tracks);
        ArgumentNullException.ThrowIfNull(preset);

        var items = new List<ConvertedTrackPreparationPlanItem>(tracks.Count);
        var manifestChanged = false;

        for (var index = 0; index < tracks.Count; index++)
        {
            var track = tracks[index];
            var sourcePath = TrackPathService.GetTrackPath(currentFolderPath, track);
            var convertedPath = ConvertedTrackPathService.GetConvertedTrackPath(
                convertedFolder,
                sourcePath,
                track);

            var reconciliation = _convertedTrackReconciliationService.ReconcileTrack(
                manifest,
                projectType,
                index,
                track,
                sourcePath,
                convertedPath,
                preset);

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
