using BookStitch.Models;
using System.IO;
using System.Text.Json;

namespace BookStitch.Services;

public sealed class FolderProjectManifestService
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    private readonly AtomicJsonStore _store = new();

    public FolderProjectManifest? Read(string projectFolder)
    {
        var path = ProjectFolderLayout.GetFolderManifestPath(projectFolder);
        if (!File.Exists(path)) return null;
        var json = File.ReadAllText(path);
        PersistentJsonSchema.Read(json, "Folder project", path);
        return JsonSerializer.Deserialize<FolderProjectManifest>(json);
    }

    public void Create(string projectFolder, string sourceFolder, int targetSampleRateHz = 44100, bool allowAacReuse = true)
    {
        var path = ProjectFolderLayout.GetFolderManifestPath(projectFolder);
        lock (AtomicJsonStore.ForPath(path))
        {
            if (File.Exists(path)) throw new IOException("Folder project already exists.");
            ProjectFolderLayout.EnsureProjectFolders(projectFolder);
            var now = DateTime.UtcNow;
            _store.Save(path, new FolderProjectManifest { ProjectFolder = projectFolder,
                SourceFolder = sourceFolder, AllowAacReuse = allowAacReuse, TargetSampleRateHz = FolderSampleRateService.RequireSupported(targetSampleRateHz), ProjectId = Guid.NewGuid().ToString("N"),
                CreatedUtc = now, UpdatedUtc = now }, Options);
        }
    }

    // Called only from the export manifest's owning writer, not from status/index readers.
    // Track and timing commits do not cause redundant main-manifest writes.
    public void SaveLifecycle(ExportWorkManifest export)
    {
        var path = ProjectFolderLayout.GetFolderManifestPath(export.ProjectWorkFolder);
        lock (AtomicJsonStore.ForPath(path))
        {
            var main = Read(export.ProjectWorkFolder);
            if (main is not null && main.PipelineState == export.State.Status)
                return;
            main ??= new FolderProjectManifest { ProjectId = export.ProjectId, CreatedUtc = export.CreatedUtc, SourceFolder = export.SourceFolder };
            main.ProjectFolder = export.ProjectWorkFolder;
            main.PipelineState = export.State.Status;
            main.UpdatedUtc = export.UpdatedUtc;
            _store.Save(path, main, Options);
        }
    }
}
