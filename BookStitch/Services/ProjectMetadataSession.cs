using BookStitch.Models;
using System.IO;

namespace BookStitch.Services;

/// <summary>
/// Owns the single live canonical metadata instance for the active project lifecycle.
/// A new project can own metadata before a project folder exists; persistence begins
/// only after that same instance is attached to a newly created project folder.
/// </summary>
public sealed class ProjectMetadataSession
{
    private readonly ProjectMetadataStore _store;

    public ProjectMetadataSession()
        : this(new ProjectMetadataStore())
    {
    }

    public ProjectMetadataSession(ProjectMetadataStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public string? ProjectFolder { get; private set; }

    public ProjectMetadata? Current { get; private set; }

    public long? PersistedRevision { get; private set; }

    public bool HasCurrent => Current is not null;

    public bool IsAttached => ProjectFolder is not null && Current is not null;

    public bool HasUnpersistedChanges =>
        Current is not null &&
        (!PersistedRevision.HasValue || Current.Revision != PersistedRevision.Value);

    public ProjectMetadata BeginNew(ProjectMetadata? metadata = null)
    {
        EnsureVacant();

        Current = metadata ?? new ProjectMetadata();
        ProjectFolder = null;
        PersistedRevision = null;
        return Current;
    }

    public ProjectMetadata AttachNewProject(string projectFolder)
    {
        var metadata = RequireCurrent();
        if (ProjectFolder is not null)
            throw new InvalidOperationException("The canonical metadata session is already attached to a project folder.");

        var normalizedFolder = NormalizeProjectFolder(projectFolder);
        var metadataPath = ProjectFolderLayout.GetProjectMetadataPath(normalizedFolder);
        if (File.Exists(metadataPath))
        {
            throw new InvalidOperationException(
                "Canonical project metadata already exists. Open the existing project instead of attaching a new project.");
        }

        _store.Save(normalizedFolder, metadata);
        ProjectFolder = normalizedFolder;
        PersistedRevision = metadata.Revision;
        return metadata;
    }

    public ProjectMetadata OpenExisting(string projectFolder)
    {
        EnsureVacant();

        var normalizedFolder = NormalizeProjectFolder(projectFolder);
        var metadataPath = ProjectFolderLayout.GetProjectMetadataPath(normalizedFolder);
        if (!File.Exists(metadataPath))
        {
            throw new FileNotFoundException(
                "Canonical project metadata does not exist for the requested project.",
                metadataPath);
        }

        var metadata = _store.LoadOrCreate(normalizedFolder);
        AttachPersisted(normalizedFolder, metadata);
        return metadata;
    }

    public bool Mutate(Action<ProjectMetadata> mutation)
    {
        ArgumentNullException.ThrowIfNull(mutation);

        var metadata = RequireCurrent();
        var previousRevision = metadata.Revision;
        mutation(metadata);
        return metadata.Revision != previousRevision;
    }

    public bool Flush()
    {
        var (projectFolder, metadata) = RequireAttached();
        if (PersistedRevision == metadata.Revision)
            return false;

        if (!PersistedRevision.HasValue)
            throw new InvalidOperationException("Attached canonical metadata session has no persisted revision baseline.");

        _store.Save(projectFolder, metadata, PersistedRevision.Value);
        PersistedRevision = metadata.Revision;
        return true;
    }

    public ProjectMetadata ReloadFromStore()
    {
        var (projectFolder, _) = RequireAttached();
        var metadataPath = ProjectFolderLayout.GetProjectMetadataPath(projectFolder);
        if (!File.Exists(metadataPath))
        {
            throw new FileNotFoundException(
                "Canonical project metadata no longer exists for the attached project.",
                metadataPath);
        }

        var metadata = _store.LoadOrCreate(projectFolder);
        AttachPersisted(projectFolder, metadata);
        return metadata;
    }

    public void Detach()
    {
        if (HasUnpersistedChanges)
        {
            throw new InvalidOperationException(
                "Canonical project metadata has unpersisted changes. Flush or explicitly discard the session before detaching it.");
        }

        Clear();
    }

    public void Discard() => Clear();

    private void AttachPersisted(string projectFolder, ProjectMetadata metadata)
    {
        ProjectFolder = projectFolder;
        Current = metadata;
        PersistedRevision = metadata.Revision;
    }

    private void EnsureVacant()
    {
        if (Current is not null || ProjectFolder is not null || PersistedRevision.HasValue)
        {
            throw new InvalidOperationException(
                "The canonical metadata session already owns metadata. Flush and detach, or explicitly discard it, before starting another project.");
        }
    }

    private ProjectMetadata RequireCurrent()
    {
        if (Current is null)
            throw new InvalidOperationException("No canonical project metadata is active.");

        return Current;
    }

    private (string ProjectFolder, ProjectMetadata Metadata) RequireAttached()
    {
        if (ProjectFolder is null || Current is null)
            throw new InvalidOperationException("The canonical metadata session is not attached to a project folder.");

        return (ProjectFolder, Current);
    }

    private void Clear()
    {
        ProjectFolder = null;
        Current = null;
        PersistedRevision = null;
    }

    private static string NormalizeProjectFolder(string projectFolder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectFolder);
        return Path.GetFullPath(projectFolder);
    }
}
