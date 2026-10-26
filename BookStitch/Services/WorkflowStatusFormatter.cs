using System.Globalization;
using BookStitch.Models;

namespace BookStitch.Services;

public sealed class WorkflowStatusFormatter
{
    private static string L(string key, params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, LocalizationText.Get("MainWindow.Status." + key), args);

    public WorkflowStatusViewState Format(WorkflowStatusSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (snapshot.Error is not null)
            return FormatError(snapshot);

        if (snapshot.Rollback is { Phase: not WorkflowRollbackPhase.None })
            return FormatRollback(snapshot.Rollback);

        if (snapshot.Warning is not null)
        {
            return new WorkflowStatusViewState(
                L("Warning", snapshot.Warning.Message),
                L("WaitingConfirmation"),
                100);
        }

        if (snapshot.IsMergeAborted)
        {
            var percent = ClampPercent(snapshot.MergeProgress?.Percent ?? 0);
            return new WorkflowStatusViewState(
                L("MergeAborted"),
                L("PercentAborted", percent),
                percent,
                ProgressVisualKind: WorkflowProgressVisualKind.Merge);
        }

        if (snapshot.IsExportAborted)
            return new WorkflowStatusViewState(L("ExportAborted"), L("Ready100"), 100);

        if (snapshot.MergeProgress is { IsWritingMetadata: true })
        {
            return new WorkflowStatusViewState(
                L("WritingMetadata"),
                L("WritingMetadataProgress"),
                100,
                ProgressVisualKind: WorkflowProgressVisualKind.Merge);
        }

        if (snapshot.MergeProgress is not null)
        {
            return new WorkflowStatusViewState(
                L("Merging"),
                L("MergeProgress", ClampPercent(snapshot.MergeProgress.Percent), snapshot.MergeProgress.CurrentFile, snapshot.MergeProgress.TotalFiles),
                ClampPercent(snapshot.MergeProgress.Percent),
                ProgressVisualKind: WorkflowProgressVisualKind.Merge);
        }

        if (snapshot.IsSuccessfulExport)
            return FormatSuccessfulExport(snapshot);

        if (snapshot.AnalysisProgress is not null)
            return FormatAnalysis(snapshot);

        if (snapshot.IsProjectPrepared)
        {
            var count = snapshot.AnalysisProgress?.Total ?? snapshot.TotalSourceItems;
            return new WorkflowStatusViewState(
                L("ProjectPrepared", count),
                L("TrackAnalysisCompleted"),
                100);
        }

        if (snapshot.IsPresetChangePending)
            return FormatPresetChangePending(snapshot);

        if (snapshot.IsLoadedProject)
            return FormatLoadedProject(snapshot);

        if (snapshot.IsReadyToMerge)
            return FormatReady(snapshot, loaded: false);

        if (snapshot.SourceProgress is not null || snapshot.ConversionProgress is not null)
            return FormatWork(snapshot);

        if (snapshot.ActiveActivities.Contains(WorkflowActivity.PreparingProject))
            return new WorkflowStatusViewState(L("ProjectStarting"), L("Initializing"), 0);

        return new WorkflowStatusViewState(L("SelectSource"), L("Ready0"), 0);
    }

    private static WorkflowStatusViewState FormatWork(WorkflowStatusSnapshot snapshot)
    {
        var source = snapshot.SourceProgress;
        var conversion = snapshot.ConversionProgress;
        var prefix = snapshot.IsPaused ? L("PausePrefix") : snapshot.IsExtension ? L("ExtensionPrefix") : string.Empty;
        var teletext = prefix + (snapshot.IsPaused
            ? BuildPausedTeletext(snapshot.ProjectKind, source, conversion)
            : BuildWorkTeletext(snapshot.ProjectKind, source, conversion));
        var progress = BuildWorkProgress(snapshot, source, conversion);
        var percent = source is not null && !source.AllSourcesFinished && !source.CurrentSourceFinished
            ? ClampPercent(source.Percent)
            : ClampPercent(conversion?.Percent ?? source?.Percent ?? 0);

        var visualKind = source is not null && !source.AllSourcesFinished && !source.CurrentSourceFinished
            ? WorkflowProgressVisualKind.Source
            : WorkflowProgressVisualKind.Conversion;

        return new WorkflowStatusViewState(
            teletext,
            progress,
            percent,
            ProgressVisualKind: visualKind);
    }

