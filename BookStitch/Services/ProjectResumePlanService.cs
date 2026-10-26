using BookStitch.Models;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BookStitch.Services;

public sealed class ProjectResumePlanService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public ProjectResumePlan? BuildFromProjectFolder(string projectFolder)
    {
        return BuildFromProjectFolder(projectFolder, selectedPresetOverride: null);
    }

    public ProjectResumePlan? BuildFromProjectFolder(string projectFolder, string? selectedPresetOverride)
    {
        if (string.IsNullOrWhiteSpace(projectFolder) || !Directory.Exists(projectFolder))
            return null;

        SafeFileSystemEnumeration.EnsureSafeTree(projectFolder);
        new ProjectExtensionRollbackService().Recover(projectFolder);
        new DiscProjectStopSaveService().Recover(projectFolder);
        var upgrade = new ProjectUpgradeCoordinator().EnsureCurrentProjectFormat(projectFolder);
        if (upgrade.SafetyBlocked || !upgrade.IsCurrent && !(upgrade.Outcome==ProjectUpgradeOutcome.RebuildRequired&&upgrade.ProjectFormatVersion==1))
            throw new IOException(LocalizationText.Get("Persistence.Manifest.Unreadable") +
                $" ({upgrade.Outcome}) {upgrade.Detail}");
        var importManifest = TryLoadMp3DiscManifest(ProjectFolderLayout.ResolveMp3DiscManifestPath(projectFolder));
        var exportManifest = TryLoadExportManifest(ProjectFolderLayout.ResolveExportManifestPath(projectFolder))
            ?? TryLoadExportManifest(ProjectFolderLayout.ResolveWorkManifestPath(projectFolder));
        var audioDiscManifest = TryLoadAudioDiscManifest(ProjectFolderLayout.ResolveAudioDiscManifestPath(projectFolder));

        if (importManifest is null && exportManifest is null && audioDiscManifest is null)
            return null;

        if (importManifest is not null)
        {
            importManifest = new Mp3DiscProjectService().LoadForResume(projectFolder);
        }
        if (audioDiscManifest is not null)
        {
            audioDiscManifest = new AudioDiscProjectService().LoadForResume(projectFolder);
        }

        return BuildPlan(
            projectFolder,
            importManifest,
            exportManifest,
            audioDiscManifest,
            selectedPresetOverride);
    }

    private static ProjectResumePlan BuildPlan(
        string projectFolder,
        Mp3DiscProjectManifest? importManifest,
        ExportWorkManifest? exportManifest,
        AudioDiscProjectManifest? audioDiscManifest,
        string? selectedPresetOverride)
    {
        // Disc manifests are authoritative. An export manifest can carry a stale
        // project type when the user switched from a loaded disc project to a
        // normal folder project before starting the next export. Without the
        // matching disc manifest, the project must therefore be treated as a
        // folder project.
        var projectType = audioDiscManifest is not null
            ? ProjectManifestTypes.AudioCdProject
            : importManifest is not null
                ? ProjectManifestTypes.Mp3DiscProject
                : ProjectManifestTypes.FolderProject;

        var isStoppedDiscProject = audioDiscManifest?.IsStoppedForResume == true || importManifest?.IsStoppedForResume == true;
        var selectedPreset = isStoppedDiscProject
            ? FirstNonEmpty(audioDiscManifest?.ExportPreset, importManifest?.ExportPreset)
            : FirstNonEmpty(
            selectedPresetOverride,
            exportManifest?.SelectedPreset,
            exportManifest?.Export.SelectedPreset,
            audioDiscManifest?.ExportPreset,
            importManifest?.ExportPreset);

        List<ProjectResumeTrackItem> tracks;
        if (audioDiscManifest is not null)
        {
            tracks = BuildTrackItemsFromAudioDiscManifest(projectFolder, audioDiscManifest).ToList();
            ApplyExportTrackState(tracks, exportManifest, selectedPreset);
        }
        else if (importManifest is not null)
        {
            // Export plans contain only included tracks. The import inventory owns
            // the full editable project, including sources excluded before export.
            tracks = BuildTrackItemsFromImportedDiscs(projectFolder, importManifest).ToList();
            if (tracks.Count == 0)
                tracks = BuildTrackItems(exportManifest, selectedPreset).ToList();
            else
                ApplyExportTrackState(tracks, exportManifest, selectedPreset, ProjectManifestTypes.Mp3DiscProject);
        }
        else
        {
            tracks = BuildFolderTrackItems(projectFolder, exportManifest, selectedPreset).ToList();
        }

        var completedDiscNumbers = audioDiscManifest is not null
            ? audioDiscManifest.Discs
                .Where(IsAudioDiscCompleted)
                .Select(disc => disc.DiscNumber)
                .Where(number => number > 0)
                .Distinct()
                .OrderBy(number => number)
                .ToList()
            : importManifest?.ImportedDiscs
                .Where(disc => string.Equals(disc.Status, Mp3DiscImportStatus.Completed, StringComparison.OrdinalIgnoreCase))
                .Select(disc => disc.DiscNumber)
                .Where(number => number > 0)
                .Distinct()
                .OrderBy(number => number)
                .ToList() ?? [];

        var totalDiscs = Math.Max(audioDiscManifest?.TotalDiscs ?? importManifest?.TotalDiscs ?? 0, 0);
        var nextMissingDisc = FindNextMissingDisc(totalDiscs, completedDiscNumbers);
        // A completed MP3 export may cover fewer discs than an old planned total.
        // Only agreement of both current manifests closes that import lifecycle;
        // a historical successful export alone never closes a later extension.
        var completedMp3Lifecycle = !isStoppedDiscProject && importManifest is not null &&
            string.Equals(importManifest.PipelineState, ProjectPipelineStateNames.Completed, StringComparison.OrdinalIgnoreCase) &&
            (string.Equals(exportManifest?.State.Status, ProjectManifestStatuses.Completed, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(exportManifest?.State.Status, ProjectManifestStatuses.ReviewBeforeMerge, StringComparison.OrdinalIgnoreCase));
        var mergePaused = exportManifest?.Resume.MergePaused == true;
        var canContinueDiscImport = nextMissingDisc.HasValue && !completedMp3Lifecycle && !mergePaused &&
            (importManifest is not null || audioDiscManifest is not null);
        var exportStatus = exportManifest?.State.Status ?? "";
        var canResumeExport = exportManifest is not null &&
            (exportManifest.Resume.CanResume || IsInterruptedStatus(exportStatus));

        var createdUtc = MinNonDefault(importManifest?.CreatedUtc, exportManifest?.CreatedUtc, audioDiscManifest?.CreatedUtc) ?? DateTime.UtcNow;
        var updatedUtc = MaxNonDefault(importManifest?.UpdatedUtc, exportManifest?.UpdatedUtc, audioDiscManifest?.UpdatedUtc) ?? createdUtc;
        var projectMetadata = new ProjectMetadataMigrationService()
            .LoadAuthoritativeOrLegacy(projectFolder, importManifest, exportManifest, audioDiscManifest);
        var metadata = ProjectMetadataMigrationService.CreateProjection(projectMetadata);

        var bookTitle = metadata.Title;
        var author = metadata.Author;
        var outputFolder = string.Equals(projectType, ProjectManifestTypes.AudioCdProject, StringComparison.OrdinalIgnoreCase)
            ? FirstNonEmpty(audioDiscManifest?.OutputFolder, exportManifest?.Export.OutputFolder)
            : ResolveResumeOutputFolder(
                projectType,
                exportManifest?.Export.OutputFolder,
                importManifest?.OutputFolder,
                author,
                bookTitle);

        return new ProjectResumePlan
        {
            ProjectFolder = projectFolder,
            ProjectType = projectType,
            DisplayName = BuildDisplayName(projectFolder, metadata, importManifest, audioDiscManifest),
            Status = isStoppedDiscProject
                ? LocalizationText.Format("Disc.StopSave.Status", completedDiscNumbers.Count, totalDiscs)
                : BuildStatus(exportManifest, importManifest, audioDiscManifest, canContinueDiscImport),
            CanResume = canContinueDiscImport || canResumeExport || tracks.Count > 0,
            IsStoppedDiscProject = isStoppedDiscProject,
            IsMergePaused = mergePaused,
            CanEditTrackOrder = tracks.Count > 0,
            CanContinueDiscImport = canContinueDiscImport,
            HasSuccessfulExport = !isStoppedDiscProject && HasSuccessfulExport(exportManifest, audioDiscManifest),
            NextMissingDiscNumber = nextMissingDisc,
            TotalDiscs = totalDiscs,
            ImportedDiscCount = completedDiscNumbers.Count,
            CreatedUtc = createdUtc,
            UpdatedUtc = updatedUtc,
            SourceFolder = FirstNonEmpty(exportManifest?.SourceFolder, audioDiscManifest is null ? null : ProjectFolderLayout.ResolveOriginalsFolder(projectFolder), importManifest?.SourceFolder),
            OutputFolder = outputFolder,
            OutputFileName = FirstNonEmpty(exportManifest?.Export.OutputFileName, importManifest?.OutputFileName),
            OutputExtension = FirstNonEmpty(exportManifest?.Export.OutputExtension, audioDiscManifest?.OutputExtension, importManifest?.OutputExtension),
            FileNameTemplate = FirstNonEmpty(audioDiscManifest?.FileNameTemplate, importManifest?.FileNameTemplate),
            ParallelJobs = FirstNonEmpty(exportManifest?.Export.ParallelJobs, audioDiscManifest?.ParallelJobs, importManifest?.ParallelJobs),
            SelectedPreset = selectedPreset,
            BookTitle = bookTitle,
            Author = author,
            Album = metadata.Album,
            Narrator = metadata.Narrator,
            Genre = metadata.Genre,
            CoverSourcePath = projectMetadata.Cover.SourcePath,
            ProcessedCoverPath = projectMetadata.Cover.ProcessedPath,
            Tracks = tracks
        };
    }

    private static bool HasSuccessfulExport(
        ExportWorkManifest? exportManifest,
        AudioDiscProjectManifest? audioDiscManifest)
    {
        if (string.Equals(
                exportManifest?.State.Status,
                ProjectPipelineStateNames.Completed,
                StringComparison.OrdinalIgnoreCase) ||
            string.Equals(
                exportManifest?.State.LastSuccessfulStep,
                "ExportCompleted",
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return audioDiscManifest?.HasSuccessfulExport == true ||
               string.Equals(
                   audioDiscManifest?.ExportStatus,
                   AudioDiscExportStatus.Completed,
                   StringComparison.OrdinalIgnoreCase) ||
               !string.IsNullOrWhiteSpace(audioDiscManifest?.LastSuccessfulOutputPath) ||
               !string.IsNullOrWhiteSpace(audioDiscManifest?.FinalOutputPath);
    }

    private static void ApplyExportTrackState(
        IReadOnlyList<ProjectResumeTrackItem> audioDiscTracks,
        ExportWorkManifest? exportManifest,
        string selectedPreset,
        string projectType = ProjectManifestTypes.AudioCdProject)
    {
        if (exportManifest is null || audioDiscTracks.Count == 0)
            return;

        var exportTracksBySourcePath = exportManifest.Tracks
            .Where(track => !string.IsNullOrWhiteSpace(track.SourcePath))
            .GroupBy(
                track => NormalizePathForComparison(track.SourcePath),
                StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => SelectExportTrackForPreset(group, selectedPreset),
                StringComparer.OrdinalIgnoreCase);

        var exportTracksBySourceId = exportManifest.Tracks
            .Where(track => track.SourceId != Guid.Empty)
            .GroupBy(track => track.SourceId)
            .ToDictionary(
                group => group.Key,
                group => SelectExportTrackForPreset(group, selectedPreset));

        var exportTracksByIndex = exportManifest.Tracks
            .Where(track => track.TrackIndex > 0)
            .GroupBy(track => track.TrackIndex)
            .ToDictionary(
                group => group.Key,
                group => SelectExportTrackForPreset(group, selectedPreset));

        foreach (var audioDiscTrack in audioDiscTracks)
        {
            ExportWorkManifestTrack? exportTrack = null;
            if (audioDiscTrack.SourceId != Guid.Empty)
                exportTracksBySourceId.TryGetValue(audioDiscTrack.SourceId, out exportTrack);

            var hasStableSourcePath = !string.IsNullOrWhiteSpace(audioDiscTrack.SourcePath);
            if (exportTrack is null && hasStableSourcePath)
            {
                exportTracksBySourcePath.TryGetValue(
                    NormalizePathForComparison(audioDiscTrack.SourcePath),
                    out exportTrack);
            }
            else if (exportTrack is null)
            {
                exportTracksByIndex.TryGetValue(audioDiscTrack.TrackIndex, out exportTrack);
            }

            if (exportTrack is null)
                continue;

            if (audioDiscTrack.SourceId == Guid.Empty)
                audioDiscTrack.SourceId = exportTrack.SourceId;
            if (audioDiscTrack.DurationTicks <= 0 && exportTrack.DurationTicks > 0)
            {
                audioDiscTrack.Duration = exportTrack.Duration;
                audioDiscTrack.DurationTicks = exportTrack.DurationTicks;
            }

            if (!string.IsNullOrWhiteSpace(exportTrack.Preset))
                audioDiscTrack.Preset = exportTrack.Preset;

            if (!string.IsNullOrWhiteSpace(exportTrack.ChapterTitle))
                audioDiscTrack.ChapterTitle = exportTrack.ChapterTitle;

            var convertedPath = exportTrack.ConvertedPath ?? string.Empty;
            var canReuseConvertedFile = PreparedConvertedTrackReuseService.CanReuseForDiscProject(
                projectType,
                audioDiscTrack.SourcePath,
                convertedPath);

            if (canReuseConvertedFile)
            {
                audioDiscTrack.ConvertedPath = convertedPath;
                audioDiscTrack.Status = ProjectManifestTrackStatuses.Converted;
                continue;
            }

            audioDiscTrack.ConvertedPath = string.Empty;
            if (ProcessingActionIds.Normalize(audioDiscTrack.Action) == ProcessingActionIds.RipFlac)
            {
                audioDiscTrack.Status = AudioDiscTrackStatus.Pending;
                continue;
            }

            audioDiscTrack.Status = string.Equals(
                exportTrack.Status,
                ProjectManifestTrackStatuses.Failed,
                StringComparison.OrdinalIgnoreCase)
                ? ProjectManifestTrackStatuses.Failed
                : ProjectManifestTrackStatuses.Pending;
        }
    }

    private static ExportWorkManifestTrack? SelectExportTrackForPreset(
        IEnumerable<ExportWorkManifestTrack> tracks,
        string selectedPreset)
    {
        var candidates = tracks.ToList();
        if (!string.IsNullOrWhiteSpace(selectedPreset))
        {
            var matchingPreset = candidates
                .Where(track => string.Equals(track.Preset, selectedPreset, StringComparison.OrdinalIgnoreCase))
                .ToList();

            candidates = matchingPreset;
        }

        return candidates
            .OrderByDescending(track => track.CompletedUtc ?? DateTime.MinValue)
            .FirstOrDefault();
    }


    private static IEnumerable<ProjectResumeTrackItem> BuildFolderTrackItems(string projectFolder, ExportWorkManifest? exportManifest, string selectedPreset)
    {
        if (exportManifest is null)
            yield break;

        // Excluded sources have no converted/export entry, but their import identity
        // is still required to restore track-list state and immutable source metadata.
        var sourceIdentitiesByPath = new SourceCopyIdentityStore().Load(projectFolder)
            .Where(identity => identity.SourceId != Guid.Empty)
            .GroupBy(identity => NormalizePathForComparison(Path.Combine(projectFolder, identity.TargetRelativePath)),
                StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().SourceId, StringComparer.OrdinalIgnoreCase);
        var manifestTracksBySourcePath = exportManifest.Tracks
            .Where(track => !string.IsNullOrWhiteSpace(track.SourcePath))
            .GroupBy(
                track => NormalizePathForComparison(track.SourcePath),
                StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => SelectExportTrackForPreset(group, selectedPreset)
                    ?? group
                        .OrderByDescending(track => track.CompletedUtc ?? DateTime.MinValue)
                        .ThenByDescending(track => track.TrackIndex)
                        .First(),
                StringComparer.OrdinalIgnoreCase);

        var sourceFiles = EnumerateAudioFiles(exportManifest.SourceFolder)
            .OrderBy(path => Path.GetRelativePath(exportManifest.SourceFolder, path), new NaturalStringComparer())
            .ToList();

        if (sourceFiles.Count == 0)
        {
            foreach (var item in BuildTrackItems(exportManifest, selectedPreset))
                yield return item;

            yield break;
        }

        var manifestCoversCompleteSourceFolder =
            sourceFiles.Count == manifestTracksBySourcePath.Count &&
            sourceFiles.All(sourcePath =>
                manifestTracksBySourcePath.ContainsKey(NormalizePathForComparison(sourcePath)));

        if (manifestCoversCompleteSourceFolder)
        {
            foreach (var item in BuildTrackItems(exportManifest, selectedPreset))
                yield return item;

            yield break;
        }

        for (var index = 0; index < sourceFiles.Count; index++)
        {
            var sourcePath = sourceFiles[index];
            manifestTracksBySourcePath.TryGetValue(
                NormalizePathForComparison(sourcePath),
                out var manifestTrack);

            yield return new ProjectResumeTrackItem
            {
                TrackIndex = index + 1,
                SourceId = manifestTrack?.SourceId is { } sourceId && sourceId != Guid.Empty
                    ? sourceId : sourceIdentitiesByPath.GetValueOrDefault(NormalizePathForComparison(sourcePath)),
                DiscNumber = null,
                TrackNumber = index + 1,
                SourcePath = sourcePath,
                SourceFileName = Path.GetFileName(sourcePath),
                RelativeFolder = GetRelativeFolder(exportManifest.SourceFolder, sourcePath),
                ChapterTitle = string.IsNullOrWhiteSpace(manifestTrack?.ChapterTitle)
                    ? Path.GetFileNameWithoutExtension(sourcePath)
                    : manifestTrack.ChapterTitle,
                Duration = manifestTrack?.Duration ?? string.Empty,
                DurationTicks = manifestTrack?.DurationTicks,
                Action = ProcessingActionIds.NormalizeOrEmpty(manifestTrack?.Action),
                Preset = manifestTrack?.Preset ?? selectedPreset,
                Status = manifestTrack?.Status ?? ProjectManifestTrackStatuses.Pending,
                ConvertedPath = manifestTrack?.ConvertedPath ?? string.Empty
            };
        }
    }

    private static IEnumerable<ProjectResumeTrackItem> BuildTrackItems(ExportWorkManifest? exportManifest, string selectedPreset)
    {
        if (exportManifest is null)
            yield break;

        var tracks = exportManifest.Tracks
            .GroupBy(GetResumeTrackDeduplicationKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => SelectExportTrackForPreset(group, selectedPreset)
                ?? group
                    .OrderByDescending(track => track.CompletedUtc ?? DateTime.MinValue)
                    .ThenByDescending(track => track.TrackIndex)
                    .First())
            .OrderBy(track => track.TrackIndex)
            .ToList();

        foreach (var track in tracks)
        {
            yield return new ProjectResumeTrackItem
            {
                TrackIndex = track.TrackIndex,
                SourceId = track.SourceId,
                DiscNumber = TryInferDiscNumber(track.SourcePath),
                TrackNumber = track.TrackIndex > 0 ? track.TrackIndex : null,
                SourcePath = track.SourcePath ?? "",
                SourceFileName = string.IsNullOrWhiteSpace(track.SourceFileName)
                    ? Path.GetFileName(track.SourcePath ?? "")
                    : track.SourceFileName,
                RelativeFolder = GetRelativeFolder(exportManifest.SourceFolder, track.SourcePath),
                ChapterTitle = track.ChapterTitle ?? "",
                Duration = track.Duration ?? "",
                DurationTicks = track.DurationTicks,
                Action = ProcessingActionIds.NormalizeOrEmpty(track.Action),
                Preset = track.Preset ?? "",
                Status = track.Status ?? "",
                ConvertedPath = track.ConvertedPath ?? ""
            };
        }
    }

    private static string GetResumeTrackDeduplicationKey(ExportWorkManifestTrack track)
    {
        if (track.SourceId != Guid.Empty)
            return "sourceid:" + track.SourceId.ToString("N");

        if (!string.IsNullOrWhiteSpace(track.SourcePath))
            return "source:" + NormalizePathForComparison(track.SourcePath);

        if (!string.IsNullOrWhiteSpace(track.ConvertedPath))
            return "converted:" + NormalizePathForComparison(track.ConvertedPath);

        return "track:" + track.TrackIndex.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + (track.SourceFileName ?? "");
    }

    private static string NormalizePathForComparison(string path)
    {
        try
        {
            return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return path.Trim();
        }
    }


    private static IEnumerable<ProjectResumeTrackItem> BuildTrackItemsFromAudioDiscManifest(
        string projectFolder,
        AudioDiscProjectManifest manifest)
    {
        foreach (var track in manifest.Discs
                     .OrderBy(disc => disc.DiscNumber)
                     .SelectMany(disc => disc.Tracks.OrderBy(item => item.GlobalIndex)))
        {
            var sourcePath = string.IsNullOrWhiteSpace(track.RelativePath)
                ? Path.Combine(ProjectFolderLayout.GetDiscOriginalsFolder(projectFolder, Math.Max(1, track.DiscNumber)), track.FileName)
                : Path.Combine(projectFolder, track.RelativePath);
            var isRipped = string.Equals(track.Status, AudioDiscTrackStatus.Ripped, StringComparison.OrdinalIgnoreCase) &&
                           File.Exists(sourcePath);

            yield return new ProjectResumeTrackItem
            {
                TrackIndex = track.GlobalIndex,
                SourceId = track.SourceId,
                DiscNumber = track.DiscNumber,
                TrackNumber = track.TrackNumber,
                SourcePath = sourcePath,
                SourceFileName = track.FileName,
                RelativeFolder = "",
                ChapterTitle = track.ChapterTitle,
                Duration = FormatDuration(track.Duration),
                DurationTicks = track.Duration.Ticks,
                Action = isRipped ? ProcessingActionIds.Convert : ProcessingActionIds.RipFlac,
                Preset = manifest.ExportPreset,
                Status = isRipped ? ProjectManifestTrackStatuses.Pending : AudioDiscTrackStatus.Pending,
                ConvertedPath = ""
            };
        }
    }

    private static bool IsAudioDiscCompleted(AudioDiscProjectManifestDisc disc)
    {
        return string.Equals(disc.Status, AudioDiscStatus.Completed, StringComparison.OrdinalIgnoreCase) ||
               (disc.Tracks.Count > 0 && disc.Tracks.All(track =>
                   string.Equals(track.Status, AudioDiscTrackStatus.Ripped, StringComparison.OrdinalIgnoreCase)));
    }

    private static string FormatDuration(TimeSpan duration)
    {
        return duration.TotalHours >= 1
            ? duration.ToString(@"hh\:mm\:ss")
            : duration.ToString(@"mm\:ss");
    }

    private static IEnumerable<ProjectResumeTrackItem> BuildTrackItemsFromImportedDiscs(string projectFolder, Mp3DiscProjectManifest importManifest)
    {
        var trackIndex = 1;

        foreach (var disc in importManifest.ImportedDiscs
                     .Where(disc => string.Equals(disc.Status, Mp3DiscImportStatus.Completed, StringComparison.OrdinalIgnoreCase))
                     .OrderBy(disc => disc.DiscNumber))
        {
            var discFolder = GetExistingDiscFolder(projectFolder, disc);
            if (string.IsNullOrWhiteSpace(discFolder))
                continue;

            var trackNumber = 1;
            foreach (var file in EnumerateAudioFiles(discFolder))
            {
                yield return new ProjectResumeTrackItem
                {
                    TrackIndex = trackIndex++,
                    SourceId = FindMp3DiscSourceId(disc, projectFolder, file),
                    DiscNumber = disc.DiscNumber > 0 ? disc.DiscNumber : TryInferDiscNumber(file),
                    TrackNumber = trackNumber++,
                    SourcePath = file,
                    SourceFileName = Path.GetFileName(file),
                    RelativeFolder = GetRelativeFolder(projectFolder, file),
                    ChapterTitle = Path.GetFileNameWithoutExtension(file),
                    Duration = "",
                    Action = ProcessingActionIds.Convert,
                    Preset = importManifest.ExportPreset ?? "",
                    Status = ProjectManifestTrackStatuses.Pending,
                    ConvertedPath = ""
                };
            }
        }
    }

    private static string GetExistingDiscFolder(string projectFolder, Mp3DiscProjectManifestDisc disc)
    {
        if (!string.IsNullOrWhiteSpace(disc.LocalFolder) && Directory.Exists(disc.LocalFolder))
            return disc.LocalFolder;

        if (disc.DiscNumber > 0)
        {
            var expectedFolder = ProjectFolderLayout.ResolveDiscOriginalsFolder(projectFolder, disc.DiscNumber);
            if (Directory.Exists(expectedFolder))
                return expectedFolder;
        }

        return "";
    }

    private static Guid FindMp3DiscSourceId(Mp3DiscProjectManifestDisc disc, string projectFolder, string file)
    {
        if (disc.SourceFiles.Count == 0)
            return Guid.Empty;

        string relative;
        try
        {
            relative = Path.GetRelativePath(ProjectFolderLayout.GetOriginalsFolder(projectFolder), file);
        }
        catch
        {
            relative = Path.GetFileName(file);
        }

        var normalizedRelative = relative.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        var fileName = Path.GetFileName(file);

        return disc.SourceFiles.FirstOrDefault(sourceFile =>
            sourceFile.SourceId != Guid.Empty &&
            (string.Equals(sourceFile.RelativePath, normalizedRelative, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(sourceFile.SourceFileName, fileName, StringComparison.OrdinalIgnoreCase)))?.SourceId ?? Guid.Empty;
    }

    private static IEnumerable<string> EnumerateAudioFiles(string folder)
    {
        IEnumerable<string> files;
        try
        {
            files = SafeFileSystemEnumeration.EnumerateFiles(folder).ToList();
        }
        catch
        {
            return [];
        }

        return files
            .Where(file => AudioInputFormatCatalog.IsLocalImportExtension(Path.GetExtension(file)))
            .Where(file => !file.EndsWith(".part", StringComparison.OrdinalIgnoreCase))
            .Where(file => !file.EndsWith(".copying", StringComparison.OrdinalIgnoreCase))
            .OrderBy(file => file, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    internal static Mp3DiscProjectManifest? TryLoadMp3DiscManifest(string manifestPath)
    {
        try
        {
            if (!File.Exists(manifestPath))
                return null;

            var json = File.ReadAllText(manifestPath, Encoding.UTF8);
            PersistentJsonSchema.Read(
                json,
                "MP3-CD-Projektmanifest",
                manifestPath,
                Mp3DiscManifestVersions.CurrentSchemaVersion);
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("ImportedDiscs", out _) &&
                !document.RootElement.TryGetProperty("TotalDiscs", out _))
            {
                PersistentJsonSchema.RequireState(json, manifestPath, "Tracks", "Export", "State");
                return null; // A valid work/export manifest occupies the shared project.json filename.
            }

            var manifest = JsonSerializer.Deserialize<Mp3DiscProjectManifest>(json, JsonOptions);
            if (manifest?.ProjectType.Contains("Mp3Disc", StringComparison.OrdinalIgnoreCase) != true)
                throw new PersistentJsonSchemaException(LocalizationText.Get("Persistence.Manifest.Invalid"));
            return manifest;
        }
        catch (PersistentJsonSchemaException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new IOException(LocalizationText.Get("Persistence.Manifest.Unreadable"), ex);
        }
    }

    internal static AudioDiscProjectManifest? TryLoadAudioDiscManifest(string manifestPath)
    {
        try
        {
            if (!File.Exists(manifestPath))
                return null;

            var json = File.ReadAllText(manifestPath, Encoding.UTF8);
            PersistentJsonSchema.Read(
                json,
                "Audio-CD-Projektmanifest",
                manifestPath,
                AudioDiscProjectManifestVersions.CurrentSchemaVersion);
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("Discs", out _))
                throw new PersistentJsonSchemaException(LocalizationText.Get("Persistence.Manifest.Invalid"));
            var manifest = JsonSerializer.Deserialize<AudioDiscProjectManifest>(json, JsonOptions);
            if (!string.Equals(manifest?.ProjectType, ProjectManifestTypes.AudioCdProject, StringComparison.OrdinalIgnoreCase))
                throw new PersistentJsonSchemaException(LocalizationText.Get("Persistence.Manifest.Invalid"));
            return manifest;
        }
        catch (PersistentJsonSchemaException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new IOException(LocalizationText.Get("Persistence.Manifest.Unreadable"), ex);
        }
    }

    internal static ExportWorkManifest? TryLoadExportManifest(string manifestPath)
    {
        try
        {
            if (!File.Exists(manifestPath))
                return null;

            var json = File.ReadAllText(manifestPath, Encoding.UTF8);
            PersistentJsonSchema.Read(
                json,
                "Work-/Export-Manifest",
                manifestPath,
                ExportWorkManifestVersions.CurrentSchemaVersion);
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.TryGetProperty("ImportedDiscs", out _) &&
                !document.RootElement.TryGetProperty("Tracks", out _)) return null;
            if (json.Contains("\"ProjectType\"", StringComparison.OrdinalIgnoreCase) &&
                json.Contains("\"Mp3Disc\"", StringComparison.OrdinalIgnoreCase) &&
                !json.Contains("\"FormatVersion\"", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            PersistentJsonSchema.RequireState(json, manifestPath, "Tracks", "Export", "State");
            var manifest = JsonSerializer.Deserialize<ExportWorkManifest>(json, JsonOptions);
            if (manifest is null || string.IsNullOrWhiteSpace(manifest.ProjectType))
                throw new PersistentJsonSchemaException(LocalizationText.Get("Persistence.Manifest.Invalid"));

            return manifest;
        }
        catch (PersistentJsonSchemaException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new IOException(LocalizationText.Get("Persistence.Manifest.Unreadable"), ex);
        }
    }

    private static int? FindNextMissingDisc(int totalDiscs, IReadOnlyCollection<int> completedDiscNumbers)
    {
        if (totalDiscs <= 0)
            return null;

        var completed = completedDiscNumbers.ToHashSet();
        for (var discNumber = 1; discNumber <= totalDiscs; discNumber++)
        {
            if (!completed.Contains(discNumber))
                return discNumber;
        }

        return null;
    }

    private static bool IsInterruptedStatus(string? status)
    {
        return status is ProjectManifestStatuses.LegacyCanceled or
            ProjectManifestStatuses.LegacyFailed or
            ProjectManifestStatuses.LegacyExporting or
            ProjectManifestStatuses.AcquiringSources or
            ProjectManifestStatuses.Converting or
            ProjectManifestStatuses.Merging;
    }

    private static string BuildDisplayName(
        string projectFolder,
        ProjectMetadataProjection metadata,
        Mp3DiscProjectManifest? importManifest,
        AudioDiscProjectManifest? audioDiscManifest)
    {
        var author = metadata.Author;
        var title = metadata.Title;

        if (!string.IsNullOrWhiteSpace(author) && !string.IsNullOrWhiteSpace(title))
            return author + " - " + title;

        if (!string.IsNullOrWhiteSpace(title))
            return title;

        var importName = audioDiscManifest?.ProjectFolder ?? importManifest?.ProjectFolder;
        if (!string.IsNullOrWhiteSpace(importName))
            return Path.GetFileName(importName.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

        return new DirectoryInfo(projectFolder).Name;
    }

    private static string ResolveResumeOutputFolder(
        string projectType,
        string? exportOutputFolder,
        string? importOutputFolder,
        string? author,
        string? title)
    {
        if (string.Equals(projectType, ProjectManifestTypes.Mp3DiscProject, StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(importOutputFolder))
        {
            return importOutputFolder.Trim();
        }

        var outputFolder = FirstNonEmpty(exportOutputFolder, importOutputFolder);
        return StripFinalAuthorTitleLayout(outputFolder, author, title);
    }

    private static string StripFinalAuthorTitleLayout(string outputFolder, string? author, string? title)
    {
        if (string.IsNullOrWhiteSpace(outputFolder) ||
            string.IsNullOrWhiteSpace(author) ||
            string.IsNullOrWhiteSpace(title))
        {
            return outputFolder;
        }

        try
        {
            var fullPath = Path.GetFullPath(outputFolder.Trim());
            var titleSegment = FileNameTemplateService.CleanWindowsFileName(title);
            var authorSegment = FileNameTemplateService.CleanWindowsFileName(author);

            if (string.IsNullOrWhiteSpace(titleSegment) || string.IsNullOrWhiteSpace(authorSegment))
                return outputFolder.Trim();

            var titleFolder = new DirectoryInfo(fullPath);
            var authorFolder = titleFolder.Parent;
            var baseFolder = authorFolder?.Parent;

            if (authorFolder is null || baseFolder is null)
                return outputFolder.Trim();

            if (!string.Equals(titleFolder.Name, titleSegment, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(authorFolder.Name, authorSegment, StringComparison.OrdinalIgnoreCase))
            {
                return outputFolder.Trim();
            }

            return baseFolder.FullName;
        }
        catch
        {
            return outputFolder.Trim();
        }
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
                return value.Trim();
        }

        return "";
    }

    private static string BuildStatus(
        ExportWorkManifest? exportManifest,
        Mp3DiscProjectManifest? importManifest,
        AudioDiscProjectManifest? audioDiscManifest,
        bool canContinueDiscImport)
    {
        if (audioDiscManifest is not null)
            return audioDiscManifest.Status;

        if (canContinueDiscImport && importManifest is not null)
            return $"MP3-CD-Import offen: {importManifest.ImportedDiscs.Count}/{importManifest.TotalDiscs} CD(s) importiert";

        if (!string.IsNullOrWhiteSpace(exportManifest?.State.Status))
            return exportManifest.State.Status;

        return importManifest is not null
            ? "MP3-CD-Projekt"
            : "Projekt";
    }

    private static DateTime? MinNonDefault(params DateTime?[] values)
    {
        var usable = values
            .Where(value => value.HasValue && value.Value != default)
            .Select(value => value!.Value)
            .ToList();

        return usable.Count == 0 ? null : usable.Min();
    }

    private static DateTime? MaxNonDefault(params DateTime?[] values)
    {
        var usable = values
            .Where(value => value.HasValue && value.Value != default)
            .Select(value => value!.Value)
            .ToList();

        return usable.Count == 0 ? null : usable.Max();
    }

    private static string GetRelativeFolder(string? sourceRoot, string? sourcePath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(sourceRoot) || string.IsNullOrWhiteSpace(sourcePath))
                return "";

            var relativePath = Path.GetRelativePath(sourceRoot, sourcePath);
            var relativeFolder = Path.GetDirectoryName(relativePath);
            return relativeFolder ?? "";
        }
        catch
        {
            return "";
        }
    }

    private static int? TryInferDiscNumber(string? sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
            return null;

        var parts = sourcePath.Split(
            new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
            StringSplitOptions.RemoveEmptyEntries);

        foreach (var part in parts.Reverse())
        {
            var match = Regex.Match(part, @"^(?:CD|Disc|Disk)\s*0*(\d+)$", RegexOptions.IgnoreCase);
            if (match.Success && int.TryParse(match.Groups[1].Value, out var number) && number > 0)
                return number;
        }

        return null;
    }
}
