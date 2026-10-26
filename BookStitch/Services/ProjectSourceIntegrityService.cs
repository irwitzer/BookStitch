using BookStitch.Models;
using System.IO;
using System.Security.Cryptography;

namespace BookStitch.Services;

/// <summary>Reconciles persisted acquisition claims with project-owned source files.</summary>
public static class ProjectSourceIntegrityService
{
    public static void Reconcile(Mp3DiscProjectManifest manifest)
    {
        var identities = new SourceCopyIdentityStore().Load(manifest.ProjectFolder)
            .Where(item => item.SourceId != Guid.Empty).GroupBy(item => item.SourceId)
            .ToDictionary(group => group.Key, group => group.First());
        foreach (var disc in manifest.ImportedDiscs)
        {
            if (!string.Equals(disc.Status, Mp3DiscImportStatus.Completed, StringComparison.OrdinalIgnoreCase)) continue;
            var valid = disc.SourceFiles.Count > 0 && disc.SourceFiles.Count >= Math.Max(disc.FileCount, disc.CopiedFiles) &&
                disc.SourceFiles.All(source =>
                {
                    identities.TryGetValue(source.SourceId, out var identity);
                    return IsValid(manifest.ProjectFolder, Path.Combine(ProjectFolderLayout.OriginalsFolderName, source.RelativePath), source.SourceSizeBytes, identity?.ContentSha256);
                });
            if (!valid)
            {
                disc.Status = "Incomplete";
                disc.CompletedUtc = default;
                manifest.PipelineState = ProjectPipelineStateNames.AcquiringSources;
            }
        }
    }

    public static void Reconcile(AudioDiscProjectManifest manifest)
    {
        foreach (var disc in manifest.Discs)
        foreach (var track in disc.Tracks)
            if (string.Equals(track.Status, AudioDiscTrackStatus.Ripped, StringComparison.OrdinalIgnoreCase) &&
                !IsValid(manifest.ProjectFolder, track.RelativePath, track.OutputSizeBytes ?? 0, track.ContentSha256))
            {
                track.Status = AudioDiscTrackStatus.Pending;
                track.CompletedUtc = null;
                track.OutputSizeBytes = null;
                track.ContentSha256 = null;
                disc.Status = AudioDiscStatus.Pending;
                disc.CompletedUtc = null;
                manifest.PipelineState = ProjectPipelineStateNames.AcquiringSources;
            }
    }

    public static bool IsValid(string projectFolder, string relativePath, long expectedSize, string? contentHash = null)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath)) return false;
            var root = Path.GetFullPath(projectFolder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var path = Path.GetFullPath(Path.Combine(root, relativePath));
            if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return false;
            for (var entry = path; entry is not null; entry = Path.GetDirectoryName(entry))
            {
                if ((File.GetAttributes(entry) & FileAttributes.ReparsePoint) != 0) return false;
                if (string.Equals(entry, root, StringComparison.OrdinalIgnoreCase)) break;
            }
            var file = new FileInfo(path);
            if (!file.Exists || file.Length <= 0 || (expectedSize > 0 && file.Length != expectedSize)) return false;
            if (string.IsNullOrWhiteSpace(contentHash)) return true; // Legacy manifests retain size/existence validation.
            return string.Equals(contentHash, ComputeHash(path), StringComparison.OrdinalIgnoreCase);
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    public static string ComputeHash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}
