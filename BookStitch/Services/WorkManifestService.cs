using BookStitch.Models;
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Runtime.CompilerServices;

namespace BookStitch.Services;

public sealed class WorkManifestService
{
    private const int MaxHistoryEntries = 200;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };
    private static readonly AtomicJsonStore JsonStore = new();
    private readonly AtomicJsonStore _jsonStore;
    private readonly PerformanceTraceService? _trace;
    public WorkManifestService() : this(JsonStore) { }
    public WorkManifestService(PerformanceTraceService trace) : this(JsonStore) => _trace = trace;
    internal WorkManifestService(AtomicJsonStore jsonStore) => _jsonStore = jsonStore;
    private static readonly ConditionalWeakTable<ExportWorkManifest, Dictionary<string, long>> Revisions = new();

    // Status readers never normalize, migrate or persist project state.
    public ExportWorkManifest ReadOnly(string manifestPath)
    {
        using var measurement = PerformanceMeasurement.Start("manifest.read", _trace);
        using var stream = new FileStream(manifestPath, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var json = reader.ReadToEnd();
        measurement?.Add("Bytes", stream.Position);
        PersistentJsonSchema.Read(json, "Work-/Export-Manifest", manifestPath,
            ExportWorkManifestVersions.CurrentSchemaVersion);
        PersistentJsonSchema.RequireState(json, manifestPath, "Tracks", "Export", "State");
        return JsonSerializer.Deserialize<ExportWorkManifest>(json)
            ?? throw new PersistentJsonSchemaException(LocalizationText.Get("Persistence.Manifest.Invalid"));
    }

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
        using var measurement = PerformanceMeasurement.Start("manifest.load", _trace);
        var state = AtomicJsonStore.ForPath(manifestPath);
        lock (state)
        {
            var manifest = LoadOrCreateCore(manifestPath, projectType, projectWorkFolder, sourceFolder, selectedPreset);
            RememberRevision(manifestPath, manifest, state.Revision);
            return manifest;
        }
    }

    private ExportWorkManifest LoadOrCreateCore(string manifestPath, string projectType,
        string projectWorkFolder, string sourceFolder, string selectedPreset)
    {
        try
        {
            if (File.Exists(manifestPath))
            {
                using var read = PerformanceMeasurement.Start("manifest.read", _trace);
                var json = File.ReadAllText(manifestPath, Encoding.UTF8);
                read?.FileBytes(manifestPath);
                var schema = PersistentJsonSchema.Read(
                    json,
                    "Work-/Export-Manifest",
                    manifestPath,
                    ExportWorkManifestVersions.CurrentSchemaVersion);
                PersistentJsonSchema.RequireState(json, manifestPath, "Tracks", "Export", "State");
                var loaded = JsonSerializer.Deserialize<ExportWorkManifest>(json);
                read?.Dispose(); // Reading/parsing excludes normalization and migration writes.

                if (loaded is not null)
                {
                    var normalized = NormalizeLoadedManifest(loaded, projectType, projectWorkFolder, sourceFolder, selectedPreset);
                    if (schema.IsLegacy || normalized)
                    {
                        Store(manifestPath, loaded);
                        if (schema.IsLegacy)
                            PersistentJsonSchema.TraceLegacyMigration("Work-/Export-Manifest", manifestPath);
                    }

                    return loaded;
                }
                throw new PersistentJsonSchemaException(LocalizationText.Get("Persistence.Manifest.Invalid"));
            }
        }
        catch (PersistentJsonSchemaException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new IOException(LocalizationText.Get("Persistence.Manifest.Unreadable"), ex);
        }

        return CreateNewManifest(projectType, projectWorkFolder, sourceFolder, selectedPreset);
    }

    public void Save(string manifestPath, ExportWorkManifest manifest)
    {
        using var measurement = PerformanceMeasurement.Start("manifest.save", _trace);
        var state = AtomicJsonStore.ForPath(manifestPath);
        lock (state)
        {
            var path = Path.GetFullPath(manifestPath);
            var revisions = Revisions.GetValue(manifest, _ => new(AtomicJsonStore.PathComparer));
            lock (revisions)
            {
                if (revisions.TryGetValue(path, out var expected)
                    ? expected != state.Revision
                    : File.Exists(path))
                    throw new IOException(LocalizationText.Get("Persistence.Manifest.Stale"));
                using (var prepare = PerformanceMeasurement.Start("manifest.save.prepare", _trace))
                {
                    manifest.UpdatedUtc = DateTime.UtcNow;
                    NormalizeBeforeSave(manifest);
                }
                try { Store(manifestPath, manifest); measurement?.FileBytes(manifestPath); }
                catch (IOException error)
                {
                    throw new IOException(LocalizationText.Get("Persistence.Manifest.CheckpointFailed"), error);
                }
                catch (UnauthorizedAccessException error)
                {
                    throw new IOException(LocalizationText.Get("Persistence.Manifest.CheckpointFailed"), error);
                }
                revisions[path] = state.Revision;
                if (manifest.ProjectType == ProjectManifestTypes.FolderProject &&
                    string.Equals(Path.GetFullPath(manifestPath), Path.GetFullPath(ProjectFolderLayout.GetExportManifestPath(manifest.ProjectWorkFolder)), StringComparison.OrdinalIgnoreCase))
                    new FolderProjectManifestService().SaveLifecycle(manifest);
            }
        }
    }

    private void Store(string path, ExportWorkManifest manifest)
    {
        using var measurement = PerformanceMeasurement.Start("manifest.save.atomic-store", _trace);
        _jsonStore.Save(path, manifest, JsonOptions, measurement);
        measurement?.Add("CommittedWrites");
    }

    private static void RememberRevision(string path, ExportWorkManifest manifest, long revision)
    {
        var revisions = Revisions.GetValue(manifest, _ => new(AtomicJsonStore.PathComparer));
        lock (revisions) revisions[Path.GetFullPath(path)] = revision;
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

    public void UpdateCoverMetadata(
        ExportWorkManifest manifest,
        string coverSourcePath,
        string processedCoverPath)
    {
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
        manifest.Resume.MergePaused = false;
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

    public void MarkConversionCompleted(ExportWorkManifest manifest)
    {
        manifest.Resume.MergePaused = false;
        manifest.State.CancelRequestedUtc = null;
        manifest.State.LastErrorUtc = null;
        manifest.State.LastErrorSummary = "";
        manifest.State.Status = ProjectManifestStatuses.ReviewBeforeMerge;
        manifest.State.LastSuccessfulStep = "ConversionCompleted";
        manifest.Resume.CanResume = false;
        manifest.Resume.Reason = "Alle Tracks wurden vorbereitet. Das Projekt kann geöffnet und final zusammengefügt werden.";
        AddHistory(manifest, "ConversionCompleted", "Alle Tracks wurden vorbereitet.");
    }

    public void MarkExportCompleted(ExportWorkManifest manifest)
    {
        manifest.Resume.MergePaused = false;
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

    public static bool IsReusableConvertedManifestEntry(
        ExportWorkManifestTrack entry,
        string presetDisplayName, PerformanceMeasurement? measurement = null)
    {
        if (!string.Equals(entry.Status, ProjectManifestTrackStatuses.Converted, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(entry.Preset, presetDisplayName, StringComparison.OrdinalIgnoreCase) ||
            entry.PreparationVersion != 1 || entry.PreparationId == Guid.Empty ||
            string.IsNullOrWhiteSpace(entry.SourcePath) ||
            string.IsNullOrWhiteSpace(entry.ConvertedPath) ||
            entry.ConvertedPath.EndsWith(".part", StringComparison.OrdinalIgnoreCase) ||
            entry.ConvertedPath.EndsWith(".copying", StringComparison.OrdinalIgnoreCase) ||
            Path.GetFileNameWithoutExtension(entry.ConvertedPath).EndsWith(".part", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            var sourceInfo = new FileInfo(entry.SourcePath);
            var convertedInfo = new FileInfo(entry.ConvertedPath);

            return Exists(sourceInfo, "SourceFileChecks", measurement) &&
                   Exists(convertedInfo, "PreparedFileChecks", measurement) &&
                   sourceInfo.Length > 0 &&
                   convertedInfo.Length > 0 &&
                   entry.SourceSizeBytes == sourceInfo.Length &&
                   entry.SourceLastWriteUtcTicks == sourceInfo.LastWriteTimeUtc.Ticks &&
                   entry.ConvertedSizeBytes == convertedInfo.Length &&
                   entry.ConvertedLastWriteUtcTicks == convertedInfo.LastWriteTimeUtc.Ticks &&
                   entry.ConvertedSizeBytes > 0;
        }
        catch
        {
            return false;
        }
    }

    private static bool Exists(FileInfo file, string counter, PerformanceMeasurement? measurement)
    {
        measurement?.Add(counter);
        return file.Exists;
    }

    internal static bool MigratePreparedEntry(ExportWorkManifestTrack entry)
    {
        if (entry.PreparationId != Guid.Empty || entry.PreparationVersion is not (0 or 1) ||
            !string.Equals(entry.Status, ProjectManifestTrackStatuses.Converted, StringComparison.OrdinalIgnoreCase)) return false;
        try
        {
            var source = new FileInfo(entry.SourcePath);
            var converted = new FileInfo(entry.ConvertedPath);
            if (!source.Exists || !converted.Exists || source.Length <= 0 || converted.Length <= 0 ||
                source.Length != entry.SourceSizeBytes || source.LastWriteTimeUtc.Ticks != entry.SourceLastWriteUtcTicks ||
                converted.Length != entry.ConvertedSizeBytes || string.IsNullOrWhiteSpace(entry.Preset) ||
                (entry.ConvertedLastWriteUtcTicks != 0 && entry.ConvertedLastWriteUtcTicks != converted.LastWriteTimeUtc.Ticks)) return false;
            entry.PreparationId = Guid.NewGuid();
            entry.PreparationVersion = 1;
            entry.ConvertedLastWriteUtcTicks = converted.LastWriteTimeUtc.Ticks;
            if (entry.AacTiming is not { IsValid: true } timing || timing.Length != converted.Length ||
                timing.LastWriteUtcTicks != converted.LastWriteTimeUtc.Ticks) entry.AacTiming = null;
            return true;
        }
        catch (IOException) { return false; }
        catch (ArgumentException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    public bool CanReuseConvertedTrack(
        ExportWorkManifest manifest,
        int index,
        TrackInfo track,
        string sourcePath,
        string convertedPath,
        ExportPreset preset,
        CancellationToken token = default)
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

        var matching = manifest.Tracks.FirstOrDefault(entry =>
            TrackPathService.PathEquals(entry.SourcePath, sourceFullPath) &&
            TrackPathService.PathEquals(entry.ConvertedPath, convertedFullPath) &&
            string.Equals(entry.Preset, preset.DisplayName, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(ProcessingActionIds.Normalize(entry.Action), action, StringComparison.OrdinalIgnoreCase) &&
            entry.TargetSampleRateHz == track.TargetSampleRateHz &&
            entry.SourceSizeBytes == sourceInfo.Length &&
            entry.SourceLastWriteUtcTicks == sourceLastWriteTicks &&
            entry.ConvertedSizeBytes == convertedInfo.Length &&
            entry.ConvertedSizeBytes > 0 &&
            string.Equals(entry.Status, ProjectManifestTrackStatuses.Converted, StringComparison.OrdinalIgnoreCase));
        token.ThrowIfCancellationRequested();
        return matching is not null && IsReusableConvertedManifestEntry(matching, preset.DisplayName) &&
            (track.SourceId == Guid.Empty || matching.SourceId == Guid.Empty || track.SourceId == matching.SourceId);

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

    public ExportWorkManifestTrack UpdateTrack(
        ExportWorkManifest manifest,
        int index,
        TrackInfo track,
        string sourcePath,
        string convertedPath,
        ExportPreset preset,
        bool existingContentVerified = false)
    {
        var sourceInfo = new FileInfo(sourcePath);
        var convertedInfo = new FileInfo(convertedPath);
        var trackIndex = index + 1;

        var sourceFullPath = Path.GetFullPath(sourcePath);
        var convertedFullPath = Path.GetFullPath(convertedPath);

        var action = AudioProcessingService.NormalizeProcessingAction(track.ProcessingAction);

        var previousEntry = FindTrackEntry(manifest, track, sourcePath, convertedPath, preset);
        if (track.SourceId == Guid.Empty) track.SourceId = previousEntry?.SourceId is Guid sourceId && sourceId != Guid.Empty
            ? sourceId : Guid.NewGuid();
        var previousTiming = previousEntry?.AacTiming;
        var retainedTiming = existingContentVerified && previousTiming is { IsValid: true } &&
            previousTiming.Length == convertedInfo.Length &&
            previousTiming.LastWriteUtcTicks == convertedInfo.LastWriteTimeUtc.Ticks
            ? previousTiming : null;

        manifest.Tracks.RemoveAll(entry => IsSameTrackCacheEntry(
            entry,
            sourceFullPath,
            convertedFullPath,
            preset.DisplayName,
            action));

        var completedEntry = new ExportWorkManifestTrack
        {
            SourceId = track.SourceId,
            TrackIndex = trackIndex,
            SourcePath = sourceFullPath,
            SourceFileName = track.FileName,
            SourceSizeBytes = sourceInfo.Length,
            SourceLastWriteUtcTicks = sourceInfo.LastWriteTimeUtc.Ticks,
            SourceCodec = track.Codec ?? "",
            SourceExtension = NormalizeSourceExtension(track.Extension),
            Duration = track.Duration ?? "",
            DurationTicks = track.DurationTicks,
            Action = action,
            Preset = preset.DisplayName,
            Status = ProjectManifestTrackStatuses.Converted,
            ConvertedPath = convertedFullPath,
            ConvertedSizeBytes = convertedInfo.Length,
            ConvertedLastWriteUtcTicks = convertedInfo.LastWriteTimeUtc.Ticks,
            PreparationId = existingContentVerified && previousEntry is { PreparationId: var previousId } && previousId != Guid.Empty
                ? previousEntry!.PreparationId : Guid.NewGuid(),
            PreparationVersion = 1,
            TargetSampleRateHz = track.TargetSampleRateHz,
            AacTiming = retainedTiming,
            CompletedUtc = DateTime.UtcNow
        };
        manifest.Tracks.Add(completedEntry);

        manifest.State.LastCompletedTrackIndex = Math.Max(manifest.State.LastCompletedTrackIndex, trackIndex);
        manifest.State.LastSuccessfulStep = "TrackConverted";
        AddHistory(manifest, "TrackConverted", "Track wurde fertig vorbereitet.", trackIndex: trackIndex);
        return completedEntry;
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
        if (track.SourceId == Guid.Empty) track.SourceId = Guid.NewGuid();

        return new ExportWorkManifestTrack
        {
            SourceId = track.SourceId,
            TrackIndex = index + 1,
            SourcePath = Path.GetFullPath(sourcePath),
            SourceFileName = track.FileName,
            SourceSizeBytes = sourceInfo.Length,
            SourceLastWriteUtcTicks = sourceInfo.LastWriteTimeUtc.Ticks,
            SourceCodec = track.Codec ?? "",
            SourceExtension = NormalizeSourceExtension(track.Extension),
            Duration = track.Duration ?? "",
            DurationTicks = track.DurationTicks,
            Action = AudioProcessingService.NormalizeProcessingAction(track.ProcessingAction),
            Preset = preset.DisplayName,
            Status = ProjectManifestTrackStatuses.Pending,
            ConvertedPath = Path.GetFullPath(convertedPath),
            ConvertedSizeBytes = 0,
            PreparationVersion = 1,
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
        if (!string.Equals(entry.Preset, presetDisplayName, StringComparison.OrdinalIgnoreCase))
            return false;

        // A physical cache file has one owner, including across processing-action changes.
        if (TrackPathService.PathEquals(entry.ConvertedPath, convertedFullPath))
            return true;

        // Legacy entries represented a physical source rather than a list occurrence.
        // They are rebuilt under the current preparation contract; do not retain stale aliases.
        return entry.PreparationVersion == 0 &&
            string.Equals(ProcessingActionIds.Normalize(entry.Action), action, StringComparison.OrdinalIgnoreCase) &&
            TrackPathService.PathEquals(entry.SourcePath, sourceFullPath);
    }

    private static string? ReadMainProjectId(string folder)
    {
        foreach (var path in new[] { ProjectFolderLayout.GetFolderManifestPath(folder),
                     ProjectFolderLayout.GetMp3DiscManifestPath(folder), ProjectFolderLayout.GetAudioDiscManifestPath(folder) })
        {
            if (!File.Exists(path)) continue;
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            if (document.RootElement.TryGetProperty("ProjectId", out var id) && !string.IsNullOrWhiteSpace(id.GetString()))
                return id.GetString();
        }
        return null;
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
            ProjectId = ReadMainProjectId(projectWorkFolder) ?? Guid.NewGuid().ToString("N"),
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

    private static bool NormalizeLoadedManifest(
        ExportWorkManifest manifest,
        string projectType,
        string projectWorkFolder,
        string sourceFolder,
        string selectedPreset)
    {
        var before = JsonSerializer.Serialize(manifest, JsonOptions);
        var changed = false;

        manifest.SchemaVersion = ExportWorkManifestVersions.CurrentSchemaVersion;
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
        changed |= NormalizeTrackActionIds(manifest.Tracks);
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
        changed |= BackfillMissingTrackSourceIds(manifest, projectWorkFolder);
        foreach (var entry in manifest.Tracks) changed |= MigratePreparedEntry(entry);
        return changed || before != JsonSerializer.Serialize(manifest, JsonOptions);
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
        manifest.SchemaVersion = ExportWorkManifestVersions.CurrentSchemaVersion;

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
        NormalizeTrackActionIds(manifest.Tracks);

        manifest.Discs = manifest.Discs
            .OrderBy(disc => disc.DiscIndex)
            .ToList();

        NormalizeTrackProgress(manifest);
        BackfillMissingTrackSourceIds(manifest, manifest.ProjectWorkFolder);
        TrimHistory(manifest);
    }

    private static bool NormalizeTrackActionIds(IEnumerable<ExportWorkManifestTrack> tracks)
    {
        var changed = false;
        foreach (var track in tracks)
        {
            var id = ProcessingActionIds.NormalizeOrEmpty(track.Action);
            if (string.Equals(track.Action, id, StringComparison.Ordinal))
                continue;

            track.Action = id;
            changed = true;
        }

        return changed;
    }

    private static bool BackfillMissingTrackSourceIds(ExportWorkManifest manifest, string projectWorkFolder)
    {
        var changed = false;
        var evidence = BuildSourceIdEvidence(projectWorkFolder);
        foreach (var track in manifest.Tracks)
        {
            if (track.SourceId != Guid.Empty)
                continue;

            track.SourceId = ResolveExistingSourceId(track, evidence) ?? Guid.NewGuid();
            changed = true;
        }

        return changed;
    }

    private static Guid? ResolveExistingSourceId(
        ExportWorkManifestTrack track,
        IReadOnlyCollection<SourceIdEvidence> evidence)
    {
        if (evidence.Count == 0)
            return null;

        if (!string.IsNullOrWhiteSpace(track.SourcePath))
        {
            var pathMatch = evidence.FirstOrDefault(item =>
                !string.IsNullOrWhiteSpace(item.SourcePath) &&
                PathsEqual(item.SourcePath, track.SourcePath));
            if (pathMatch is not null)
                return pathMatch.SourceId;
        }

        if (track.SourceSizeBytes > 0 && track.SourceLastWriteUtcTicks > 0)
        {
            var matches = evidence
                .Where(item =>
                    item.SourceSizeBytes == track.SourceSizeBytes &&
                    item.SourceLastWriteUtcTicks == track.SourceLastWriteUtcTicks &&
                    string.Equals(item.SourceFileName, track.SourceFileName, StringComparison.OrdinalIgnoreCase))
                .Select(item => item.SourceId)
                .Distinct()
                .Take(2)
                .ToList();

            if (matches.Count == 1)
                return matches[0];
        }

        return null;
    }

    private static IReadOnlyCollection<SourceIdEvidence> BuildSourceIdEvidence(string projectWorkFolder)
    {
        if (string.IsNullOrWhiteSpace(projectWorkFolder))
            return [];

        var result = new List<SourceIdEvidence>();
        AddSourceCopyIdentityEvidence(projectWorkFolder, result);
        AddMp3DiscManifestEvidence(projectWorkFolder, result);
        AddAudioDiscManifestEvidence(projectWorkFolder, result);
        return result;
    }

    private static void AddSourceCopyIdentityEvidence(string projectWorkFolder, List<SourceIdEvidence> result)
    {
        try
        {
            foreach (var identity in new SourceCopyIdentityStore().Load(projectWorkFolder))
            {
                if (identity.SourceId == Guid.Empty)
                    continue;

                result.Add(new SourceIdEvidence(
                    identity.SourceId,
                    Path.Combine(projectWorkFolder, identity.TargetRelativePath),
                    identity.SourceFileName,
                    identity.SourceSizeBytes,
                    identity.SourceLastWriteUtcTicks));
            }
        }
        catch
        {
            // Source-copy evidence is optional during legacy manifest normalization.
        }
    }

    private static void AddMp3DiscManifestEvidence(string projectWorkFolder, List<SourceIdEvidence> result)
    {
        try
        {
            var manifestPath = ProjectFolderLayout.ResolveMp3DiscManifestPath(projectWorkFolder);
            if (!File.Exists(manifestPath))
                return;

            var json = File.ReadAllText(manifestPath, Encoding.UTF8);
            if (!json.Contains("Mp3Disc", StringComparison.OrdinalIgnoreCase))
                return;

            PersistentJsonSchema.Read(
                json,
                "MP3-CD-Projektmanifest",
                manifestPath,
                Mp3DiscManifestVersions.CurrentSchemaVersion);
            var manifest = JsonSerializer.Deserialize<Mp3DiscProjectManifest>(json, JsonOptions);
            if (manifest is null ||
                !manifest.ProjectType.Contains("Mp3Disc", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            foreach (var sourceFile in manifest.ImportedDiscs.SelectMany(disc => disc.SourceFiles))
            {
                if (sourceFile.SourceId == Guid.Empty)
                    continue;

                result.Add(new SourceIdEvidence(
                    sourceFile.SourceId,
                    Path.Combine(ProjectFolderLayout.GetOriginalsFolder(projectWorkFolder), sourceFile.RelativePath),
                    sourceFile.SourceFileName,
                    sourceFile.SourceSizeBytes,
                    sourceFile.SourceLastWriteUtcTicks));
            }
        }
        catch
        {
            // Disc manifest evidence is optional during export manifest normalization.
        }
    }

    private static void AddAudioDiscManifestEvidence(string projectWorkFolder, List<SourceIdEvidence> result)
    {
        try
        {
            var manifestPath = ProjectFolderLayout.ResolveAudioDiscManifestPath(projectWorkFolder);
            if (!File.Exists(manifestPath))
                return;

            var json = File.ReadAllText(manifestPath, Encoding.UTF8);
            PersistentJsonSchema.Read(
                json,
                "Audio-CD-Projektmanifest",
                manifestPath,
                AudioDiscProjectManifestVersions.CurrentSchemaVersion);
            var manifest = JsonSerializer.Deserialize<AudioDiscProjectManifest>(json, JsonOptions);
            if (manifest is null)
                return;

            foreach (var track in manifest.Discs.SelectMany(disc => disc.Tracks))
            {
                if (track.SourceId == Guid.Empty)
                    continue;

                result.Add(new SourceIdEvidence(
                    track.SourceId,
                    Path.Combine(projectWorkFolder, track.RelativePath),
                    track.FileName,
                    SourceSizeBytes: 0,
                    SourceLastWriteUtcTicks: 0));
            }
        }
        catch
        {
            // Audio-disc manifest evidence is optional during export manifest normalization.
        }
    }

    private static bool PathsEqual(string left, string right)
    {
        try
        {
            return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);
        }
    }

    private sealed record SourceIdEvidence(
        Guid SourceId,
        string SourcePath,
        string SourceFileName,
        long SourceSizeBytes,
        long SourceLastWriteUtcTicks);


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
