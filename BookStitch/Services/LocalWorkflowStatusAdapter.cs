using BookStitch.Models;

namespace BookStitch.Services;

public sealed class LocalWorkflowStatusAdapter
{
    public WorkflowStatusSnapshot CreateRunningSnapshot(
        string? projectId,
        ProjectPipelineState projectState,
        LocalProjectLivePreparationProgress progress,
        ExportPreset preset,
        bool isExtension = false,
        bool isPaused = false,
        int existingConvertedCount = 0,
        int? conversionTotalOverride = null,
        int activeTrackNumberOffset = 0)
    {
        ArgumentNullException.ThrowIfNull(progress);
        ArgumentNullException.ThrowIfNull(preset);

        var total = Math.Max(0, progress.TotalFiles);
        var copied = Math.Clamp(progress.CopiedFiles, 0, total);
        var preparedThisRun = Math.Clamp(progress.PreparedFiles, 0, total);
        var conversionTotal = Math.Max(total, conversionTotalOverride ?? total);
        var converted = Math.Clamp(existingConvertedCount + preparedThisRun, 0, conversionTotal);
        var activeFraction = Math.Clamp(progress.ActivePreparationFraction, 0d, Math.Max(0, conversionTotal - converted));
        var copyingFinished = total > 0 && copied >= total;
        var conversionPercent = conversionTotal == 0
            ? 0
            : (int)Math.Clamp(Math.Round((converted + activeFraction) * 100d / conversionTotal, MidpointRounding.AwayFromZero), 0, 100);
        var copyPercent = total == 0 ? 0 : copied * 100 / total;
        var activities = new HashSet<WorkflowActivity>();

        if (!copyingFinished)
            activities.Add(WorkflowActivity.CopyingSources);
        if (converted < conversionTotal || progress.ActiveTrackNumbers.Count > 0)
            activities.Add(WorkflowActivity.Converting);

        var activeTrackNumbers = progress.ActiveTrackNumbers
            .Select(number => number + activeTrackNumberOffset)
            .ToArray();

        return new WorkflowStatusSnapshot
        {
            ProjectId = projectId,
            ProjectKind = WorkflowProjectKind.Folder,
            ProjectState = projectState,
            ActiveActivities = activities,
            SourceProgress = new SourceAcquisitionProgress(
                SourceAcquisitionKind.Copying,
                copied,
                total,
                copied,
                total,
                Percent: copyPercent,
                CurrentSourceFinished: copyingFinished,
                AllSourcesFinished: copyingFinished),
            ConversionProgress = new ConversionActivityProgress(
                converted,
                conversionTotal,
                conversionPercent,
                activeTrackNumbers,
                preset.BitrateKbps,
                preset.Channels == 1,
                IsLive: true),
            IsPaused = isPaused,
            IsExtension = isExtension,
            TotalSourceItems = total
        };
    }

    public WorkflowStatusSnapshot CreateReadySnapshot(
        string? projectId,
        int totalFiles,
        ExportPreset preset,
        bool isLoadedProject = false)
    {
        ArgumentNullException.ThrowIfNull(preset);

        var total = Math.Max(0, totalFiles);
        return new WorkflowStatusSnapshot
        {
            ProjectId = projectId,
            ProjectKind = WorkflowProjectKind.Folder,
            ProjectState = ProjectPipelineState.ReviewBeforeMerge,
            SourceProgress = new SourceAcquisitionProgress(
                SourceAcquisitionKind.Copying,
                total,
                total,
                total,
                total,
                Percent: 100,
                CurrentSourceFinished: true,
                AllSourcesFinished: true),
            ConversionProgress = new ConversionActivityProgress(
                total,
                total,
                100,
                Array.Empty<int>(),
                preset.BitrateKbps,
                preset.Channels == 1,
                IsLive: true),
            IsReadyToMerge = true,
            IsLoadedProject = isLoadedProject,
            TotalSourceItems = total
        };
    }
}
