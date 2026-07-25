using BookStitch.Models;

namespace BookStitch.Services;

public sealed class AudioDiscWorkflowStatusAdapter
{
    public WorkflowStatusSnapshot CreateRunningSnapshot(
        string? projectId,
        int currentDisc,
        int totalDiscs,
        int rippedCurrentDisc,
        int totalCurrentDisc,
        int rippedProject,
        int totalProject,
        int convertedProject,
        IReadOnlyList<int> activeTrackNumbers,
        ExportPreset preset,
        string workingFormat,
        bool isPaused = false,
        bool currentDiscFinished = false,
        bool allDiscsFinished = false,
        bool isExtension = false)
    {
        var safeCurrentTotal = Math.Max(0, totalCurrentDisc);
        var safeProjectTotal = Math.Max(0, totalProject);
        var safeCurrentRipped = Math.Clamp(rippedCurrentDisc, 0, safeCurrentTotal);
        var safeProjectRipped = Math.Clamp(rippedProject, 0, safeProjectTotal);
        var safeConverted = Math.Clamp(convertedProject, 0, safeProjectTotal);
        var ripPercent = safeCurrentTotal == 0 ? 0 : safeCurrentRipped * 100 / safeCurrentTotal;
        var conversionPercent = safeProjectTotal == 0 ? 0 : safeConverted * 100 / safeProjectTotal;
        var activities = new HashSet<WorkflowActivity>();

        if (!allDiscsFinished && !currentDiscFinished)
            activities.Add(WorkflowActivity.Ripping);
        if (!allDiscsFinished && currentDiscFinished)
            activities.Add(WorkflowActivity.WaitingForDisc);
        if (safeConverted < safeProjectTotal || activeTrackNumbers.Count > 0)
            activities.Add(WorkflowActivity.Converting);

        return new WorkflowStatusSnapshot
        {
            ProjectId = projectId,
            ProjectKind = WorkflowProjectKind.AudioDisc,
            ProjectState = ProjectPipelineState.AcquiringSources,
            ActiveActivities = activities,
            SourceProgress = new SourceAcquisitionProgress(
                SourceAcquisitionKind.Ripping,
                safeCurrentRipped,
                safeCurrentTotal,
                safeProjectRipped,
                safeProjectTotal,
                CurrentDisc: Math.Max(1, currentDisc),
                TotalDiscs: Math.Max(1, totalDiscs),
                Percent: ripPercent,
                WorkingFormat: workingFormat,
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
        int totalTracks,
        ExportPreset preset,
        string workingFormat)
    {
        var total = Math.Max(0, totalTracks);
        return new WorkflowStatusSnapshot
        {
            ProjectId = projectId,
            ProjectKind = WorkflowProjectKind.AudioDisc,
            ProjectState = ProjectPipelineState.ReviewBeforeMerge,
            SourceProgress = new SourceAcquisitionProgress(
                SourceAcquisitionKind.Ripping,
                total,
                total,
                total,
                total,
                CurrentDisc: Math.Max(1, totalDiscs),
                TotalDiscs: Math.Max(1, totalDiscs),
                Percent: 100,
                WorkingFormat: workingFormat,
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
            TotalSourceItems = total
        };
    }
}
