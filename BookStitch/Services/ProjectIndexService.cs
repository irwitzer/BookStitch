using BookStitch.Models;
using System.IO;
using System.Text.Json;

namespace BookStitch.Services;

public sealed class ProjectIndexService
{
    public const int DefaultRetentionDays = 180;
    public const int MaximumRetentionDays = 180;
    public const int DefaultDeleteOlderThanDays = 180;

    public IReadOnlyList<ProjectIndexItem> ScanProjects(string workingRootFolder, int retentionDays = DefaultRetentionDays)
    {
        return ScanProjects(workingRootFolder, DateTime.UtcNow, retentionDays);
    }

    public IReadOnlyList<ProjectIndexItem> ScanProjects(string workingRootFolder, DateTime nowUtc, int retentionDays = DefaultRetentionDays)
    {
        if (string.IsNullOrWhiteSpace(workingRootFolder) || !Directory.Exists(workingRootFolder))
            return [];

        var normalizedRetentionDays = NormalizeRetentionDays(retentionDays);
        var projectFolders = EnumerateManifestFiles(workingRootFolder)
            .Select(ProjectFolderLayout.GetProjectFolderFromManifestPath)
            .Where(folder => !string.IsNullOrWhiteSpace(folder))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var projects = new List<ProjectIndexItem>();

        foreach (var projectFolder in projectFolders)
        {
            var item = TryReadProject(projectFolder!, nowUtc, normalizedRetentionDays);
            if (item is not null)
                projects.Add(item);
        }

        return projects
            .OrderByDescending(project => project.UpdatedUtc)
            .ThenByDescending(project => project.CreatedUtc)
            .ThenBy(project => project.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public IReadOnlyList<ProjectIndexItem> ScanSelectableProjects(string workingRootFolder, int retentionDays = DefaultRetentionDays)
    {
        return ScanSelectableProjects(workingRootFolder, DateTime.UtcNow, retentionDays);
    }

    public IReadOnlyList<ProjectIndexItem> ScanSelectableProjects(
        string workingRootFolder,
        DateTime nowUtc,
        int retentionDays = DefaultRetentionDays)
    {
        return ScanProjects(workingRootFolder, nowUtc, retentionDays)
            .Where(project => project.IsSelectableProject)
            .ToList();
    }


    public IReadOnlyList<DamagedProjectInfo> ScanDamagedProjects(string workingRootFolder)
    {
        if (string.IsNullOrWhiteSpace(workingRootFolder) || !Directory.Exists(workingRootFolder))
            return [];

        var structure = WorkFolderStructure.FromRoot(workingRootFolder);
        var damagedProjects = new List<DamagedProjectInfo>();

        ScanDamagedProjectType(
            structure.LocalProjectsFolder,
            ProjectManifestTypes.FolderProject,
            ProjectFolderLayout.WorkManifestFileName,
            path => TryReadExportManifest(path) is not null,
            damagedProjects);

        ScanDamagedProjectType(
            structure.Mp3DiscProjectsFolder,
            ProjectManifestTypes.Mp3DiscProject,
            ProjectFolderLayout.WorkManifestFileName,
            path => TryReadMp3DiscManifest(path) is not null,
            damagedProjects);

        ScanDamagedAudioProjects(structure.AudioDiscProjectsFolder, damagedProjects);

        return damagedProjects
            .OrderBy(item => item.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public IReadOnlyList<ProjectIndexItem> FindExpiredProjects(string workingRootFolder, DateTime nowUtc, int retentionDays = DefaultRetentionDays)
    {
        return ScanProjects(workingRootFolder, nowUtc, retentionDays)
            .Where(project => project.IsExpired)
            .ToList();
    }

    public ProjectCleanupResult DeleteProjectsOlderThan(string workingRootFolder, DateTime nowUtc, int olderThanDays)
    {
        var normalizedOlderThanDays = NormalizeRetentionDays(olderThanDays);
        var projectsToDelete = normalizedOlderThanDays == 0
            ? ScanProjects(workingRootFolder, nowUtc, MaximumRetentionDays)
            : FindExpiredProjects(workingRootFolder, nowUtc, normalizedOlderThanDays);
        var deleted = 0;
        var failures = new List<string>();

        foreach (var project in projectsToDelete)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(project.ProjectFolder) || !Directory.Exists(project.ProjectFolder))
                    continue;

                DeleteDirectory(project.ProjectFolder);
                deleted++;
            }
            catch (Exception ex)
            {
                failures.Add($"{project.DisplayName}: {ex.Message}");
            }
        }

        return new ProjectCleanupResult(projectsToDelete.Count, deleted, failures);
    }

    public ProjectDeletionResult DeleteProject(string workingRootFolder, string projectFolder)
    {
        if (string.IsNullOrWhiteSpace(workingRootFolder) || string.IsNullOrWhiteSpace(projectFolder))
            return new ProjectDeletionResult(false, "Projekt- oder Arbeitsordner fehlt.");

        try
        {
            var rootPath = Path.GetFullPath(workingRootFolder)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var projectPath = Path.GetFullPath(projectFolder)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var rootPrefix = rootPath + Path.DirectorySeparatorChar;

            if (string.Equals(projectPath, rootPath, StringComparison.OrdinalIgnoreCase) ||
                !projectPath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return new ProjectDeletionResult(false, "Der ausgewählte Ordner liegt nicht innerhalb des BookStitch-Projektbereichs.");
            }

            if (!Directory.Exists(projectPath))
                return new ProjectDeletionResult(false, "Der Projektordner ist nicht mehr vorhanden.");

            DeleteDirectory(projectPath);
            return new ProjectDeletionResult(true, null);
        }
        catch (Exception ex)
        {
            return new ProjectDeletionResult(false, ex.Message);
        }
    }

    public static int NormalizeRetentionDays(int retentionDays)
    {
        return Math.Clamp(retentionDays, 0, MaximumRetentionDays);
    }

    public static int NormalizeDeleteOlderThanDays(int olderThanDays)
    {
        return NormalizeRetentionDays(olderThanDays);
    }

    private static void DeleteDirectory(string folderPath)
    {
        foreach (var file in Directory.EnumerateFiles(folderPath, "*", SearchOption.AllDirectories))
        {
            try
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }
            catch
            {
                // Der spätere Delete wirft bei Bedarf den eigentlichen Fehler.
            }
        }

        Directory.Delete(folderPath, recursive: true);
    }