    private static string BuildPausedTeletext(
        WorkflowProjectKind projectKind,
        SourceAcquisitionProgress? source,
        ConversionActivityProgress? conversion)
    {
        if (source is null)
            return conversion is null ? L("Paused") : BuildConversionTeletext(conversion);

        string sourceText;
        if (projectKind is WorkflowProjectKind.AudioDisc or WorkflowProjectKind.Mp3Disc)
        {
            var label = projectKind == WorkflowProjectKind.AudioDisc ? LocalizationText.Get("Disc.Media.Audio") : LocalizationText.Get("Disc.Media.Mp3");
            var suffix = source.Kind == SourceAcquisitionKind.Ripping && !string.IsNullOrWhiteSpace(source.WorkingFormat)
                ? $" {source.WorkingFormat}"
                : string.Empty;
            sourceText = L("PausedDiscProgress", label, source.CurrentDisc, source.TotalDiscs, source.CompletedCurrentSource.ToString("00", CultureInfo.CurrentCulture), source.TotalCurrentSource.ToString("00", CultureInfo.CurrentCulture), suffix);
        }
        else
        {
            sourceText = L("CopyingCount", source.CompletedProject, source.TotalProject);
        }

        return conversion is null ? sourceText : $"{sourceText} | {BuildConversionTeletext(conversion)}";
    }

    private static string BuildWorkTeletext(
        WorkflowProjectKind projectKind,
        SourceAcquisitionProgress? source,
        ConversionActivityProgress? conversion)
    {
        var sourceText = source is null ? string.Empty : BuildSourceTeletext(projectKind, source);
        var conversionText = conversion is null ? string.Empty : BuildConversionTeletext(conversion);

        if (string.IsNullOrWhiteSpace(sourceText))
            return conversionText;
        if (string.IsNullOrWhiteSpace(conversionText))
            return sourceText;

        return $"{sourceText} | {conversionText}";
    }

    private static string BuildSourceTeletext(WorkflowProjectKind projectKind, SourceAcquisitionProgress source)
    {
        if (projectKind is WorkflowProjectKind.AudioDisc or WorkflowProjectKind.Mp3Disc)
        {
            var label = projectKind == WorkflowProjectKind.AudioDisc ? LocalizationText.Get("Disc.Media.Audio") : LocalizationText.Get("Disc.Media.Mp3");
            if (source.AllSourcesFinished)
                return L("DiscFinished", label, source.TotalDiscs, source.TotalDiscs);

            if (source.CurrentSourceFinished)
                return L("InsertDisc", label, Math.Min(source.CurrentDisc + 1, source.TotalDiscs), source.TotalDiscs);

            var verb = source.Kind == SourceAcquisitionKind.Ripping ? L("RippingVerb") : L("CopyingVerb");
            var suffix = source.Kind == SourceAcquisitionKind.Ripping && !string.IsNullOrWhiteSpace(source.WorkingFormat)
                ? $" {source.WorkingFormat}"
                : string.Empty;
            var key = string.IsNullOrWhiteSpace(source.DriveDisplayName)
                ? "ActiveDiscProgress"
                : "ActiveDiscProgressWithDrive";
            return L(key, label, source.CurrentDisc, source.TotalDiscs, verb, source.CompletedCurrentSource.ToString("00", CultureInfo.CurrentCulture), source.TotalCurrentSource.ToString("00", CultureInfo.CurrentCulture), suffix, source.DriveDisplayName);
        }

        if (source.AllSourcesFinished)
            return L("CopyCompleted", source.CompletedProject, source.TotalProject);

        return L("CopyingCount", source.CompletedProject, source.TotalProject);
    }

