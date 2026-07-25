using BookStitch.Models;

namespace BookStitch.Services;

public sealed class Mp3DiscWorkflowStatusAdapter
{
    public WorkflowStatusSnapshot CreateRunningSnapshot(
        string? projectId,
        ProjectPipelineState projectState,
        int currentDisc,
        int totalDiscs,
        int copiedCurrentDisc,
        int totalCurrentDisc,
        int copiedProject,
        int totalProject,
        int convertedProject,
        IReadOnlyList<int> activeTrackNumbers,
        ExportPreset preset,
        bool isExtension = false,
        bool isPaused = false,
        bool currentDiscFinished = false,
        bool allDiscsFinished = false)
    {
        ArgumentNullException.ThrowIfNull(activeTrackNumbers);
        ArgumentNullException.ThrowIfNull(preset);

        var safeCurrentTotal = Math.Max(0, totalCurrentDisc);
        var safeProjectTotal = Math.Max(0, totalProject);
        var safeCurrentCopied = Math.Clamp(copiedCurrentDisc, 0, safeCurrentTotal);
        var safeProjectCopied = Math.Clamp(copiedProject, 0, safeProjectTotal);
        var safeConverted = Math.Clamp(convertedProject, 0, safeProjectTotal);
        var copyPercent = safeCurrentTotal == 0 ? 0 : safeCurrentCopied * 100 / safeCurrentTotal;
        var conversionPercent = safeProjectTotal == 0 ? 0 : safeConverted * 100 / safeProjectTotal;
        var activities = new HashSet<WorkflowActivity>();

        if (!allDiscsFinished && !currentDiscFinished)
            activities.Add(WorkflowActivity.CopyingSources);
        if (!allDiscsFinished && currentDiscFinished)
            activities.Add(WorkflowActivity.WaitingForDisc);
        if (safeConverted < safeProjectTotal || activeTrackNumbers.Count > 0)
            activities.Add(WorkflowActivity.Converting);

        return new WorkflowStatusSnapshot
        {
            ProjectId = projectId,
            ProjectKind = WorkflowProjectKind.Mp3Disc,
            ProjectState = projectState,
            ActiveActivities = activities,
            SourceProgress = new SourceAcquisitionProgress(
                SourceAcquisitionKind.Copying,
                safeCurrentCopied,
                safeCurrentTotal,
                safeProjectCopied,
                safeProjectTotal,
                CurrentDisc: Math.Max(1, currentDisc),
                TotalDiscs: Math.Max(1, totalDiscs),
                Percent: copyPercent,
                CurrentSourceFinished: currentDiscFinished,
                AllSourcesFinished: allDiscsFinished),
            ConversionProgress = new ConversionActivityProgress(
                safeConverted,
                safeProjectTotal,
                conversionPercent,
                activeTrackNumbers,
                preset.BitrateKbps,
                preset.Channels == 1,
                IsLive: true),
            IsPaused = isPaused,
            IsExtension = isExtension,
            TotalSourceItems = safeProjectTotal
        };
    }

    public WorkflowStatusSnapshot CreateReadySnapshot(
        string? projectId,
        int totalDiscs,
        int totalFiles,
        ExportPreset preset,
        bool isLoadedProject = false)
    {
        ArgumentNullException.ThrowIfNull(preset);

        var total = Math.Max(0, totalFiles);
        return new WorkflowStatusSnapshot
        {
            ProjectId = projectId,
            ProjectKind = WorkflowProjectKind.Mp3Disc,
            ProjectState = ProjectPipelineState.ReviewBeforeMerge,
            SourceProgress = new SourceAcquisitionProgress(
                SourceAcquisitionKind.Copying,
                total,
                total,
                total,
                total,
                CurrentDisc: Math.Max(1, totalDiscs),
                TotalDiscs: Math.Max(1, totalDiscs),
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