    private static void ScanDamagedProjectType(
        string projectTypeFolder,
        string projectType,
        string requiredManifestFileName,
        Func<string, bool> canReadManifest,
        ICollection<DamagedProjectInfo> damagedProjects)
    {
        if (!Directory.Exists(projectTypeFolder))
            return;

        IEnumerable<string> projectFolders;
        try
        {
            projectFolders = Directory.EnumerateDirectories(projectTypeFolder, "*", SearchOption.TopDirectoryOnly).ToList();
        }
        catch
        {
            return;
        }

        foreach (var projectFolder in projectFolders)
        {
            if (!ContainsProjectContent(projectFolder))
                continue;

            var requiredManifestPath = requiredManifestFileName == ProjectFolderLayout.AudioDiscManifestFileName
                ? ProjectFolderLayout.ResolveAudioDiscManifestPath(projectFolder)
                : ProjectFolderLayout.ResolveWorkManifestPath(projectFolder);

            if (!File.Exists(requiredManifestPath))
            {
                damagedProjects.Add(new DamagedProjectInfo(
                    projectFolder,
                    projectType,
                    Path.GetFileName(projectFolder),
                    requiredManifestFileName,
                    $"Die erforderliche Projektdatei „{requiredManifestFileName}“ fehlt."));
                continue;
            }

            if (!canReadManifest(requiredManifestPath))
            {
                if (CanReadAsDifferentProjectManifest(requiredManifestPath, projectType))
                    continue;

                damagedProjects.Add(new DamagedProjectInfo(
                    projectFolder,
                    projectType,
                    Path.GetFileName(projectFolder),
                    requiredManifestFileName,
                    $"Die erforderliche Projektdatei „{requiredManifestFileName}“ ist nicht lesbar oder enthält kein gültiges {FormatProjectTypeForDamageMessage(projectType)}-Manifest."));
            }
        }
    }


