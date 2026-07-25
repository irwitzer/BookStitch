using BookStitch.Models;
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace BookStitch.Services;

public sealed class WorkManifestService
{
    private const int MaxHistoryEntries = 200;

    public ExportWorkManifest LoadOrCreate(
        string manifestPath,
        string projectWorkFolder,
        string sourceFolder,
        string selectedPreset)
    {
        return LoadOrCreate(
            manifestPath,
            ProjectManifestTypes.FolderProject,
            projectWorkFolder,
            sourceFolder,
            selectedPreset);
    }

    public ExportWorkManifest LoadOrCreate(
        string manifestPath,
        string projectType,
        string projectWorkFolder,
        string sourceFolder,
        string selectedPreset)
    {
        try
        {
            if (File.Exists(manifestPath))
            {
                var json = File.ReadAllText(manifestPath, Encoding.UTF8);
                var loaded = JsonSerializer.Deserialize<ExportWorkManifest>(json);

                if (loaded is not null)
                {
                    NormalizeLoadedManifest(loaded, projectType, projectWorkFolder, sourceFolder, selectedPreset);
                    return loaded;
                }
            }
        }
        catch
        {
            // Eine beschädigte project.json darf den Export nicht blockieren.
            // Sie wird unten sauber neu geschrieben.
        }

        return CreateNewManifest(projectType, projectWorkFolder, sourceFolder, selectedPreset);
    }

