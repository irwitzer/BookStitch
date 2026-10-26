using System.IO;
using System.Text.Json;

namespace BookStitch.Services;

/// <summary>Reads the immutable target from the project's primary manifest, never global settings.</summary>
public static class ProjectSampleRateService
{
    public static bool AllowsAacReuse(string? projectFolder)
    {
        if (string.IsNullOrWhiteSpace(projectFolder)) return true;
        return new FolderProjectManifestService().Read(projectFolder)?.AllowAacReuse ?? true;
    }

    public static int ReadTarget(string? projectFolder)
    {
        if (string.IsNullOrWhiteSpace(projectFolder)) return 44100;
        var path = new[] { ProjectFolderLayout.GetFolderManifestPath(projectFolder),
            ProjectFolderLayout.GetMp3DiscManifestPath(projectFolder), ProjectFolderLayout.GetAudioDiscManifestPath(projectFolder) }
            .FirstOrDefault(File.Exists);
        if (path is null) return 44100;
        var json = File.ReadAllText(path);
        PersistentJsonSchema.Read(json, "Project sample rate", path);
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("TargetSampleRateHz", out var value)) return 44100;
        return FolderSampleRateService.RequireSupported(value.GetInt32());
    }
}