    private static bool CanReadAsDifferentProjectManifest(string manifestPath, string expectedProjectType)
    {
        if (!string.Equals(expectedProjectType, ProjectManifestTypes.FolderProject, StringComparison.OrdinalIgnoreCase) &&
            TryReadExportManifest(manifestPath) is not null)
        {
            return true;
        }

        if (!string.Equals(expectedProjectType, ProjectManifestTypes.Mp3DiscProject, StringComparison.OrdinalIgnoreCase) &&
            TryReadMp3DiscManifest(manifestPath) is not null)
        {
            return true;
        }

        if (!string.Equals(expectedProjectType, ProjectManifestTypes.AudioCdProject, StringComparison.OrdinalIgnoreCase) &&
            TryReadAudioDiscManifest(manifestPath) is not null)
        {
            return true;
        }

        return false;
    }

    private static void ScanDamagedAudioProjects(
        string projectTypeFolder,
        ICollection<DamagedProjectInfo> damagedProjects)
    {
        if (!Directory.Exists(projectTypeFolder))
            return;

        IEnumerable<string> projectFolders;
        try
        {
            projectFolders = Directory.EnumerateDirectories(projectTypeFolder, "*", SearchOption.TopDirectoryOnly).ToList();
        }
        catch
        {
            return;
        }

        foreach (var projectFolder in projectFolders)
        {
            if (!ContainsProjectContent(projectFolder))
                continue;

            var problems = new List<string>();
            var missingFiles = new List<string>();

            var audioManifestPath = ProjectFolderLayout.ResolveAudioDiscManifestPath(projectFolder);
            var audioManifest = TryReadAudioDiscManifest(audioManifestPath);
            if (!File.Exists(audioManifestPath))
            {
                missingFiles.Add(ProjectFolderLayout.AudioDiscManifestFileName);
                problems.Add($"Die erforderliche Projektdatei „{ProjectFolderLayout.AudioDiscManifestFileName}“ fehlt.");
            }
            else if (audioManifest is null)
            {
                problems.Add($"Die erforderliche Projektdatei „{ProjectFolderLayout.AudioDiscManifestFileName}“ ist nicht lesbar oder enthält kein gültiges Audio-CD-Projekt-Manifest.");
            }

            if (audioManifest is not null)
            {
                var workManifestPath = ProjectFolderLayout.ResolveWorkManifestPath(projectFolder);
                if (File.Exists(workManifestPath) &&
                    TryReadExportManifest(workManifestPath) is null &&
                    !CanReadAsDifferentProjectManifest(workManifestPath, ProjectManifestTypes.AudioCdProject))
                {
                    problems.Add($"Die optionale Export-Projektdatei „{ProjectFolderLayout.WorkManifestFileName}“ ist nicht lesbar.");
                }
            }

            if (problems.Count == 0)
                continue;

            damagedProjects.Add(new DamagedProjectInfo(
                projectFolder,
                ProjectManifestTypes.AudioCdProject,
                Path.GetFileName(projectFolder),
                missingFiles.Count > 0
                    ? string.Join(", ", missingFiles)
                    : $"{ProjectFolderLayout.AudioDiscManifestFileName}, {ProjectFolderLayout.WorkManifestFileName}",
                string.Join(" ", problems)));
        }
    }

    private static bool ContainsProjectContent(string projectFolder)
    {
        try
        {
            return Directory.EnumerateFileSystemEntries(projectFolder).Any();
        }
        catch
        {
            return true;
        }
    }

    private static string FormatProjectTypeForDamageMessage(string projectType) => projectType switch
    {
        ProjectManifestTypes.Mp3DiscProject => "MP3-CD-Projekt",
        ProjectManifestTypes.AudioCdProject => "Audio-CD-Projekt",
        ProjectManifestTypes.FolderProject => "Ordnerprojekt",
        _ => "BookStitch-Projekt"
    };