    private static string BuildConversionTeletext(ConversionActivityProgress conversion)
    {
        var label = conversion.IsLive ? L("LiveConversion") : L("NewConversion");
        return L("ConversionCount", label, conversion.Completed, conversion.Total, FormatPreset(conversion));
    }

    private static string BuildWorkProgress(
        WorkflowStatusSnapshot snapshot,
        SourceAcquisitionProgress? source,
        ConversionActivityProgress? conversion)
    {
        var paused = snapshot.IsPaused ? L("PausedSuffix") : string.Empty;

        if (source is not null && !source.AllSourcesFinished && !source.CurrentSourceFinished)
        {
            var verb = source.Kind == SourceAcquisitionKind.Ripping ? L("RippedVerb") : L("CopiedVerb");
            var active = FormatActiveConversionJobs(conversion, L("ConversionJobs"));
            var completed = source.CompletedProject;
            var total = source.TotalProject;
            return $"{ClampPercent(source.Percent)} %{paused} | {completed} / {total} {verb}{active}";
        }

        if (conversion is not null)
        {
            var active = FormatActiveConversionJobs(conversion, L("ActiveJobs"));
            return L("ConvertedProgress", ClampPercent(conversion.Percent), paused, conversion.Completed, conversion.Total, active);
        }

        return $"{ClampPercent(source?.Percent ?? 0)} %{paused}";
    }

    private static string FormatActiveConversionJobs(ConversionActivityProgress? conversion, string label)
    {
        if (conversion?.ActiveTrackNumbers is not { Count: > 0 })
            return string.Empty;

        return $" | {label}: {string.Join(", ", conversion.ActiveTrackNumbers.Select(number => number.ToString("00", CultureInfo.InvariantCulture)))}";
    }

    private static WorkflowStatusViewState FormatAnalysis(WorkflowStatusSnapshot snapshot)
    {
        var analysis = snapshot.AnalysisProgress!;
        var percent = ClampPercent(analysis.Percent);

        return analysis.Kind switch
        {
            WorkflowAnalysisKind.SourceTracks => new WorkflowStatusViewState(
                L("ProjectPreparing"),
                L("TrackAnalysisProgress", percent, analysis.Completed, analysis.Total),
                percent),
            WorkflowAnalysisKind.ConvertedTracks => new WorkflowStatusViewState(
                BuildStableContext(snapshot),
                L("AacAnalysisProgress", percent, analysis.Completed, analysis.Total),
                percent),
            WorkflowAnalysisKind.Chapters => new WorkflowStatusViewState(
                L("ReadingChapters"),
                L("ChapterProgress", percent, analysis.Completed, analysis.Total),
                percent),
            _ => new WorkflowStatusViewState(L("ProjectPreparing"), $"{percent} %", percent)
        };
    }

    private static WorkflowStatusViewState FormatReady(WorkflowStatusSnapshot snapshot, bool loaded)
    {
        var teletext = BuildReadyText(snapshot);
        var progress = loaded ? L("Loaded100") : "100 %";
        return new WorkflowStatusViewState(
            teletext,
            progress,
            100,
            ProgressVisualKind: WorkflowProgressVisualKind.Conversion);
    }