    public void Save(string manifestPath, ExportWorkManifest manifest)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(manifestPath)!);
        manifest.UpdatedUtc = DateTime.UtcNow;
        NormalizeBeforeSave(manifest);

        var json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        var partPath = manifestPath + ".part";
        File.WriteAllText(partPath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        if (File.Exists(manifestPath))
        {
            File.Replace(partPath, manifestPath, null);
        }
        else
        {
            File.Move(partPath, manifestPath);
        }
    }

    public void UpdateExportSettings(
        ExportWorkManifest manifest,
        string selectedPreset,
        string outputFolder,
        string outputFileName,
        string outputExtension,
        string parallelJobs)
    {
        manifest.SelectedPreset = selectedPreset;
        manifest.Export.SelectedPreset = selectedPreset;
        manifest.Export.OutputFolder = outputFolder ?? "";
        manifest.Export.OutputFileName = outputFileName ?? "";
        manifest.Export.OutputExtension = outputExtension ?? "";
        manifest.Export.ParallelJobs = parallelJobs ?? "";
    }

    public void UpdateBookMetadata(
        ExportWorkManifest manifest,
        string title,
        string author,
        string album,
        string narrator,
        string genre,
        string coverSourcePath,
        string processedCoverPath)
    {
        manifest.Metadata.Title = title ?? "";
        manifest.Metadata.Author = author ?? "";
        manifest.Metadata.Album = album ?? "";
        manifest.Metadata.Narrator = narrator ?? "";
        manifest.Metadata.Genre = genre ?? "";
        manifest.Metadata.CoverSourcePath = coverSourcePath ?? "";
        manifest.Metadata.ProcessedCoverPath = processedCoverPath ?? "";
    }

    public void MarkConversionPreparationStarted(ExportWorkManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        var wasInterrupted = manifest.Resume.CanResume &&
            (manifest.State.CancelRequestedUtc.HasValue || manifest.State.LastErrorUtc.HasValue);
        var isAcquiringSources = string.Equals(
            manifest.State.Status,
            ProjectManifestStatuses.AcquiringSources,
            StringComparison.OrdinalIgnoreCase);
        var isAlreadyRunning = isAcquiringSources || string.Equals(
            manifest.State.Status,
            ProjectManifestStatuses.Converting,
            StringComparison.OrdinalIgnoreCase);

        manifest.State.Status = isAcquiringSources
            ? ProjectManifestStatuses.AcquiringSources
            : ProjectManifestStatuses.Converting;
        manifest.State.CancelRequestedUtc = null;
        manifest.State.LastErrorUtc = null;
        manifest.State.LastErrorSummary = "";
        manifest.Resume.CanResume = true;

        if (wasInterrupted)
        {
            manifest.Resume.Reason = "AAC-Vorbereitung wurde fortgesetzt.";
            AddHistory(manifest, "ConversionPreparationResumed", manifest.Resume.Reason);
            return;
        }

        if (isAlreadyRunning)
        {
            manifest.Resume.Reason = "AAC-Vorbereitung läuft.";
            return;
        }

        manifest.Resume.Reason = "AAC-Vorbereitung wurde gestartet.";
        AddHistory(manifest, "ConversionPreparationStarted", manifest.Resume.Reason);
    }

    public void MarkExportStarted(ExportWorkManifest manifest)
    {
        manifest.State.Status = ProjectManifestStatuses.Converting;
        manifest.State.CancelRequestedUtc = null;
        manifest.State.LastErrorUtc = null;
        manifest.State.LastErrorSummary = "";
        manifest.Resume.CanResume = true;
        manifest.Resume.Reason = "Export wurde gestartet.";
        AddHistory(manifest, "ExportStarted", "Export wurde gestartet.");
    }

    public void MarkMergingStarted(ExportWorkManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        manifest.State.Status = ProjectManifestStatuses.Merging;
        manifest.State.CancelRequestedUtc = null;
        manifest.State.LastErrorUtc = null;
        manifest.State.LastErrorSummary = "";
        manifest.Resume.CanResume = true;
        manifest.Resume.Reason = "Zusammenfügen läuft.";
        AddHistory(manifest, "MergingStarted", manifest.Resume.Reason);
    }

    public void MarkExportCanceled(ExportWorkManifest manifest, string reason)
    {
        manifest.State.Status = string.Equals(
            manifest.State.Status,
            ProjectManifestStatuses.AcquiringSources,
            StringComparison.OrdinalIgnoreCase)
            ? ProjectManifestStatuses.AcquiringSources
            : ProjectManifestStatuses.ReviewBeforeMerge;
        manifest.State.CancelRequestedUtc = DateTime.UtcNow;
        manifest.Resume.CanResume = true;
        manifest.Resume.Reason = string.IsNullOrWhiteSpace(reason)
            ? "Export wurde abgebrochen."
            : reason;
        manifest.Resume.LastCleanupUtc = DateTime.UtcNow;
        AddHistory(manifest, "ExportCanceled", manifest.Resume.Reason);
    }

    public void MarkExportFailed(ExportWorkManifest manifest, string errorSummary)
    {
        manifest.State.Status = string.Equals(
            manifest.State.Status,
            ProjectManifestStatuses.AcquiringSources,
            StringComparison.OrdinalIgnoreCase)
            ? ProjectManifestStatuses.AcquiringSources
            : ProjectManifestStatuses.ReviewBeforeMerge;
        manifest.State.LastErrorUtc = DateTime.UtcNow;
        manifest.State.LastErrorSummary = errorSummary ?? "";
        manifest.Resume.CanResume = true;
        manifest.Resume.Reason = "Export ist fehlgeschlagen und kann nach Prüfung fortgesetzt werden.";
        manifest.Resume.LastCleanupUtc = DateTime.UtcNow;
        AddHistory(manifest, "ExportFailed", manifest.State.LastErrorSummary);
    }

    public void MarkConversionCompleted(ExportWorkManifest manifest, string selectedPreset)
    {
        manifest.State.Status = ProjectManifestStatuses.ReviewBeforeMerge;
        manifest.State.LastSuccessfulStep = "ConversionCompleted";
        manifest.Resume.CanResume = false;
        manifest.Resume.Reason = "Alle Tracks wurden vorbereitet. Das Projekt kann geöffnet und final zusammengefügt werden.";
        MarkManualMergeReviewCompleted(manifest, selectedPreset);
        AddHistory(manifest, "ConversionCompleted", "Alle Tracks wurden vorbereitet.");
    }

    public bool HasCompletedManualMergeReview(ExportWorkManifest manifest, string selectedPreset)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        var normalizedPreset = (selectedPreset ?? string.Empty).Trim();
        if (normalizedPreset.Length == 0)
            return false;

        return manifest.State.ManualMergeReviewCompletedPresets.Any(preset =>
            string.Equals(preset, normalizedPreset, StringComparison.OrdinalIgnoreCase));
    }

    public void MarkManualMergeReviewCompleted(ExportWorkManifest manifest, string selectedPreset)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        var normalizedPreset = (selectedPreset ?? string.Empty).Trim();
        if (normalizedPreset.Length == 0 || HasCompletedManualMergeReview(manifest, normalizedPreset))
            return;

        manifest.State.ManualMergeReviewCompletedPresets.Add(normalizedPreset);
    }

    public void MarkExportCompleted(ExportWorkManifest manifest)
    {
        manifest.State.Status = ProjectManifestStatuses.Completed;
        manifest.State.LastSuccessfulStep = "ExportCompleted";
        manifest.Resume.CanResume = false;
        manifest.Resume.Reason = "Export wurde abgeschlossen.";
        AddHistory(manifest, "ExportCompleted", "Export wurde abgeschlossen.");
    }

    public void MarkTrackStarted(ExportWorkManifest manifest, int index, string status)
    {
        var trackIndex = index + 1;
        manifest.State.LastStartedTrackIndex = trackIndex;

        var entry = manifest.Tracks.FirstOrDefault(track => track.TrackIndex == trackIndex);

        if (entry is not null)
        {
            entry.Status = status;
            entry.StartedUtc = DateTime.UtcNow;
            entry.LastError = "";
        }

        AddHistory(manifest, "TrackStarted", status, trackIndex: trackIndex);
    }

    public void MarkTrackStarted(
        ExportWorkManifest manifest,
        int index,
        TrackInfo track,
        string sourcePath,
        string convertedPath,
        ExportPreset preset,
        string status)
    {
        var trackIndex = index + 1;
        var entry = FindTrackEntry(manifest, track, sourcePath, convertedPath, preset);

        if (entry is null)
        {
            entry = CreateTrackEntry(index, track, sourcePath, convertedPath, preset);
            manifest.Tracks.Add(entry);
        }

        entry.TrackIndex = trackIndex;
        entry.Status = status;
        entry.StartedUtc = DateTime.UtcNow;
        entry.LastError = "";

        manifest.State.LastStartedTrackIndex = trackIndex;
        AddHistory(manifest, "TrackStarted", status, trackIndex: trackIndex);
    }

    public void MarkTrackFailed(ExportWorkManifest manifest, int index, string errorSummary)
    {
        var trackIndex = index + 1;
        var entry = manifest.Tracks.FirstOrDefault(track => track.TrackIndex == trackIndex);

        if (entry is not null)
        {
            entry.Status = ProjectManifestTrackStatuses.Failed;
            entry.LastError = errorSummary ?? "";
        }

        manifest.State.LastErrorUtc = DateTime.UtcNow;
        manifest.State.LastErrorSummary = errorSummary ?? "";
        AddHistory(manifest, "TrackFailed", errorSummary ?? "", trackIndex: trackIndex);
    }

    public void MarkTrackFailed(
        ExportWorkManifest manifest,
        int index,
        TrackInfo track,
        string sourcePath,
        string convertedPath,
        ExportPreset preset,
        string errorSummary)
    {
        var trackIndex = index + 1;
        var entry = FindTrackEntry(manifest, track, sourcePath, convertedPath, preset);

        if (entry is null)
        {
            entry = CreateTrackEntry(index, track, sourcePath, convertedPath, preset);
            manifest.Tracks.Add(entry);
        }

        entry.TrackIndex = trackIndex;
        entry.Status = ProjectManifestTrackStatuses.Failed;
        entry.LastError = errorSummary ?? "";

        manifest.State.LastErrorUtc = DateTime.UtcNow;
        manifest.State.LastErrorSummary = errorSummary ?? "";
        AddHistory(manifest, "TrackFailed", errorSummary ?? "", trackIndex: trackIndex);
    }

    public void MarkTrackCanceled(
        ExportWorkManifest manifest,
        int index,
        TrackInfo track,
        string sourcePath,
        string convertedPath,
        ExportPreset preset)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(track);
        ArgumentNullException.ThrowIfNull(preset);

        var trackIndex = index + 1;
        var entry = FindTrackEntry(manifest, track, sourcePath, convertedPath, preset);

        if (entry is null)
        {
            entry = CreateTrackEntry(index, track, sourcePath, convertedPath, preset);
            manifest.Tracks.Add(entry);
        }

        entry.TrackIndex = trackIndex;
        entry.Status = ProjectManifestTrackStatuses.Canceled;
        entry.CompletedUtc = null;
        entry.LastError = "";

        manifest.State.Status = string.Equals(
            manifest.State.Status,
            ProjectManifestStatuses.AcquiringSources,
            StringComparison.OrdinalIgnoreCase)
            ? ProjectManifestStatuses.AcquiringSources
            : ProjectManifestStatuses.ReviewBeforeMerge;
        manifest.State.CancelRequestedUtc = DateTime.UtcNow;
        manifest.Resume.CanResume = true;
        manifest.Resume.Reason = "AAC-Vorbereitung wurde abgebrochen.";
        AddHistory(manifest, "TrackCanceled", "AAC-Vorbereitung wurde abgebrochen.", trackIndex: trackIndex);
    }


    public void PruneInvalidEntries(ExportWorkManifest manifest)
    {
        manifest.Tracks = manifest.Tracks
            .Where(entry =>
                !string.IsNullOrWhiteSpace(entry.ConvertedPath) &&
                File.Exists(entry.ConvertedPath) &&
                new FileInfo(entry.ConvertedPath).Length > 0)
            .ToList();
    }

    public int CountReusableConvertedTracks(
        ExportWorkManifest manifest,
        ExportPreset preset)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(preset);

        return manifest.Tracks
            .Where(entry => IsReusableConvertedManifestEntry(entry, preset.DisplayName))
            .DistinctBy(
                entry => $"{entry.SourcePath}\0{entry.ConvertedPath}",
                StringComparer.OrdinalIgnoreCase)
            .Count();
    }

    private static bool IsReusableConvertedManifestEntry(
        ExportWorkManifestTrack entry,
        string presetDisplayName)
    {
        if (!string.Equals(entry.Status, ProjectManifestTrackStatuses.Converted, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(entry.Preset, presetDisplayName, StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(entry.SourcePath) ||
            string.IsNullOrWhiteSpace(entry.ConvertedPath))
        {
            return false;
        }

        try
        {
            var sourceInfo = new FileInfo(entry.SourcePath);
            var convertedInfo = new FileInfo(entry.ConvertedPath);

            return sourceInfo.Exists &&
                   convertedInfo.Exists &&
                   sourceInfo.Length > 0 &&
                   convertedInfo.Length > 0 &&
                   entry.SourceSizeBytes == sourceInfo.Length &&
                   entry.SourceLastWriteUtcTicks == sourceInfo.LastWriteTimeUtc.Ticks &&
                   entry.ConvertedSizeBytes == convertedInfo.Length &&
                   entry.ConvertedSizeBytes > 0;
        }
        catch
        {
            return false;
        }
    }

    public bool CanReuseConvertedTrack(
        ExportWorkManifest manifest,
        int index,
        TrackInfo track,
        string sourcePath,
        string convertedPath,
        ExportPreset preset)
    {
        if (!File.Exists(sourcePath) || !File.Exists(convertedPath))
            return false;

        var sourceInfo = new FileInfo(sourcePath);
        var convertedInfo = new FileInfo(convertedPath);

        if (sourceInfo.Length <= 0 || convertedInfo.Length <= 0)
            return false;

        var action = AudioProcessingService.NormalizeProcessingAction(track.ProcessingAction);
        var sourceLastWriteTicks = sourceInfo.LastWriteTimeUtc.Ticks;
        var sourceFullPath = Path.GetFullPath(sourcePath);
        var convertedFullPath = Path.GetFullPath(convertedPath);

        return manifest.Tracks.Any(entry =>
            string.Equals(entry.SourcePath, sourceFullPath, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(entry.ConvertedPath, convertedFullPath, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(entry.Preset, preset.DisplayName, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(entry.Action, action, StringComparison.OrdinalIgnoreCase) &&
            entry.SourceSizeBytes == sourceInfo.Length &&
            entry.SourceLastWriteUtcTicks == sourceLastWriteTicks &&
            entry.ConvertedSizeBytes == convertedInfo.Length &&
            entry.ConvertedSizeBytes > 0 &&
            string.Equals(entry.Status, ProjectManifestTrackStatuses.Converted, StringComparison.OrdinalIgnoreCase));
    }

    public bool MarkTrackPendingForPreparation(
        ExportWorkManifest manifest,
        int index,
        TrackInfo track,
        string sourcePath,
        string convertedPath,
        ExportPreset preset)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(track);
        ArgumentNullException.ThrowIfNull(preset);

        var sourceFullPath = Path.GetFullPath(sourcePath);
        var convertedFullPath = Path.GetFullPath(convertedPath);
        var action = AudioProcessingService.NormalizeProcessingAction(track.ProcessingAction);

        var matchingEntries = manifest.Tracks
            .Where(entry => IsSameTrackCacheEntry(
                entry,
                sourceFullPath,
                convertedFullPath,
                preset.DisplayName,
                action))
            .ToList();

        if (matchingEntries.Count == 0)
            return false;

        manifest.Tracks.RemoveAll(entry => matchingEntries.Contains(entry));

        var pendingEntry = CreateTrackEntry(
            index,
            track,
            sourcePath,
            convertedPath,
            preset);

        pendingEntry.Status = ProjectManifestTrackStatuses.Pending;
        pendingEntry.StartedUtc = null;
        pendingEntry.CompletedUtc = null;
        pendingEntry.LastError = "";

        manifest.Tracks.Add(pendingEntry);
        NormalizeTrackProgress(manifest);

        return true;
    }

    public void UpdateTrack(
        ExportWorkManifest manifest,
        int index,
        TrackInfo track,
        string sourcePath,
        string convertedPath,
        ExportPreset preset)
    {
        var sourceInfo = new FileInfo(sourcePath);
        var convertedInfo = new FileInfo(convertedPath);
        var trackIndex = index + 1;

        var sourceFullPath = Path.GetFullPath(sourcePath);
        var convertedFullPath = Path.GetFullPath(convertedPath);

        var action = AudioProcessingService.NormalizeProcessingAction(track.ProcessingAction);

        manifest.Tracks.RemoveAll(entry => IsSameTrackCacheEntry(
            entry,
            sourceFullPath,
            convertedFullPath,
            preset.DisplayName,
            action));

        manifest.Tracks.Add(new ExportWorkManifestTrack
        {
            TrackIndex = trackIndex,
            SourcePath = sourceFullPath,
            SourceFileName = track.FileName,
            SourceSizeBytes = sourceInfo.Length,
            SourceLastWriteUtcTicks = sourceInfo.LastWriteTimeUtc.Ticks,
            SourceCodec = track.Codec ?? "",
            SourceExtension = NormalizeSourceExtension(track.Extension),
            Duration = track.Duration ?? "",
            DurationTicks = track.DurationTicks,
            ChapterTitle = track.ChapterTitle ?? "",
            Action = action,
            Preset = preset.DisplayName,
            Status = ProjectManifestTrackStatuses.Converted,
            ConvertedPath = convertedFullPath,
            ConvertedSizeBytes = convertedInfo.Length,
            ConvertedLastWriteUtcTicks = convertedInfo.LastWriteTimeUtc.Ticks,
            CompletedUtc = DateTime.UtcNow
        });

        manifest.State.LastCompletedTrackIndex = Math.Max(manifest.State.LastCompletedTrackIndex, trackIndex);
        manifest.State.LastSuccessfulStep = "TrackConverted";
        AddHistory(manifest, "TrackConverted", "Track wurde fertig vorbereitet.", trackIndex: trackIndex);
    }

    public void AddHistory(
        ExportWorkManifest manifest,
        string eventName,
        string message,
        int? trackIndex = null,
        int? discIndex = null)
    {
        manifest.History.Add(new ExportWorkManifestEvent
        {
            Utc = DateTime.UtcNow,
            Event = eventName ?? "",
            Message = message ?? "",
            TrackIndex = trackIndex,
            DiscIndex = discIndex
        });

        TrimHistory(manifest);
    }

    private static ExportWorkManifestTrack? FindTrackEntry(
        ExportWorkManifest manifest,
        TrackInfo track,
        string sourcePath,
        string convertedPath,
        ExportPreset preset)
    {
        var sourceFullPath = Path.GetFullPath(sourcePath);
        var convertedFullPath = Path.GetFullPath(convertedPath);
        var action = AudioProcessingService.NormalizeProcessingAction(track.ProcessingAction);

        return manifest.Tracks.FirstOrDefault(entry => IsSameTrackCacheEntry(
            entry,
            sourceFullPath,
            convertedFullPath,
            preset.DisplayName,
            action));
    }

    private static ExportWorkManifestTrack CreateTrackEntry(
        int index,
        TrackInfo track,
        string sourcePath,
        string convertedPath,
        ExportPreset preset)
    {
        var sourceInfo = new FileInfo(sourcePath);

        return new ExportWorkManifestTrack
        {
            TrackIndex = index + 1,
            SourcePath = Path.GetFullPath(sourcePath),
            SourceFileName = track.FileName,
            SourceSizeBytes = sourceInfo.Length,
            SourceLastWriteUtcTicks = sourceInfo.LastWriteTimeUtc.Ticks,
            SourceCodec = track.Codec ?? "",
            SourceExtension = NormalizeSourceExtension(track.Extension),
            Duration = track.Duration ?? "",
            DurationTicks = track.DurationTicks,
            ChapterTitle = track.ChapterTitle ?? "",
            Action = AudioProcessingService.NormalizeProcessingAction(track.ProcessingAction),
            Preset = preset.DisplayName,
            Status = ProjectManifestTrackStatuses.Pending,
            ConvertedPath = Path.GetFullPath(convertedPath),
            ConvertedSizeBytes = 0,
            ConvertedLastWriteUtcTicks = 0
        };
    }

    private static bool IsSameTrackCacheEntry(
        ExportWorkManifestTrack entry,
        string sourceFullPath,
        string convertedFullPath,
        string presetDisplayName,
        string action)
    {
        if (!string.Equals(entry.Preset, presetDisplayName, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(entry.Action, action, StringComparison.OrdinalIgnoreCase))
            return false;

        if (string.Equals(entry.ConvertedPath, convertedFullPath, StringComparison.OrdinalIgnoreCase))
            return true;

        return string.Equals(entry.SourcePath, sourceFullPath, StringComparison.OrdinalIgnoreCase);
    }

    private static ExportWorkManifest CreateNewManifest(
        string projectType,
        string projectWorkFolder,
        string sourceFolder,
        string selectedPreset)
    {
        var manifest = new ExportWorkManifest
        {
            FormatVersion = ExportWorkManifestVersions.Current,
            ProjectType = NormalizeProjectType(projectType),
            ProjectId = Guid.NewGuid().ToString("N"),
            ProjectWorkFolder = projectWorkFolder,
            SourceFolder = sourceFolder,
            SelectedPreset = selectedPreset,
            CreatedUtc = DateTime.UtcNow,
            UpdatedUtc = DateTime.UtcNow,
            Tracks = [],
            Discs = [],
            History = []
        };

        manifest.Export.SelectedPreset = selectedPreset;
        manifest.State.Status = ProjectManifestStatuses.Preparing;
        manifest.Resume.CanResume = false;
        manifest.Resume.Reason = "Neues Projekt.";

        return manifest;
    }

    private static void NormalizeLoadedManifest(
        ExportWorkManifest manifest,
        string projectType,
        string projectWorkFolder,
        string sourceFolder,
        string selectedPreset)
    {
        manifest.FormatVersion = string.IsNullOrWhiteSpace(manifest.FormatVersion)
            ? ExportWorkManifestVersions.Current
            : manifest.FormatVersion;

        manifest.ProjectType = NormalizeProjectType(string.IsNullOrWhiteSpace(manifest.ProjectType)
            ? projectType
            : manifest.ProjectType);

        if (string.IsNullOrWhiteSpace(manifest.ProjectId))
            manifest.ProjectId = Guid.NewGuid().ToString("N");

        manifest.ProjectWorkFolder = projectWorkFolder;
        manifest.SourceFolder = sourceFolder;
        manifest.SelectedPreset = selectedPreset;

        manifest.Export ??= new ExportManifestExportSettings();
        manifest.Metadata ??= new ExportManifestBookMetadata();
        manifest.State ??= new ExportManifestState();
        manifest.State.ManualMergeReviewCompletedPresets ??= [];
        manifest.Resume ??= new ExportManifestResume();
        manifest.Tracks ??= [];
        manifest.Discs ??= [];
        manifest.History ??= [];

        if (string.IsNullOrWhiteSpace(manifest.Export.SelectedPreset))
            manifest.Export.SelectedPreset = selectedPreset;

        var sourcesComplete = manifest.Tracks.Count > 0 && manifest.Tracks.All(track =>
            !string.IsNullOrWhiteSpace(track.SourcePath) && File.Exists(track.SourcePath));
        var hasSuccessfulExport = string.Equals(
            manifest.State.Status,
            ProjectManifestStatuses.Completed,
            StringComparison.OrdinalIgnoreCase) ||
            string.Equals(
                manifest.State.LastSuccessfulStep,
                "ExportCompleted",
                StringComparison.OrdinalIgnoreCase);
        manifest.State.Status = ProjectPipelineStateNames.FromManifestValue(
            manifest.State.Status,
            hasSuccessfulExport,
            sourcesComplete).ToManifestValue();

        NormalizeTrackProgress(manifest);
    }

    private static string NormalizeProjectType(string projectType)
    {
        return projectType switch
        {
            ProjectManifestTypes.Mp3DiscProject => ProjectManifestTypes.Mp3DiscProject,
            ProjectManifestTypes.AudioCdProject => ProjectManifestTypes.AudioCdProject,
            _ => ProjectManifestTypes.FolderProject
        };
    }

    private static void NormalizeBeforeSave(ExportWorkManifest manifest)
    {
        manifest.State ??= new ExportManifestState();
        manifest.State.ManualMergeReviewCompletedPresets ??= [];
        manifest.State.ManualMergeReviewCompletedPresets = manifest.State.ManualMergeReviewCompletedPresets
            .Where(preset => !string.IsNullOrWhiteSpace(preset))
            .Select(preset => preset.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        manifest.Tracks = manifest.Tracks
            .OrderBy(track => track.TrackIndex)
            .ToList();

        manifest.Discs = manifest.Discs
            .OrderBy(disc => disc.DiscIndex)
            .ToList();

        NormalizeTrackProgress(manifest);
        TrimHistory(manifest);
    }


    private static void NormalizeTrackProgress(ExportWorkManifest manifest)
    {
        var highestCompletedTrackIndex = manifest.Tracks
            .Where(track => string.Equals(
                track.Status,
                ProjectManifestTrackStatuses.Converted,
                StringComparison.OrdinalIgnoreCase))
            .Select(track => track.TrackIndex)
            .DefaultIfEmpty(0)
            .Max();

        manifest.State.LastCompletedTrackIndex = highestCompletedTrackIndex;
    }

    private static string NormalizeSourceExtension(string? extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
            return "";

        var normalized = extension.Trim().ToLowerInvariant();

        return normalized.StartsWith(".", StringComparison.Ordinal)
            ? normalized
            : "." + normalized;
    }

    private static void TrimHistory(ExportWorkManifest manifest)
    {
        if (manifest.History.Count <= MaxHistoryEntries)
            return;

        manifest.History = manifest.History
            .Skip(Math.Max(0, manifest.History.Count - MaxHistoryEntries))
            .ToList();
    }
}
