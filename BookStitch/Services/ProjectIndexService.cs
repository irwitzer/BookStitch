using BookStitch.Models;
using System.IO;
using System.Text.Json;

namespace BookStitch.Services;

public sealed class ProjectIndexService
{
    public ProjectIndexService() => Catalog = new ProjectCatalog(ScanCatalogIndex,
        (root, data, token) => ScanCatalogDamage(root, data.ProjectFolders, data.ReadFailures, token));

    public ProjectCatalog Catalog { get; }

    public Action? BeforeManualDeletion { get; set; }

    private Func<string?> _activeProjectFolderProvider = () => null;

    public void SetActiveProjectFolderProvider(Func<string?> provider) =>
        _activeProjectFolderProvider = provider ?? throw new ArgumentNullException(nameof(provider));

    private bool ContainsActiveProject(string folder)
    {
        var activeFolder = _activeProjectFolderProvider();
        if (string.IsNullOrWhiteSpace(activeFolder)) return false;
        var target = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var active = Path.GetFullPath(activeFolder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.Equals(target, active, StringComparison.OrdinalIgnoreCase) ||
            active.StartsWith(target + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    public const int DefaultRetentionDays = 180;
    public const int MaximumRetentionDays = 360;
    public const int DefaultDeleteOlderThanDays = 180;

    public IReadOnlyList<ProjectIndexItem> ScanProjects(string workingRootFolder, int retentionDays = DefaultRetentionDays, ICollection<DamagedProjectInfo>? readFailures = null)
    {
        return ScanProjects(workingRootFolder, DateTime.UtcNow, retentionDays, readFailures);
    }

    public IReadOnlyList<ProjectIndexItem> ScanProjects(string workingRootFolder, DateTime nowUtc, int retentionDays = DefaultRetentionDays, ICollection<DamagedProjectInfo>? readFailures = null)
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
            var item = TryReadProjectForIndex(projectFolder!, nowUtc, normalizedRetentionDays, readFailures);
            if (item is not null) { item.WorkingRoot = workingRootFolder; projects.Add(item); }
        }

        return projects
            .OrderByDescending(project => project.UpdatedUtc)
            .ThenByDescending(project => project.CreatedUtc)
            .ThenBy(project => project.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public IReadOnlyList<ProjectIndexItem> ScanSelectableProjects(string workingRootFolder, int retentionDays = DefaultRetentionDays, ICollection<DamagedProjectInfo>? readFailures = null)
    {
        return ScanSelectableProjects(workingRootFolder, DateTime.UtcNow, retentionDays, readFailures);
    }

    public IReadOnlyList<ProjectIndexItem> ScanSelectableProjects(
        string workingRootFolder,
        DateTime nowUtc,
        int retentionDays = DefaultRetentionDays,
        ICollection<DamagedProjectInfo>? readFailures = null)
    {
        return ScanProjects(workingRootFolder, nowUtc, retentionDays, readFailures)
            .Where(project => project.IsVisibleInProjectBrowser)
            .ToList();
    }


    public ProjectCatalogIndex ScanCatalogIndex(string workingRootFolder, int retentionDays, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var folders = EnumerateStructuredProjectFolders(workingRootFolder).ToArray();
        var candidates = folders.Length > 0 ? folders : EnumerateManifestFiles(workingRootFolder)
            .Select(ProjectFolderLayout.GetProjectFolderFromManifestPath).Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => path!).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var failures = new List<DamagedProjectInfo>();
        var projects = new List<ProjectIndexItem>();
        var now = DateTime.UtcNow;
        foreach (var folder in candidates)
        {
            token.ThrowIfCancellationRequested();
            var item = TryReadProjectForIndex(folder, now, NormalizeRetentionDays(retentionDays), failures);
            if (item is not null) { item.WorkingRoot = workingRootFolder; projects.Add(item); }
        }
        return new ProjectCatalogIndex(projects.OrderByDescending(item => item.UpdatedUtc)
            .ThenByDescending(item => item.CreatedUtc).ThenBy(item => item.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToArray(), folders, failures.ToArray());
    }

    public IReadOnlyList<DamagedProjectInfo> ScanDamagedProjects(string workingRootFolder) =>
        ScanCatalogDamage(workingRootFolder, EnumerateStructuredProjectFolders(workingRootFolder).ToArray(), [], CancellationToken.None);

    public IReadOnlyList<DamagedProjectInfo> ScanCatalogDamage(string workingRootFolder,
        IReadOnlyList<string> folders, IReadOnlyList<DamagedProjectInfo> readFailures, CancellationToken token)
    {
        var structure = WorkFolderStructure.FromRoot(workingRootFolder);
        var damaged = new List<DamagedProjectInfo>(readFailures);
        // Reuse index discovery; no second working-root enumeration.
        foreach (var folder in folders)
        {
            token.ThrowIfCancellationRequested();
            var parent = Path.GetDirectoryName(folder);
            if (string.Equals(parent, structure.LocalProjectsFolder, StringComparison.OrdinalIgnoreCase))
                ScanDamagedProjectType(structure.LocalProjectsFolder, ProjectManifestTypes.FolderProject,
                    ProjectFolderLayout.WorkManifestFileName, path => ReadForDamageScan(() => TryReadExportManifest(path)) is not null, damaged, [folder]);
            else if (string.Equals(parent, structure.Mp3DiscProjectsFolder, StringComparison.OrdinalIgnoreCase))
                ScanDamagedProjectType(structure.Mp3DiscProjectsFolder, ProjectManifestTypes.Mp3DiscProject,
                    ProjectFolderLayout.WorkManifestFileName, path => ReadForDamageScan(() => TryReadMp3DiscManifest(path)) is not null, damaged, [folder]);
            else if (string.Equals(parent, structure.AudioDiscProjectsFolder, StringComparison.OrdinalIgnoreCase))
                ScanDamagedAudioProjects(structure.AudioDiscProjectsFolder, damaged, [folder]);
        }
        return damaged.GroupBy(item => item.ProjectFolder, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First()).OrderBy(item => item.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    public IReadOnlyList<ProjectIndexItem> FindExpiredProjects(string workingRootFolder, DateTime nowUtc, int retentionDays = DefaultRetentionDays)
    {
        return ScanProjects(workingRootFolder, nowUtc, retentionDays)
            .Where(project => project.IsExpired)
            .ToList();
    }

    public ProjectCleanupResult DeleteProjectsOlderThan(string workingRootFolder, DateTime nowUtc, int olderThanDays, CancellationToken cancellationToken = default,
        IReadOnlyList<ProjectIndexItem>? knownProjects = null)
    {
        if (knownProjects is null) BeforeManualDeletion?.Invoke();
        cancellationToken.ThrowIfCancellationRequested();
        var normalizedOlderThanDays = NormalizeRetentionDays(olderThanDays);
        var projectsToDelete = knownProjects is not null
            ? knownProjects.Where(project => normalizedOlderThanDays == 0 ||
                project.IsExpired).ToList()
            : normalizedOlderThanDays == 0
            ? ScanProjects(workingRootFolder, nowUtc, MaximumRetentionDays)
            : FindExpiredProjects(workingRootFolder, nowUtc, normalizedOlderThanDays);
        var deleted = 0;
        var failures = new List<string>();

        foreach (var project in projectsToDelete)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (string.IsNullOrWhiteSpace(project.ProjectFolder) || !Directory.Exists(project.ProjectFolder))
                    continue;

                if (ContainsActiveProject(project.ProjectFolder)) continue;
                // The catalog is only a candidate inventory, never deletion authority.
                // Re-read eligibility so a stale browser projection cannot delete a changed project.
                if (knownProjects is not null && normalizedOlderThanDays > 0)
                {
                    var current = TryReadProjectForIndex(project.ProjectFolder, nowUtc, normalizedOlderThanDays, null);
                    if (current is null || !current.IsExpired) continue;
                }
                cancellationToken.ThrowIfCancellationRequested();
                if (ContainsActiveProject(project.ProjectFolder)) continue;
                DeleteDirectory(project.ProjectFolder, cancellationToken);
                deleted++;
                Catalog.MarkDirty();
            }
            catch (OperationCanceledException) { throw; }
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
            return new ProjectDeletionResult(false, LocalizationText.Get("Settings.Projects.DeleteOne.MissingFolder"));

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
                return new ProjectDeletionResult(false, LocalizationText.Get("Settings.Projects.DeleteOne.OutsideProjectArea"));
            }

            if (!Directory.Exists(projectPath))
                return new ProjectDeletionResult(false, LocalizationText.Get("Settings.Projects.DeleteOne.AlreadyMissing"));

            if (ContainsActiveProject(projectPath))
                return new ProjectDeletionResult(false, LocalizationText.Get("Settings.Projects.DeleteOne.ActiveProject"));

            BeforeManualDeletion?.Invoke();
            DeleteDirectory(projectPath);
            Catalog.MarkDirty();
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

    private static void DeleteDirectory(string folderPath, CancellationToken cancellationToken = default)
    {
        SafeFileSystemEnumeration.EnsureSafeTree(folderPath, cancellationToken);
        foreach (var file in Directory.EnumerateFiles(folderPath, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            SafeFileSystemEnumeration.EnsureNoReparseAncestors(file);
            try
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }
            catch
            {
                // Der spätere Delete wirft bei Bedarf den eigentlichen Fehler.
            }
        }

        SafeFileSystemEnumeration.EnsureSafeTree(folderPath, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        Directory.Delete(folderPath, recursive: true);
    }


    private static void ScanDamagedProjectType(
        string projectTypeFolder,
        string projectType,
        string requiredManifestFileName,
        Func<string, bool> canReadManifest,
        ICollection<DamagedProjectInfo> damagedProjects, IReadOnlyList<string>? discoveredFolders = null)
    {
        if (!Directory.Exists(projectTypeFolder))
            return;

        IEnumerable<string> projectFolders;
        try
        {
            projectFolders = discoveredFolders ?? Directory.EnumerateDirectories(projectTypeFolder, "*", SearchOption.TopDirectoryOnly).ToList();
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
                : (projectType == ProjectManifestTypes.Mp3DiscProject
                    ? ProjectFolderLayout.ResolveMp3DiscManifestPath(projectFolder)
                    : File.Exists(ProjectFolderLayout.GetExportManifestPath(projectFolder))
                        ? ProjectFolderLayout.GetExportManifestPath(projectFolder)
                        : ProjectFolderLayout.ResolveWorkManifestPath(projectFolder));

            if (!File.Exists(requiredManifestPath))
            {
                damagedProjects.Add(new DamagedProjectInfo(
                    projectFolder,
                    projectType,
                    Path.GetFileName(projectFolder),
                    requiredManifestFileName,
                    LocalizationText.Format("Project.Damage.RequiredFileMissing", requiredManifestFileName)));
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
                    LocalizationText.Format("Project.Damage.RequiredManifestUnreadable", requiredManifestFileName, FormatProjectTypeForDamageMessage(projectType))));
                continue;
            }

            AddDamagedProjectMetadataIfNeeded(projectFolder, projectType, damagedProjects);
        }
    }


    private static T? ReadForDamageScan<T>(Func<T?> read) where T : class
    {
        try { return read(); }
        catch (Exception ex) when (ex is not OperationCanceledException) { return null; }
    }

    private static bool CanReadAsDifferentProjectManifest(string manifestPath, string expectedProjectType)
    {
        if (!string.Equals(expectedProjectType, ProjectManifestTypes.FolderProject, StringComparison.OrdinalIgnoreCase) &&
            ReadForDamageScan(() => TryReadExportManifest(manifestPath)) is not null)
        {
            return true;
        }

        if (!string.Equals(expectedProjectType, ProjectManifestTypes.Mp3DiscProject, StringComparison.OrdinalIgnoreCase) &&
            ReadForDamageScan(() => TryReadMp3DiscManifest(manifestPath)) is not null)
        {
            return true;
        }

        if (!string.Equals(expectedProjectType, ProjectManifestTypes.AudioCdProject, StringComparison.OrdinalIgnoreCase) &&
            ReadForDamageScan(() => TryReadAudioDiscManifest(manifestPath)) is not null)
        {
            return true;
        }

        return false;
    }

    private static void ScanDamagedAudioProjects(
        string projectTypeFolder,
        ICollection<DamagedProjectInfo> damagedProjects, IReadOnlyList<string>? discoveredFolders = null)
    {
        if (!Directory.Exists(projectTypeFolder))
            return;

        IEnumerable<string> projectFolders;
        try
        {
            projectFolders = discoveredFolders ?? Directory.EnumerateDirectories(projectTypeFolder, "*", SearchOption.TopDirectoryOnly).ToList();
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
            var audioManifest = ReadForDamageScan(() => TryReadAudioDiscManifest(audioManifestPath));
            if (!File.Exists(audioManifestPath))
            {
                missingFiles.Add(ProjectFolderLayout.AudioDiscManifestFileName);
                problems.Add(LocalizationText.Format("Project.Damage.RequiredFileMissing", ProjectFolderLayout.AudioDiscManifestFileName));
            }
            else if (audioManifest is null)
            {
                problems.Add(LocalizationText.Format("Project.Damage.RequiredManifestUnreadable", ProjectFolderLayout.AudioDiscManifestFileName, FormatProjectTypeForDamageMessage(ProjectManifestTypes.AudioCdProject)));
            }

            if (audioManifest is not null)
            {
                var workManifestPath = File.Exists(ProjectFolderLayout.GetExportManifestPath(projectFolder))
                    ? ProjectFolderLayout.GetExportManifestPath(projectFolder)
                    : ProjectFolderLayout.ResolveWorkManifestPath(projectFolder);
                if (File.Exists(workManifestPath) &&
                    ReadForDamageScan(() => TryReadExportManifest(workManifestPath)) is null &&
                    !CanReadAsDifferentProjectManifest(workManifestPath, ProjectManifestTypes.AudioCdProject))
                {
                    problems.Add(LocalizationText.Format("Project.Damage.OptionalExportUnreadable", Path.GetFileName(workManifestPath)));
                }

                if (HasUnreadableProjectMetadata(projectFolder))
                {
                    problems.Add(LocalizationText.Format("Project.Damage.MetadataUnreadable", ProjectFolderLayout.ProjectMetadataFileName));
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
                    : $"{ProjectFolderLayout.AudioDiscManifestFileName}, {ProjectFolderLayout.WorkManifestFileName}, {ProjectFolderLayout.ProjectMetadataFileName}",
                string.Join(" ", problems)));
        }
    }

    private static void AddDamagedProjectMetadataIfNeeded(
        string projectFolder,
        string projectType,
        ICollection<DamagedProjectInfo> damagedProjects)
    {
        if (!HasUnreadableProjectMetadata(projectFolder))
            return;

        damagedProjects.Add(new DamagedProjectInfo(
            projectFolder,
            projectType,
            Path.GetFileName(projectFolder),
            ProjectFolderLayout.ProjectMetadataFileName,
            LocalizationText.Format("Project.Damage.MetadataUnreadable", ProjectFolderLayout.ProjectMetadataFileName)));
    }

    private static bool HasUnreadableProjectMetadata(string projectFolder)
    {
        var path = ProjectFolderLayout.GetProjectMetadataPath(projectFolder);
        if (!File.Exists(path))
            return false;

        try
        {
            new ProjectMetadataStore().LoadOrCreate(projectFolder);
            return false;
        }
        catch
        {
            return true;
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
        ProjectManifestTypes.Mp3DiscProject => LocalizationText.Get("Project.Damage.Type.Mp3Disc"),
        ProjectManifestTypes.AudioCdProject => LocalizationText.Get("Project.Damage.Type.AudioCd"),
        ProjectManifestTypes.FolderProject => LocalizationText.Get("Project.Damage.Type.Folder"),
        _ => LocalizationText.Get("Project.Damage.Type.Unknown")
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
            var audioDiscProjectJsonFiles = Directory.EnumerateFiles(workingRootFolder, "audio-disc-project.json", SearchOption.AllDirectories)
                .Concat(Directory.EnumerateFiles(workingRootFolder, "audio-cd-project.json", SearchOption.AllDirectories))
                .Concat(Directory.EnumerateFiles(workingRootFolder, "mp3-cd-project.json", SearchOption.AllDirectories))
                .Concat(Directory.EnumerateFiles(workingRootFolder, "folder-project.json", SearchOption.AllDirectories)).ToList();

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

    public static IReadOnlyList<string> DiscoverProjectFolders(string workingRoot)
    {
        if (!Directory.Exists(workingRoot)) throw new DirectoryNotFoundException(workingRoot);
        var structured = EnumerateStructuredProjectFolders(workingRoot, failOnError: true).ToArray();
        if (structured.Length > 0) return structured;
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { ProjectFolderLayout.WorkManifestFileName, ProjectFolderLayout.ExportManifestFileName,
            ProjectFolderLayout.FolderManifestFileName, ProjectFolderLayout.Mp3DiscManifestFileName,
            ProjectFolderLayout.AudioDiscManifestFileName, ProjectFolderLayout.LegacyAudioDiscManifestFileName };
        // A completed sweep marker must never hide an incomplete discovery caused by I/O errors.
        return SafeFileSystemEnumeration.EnumerateFiles(workingRoot,
                onError: (path, error) => throw new IOException($"Project discovery failed: {path}", error))
            .Where(path => names.Contains(Path.GetFileName(path)))
            .Select(ProjectFolderLayout.GetProjectFolderFromManifestPath).Distinct(AtomicJsonStore.PathComparer).ToArray();
    }

    private static IEnumerable<string> EnumerateStructuredProjectFolders(string workingRootFolder, bool failOnError = false)
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
            catch when (!failOnError)
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
        yield return ProjectFolderLayout.GetFolderManifestPath(projectFolder);
        yield return ProjectFolderLayout.GetMp3DiscManifestPath(projectFolder);
        yield return ProjectFolderLayout.GetExportManifestPath(projectFolder);
        yield return ProjectFolderLayout.GetAudioDiscManifestPath(projectFolder);
        yield return Path.Combine(projectFolder, ProjectFolderLayout.WorkManifestFileName);
        yield return Path.Combine(projectFolder, ProjectFolderLayout.ExportManifestFileName);
        yield return Path.Combine(projectFolder, ProjectFolderLayout.LegacyAudioDiscManifestFileName);
        yield return Path.Combine(ProjectFolderLayout.GetSettingsFolder(projectFolder), ProjectFolderLayout.LegacyAudioDiscManifestFileName);
    }

    private static ProjectIndexItem? TryReadProjectForIndex(string projectFolder, DateTime nowUtc, int retentionDays, ICollection<DamagedProjectInfo>? readFailures)
    {
        try
        {
            return TryReadProject(projectFolder, nowUtc, retentionDays) ?? ReadRecoveryFallback(projectFolder);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            System.Diagnostics.Trace.TraceWarning("Project index read failed for {0}: {1}", projectFolder, ex);
            readFailures?.Add(new DamagedProjectInfo(
                projectFolder, "Unknown", Path.GetFileName(projectFolder), string.Empty,
                LocalizationText.Get("Project.Browser.ProjectUnreadable")));
            return ReadRecoveryFallback(projectFolder);
        }
    }

    private static ProjectIndexItem? ReadRecoveryFallback(string projectFolder)
    {
        var reportPath = Path.Combine(ProjectFolderLayout.GetSettingsFolder(projectFolder), "project-integrity.json");
        if (!File.Exists(reportPath)) return null;
        var report = ProjectIntegrityService.ReadPersistedResult(projectFolder);
        var item = new ProjectIndexItem { ProjectFolder = projectFolder, DisplayName = Path.GetFileName(projectFolder),
            HealthStatus = report?.Health ?? ProjectHealthStatus.Damaged, IntegrityFindings = report?.Findings ?? [],
            ProjectType = "Unknown", IsSelectableProject = false };
        // The established project tree provides type evidence even when a manifest is unreadable.
        var parent = Directory.GetParent(projectFolder);
        if (string.Equals(parent?.Parent?.Name, "Projects", StringComparison.OrdinalIgnoreCase))
            item.ProjectType = parent!.Name switch {
                "LocalProjects" => ProjectManifestTypes.FolderProject,
                "MP3DiscProjects" => ProjectManifestTypes.Mp3DiscProject,
                "AudioDiscProjects" => ProjectManifestTypes.AudioCdProject,
                _ => "Unknown" };
        try
        {
            var metadata = new ProjectMetadataStore().LoadOrCreate(projectFolder);
            var projection = ProjectMetadataMigrationService.CreateProjection(metadata);
            item.Title = projection.Title; item.Author = projection.Author; item.Album = projection.Album;
            item.Narrator = projection.Narrator; item.Genre = projection.Genre;
            item.DisplayName = BuildDisplayName(projectFolder, item.Title, item.Author);
            item.CoverSourcePath = metadata.Cover.SourcePath; item.ProcessedCoverPath = metadata.Cover.ProcessedPath;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidOperationException or ArgumentException or FormatException)
        { System.Diagnostics.Trace.TraceWarning("Recovery metadata read failed for {0}: {1}", projectFolder, ex.Message); }
        foreach (var (path, type) in new[] {
            (ProjectFolderLayout.GetFolderManifestPath(projectFolder), ProjectManifestTypes.FolderProject),
            (ProjectFolderLayout.GetMp3DiscManifestPath(projectFolder), ProjectManifestTypes.Mp3DiscProject),
            (ProjectFolderLayout.GetAudioDiscManifestPath(projectFolder), ProjectManifestTypes.AudioCdProject) })
        {
            try
            {
                if (!File.Exists(path)) continue;
                var manifest = ProjectIntegrityValidator.Read(path);
                if (ProjectIntegrityValidator.Text(manifest, "ProjectType") != type) continue;
                item.ProjectType = type; item.PrimaryManifestPath = path;
                if (manifest["CreatedUtc"] is System.Text.Json.Nodes.JsonValue created && created.TryGetValue<DateTime>(out var createdUtc)) item.CreatedUtc = createdUtc;
                if (manifest["UpdatedUtc"] is System.Text.Json.Nodes.JsonValue updated && updated.TryGetValue<DateTime>(out var updatedUtc)) item.UpdatedUtc = updatedUtc;
                if (manifest["ProjectFormatVersion"] is System.Text.Json.Nodes.JsonValue version && version.TryGetValue<int>(out var format)) item.ProjectFormatVersion = format;
                break;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or ArgumentException or FormatException)
            { System.Diagnostics.Trace.TraceWarning("Recovery manifest read failed for {0}: {1}", projectFolder, ex.Message); }
        }
        return item;
    }

    private static ProjectIndexItem? TryReadProject(string projectFolder, DateTime nowUtc, int retentionDays)
    {
        var importManifestPath = ProjectFolderLayout.ResolveMp3DiscManifestPath(projectFolder);
        var exportManifestPath = ProjectFolderLayout.ResolveExportManifestPath(projectFolder);
        var audioDiscManifestPath = ProjectFolderLayout.ResolveAudioDiscManifestPath(projectFolder);

        var importManifest = TryReadMp3DiscManifest(importManifestPath);
        var exportManifest = TryReadExportManifest(exportManifestPath) ?? TryReadExportManifest(importManifestPath);
        var audioDiscManifest = TryReadAudioDiscManifest(audioDiscManifestPath);

        var folderManifest = new FolderProjectManifestService().Read(projectFolder);
        if (importManifest is null && exportManifest is null && audioDiscManifest is null && folderManifest is null)
            return null;
        if (exportManifest is null && folderManifest is not null)
            exportManifest = new ExportWorkManifest { ProjectId = folderManifest.ProjectId, ProjectWorkFolder = projectFolder,
                SourceFolder = folderManifest.SourceFolder, CreatedUtc = folderManifest.CreatedUtc, UpdatedUtc = folderManifest.UpdatedUtc,
                State = new() { Status = folderManifest.PipelineState } };

        var createdUtc = MinNonDefault(importManifest?.CreatedUtc, exportManifest?.CreatedUtc, audioDiscManifest?.CreatedUtc) ?? DateTime.UtcNow;
        var updatedUtc = MaxNonDefault(importManifest?.UpdatedUtc, exportManifest?.UpdatedUtc, audioDiscManifest?.UpdatedUtc) ?? createdUtc;

        var projectMetadata = new ProjectMetadataMigrationService()
            .LoadAuthoritativeOrLegacy(projectFolder, importManifest, exportManifest, audioDiscManifest);
        var metadata = ProjectMetadataMigrationService.CreateProjection(projectMetadata);
        var title = metadata.Title;
        var author = metadata.Author;
        var album = metadata.Album;
        var narrator = metadata.Narrator;
        var genre = metadata.Genre;
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
        var stoppedDiscProject = audioDiscManifest?.IsStoppedForResume == true || importManifest?.IsStoppedForResume == true ||
            new DiscProjectStopSaveService().HasPendingCommit(projectFolder);
        var isCompletedProject = !stoppedDiscProject && HasSuccessfulExport(exportManifest, audioDiscManifest);
        var expirationLocalDate = isCompletedProject
            ? CalculateExpirationLocalDate(updatedUtc, retentionDays)
            : default;
        var expiresUtc = expirationLocalDate == default
            ? default
            : DateTime.SpecifyKind(expirationLocalDate, DateTimeKind.Local).ToUniversalTime();
        var remainingDays = isCompletedProject
            ? (expirationLocalDate - nowUtc.ToLocalTime().Date).Days
            : int.MaxValue;
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
            HealthStatus = ReadHealth(projectFolder, projectType).Status,
            IntegrityFindings = ReadHealth(projectFolder, projectType).Findings,
            ProjectFormatVersion = ReadProjectFormatVersion(projectFolder, projectType),
            ProjectType = projectType,
            DisplayName = displayName,
            Status = status,
            CanResume = stoppedDiscProject || importCanResume || audioCanResume || exportCanResume,
            IsSelectableProject = sourcesComplete || stoppedDiscProject,
            IsCompletedProject = isCompletedProject,
            IsStoppedDiscProject = stoppedDiscProject,
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
            TrackCount = audioDiscManifest is not null
                ? audioDiscManifest.Discs.Sum(disc => disc.Tracks.Count)
                : importManifest is not null
                    ? importManifest.ImportedDiscs.Where(disc => string.Equals(disc.Status, Mp3DiscImportStatus.Completed, StringComparison.OrdinalIgnoreCase))
                        .Sum(disc => Math.Max(disc.SourceFiles.Count, Math.Max(disc.FileCount, disc.CopiedFiles)))
                    : exportManifest?.Tracks.Select(track => track.TrackIndex).Distinct().Count() ?? 0,
            SelectedPreset = stoppedDiscProject
                ? FirstNonEmpty(audioDiscManifest?.ExportPreset, importManifest?.ExportPreset)
                : FirstNonEmpty(exportManifest?.SelectedPreset, exportManifest?.Export.SelectedPreset,
                    audioDiscManifest?.ExportPreset, importManifest?.ExportPreset),
            CoverSourcePath = projectMetadata.Cover.SourcePath,
            ProcessedCoverPath = projectMetadata.Cover.ProcessedPath,
            SavedConvertedTrackCounts = (exportManifest?.Tracks ?? [])
                .Where(track => !string.IsNullOrWhiteSpace(track.Preset) &&
                    track.Status == ProjectManifestTrackStatuses.Converted && !string.IsNullOrWhiteSpace(track.ConvertedPath))
                .GroupBy(track => track.Preset, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Select(track => track.TrackIndex).Distinct().Count(),
                    StringComparer.OrdinalIgnoreCase),
            PrimaryManifestPath = folderManifest is not null ? ProjectFolderLayout.GetFolderManifestPath(projectFolder)
                : File.Exists(exportManifestPath) ? exportManifestPath
                : File.Exists(audioDiscManifestPath)
                    ? audioDiscManifestPath
                    : importManifestPath
        };
    }


    private static (ProjectHealthStatus Status, IReadOnlyList<IntegrityFinding> Findings) ReadHealth(string folder, string type)
    {
        var path = type == ProjectManifestTypes.AudioCdProject ? ProjectFolderLayout.GetAudioDiscManifestPath(folder)
            : type == ProjectManifestTypes.Mp3DiscProject ? ProjectFolderLayout.GetMp3DiscManifestPath(folder) : ProjectFolderLayout.GetFolderManifestPath(folder);
        if (!File.Exists(path)) return (ProjectHealthStatus.NotChecked, []);
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        if (!document.RootElement.TryGetProperty("Health", out var health)) return (ProjectHealthStatus.NotChecked, []);
        var state = health.TryGetProperty("LastIntegrityStatus", out var status) && Enum.TryParse<ProjectHealthStatus>(status.GetString(), out var parsed)
            ? parsed : ProjectHealthStatus.NotChecked;
        var findings = health.TryGetProperty("Findings", out var rows) ? rows.Deserialize<List<IntegrityFinding>>() ?? [] : [];
        return (state, findings);
    }

    private static int ReadProjectFormatVersion(string folder, string type)
    {
        var path = type == ProjectManifestTypes.AudioCdProject ? ProjectFolderLayout.GetAudioDiscManifestPath(folder)
            : type == ProjectManifestTypes.Mp3DiscProject ? ProjectFolderLayout.GetMp3DiscManifestPath(folder)
            : ProjectFolderLayout.GetFolderManifestPath(folder);
        if (!File.Exists(path)) return 0;
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.TryGetProperty("ProjectFormatVersion", out var version) ? version.GetInt32() : 0;
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

    private static Mp3DiscProjectManifest? TryReadMp3DiscManifest(string path) =>
        ProjectResumePlanService.TryLoadMp3DiscManifest(path);

    private static AudioDiscProjectManifest? TryReadAudioDiscManifest(string path) =>
        ProjectResumePlanService.TryLoadAudioDiscManifest(path);

    private static ExportWorkManifest? TryReadExportManifest(string path) =>
        ProjectResumePlanService.TryLoadExportManifest(path);

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