    private static IEnumerable<string> EnumerateManifestFiles(string workingRootFolder)
    {
        try
        {
            var structuredProjectFolders = EnumerateStructuredProjectFolders(workingRootFolder).ToList();
            if (structuredProjectFolders.Count > 0)
            {
                return structuredProjectFolders
                    .SelectMany(EnumerateKnownManifestPaths)
                    .Where(File.Exists)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            var projectJsonFiles = Directory.EnumerateFiles(workingRootFolder, "project.json", SearchOption.AllDirectories).ToList();
            var exportProjectJsonFiles = Directory.EnumerateFiles(workingRootFolder, "export-project.json", SearchOption.AllDirectories).ToList();
            var audioDiscProjectJsonFiles = Directory.EnumerateFiles(workingRootFolder, "audio-disc-project.json", SearchOption.AllDirectories).ToList();

            return projectJsonFiles
                .Concat(exportProjectJsonFiles)
                .Concat(audioDiscProjectJsonFiles)
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    private static IEnumerable<string> EnumerateStructuredProjectFolders(string workingRootFolder)
    {
        var structure = WorkFolderStructure.FromRoot(workingRootFolder);
        foreach (var projectTypeFolder in new[]
                 {
                     structure.LocalProjectsFolder,
                     structure.Mp3DiscProjectsFolder,
                     structure.AudioDiscProjectsFolder
                 })
        {
            if (!Directory.Exists(projectTypeFolder))
                continue;

            IEnumerable<string> projectFolders;
            try
            {
                projectFolders = Directory.EnumerateDirectories(projectTypeFolder, "*", SearchOption.TopDirectoryOnly).ToList();
            }
            catch
            {
                continue;
            }

            foreach (var projectFolder in projectFolders)
                yield return projectFolder;
        }
    }

    private static IEnumerable<string> EnumerateKnownManifestPaths(string projectFolder)
    {
        yield return ProjectFolderLayout.GetWorkManifestPath(projectFolder);
        yield return ProjectFolderLayout.GetExportManifestPath(projectFolder);
        yield return ProjectFolderLayout.GetAudioDiscManifestPath(projectFolder);
        yield return Path.Combine(projectFolder, ProjectFolderLayout.WorkManifestFileName);
        yield return Path.Combine(projectFolder, ProjectFolderLayout.ExportManifestFileName);
        yield return Path.Combine(projectFolder, ProjectFolderLayout.AudioDiscManifestFileName);
    }

    private static ProjectIndexItem? TryReadProject(string projectFolder, DateTime nowUtc, int retentionDays)
    {
        new ProjectFolderMigrationService().MigrateIfNeeded(projectFolder);
        var importManifestPath = ProjectFolderLayout.ResolveWorkManifestPath(projectFolder);
        var exportManifestPath = ProjectFolderLayout.ResolveExportManifestPath(projectFolder);
        var audioDiscManifestPath = ProjectFolderLayout.ResolveAudioDiscManifestPath(projectFolder);

        var importManifest = TryReadMp3DiscManifest(importManifestPath);
        var exportManifest = TryReadExportManifest(exportManifestPath) ?? TryReadExportManifest(importManifestPath);
        var audioDiscManifest = TryReadAudioDiscManifest(audioDiscManifestPath);

        if (importManifest is null && exportManifest is null && audioDiscManifest is null)
            return null;

        var createdUtc = MinNonDefault(importManifest?.CreatedUtc, exportManifest?.CreatedUtc, audioDiscManifest?.CreatedUtc) ?? DateTime.UtcNow;
        var updatedUtc = MaxNonDefault(importManifest?.UpdatedUtc, exportManifest?.UpdatedUtc, audioDiscManifest?.UpdatedUtc) ?? createdUtc;
        var expirationLocalDate = CalculateExpirationLocalDate(createdUtc, retentionDays);
        var expiresUtc = DateTime.SpecifyKind(expirationLocalDate, DateTimeKind.Local).ToUniversalTime();
        var remainingDays = (expirationLocalDate - nowUtc.ToLocalTime().Date).Days;

        // Disc project manifests are the authoritative source for the current visible metadata.
        // An older export manifest may still contain values from a previous export or even an
        // incorrectly classified legacy run.
        var title = FirstNonEmpty(audioDiscManifest?.Title, importManifest?.Title, exportManifest?.Metadata.Title);
        var author = FirstNonEmpty(audioDiscManifest?.Author, importManifest?.Author, exportManifest?.Metadata.Author);
        var album = FirstNonEmpty(audioDiscManifest?.Album, importManifest?.Album, exportManifest?.Metadata.Album);
        var narrator = FirstNonEmpty(audioDiscManifest?.Narrator, importManifest?.Narrator, exportManifest?.Metadata.Narrator);
        var genre = FirstNonEmpty(audioDiscManifest?.Genre, importManifest?.Genre, exportManifest?.Metadata.Genre);
        var displayName = BuildDisplayName(projectFolder, title, author);

        var mp3ImportedDiscCount = importManifest?.ImportedDiscs.Count(disc =>
            string.Equals(disc.Status, Mp3DiscImportStatus.Completed, StringComparison.OrdinalIgnoreCase)) ?? 0;
        var audioCompletedDiscCount = audioDiscManifest?.Discs.Count(disc =>
            string.Equals(disc.Status, AudioDiscStatus.Completed, StringComparison.OrdinalIgnoreCase)) ?? 0;
        var importedDiscCount = audioDiscManifest is not null ? audioCompletedDiscCount : mp3ImportedDiscCount;
        var totalDiscs = audioDiscManifest?.TotalDiscs ?? importManifest?.TotalDiscs ?? 0;

        var importCanResume = importManifest is not null && totalDiscs > 0 && importedDiscCount < totalDiscs;
        var audioCanResume = audioDiscManifest is not null && !AreAudioDiscSourcesComplete(audioDiscManifest);
        var exportCanResume = exportManifest?.Resume.CanResume == true || IsResumeStatus(exportManifest?.State.Status);
        var isCompletedProject = HasSuccessfulExport(exportManifest, audioDiscManifest);
        var sourcesComplete = isCompletedProject ||
            AreSourcesComplete(projectFolder, exportManifest, importManifest, audioDiscManifest);
        var status = ResolvePipelineStatus(
            exportManifest,
            importManifest,
            audioDiscManifest,
            sourcesComplete,
            isCompletedProject);
        var projectType = audioDiscManifest is not null
            ? ProjectManifestTypes.AudioCdProject
            : importManifest is not null
                ? ProjectManifestTypes.Mp3DiscProject
                : exportManifest is not null
                    ? ProjectManifestTypes.FolderProject
                    : "Unknown";

        return new ProjectIndexItem
        {
            ProjectFolder = projectFolder,
            ProjectType = projectType,
            DisplayName = displayName,
            Status = status,
            CanResume = importCanResume || audioCanResume || exportCanResume,
            IsSelectableProject = sourcesComplete,
            IsCompletedProject = isCompletedProject,
            IsExpired = remainingDays < 0,
            CreatedUtc = createdUtc,
            UpdatedUtc = updatedUtc,
            ExpiresUtc = expiresUtc,
            SourceFolder = exportManifest?.SourceFolder
                ?? (audioDiscManifest is not null ? ProjectFolderLayout.ResolveOriginalsFolder(projectFolder) : importManifest?.SourceFolder)
                ?? "",
            OutputFolder = exportManifest?.Export.OutputFolder ?? audioDiscManifest?.OutputFolder ?? importManifest?.OutputFolder ?? "",
            OutputFileName = FirstNonEmpty(
                exportManifest?.Export.OutputFileName,
                Path.GetFileName(audioDiscManifest?.FinalOutputPath),
                importManifest?.OutputFileName),
            Title = title,
            Author = author,
            Album = album,
            Narrator = narrator,
            Genre = genre,
            TotalDiscs = totalDiscs,
            ImportedDiscCount = importedDiscCount,
            PrimaryManifestPath = File.Exists(exportManifestPath)
                ? exportManifestPath
                : File.Exists(audioDiscManifestPath)
                    ? audioDiscManifestPath
                    : importManifestPath
        };
    }


    private static bool HasSuccessfulExport(
        ExportWorkManifest? exportManifest,
        AudioDiscProjectManifest? audioDiscManifest)
    {
        return string.Equals(
                   exportManifest?.State.Status,
                   ProjectPipelineStateNames.Completed,
                   StringComparison.OrdinalIgnoreCase) ||
               string.Equals(
                   exportManifest?.State.LastSuccessfulStep,
                   "ExportCompleted",
                   StringComparison.OrdinalIgnoreCase) ||
               audioDiscManifest?.HasSuccessfulExport == true ||
               string.Equals(
                   audioDiscManifest?.ExportStatus,
                   AudioDiscExportStatus.Completed,
                   StringComparison.OrdinalIgnoreCase) ||
               !string.IsNullOrWhiteSpace(audioDiscManifest?.LastSuccessfulOutputPath) ||
               !string.IsNullOrWhiteSpace(audioDiscManifest?.FinalOutputPath);
    }

    private static bool AreSourcesComplete(
        string projectFolder,
        ExportWorkManifest? exportManifest,
        Mp3DiscProjectManifest? importManifest,
        AudioDiscProjectManifest? audioDiscManifest)
    {
        if (audioDiscManifest is not null)
            return AreAudioDiscSourcesComplete(audioDiscManifest);

        if (importManifest is not null)
        {
            return importManifest.TotalDiscs > 0 &&
                   Enumerable.Range(1, importManifest.TotalDiscs).All(number =>
                       importManifest.ImportedDiscs.Any(disc =>
                           disc.DiscNumber == number &&
                           string.Equals(disc.Status, Mp3DiscImportStatus.Completed, StringComparison.OrdinalIgnoreCase) &&
                           disc.CopiedFiles > 0 &&
                           disc.CopiedFiles >= disc.FileCount &&
                           Directory.Exists(disc.LocalFolder)));
        }

        if (exportManifest is null || exportManifest.Tracks.Count == 0)
            return false;

        var sourceGroups = exportManifest.Tracks
            .Where(track => !string.IsNullOrWhiteSpace(track.SourcePath))
            .GroupBy(track => track.SourcePath, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (sourceGroups.Count > 0)
        {
            return sourceGroups.All(group =>
                File.Exists(group.Key) && new FileInfo(group.Key).Length > 0);
        }

        // Kompatibilität für ältere Manifeste, die die internen Quellpfade noch nicht
        // vollständig gespeichert haben. Vollständig konvertierte Tracks belegen, dass
        // die Quellen zum Zeitpunkt der Vorbereitung vollständig vorhanden waren.
        return exportManifest.Tracks.Count > 0 && exportManifest.Tracks.All(track =>
            string.Equals(track.Status, ProjectManifestTrackStatuses.Converted, StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(track.ConvertedPath));
    }

    private static bool AreAudioDiscSourcesComplete(AudioDiscProjectManifest manifest)
    {
        return manifest.TotalDiscs > 0 &&
               Enumerable.Range(1, manifest.TotalDiscs).All(number =>
                   manifest.Discs.Any(disc =>
                       disc.DiscNumber == number &&
                       string.Equals(disc.Status, AudioDiscStatus.Completed, StringComparison.OrdinalIgnoreCase) &&
                       disc.Tracks.Count > 0 &&
                       disc.Tracks.All(track =>
                           string.Equals(track.Status, AudioDiscTrackStatus.Ripped, StringComparison.OrdinalIgnoreCase))));
    }

    private static string ResolvePipelineStatus(
        ExportWorkManifest? exportManifest,
        Mp3DiscProjectManifest? importManifest,
        AudioDiscProjectManifest? audioDiscManifest,
        bool sourcesComplete,
        bool hasSuccessfulExport)
    {
        if (hasSuccessfulExport)
            return ProjectPipelineStateNames.Completed;

        if (!sourcesComplete && (importManifest is not null || audioDiscManifest is not null))
            return ProjectPipelineStateNames.AcquiringSources;

        var exportStatus = exportManifest?.State.Status;
        var exportState = ProjectPipelineStateNames.FromManifestValue(
            exportStatus,
            hasSuccessfulExport,
            sourcesComplete);
        if (exportState is ProjectPipelineState.Converting or
            ProjectPipelineState.ReviewBeforeMerge or
            ProjectPipelineState.Merging or
            ProjectPipelineState.Completed)
        {
            return exportState.ToManifestValue();
        }

        string? discPipelineState = null;
        if (audioDiscManifest is not null)
        {
            discPipelineState = !string.Equals(
                    audioDiscManifest.FormatVersion,
                    AudioDiscProjectManifestVersions.Current,
                    StringComparison.OrdinalIgnoreCase) &&
                string.Equals(
                    audioDiscManifest.PipelineState,
                    ProjectPipelineStateNames.Preparing,
                    StringComparison.OrdinalIgnoreCase)
                ? FirstNonEmpty(audioDiscManifest.ExportStatus, audioDiscManifest.Status)
                : audioDiscManifest.PipelineState;
        }
        else if (importManifest is not null)
        {
            discPipelineState = !string.Equals(
                    importManifest.FormatVersion,
                    Mp3DiscManifestVersions.Current,
                    StringComparison.OrdinalIgnoreCase) &&
                string.Equals(
                    importManifest.PipelineState,
                    ProjectPipelineStateNames.Preparing,
                    StringComparison.OrdinalIgnoreCase)
                ? (sourcesComplete
                    ? ProjectPipelineStateNames.ReviewBeforeMerge
                    : ProjectPipelineStateNames.AcquiringSources)
                : importManifest.PipelineState;
        }

        return ProjectPipelineStateNames.FromManifestValue(
            discPipelineState ?? exportStatus,
            hasSuccessfulExport,
            sourcesComplete).ToManifestValue();
    }

    private static bool IsResumeStatus(string? status)
    {
        return status is ProjectManifestStatuses.LegacyCanceled or
            ProjectManifestStatuses.LegacyFailed or
            ProjectManifestStatuses.LegacyExporting;
    }

    private static Mp3DiscProjectManifest? TryReadMp3DiscManifest(string manifestPath)
    {
        try
        {
            if (!File.Exists(manifestPath))
                return null;

            var json = File.ReadAllText(manifestPath);
            using var document = JsonDocument.Parse(json);

            if (!document.RootElement.TryGetProperty("ProjectType", out var projectType) ||
                !string.Equals(projectType.GetString(), "Mp3Disc", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return JsonSerializer.Deserialize<Mp3DiscProjectManifest>(json);
        }
        catch
        {
            return null;
        }
    }

    private static AudioDiscProjectManifest? TryReadAudioDiscManifest(string manifestPath)
    {
        try
        {
            if (!File.Exists(manifestPath))
                return null;

            var json = File.ReadAllText(manifestPath);
            using var document = JsonDocument.Parse(json);

            if (!document.RootElement.TryGetProperty("ProjectType", out var projectType) ||
                !string.Equals(projectType.GetString(), ProjectManifestTypes.AudioCdProject, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return JsonSerializer.Deserialize<AudioDiscProjectManifest>(json);
        }
        catch
        {
            return null;
        }
    }

    private static ExportWorkManifest? TryReadExportManifest(string manifestPath)
    {
        try
        {
            if (!File.Exists(manifestPath))
                return null;

            var json = File.ReadAllText(manifestPath);
            using var document = JsonDocument.Parse(json);

            if (document.RootElement.TryGetProperty("ProjectType", out var projectType) &&
                string.Equals(projectType.GetString(), "Mp3Disc", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            if (!document.RootElement.TryGetProperty("FormatVersion", out _) &&
                !document.RootElement.TryGetProperty("State", out _))
            {
                return null;
            }

            return JsonSerializer.Deserialize<ExportWorkManifest>(json);
        }
        catch
        {
            return null;
        }
    }

    private static DateTime CalculateExpirationLocalDate(DateTime createdUtc, int retentionDays)
    {
        var createdLocal = createdUtc.Kind == DateTimeKind.Local
            ? createdUtc
            : createdUtc.ToLocalTime();

        return createdLocal.Date.AddDays(retentionDays);
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

    private static string BuildDisplayName(string projectFolder, string title, string author)
    {
        if (!string.IsNullOrWhiteSpace(author) && !string.IsNullOrWhiteSpace(title))
            return $"{author.Trim()} - {title.Trim()}";

        if (!string.IsNullOrWhiteSpace(title))
            return title.Trim();

        return new DirectoryInfo(projectFolder).Name;
    }


    private static DateTime? MinNonDefault(params DateTime?[] values)
    {
        var validValues = values
            .Where(value => value.HasValue && value.Value != default)
            .Select(value => value!.Value)
            .ToList();

        return validValues.Count == 0 ? null : validValues.Min();
    }

    private static DateTime? MaxNonDefault(params DateTime?[] values)
    {
        var validValues = values
            .Where(value => value.HasValue && value.Value != default)
            .Select(value => value!.Value)
            .ToList();

        return validValues.Count == 0 ? null : validValues.Max();
    }
}

public sealed record ProjectCleanupResult(int MatchedCount, int DeletedCount, IReadOnlyList<string> Failures)
{
    public bool HasFailures => Failures.Count > 0;
}

public sealed record ProjectDeletionResult(bool Deleted, string? ErrorMessage);


public sealed record DamagedProjectInfo(
    string ProjectFolder,
    string ProjectType,
    string DisplayName,
    string RequiredManifestFileName,
    string Reason);
