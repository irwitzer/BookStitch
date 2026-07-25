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

    private static readonly HashSet<string> SupportedAudioExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3",
        ".aac",
        ".m4a",
        ".m4b",
        ".wav",
        ".flac"
    };

    public ProjectResumePlan? BuildFromProjectFolder(string projectFolder)
    {
        return BuildFromProjectFolder(projectFolder, selectedPresetOverride: null);
    }

    public ProjectResumePlan? BuildFromProjectFolder(string projectFolder, string? selectedPresetOverride)
    {
        if (string.IsNullOrWhiteSpace(projectFolder) || !Directory.Exists(projectFolder))
            return null;

        new ProjectFolderMigrationService().MigrateIfNeeded(projectFolder);
        var importManifest = TryLoadMp3DiscManifest(ProjectFolderLayout.ResolveWorkManifestPath(projectFolder));
        var exportManifest = TryLoadExportManifest(ProjectFolderLayout.ResolveExportManifestPath(projectFolder))
            ?? TryLoadExportManifest(ProjectFolderLayout.ResolveWorkManifestPath(projectFolder));
        var audioDiscManifest = TryLoadAudioDiscManifest(ProjectFolderLayout.ResolveAudioDiscManifestPath(projectFolder));

        if (importManifest is null && exportManifest is null && audioDiscManifest is null)
            return null;

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

        var selectedPreset = FirstNonEmpty(
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
            tracks = BuildTrackItems(exportManifest, selectedPreset).ToList();
            if (tracks.Count == 0)
                tracks = BuildTrackItemsFromImportedDiscs(projectFolder, importManifest).ToList();
        }
        else
        {
            tracks = BuildFolderTrackItems(exportManifest, selectedPreset).ToList();
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
        var canContinueDiscImport = nextMissingDisc.HasValue && (importManifest is not null || audioDiscManifest is not null);
        var exportStatus = exportManifest?.State.Status ?? "";
        var canResumeExport = exportManifest is not null &&
            (exportManifest.Resume.CanResume || IsInterruptedStatus(exportStatus));

        var createdUtc = MinNonDefault(importManifest?.CreatedUtc, exportManifest?.CreatedUtc, audioDiscManifest?.CreatedUtc) ?? DateTime.UtcNow;
        var updatedUtc = MaxNonDefault(importManifest?.UpdatedUtc, exportManifest?.UpdatedUtc, audioDiscManifest?.UpdatedUtc) ?? createdUtc;

        var bookTitle = FirstNonEmpty(exportManifest?.Metadata.Title, audioDiscManifest?.Title, importManifest?.Title);
        var author = FirstNonEmpty(exportManifest?.Metadata.Author, audioDiscManifest?.Author, importManifest?.Author);
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
            DisplayName = BuildDisplayName(projectFolder, exportManifest, importManifest, audioDiscManifest),
            Status = BuildStatus(exportManifest, importManifest, audioDiscManifest, canContinueDiscImport),
            CanResume = canContinueDiscImport || canResumeExport || tracks.Count > 0,
            CanEditTrackOrder = tracks.Count > 0,
            CanContinueDiscImport = canContinueDiscImport,
            HasSuccessfulExport = HasSuccessfulExport(exportManifest, audioDiscManifest),
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
            Album = FirstNonEmpty(exportManifest?.Metadata.Album, audioDiscManifest?.Album, importManifest?.Album),
            Narrator = FirstNonEmpty(exportManifest?.Metadata.Narrator, audioDiscManifest?.Narrator, importManifest?.Narrator),
            Genre = FirstNonEmpty(exportManifest?.Metadata.Genre, audioDiscManifest?.Genre, importManifest?.Genre),
            CoverSourcePath = FirstNonEmpty(exportManifest?.Metadata.CoverSourcePath, audioDiscManifest?.CoverSourcePath, importManifest?.CoverSourcePath),
            ProcessedCoverPath = FirstNonEmpty(exportManifest?.Metadata.ProcessedCoverPath, audioDiscManifest?.ProcessedCoverPath, importManifest?.ProcessedCoverPath),
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
        string selectedPreset)
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

        var exportTracksByIndex = exportManifest.Tracks
            .Where(track => track.TrackIndex > 0)
            .GroupBy(track => track.TrackIndex)
            .ToDictionary(
                group => group.Key,
                group => SelectExportTrackForPreset(group, selectedPreset));

        foreach (var audioDiscTrack in audioDiscTracks)
        {
            ExportWorkManifestTrack? exportTrack = null;
            var hasStableSourcePath = !string.IsNullOrWhiteSpace(audioDiscTrack.SourcePath);
            if (hasStableSourcePath)
            {
                exportTracksBySourcePath.TryGetValue(
                    NormalizePathForComparison(audioDiscTrack.SourcePath),
                    out exportTrack);
            }
            else
            {
                exportTracksByIndex.TryGetValue(audioDiscTrack.TrackIndex, out exportTrack);
            }

            if (exportTrack is null)
                continue;

            if (!string.IsNullOrWhiteSpace(exportTrack.Preset))
                audioDiscTrack.Preset = exportTrack.Preset;

            if (!string.IsNullOrWhiteSpace(exportTrack.ChapterTitle))
                audioDiscTrack.ChapterTitle = exportTrack.ChapterTitle;

            var convertedPath = exportTrack.ConvertedPath ?? string.Empty;
            var canReuseConvertedFile = PreparedConvertedTrackReuseService.CanReuseForDiscProject(
                ProjectManifestTypes.AudioCdProject,
                audioDiscTrack.SourcePath,
                convertedPath);

            if (canReuseConvertedFile)
            {
                audioDiscTrack.ConvertedPath = convertedPath;
                audioDiscTrack.Status = ProjectManifestTrackStatuses.Converted;
                continue;
            }

            audioDiscTrack.ConvertedPath = string.Empty;
            if (string.Equals(audioDiscTrack.Action, "FLAC rippen", StringComparison.OrdinalIgnoreCase))
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


    private static IEnumerable<ProjectResumeTrackItem> BuildFolderTrackItems(ExportWorkManifest? exportManifest, string selectedPreset)
    {
        if (exportManifest is null)
            yield break;

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
                Action = manifestTrack?.Action ?? string.Empty,
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
                Action = track.Action ?? "",
                Preset = track.Preset ?? "",
                Status = track.Status ?? "",
                ConvertedPath = track.ConvertedPath ?? ""
            };
        }
    }

    private static string GetResumeTrackDeduplicationKey(ExportWorkManifestTrack track)
    {
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
                DiscNumber = track.DiscNumber,
                TrackNumber = track.TrackNumber,
                SourcePath = sourcePath,
                SourceFileName = track.FileName,
                RelativeFolder = "",
                ChapterTitle = track.ChapterTitle,
                Duration = FormatDuration(track.Duration),
                DurationTicks = track.Duration.Ticks,
                Action = isRipped ? "Konvertieren" : "FLAC rippen",
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
                    DiscNumber = disc.DiscNumber > 0 ? disc.DiscNumber : TryInferDiscNumber(file),
                    TrackNumber = trackNumber++,
                    SourcePath = file,
                    SourceFileName = Path.GetFileName(file),
                    RelativeFolder = GetRelativeFolder(projectFolder, file),
                    ChapterTitle = Path.GetFileNameWithoutExtension(file),
                    Duration = "",
                    Action = "Konvertieren",
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

    private static IEnumerable<string> EnumerateAudioFiles(string folder)
    {
        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(folder, "*.*", SearchOption.AllDirectories).ToList();
        }
        catch
        {
            return [];
        }

        return files
            .Where(file => SupportedAudioExtensions.Contains(Path.GetExtension(file)))
            .Where(file => !file.EndsWith(".part", StringComparison.OrdinalIgnoreCase))
            .Where(file => !file.EndsWith(".copying", StringComparison.OrdinalIgnoreCase))
            .OrderBy(file => file, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static Mp3DiscProjectManifest? TryLoadMp3DiscManifest(string manifestPath)
    {
        try
        {
            if (!File.Exists(manifestPath))
                return null;

            var json = File.ReadAllText(manifestPath, Encoding.UTF8);
            if (!json.Contains("Mp3Disc", StringComparison.OrdinalIgnoreCase))
                return null;

            var manifest = JsonSerializer.Deserialize<Mp3DiscProjectManifest>(json, JsonOptions);
            return manifest?.ProjectType.Contains("Mp3Disc", StringComparison.OrdinalIgnoreCase) == true
                ? manifest
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static AudioDiscProjectManifest? TryLoadAudioDiscManifest(string manifestPath)
    {
        try
        {
            if (!File.Exists(manifestPath))
                return null;

            var json = File.ReadAllText(manifestPath, Encoding.UTF8);
            var manifest = JsonSerializer.Deserialize<AudioDiscProjectManifest>(json, JsonOptions);
            return string.Equals(manifest?.ProjectType, ProjectManifestTypes.AudioCdProject, StringComparison.OrdinalIgnoreCase)
                ? manifest
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static ExportWorkManifest? TryLoadExportManifest(string manifestPath)
    {
        try
        {
            if (!File.Exists(manifestPath))
                return null;

            var json = File.ReadAllText(manifestPath, Encoding.UTF8);
            if (json.Contains("\"ProjectType\"", StringComparison.OrdinalIgnoreCase) &&
                json.Contains("\"Mp3Disc\"", StringComparison.OrdinalIgnoreCase) &&
                !json.Contains("\"FormatVersion\"", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var manifest = JsonSerializer.Deserialize<ExportWorkManifest>(json, JsonOptions);
            if (manifest is null || string.IsNullOrWhiteSpace(manifest.ProjectType))
                return null;

            return manifest;
        }
        catch
        {
            return null;
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
        ExportWorkManifest? exportManifest,
        Mp3DiscProjectManifest? importManifest,
        AudioDiscProjectManifest? audioDiscManifest)
    {
        var author = FirstNonEmpty(exportManifest?.Metadata.Author, audioDiscManifest?.Author, importManifest?.Author);
        var title = FirstNonEmpty(exportManifest?.Metadata.Title, audioDiscManifest?.Title, importManifest?.Title);

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