    private static WorkflowStatusViewState FormatPresetChangePending(WorkflowStatusSnapshot snapshot)
    {
        var conversion = snapshot.ConversionProgress;
        var total = conversion?.Total ?? snapshot.TotalSourceItems;
        var sourceSummary = snapshot.ProjectKind switch
        {
            WorkflowProjectKind.AudioDisc => L("AudioDiscChapterSummary", GetDiscCount(snapshot), snapshot.TotalChapters),
            WorkflowProjectKind.Mp3Disc => L("Mp3DiscChapterSummary", GetDiscCount(snapshot), snapshot.TotalChapters),
            WorkflowProjectKind.Folder => L("FileChapterSummary", snapshot.TotalSourceItems, snapshot.TotalChapters),
            _ => L("ChapterSummary", snapshot.TotalChapters)
        };
        var preset = conversion is null ? "AAC" : FormatPreset(conversion);
        var completed = Math.Clamp(conversion?.Completed ?? 0, 0, Math.Max(0, total));
        var remaining = Math.Max(0, total - completed);
        var percent = ClampPercent(conversion?.Percent ?? 0);

        return new WorkflowStatusViewState(
            L("PresetChangePending", sourceSummary, remaining, total, preset),
            L("PreparedProgress", percent, completed, total),
            percent,
            ProgressVisualKind: WorkflowProgressVisualKind.Conversion);
    }

    private static WorkflowStatusViewState FormatLoadedProject(WorkflowStatusSnapshot snapshot)
    {
        if (snapshot.ConversionProgress is { Total: > 0 } conversion && conversion.Completed < conversion.Total)
        {
            var sourceSummary = BuildLoadedSourceSummary(snapshot);
            return new WorkflowStatusViewState(
                L("PresetChangePending", sourceSummary, conversion.Completed, conversion.Total, FormatPreset(conversion)),
                L("LoadedProgress", ClampPercent(conversion.Percent)),
                ClampPercent(conversion.Percent));
        }

        if (snapshot.ProjectState == ProjectPipelineState.Completed)
        {
            return new WorkflowStatusViewState(
                L("LoadedReady", BuildLoadedSourceSummary(snapshot), snapshot.TotalChapters),
                L("Loaded100"),
                100);
        }

        return FormatReady(snapshot, loaded: true);
    }

    private static WorkflowStatusViewState FormatSuccessfulExport(WorkflowStatusSnapshot snapshot)
    {
        var sourceSummary = BuildCompletedSourceSummary(snapshot);
        var size = snapshot.OutputFileSizeBytes is > 0 ? $" {FormatFileSize(snapshot.OutputFileSizeBytes.Value)}" : string.Empty;
        return new WorkflowStatusViewState(
            L("ExportSuccess", sourceSummary, snapshot.TotalChapters, size),
            L("Finished100"),
            100,
            ProgressVisualKind: WorkflowProgressVisualKind.Merge);
    }

    private static WorkflowStatusViewState FormatError(WorkflowStatusSnapshot snapshot)
    {
        var error = snapshot.Error!;
        var parts = new List<string>
        {
            $"{DetermineCurrentPercent(snapshot)} %",
            L("ErrorLabel")
        };

        var source = snapshot.SourceProgress;
        if (source is not null)
        {
            if (snapshot.ProjectKind == WorkflowProjectKind.AudioDisc)
                parts.Add(L("DiscCount", "Audio-CD", source.CurrentDisc, source.TotalDiscs));
            else if (snapshot.ProjectKind == WorkflowProjectKind.Mp3Disc)
                parts.Add(L("DiscCount", "MP3-CD", source.CurrentDisc, source.TotalDiscs));

            var verb = source.Kind == SourceAcquisitionKind.Ripping ? L("RippedVerb") : L("CopiedVerb");
            parts.Add($"{source.CompletedProject} / {source.TotalProject} {verb}");
        }

        if (error.FailedTrackOrFileNumber is int failed)
        {
            var noun = snapshot.ProjectKind == WorkflowProjectKind.AudioDisc ? L("TrackLabel") : L("FileLabel");
            parts.Add(L("ItemError", noun, failed));
        }

        if (snapshot.ConversionProgress is not null)
            parts.Add(L("ConvertedCount", snapshot.ConversionProgress.Completed, snapshot.ConversionProgress.Total));
        else if (snapshot.MergeProgress is not null)
            parts.Add(L("FileCount", snapshot.MergeProgress.CurrentFile, snapshot.MergeProgress.TotalFiles));

        return new WorkflowStatusViewState(
            L("ErrorMessage", error.Message),
            string.Join(" | ", parts),
            DetermineCurrentPercent(snapshot));
    }

