using BookStitch.Models;
using System.IO;
using System.Text.Json;

namespace BookStitch.Services;

/// <summary>In-memory ownership of a newly created workspace; never restored from disk.</summary>
internal sealed class SessionProjectLifecycle
{
    public string ProjectFolder { get; private set; } = "";
    public string ProjectType { get; private set; } = "";
    public bool StartAccepted { get; private set; }
    private string _projectId = "";
    private string _workingRoot = "";
    private DateTime _createdUtc;

    public void Register(string folder, string type, bool startAccepted)
    {
        Clear();
        ProjectFolder = Path.GetFullPath(folder);
        ProjectType = type;
        StartAccepted = startAccepted;
    }

    public void RegisterAudioPreparation(string root, AudioDiscProjectManifest manifest, bool startAccepted)
    {
        Register(manifest.ProjectFolder, ProjectManifestTypes.AudioCdProject, startAccepted);
        _workingRoot = Path.GetFullPath(root);
        _projectId = manifest.ProjectId;
        _createdUtc = manifest.CreatedUtc;
    }

    public void AcceptStart(string folder)
    {
        if (Owns(folder)) StartAccepted = true;
    }

    public bool Owns(string folder) => !string.IsNullOrWhiteSpace(ProjectFolder) &&
        TrackPathService.PathEquals(ProjectFolder, folder);

    public bool IsUnstartedAudio(string folder) => Owns(folder) &&
        ProjectType == ProjectManifestTypes.AudioCdProject && !StartAccepted;

    public bool DeleteOnWindowClose(string folder, bool isImportPhase) =>
        IsUnstartedAudio(folder) || (Owns(folder) && isImportPhase && ProjectType == ProjectManifestTypes.FolderProject);

    public bool CanDeferCompletion(string folder, ProjectPipelineState phase, bool activeOrPaused) =>
        !IsUnstartedAudio(folder) &&
        ((activeOrPaused && phase is ProjectPipelineState.AcquiringSources or ProjectPipelineState.Converting) ||
         (Owns(folder) && StartAccepted && phase == ProjectPipelineState.Preparing));

    public void ValidateAudioDiscard(string root, string audioProjectsFolder, string currentFolder)
    {
        if (!IsUnstartedAudio(currentFolder) || string.IsNullOrWhiteSpace(_projectId) ||
            !TrackPathService.PathEquals(root, _workingRoot) ||
            !TrackPathService.IsPathInsideFolder(ProjectFolder, root) ||
            !TrackPathService.PathEquals(Path.GetDirectoryName(ProjectFolder)!, audioProjectsFolder))
            throw new IOException(LocalizationText.Get("Project.Preparation.Unsafe"));

        SafeFileSystemEnumeration.EnsureSafeTree(ProjectFolder);
        // Read the raw manifest: a normal loader may normalize identity/path/state.
        var manifest = JsonSerializer.Deserialize<AudioDiscProjectManifest>(
            File.ReadAllText(ProjectFolderLayout.GetAudioDiscManifestPath(ProjectFolder)));
        if (manifest is null || manifest.ProjectId != _projectId || manifest.CreatedUtc != _createdUtc ||
            manifest.ProjectFormatVersion != ProjectFormat.CurrentProjectFormatVersion ||
            manifest.SchemaVersion != AudioDiscProjectManifestVersions.CurrentSchemaVersion ||
            manifest.ProjectType != ProjectManifestTypes.AudioCdProject ||
            !TrackPathService.PathEquals(manifest.ProjectFolder, ProjectFolder) ||
            manifest.Status != AudioDiscProjectStatus.AwaitingRip || manifest.IsStoppedForResume ||
            manifest.ExportStatus != AudioDiscExportStatus.NotStarted || manifest.HasSuccessfulExport ||
            manifest.CompletedUtc.HasValue || manifest.TotalDiscs < 1 ||
            manifest.Discs is null || manifest.Discs.Count != 1 || manifest.Discs.Any(d => d is null ||
                d.DiscNumber != 1 || d.Status != AudioDiscStatus.Pending || d.CompletedUtc.HasValue ||
                d.Tracks is null || d.Tracks.Any(t => t is null || t.Status != AudioDiscTrackStatus.Pending || t.CompletedUtc.HasValue)))
            throw new IOException(LocalizationText.Get("Project.Preparation.Unsafe"));

        foreach (var folder in new[] { ProjectFolderLayout.GetOriginalsFolder(ProjectFolder),
                     ProjectFolderLayout.GetConvertedFolder(ProjectFolder), ProjectFolderLayout.GetMergeFolder(ProjectFolder) })
            if (Directory.Exists(folder) && Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Any())
                throw new IOException(LocalizationText.Get("Project.Preparation.Unsafe"));
    }

    public void Clear()
    {
        ProjectFolder = ProjectType = _projectId = _workingRoot = "";
        StartAccepted = false;
        _createdUtc = default;
    }
}
