using System.Globalization;
using BookStitch.Models;

namespace BookStitch.Services;

public sealed class WorkflowStatusFormatter
{
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
                $"Warnung | {snapshot.Warning.Message}",
                "100 % | wartet auf Bestätigung",
                100);
        }

        if (snapshot.IsMergeAborted)
        {
            var percent = ClampPercent(snapshot.MergeProgress?.Percent ?? 0);
            return new WorkflowStatusViewState(
                "Zusammenfügen abgebrochen | Bereit zum Zusammenfügen",
                $"{percent} % | abgebrochen",
                percent,
                ProgressVisualKind: WorkflowProgressVisualKind.Merge);
        }

        if (snapshot.IsExportAborted)
            return new WorkflowStatusViewState("Export abgebrochen", "100 % | bereit", 100);

        if (snapshot.MergeProgress is { IsWritingMetadata: true })
        {
            return new WorkflowStatusViewState(
                "Zusammenfügen abgeschlossen, Metadaten werden geschrieben ...",
                "100 % | Metadaten werden geschrieben ...",
                100,
                ProgressVisualKind: WorkflowProgressVisualKind.Merge);
        }

        if (snapshot.MergeProgress is not null)
        {
            return new WorkflowStatusViewState(
                "Zusammenfügen ...",
                $"{ClampPercent(snapshot.MergeProgress.Percent)} % | Datei {snapshot.MergeProgress.CurrentFile} von {snapshot.MergeProgress.TotalFiles}",
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
                $"Projekt vorbereitet • {count} Tracks erkannt",
                "100 % | Track-Analyse abgeschlossen",
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
            return new WorkflowStatusViewState("Projekt wird gestartet ...", "0 % | Initialisierung", 0);

        return new WorkflowStatusViewState("Quelle auswählen oder Projekt öffnen.", "0 % | bereit", 0);
    }

    private static WorkflowStatusViewState FormatWork(WorkflowStatusSnapshot snapshot)
    {
        var source = snapshot.SourceProgress;
        var conversion = snapshot.ConversionProgress;
        var prefix = snapshot.IsPaused ? "Pause • " : snapshot.IsExtension ? "Projekt wird erweitert • " : string.Empty;
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
            return conversion is null ? "Pausiert" : BuildConversionTeletext(conversion);

        string sourceText;
        if (projectKind is WorkflowProjectKind.AudioDisc or WorkflowProjectKind.Mp3Disc)
        {
            var label = projectKind == WorkflowProjectKind.AudioDisc ? "Audio-CD" : "MP3-CD";
            var suffix = source.Kind == SourceAcquisitionKind.Ripping && !string.IsNullOrWhiteSpace(source.WorkingFormat)
                ? $" {source.WorkingFormat}"
                : string.Empty;
            sourceText = $"{label} {source.CurrentDisc} von {source.TotalDiscs} • {source.CompletedCurrentSource:00} / {source.TotalCurrentSource:00}{suffix}";
        }
        else
        {
            sourceText = $"Kopieren • {source.CompletedProject} / {source.TotalProject}";
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
            var label = projectKind == WorkflowProjectKind.AudioDisc ? "Audio-CD" : "MP3-CD";
            if (source.AllSourcesFinished)
                return $"{label} {source.TotalDiscs} von {source.TotalDiscs} fertig";

            if (source.CurrentSourceFinished)
                return $"{label} {Math.Min(source.CurrentDisc + 1, source.TotalDiscs)} von {source.TotalDiscs} einlegen";

            var verb = source.Kind == SourceAcquisitionKind.Ripping ? "wird gerippt" : "wird kopiert";
            var suffix = source.Kind == SourceAcquisitionKind.Ripping && !string.IsNullOrWhiteSpace(source.WorkingFormat)
                ? $" {source.WorkingFormat}"
                : string.Empty;
            return $"{label} {source.CurrentDisc} von {source.TotalDiscs} {verb} • {source.CompletedCurrentSource:00} / {source.TotalCurrentSource:00}{suffix}";
        }

        if (source.AllSourcesFinished)
            return $"Kopieren abgeschlossen {source.CompletedProject} / {source.TotalProject}";

        return $"Kopieren • {source.CompletedProject} / {source.TotalProject}";
    }

    private static string BuildConversionTeletext(ConversionActivityProgress conversion)
    {
        var label = conversion.IsLive ? "Live-Konvertierung" : "Neu konvertieren";
        return $"{label} {conversion.Completed} / {conversion.Total} {FormatPreset(conversion)}";
    }

    private static string BuildWorkProgress(
        WorkflowStatusSnapshot snapshot,
        SourceAcquisitionProgress? source,
        ConversionActivityProgress? conversion)
    {
        var paused = snapshot.IsPaused ? " | pausiert" : string.Empty;

        if (source is not null && !source.AllSourcesFinished && !source.CurrentSourceFinished)
        {
            var verb = source.Kind == SourceAcquisitionKind.Ripping ? "gerippt" : "kopiert";
            var active = FormatActiveConversionJobs(conversion, "Konvertierung");
            var completed = source.CompletedProject;
            var total = source.TotalProject;
            return $"{ClampPercent(source.Percent)} %{paused} | {completed} / {total} {verb}{active}";
        }

        if (conversion is not null)
        {
            var active = FormatActiveConversionJobs(conversion, "Aktive Jobs");
            return $"{ClampPercent(conversion.Percent)} %{paused} | {conversion.Completed} / {conversion.Total} konvertiert{active}";
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
                "Projekt wird vorbereitet ...",
                $"{percent} % | Track-Analyse läuft • {analysis.Completed} / {analysis.Total}",
                percent),
            WorkflowAnalysisKind.ConvertedTracks => new WorkflowStatusViewState(
                BuildStableContext(snapshot),
                $"{percent} % | AAC-Analyse läuft • {analysis.Completed} / {analysis.Total}",
                percent),
            WorkflowAnalysisKind.Chapters => new WorkflowStatusViewState(
                "Kapitel werden eingelesen ...",
                $"{percent} % | Kapitel {analysis.Completed} / {analysis.Total}",
                percent),
            _ => new WorkflowStatusViewState("Projekt wird vorbereitet ...", $"{percent} %", percent)
        };
    }

    private static WorkflowStatusViewState FormatReady(WorkflowStatusSnapshot snapshot, bool loaded)
    {
        var teletext = BuildReadyText(snapshot);
        var progress = loaded ? "100 % | geladenes Projekt" : "100 %";
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
            WorkflowProjectKind.AudioDisc => $"{GetDiscCount(snapshot)} Audio-CDs / {snapshot.TotalChapters} Kapitel",
            WorkflowProjectKind.Mp3Disc => $"{GetDiscCount(snapshot)} MP3-CDs / {snapshot.TotalChapters} Kapitel",
            WorkflowProjectKind.Folder => $"{snapshot.TotalSourceItems} Dateien / {snapshot.TotalChapters} Kapitel",
            _ => $"{snapshot.TotalChapters} Kapitel"
        };
        var preset = conversion is null ? "AAC" : FormatPreset(conversion);

        return new WorkflowStatusViewState(
            $"{sourceSummary} | Neu konvertieren • 0 / {total} {preset}",
            "0 % | bereit",
            0,
            ProgressVisualKind: WorkflowProgressVisualKind.Conversion);
    }

    private static WorkflowStatusViewState FormatLoadedProject(WorkflowStatusSnapshot snapshot)
    {
        if (snapshot.ConversionProgress is { Total: > 0 } conversion && conversion.Completed < conversion.Total)
        {
            var sourceSummary = BuildLoadedSourceSummary(snapshot);
            return new WorkflowStatusViewState(
                $"{sourceSummary} | Neu konvertieren • {conversion.Completed} / {conversion.Total} {FormatPreset(conversion)}",
                $"{ClampPercent(conversion.Percent)} % | geladenes Projekt",
                ClampPercent(conversion.Percent));
        }

        if (snapshot.ProjectState == ProjectPipelineState.Completed)
        {
            return new WorkflowStatusViewState(
                $"{BuildLoadedSourceSummary(snapshot)} / {snapshot.TotalChapters} Kapitel | Bereit",
                "100 % | geladenes Projekt",
                100);
        }

        return FormatReady(snapshot, loaded: true);
    }

    private static WorkflowStatusViewState FormatSuccessfulExport(WorkflowStatusSnapshot snapshot)
    {
        var sourceSummary = BuildCompletedSourceSummary(snapshot);
        var size = snapshot.OutputFileSizeBytes is > 0 ? $" {FormatFileSize(snapshot.OutputFileSizeBytes.Value)}" : string.Empty;
        return new WorkflowStatusViewState(
            $"{sourceSummary} / {snapshot.TotalChapters} Kapitel{size} | Hörbuch erfolgreich erstellt.",
            "100 % | fertig",
            100,
            ProgressVisualKind: WorkflowProgressVisualKind.Merge);
    }

    private static WorkflowStatusViewState FormatError(WorkflowStatusSnapshot snapshot)
    {
        var error = snapshot.Error!;
        var parts = new List<string>
        {
            $"{DetermineCurrentPercent(snapshot)} %",
            "Fehler"
        };

        var source = snapshot.SourceProgress;
        if (source is not null)
        {
            if (snapshot.ProjectKind == WorkflowProjectKind.AudioDisc)
                parts.Add($"Audio-CD {source.CurrentDisc} von {source.TotalDiscs}");
            else if (snapshot.ProjectKind == WorkflowProjectKind.Mp3Disc)
                parts.Add($"MP3-CD {source.CurrentDisc} von {source.TotalDiscs}");

            var verb = source.Kind == SourceAcquisitionKind.Ripping ? "gerippt" : "kopiert";
            parts.Add($"{source.CompletedProject} / {source.TotalProject} {verb}");
        }

        if (error.FailedTrackOrFileNumber is int failed)
        {
            var noun = snapshot.ProjectKind == WorkflowProjectKind.AudioDisc ? "Track" : "Datei";
            parts.Add($"Fehler bei {noun} {failed}");
        }

        if (snapshot.ConversionProgress is not null)
            parts.Add($"{snapshot.ConversionProgress.Completed} / {snapshot.ConversionProgress.Total} konvertiert");
        else if (snapshot.MergeProgress is not null)
            parts.Add($"Datei {snapshot.MergeProgress.CurrentFile} von {snapshot.MergeProgress.TotalFiles}");

        return new WorkflowStatusViewState(
            $"Fehler | {error.Message}",
            string.Join(" | ", parts),
            DetermineCurrentPercent(snapshot));
    }

    private static WorkflowStatusViewState FormatRollback(WorkflowRollbackStatus rollback) => rollback.Phase switch
    {
        WorkflowRollbackPhase.Running => new WorkflowStatusViewState(
            "Erweiterung abgebrochen | Änderungen werden zurückgesetzt ...",
            "Rollback läuft ...",
            0,
            true),
        WorkflowRollbackPhase.Completed => new WorkflowStatusViewState(
            "Erweiterung zurückgesetzt | Projekt wiederhergestellt",
            "Rollback abgeschlossen",
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
            ? $"{sourceText} | Bereit zum Zusammenfügen"
            : $"{sourceText} | {conversionText} | Bereit zum Zusammenfügen";
    }

    private static string BuildStableContext(WorkflowStatusSnapshot snapshot)
    {
        if (snapshot.SourceProgress is not null || snapshot.ConversionProgress is not null)
            return BuildWorkTeletext(snapshot.ProjectKind, snapshot.SourceProgress, snapshot.ConversionProgress);

        return "Projekt wird vorbereitet ...";
    }

    private static string BuildLoadedSourceSummary(WorkflowStatusSnapshot snapshot) => snapshot.ProjectKind switch
    {
        WorkflowProjectKind.AudioDisc => $"{GetDiscCount(snapshot)} Audio-CDs",
        WorkflowProjectKind.Mp3Disc => $"{GetDiscCount(snapshot)} MP3-CDs",
        WorkflowProjectKind.Folder => $"{snapshot.TotalSourceItems} Dateien",
        _ => "Projekt"
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