    private static WorkflowStatusViewState FormatRollback(WorkflowRollbackStatus rollback) => rollback.Phase switch
    {
        WorkflowRollbackPhase.Running => new WorkflowStatusViewState(
            L("RollbackRunning"),
            L("RollbackProgress"),
            0,
            true),
        WorkflowRollbackPhase.Completed => new WorkflowStatusViewState(
            L("RollbackCompleted"),
            L("RollbackFinished"),
            100),
        _ => new WorkflowStatusViewState(string.Empty, string.Empty, 0)
    };

    private static string BuildReadyText(WorkflowStatusSnapshot snapshot)
    {
        var source = snapshot.SourceProgress;
        var conversion = snapshot.ConversionProgress;
        var sourceText = source is not null
            ? BuildSourceTeletext(snapshot.ProjectKind, source with { AllSourcesFinished = true })
            : BuildLoadedSourceSummary(snapshot);
        var conversionText = conversion is not null
            ? $"{conversion.Completed} / {conversion.Total} {FormatPreset(conversion)}"
            : string.Empty;

        return string.IsNullOrWhiteSpace(conversionText)
            ? L("ReadyToMerge", sourceText)
            : L("ReadyToMergeWithConversion", sourceText, conversionText);
    }

    private static string BuildStableContext(WorkflowStatusSnapshot snapshot)
    {
        if (snapshot.SourceProgress is not null || snapshot.ConversionProgress is not null)
            return BuildWorkTeletext(snapshot.ProjectKind, snapshot.SourceProgress, snapshot.ConversionProgress);

        return L("ProjectPreparing");
    }

    private static string BuildLoadedSourceSummary(WorkflowStatusSnapshot snapshot) => snapshot.ProjectKind switch
    {
        WorkflowProjectKind.AudioDisc => L("AudioDiscSummary", GetDiscCount(snapshot)),
        WorkflowProjectKind.Mp3Disc => L("Mp3DiscSummary", GetDiscCount(snapshot)),
        WorkflowProjectKind.Folder => L("FileSummary", snapshot.TotalSourceItems),
        _ => L("ProjectLabel")
    };

    private static string BuildCompletedSourceSummary(WorkflowStatusSnapshot snapshot) => BuildLoadedSourceSummary(snapshot);

    private static int GetDiscCount(WorkflowStatusSnapshot snapshot) =>
        snapshot.SourceProgress?.TotalDiscs > 0 ? snapshot.SourceProgress.TotalDiscs : snapshot.TotalSourceItems;

    private static string FormatPreset(ConversionActivityProgress conversion) =>
        $"AAC {conversion.BitrateKbps} kbps{(conversion.IsMono ? " Mono" : string.Empty)}";

    public static string FormatFileSize(long bytes)
    {
        const long bytesPerMegabyte = 1024L * 1024L;
        const long bytesPerGigabyte = 1024L * 1024L * 1024L;

        if (bytes < bytesPerGigabyte)
            return $"{Math.Round(bytes / (double)bytesPerMegabyte, MidpointRounding.AwayFromZero):0} MB";

        var gigabytes = bytes / (double)bytesPerGigabyte;
        return gigabytes.ToString(gigabytes >= 10 || Math.Abs(gigabytes - Math.Round(gigabytes)) < 0.05 ? "0" : "0.#", CultureInfo.GetCultureInfo("de-DE")) + " GB";
    }

    private static int DetermineCurrentPercent(WorkflowStatusSnapshot snapshot) => ClampPercent(
        snapshot.MergeProgress?.Percent
        ?? (snapshot.SourceProgress is not null && !snapshot.SourceProgress.AllSourcesFinished
            ? snapshot.SourceProgress.Percent
            : snapshot.ConversionProgress?.Percent)
        ?? snapshot.AnalysisProgress?.Percent
        ?? 0);

    private static int ClampPercent(int value) => Math.Clamp(value, 0, 100);
}
