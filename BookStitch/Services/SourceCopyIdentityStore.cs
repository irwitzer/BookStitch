using System.IO;
using System.Security.Cryptography;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BookStitch.Services;

public sealed record SourceFileCopyIdentity(
    string TargetRelativePath,
    string SourceFileName,
    long SourceSizeBytes,
    long SourceLastWriteUtcTicks,
    Guid SourceId = default,
    string? ContentSha256 = null);

public sealed class SourceCopyIdentityStore
{
    public const string FileName = "source-copy-identities.json";
    private static readonly ConcurrentDictionary<string, object> ProjectLocks = new(StringComparer.OrdinalIgnoreCase);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };
    private static readonly AtomicJsonStore JsonStore = new();

    public bool CanReuseExistingCopy(string projectFolder, string sourceFile, string targetFile, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (GetProjectLock(projectFolder))
        {
            if (!File.Exists(targetFile))
                return false;

            try
            {
                var sourceInfo = new FileInfo(sourceFile);
                var targetInfo = new FileInfo(targetFile);
                if (!sourceInfo.Exists ||
                    !targetInfo.Exists ||
                    sourceInfo.Length <= 0 ||
                    targetInfo.Length != sourceInfo.Length)
                {
                    return false;
                }

                var identity = LoadCore(projectFolder).FirstOrDefault(item =>
                    string.Equals(
                        NormalizeRelativePath(item.TargetRelativePath),
                        NormalizeRelativePath(CreateTargetRelativePath(projectFolder, targetFile)),
                        StringComparison.OrdinalIgnoreCase));

                return identity is not null &&
                       string.Equals(identity.SourceFileName, Path.GetFileName(sourceFile), StringComparison.OrdinalIgnoreCase) &&
                       identity.SourceSizeBytes == sourceInfo.Length &&
                       identity.SourceLastWriteUtcTicks == sourceInfo.LastWriteTimeUtc.Ticks &&
                       !string.IsNullOrWhiteSpace(identity.ContentSha256) &&
                       string.Equals(identity.ContentSha256, ComputeContentHash(targetFile, cancellationToken), StringComparison.OrdinalIgnoreCase);
            }
            catch (OperationCanceledException) { throw; }
            catch
            {
                return false;
            }
        }
    }

    public SourceFileCopyIdentity RecordCopiedFile(
        string projectFolder,
        string sourceFile,
        string targetFile,
        Guid preferredSourceId = default,
        string? contentSha256 = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (GetProjectLock(projectFolder))
        {
            var sourceInfo = new FileInfo(sourceFile);
            var targetRelativePath = CreateTargetRelativePath(projectFolder, targetFile);
            var loaded = LoadCore(projectFolder);
            var existing = loaded.FirstOrDefault(item =>
                string.Equals(
                    NormalizeRelativePath(item.TargetRelativePath),
                    NormalizeRelativePath(targetRelativePath),
                    StringComparison.OrdinalIgnoreCase) &&
                string.Equals(item.SourceFileName, Path.GetFileName(sourceFile), StringComparison.OrdinalIgnoreCase) &&
                item.SourceSizeBytes == sourceInfo.Length &&
                item.SourceLastWriteUtcTicks == sourceInfo.LastWriteTimeUtc.Ticks);
            var identity = new SourceFileCopyIdentity(
                targetRelativePath,
                Path.GetFileName(sourceFile),
                sourceInfo.Length,
                sourceInfo.LastWriteTimeUtc.Ticks,
                existing?.SourceId is { } sourceId && sourceId != Guid.Empty
                    ? sourceId
                    : preferredSourceId != Guid.Empty
                        ? preferredSourceId
                        : Guid.NewGuid(),
                contentSha256 ?? ComputeContentHash(targetFile, cancellationToken));

            var identities = loaded
                .Where(item => !string.Equals(
                    NormalizeRelativePath(item.TargetRelativePath),
                    NormalizeRelativePath(identity.TargetRelativePath),
                    StringComparison.OrdinalIgnoreCase))
                .Append(identity)
                .OrderBy(item => item.TargetRelativePath, StringComparer.OrdinalIgnoreCase)
                .ToList();

            SaveCore(projectFolder, identities);
            return identity;
        }
    }

    private static string ComputeContentHash(string path, CancellationToken cancellationToken = default)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            128 * 1024, FileOptions.SequentialScan);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[128 * 1024];
        int read;
        while ((read = stream.Read(buffer)) != 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            hash.AppendData(buffer, 0, read);
        }
        cancellationToken.ThrowIfCancellationRequested();
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    public IReadOnlyList<SourceFileCopyIdentity> Load(string projectFolder)
    {
        lock (GetProjectLock(projectFolder))
            return LoadCore(projectFolder);
    }

    private static IReadOnlyList<SourceFileCopyIdentity> LoadCore(string projectFolder)
    {
        var path = GetPath(projectFolder);
        if (!File.Exists(path))
            return [];

        try
        {
            var json = File.ReadAllText(path);
            var schema = PersistentJsonSchema.Read(
                json,
                "SourceCopyIdentity",
                path,
                SourceCopyIdentityState.CurrentSchemaVersion,
                allowLegacyArray: true);

            if (schema.IsLegacy)
            {
                var legacyIdentities = JsonSerializer.Deserialize<List<SourceFileCopyIdentity>>(
                    json,
                    JsonOptions) ?? [];
                SaveCore(projectFolder, legacyIdentities);
                PersistentJsonSchema.TraceLegacyMigration("SourceCopyIdentity", path);
                return legacyIdentities;
            }

            var state = JsonSerializer.Deserialize<SourceCopyIdentityState>(
                json,
                JsonOptions);
            return state?.Files ?? [];
        }
        catch (PersistentJsonSchemaException)
        {
            throw;
        }
        catch
        {
            return [];
        }
    }

    private static void SaveCore(string projectFolder, IReadOnlyCollection<SourceFileCopyIdentity> identities)
    {
        var path = GetPath(projectFolder);
        JsonStore.Save(
            path,
            new SourceCopyIdentityState
            {
                Files = identities.ToList()
            },
            JsonOptions);
    }

    private static string GetPath(string projectFolder)
    {
        return Path.Combine(ProjectFolderLayout.GetSettingsFolder(projectFolder), FileName);
    }

    private static object GetProjectLock(string projectFolder)
    {
        var path = Path.GetFullPath(GetPath(projectFolder));
        return ProjectLocks.GetOrAdd(path, _ => new object());
    }

    private static string CreateTargetRelativePath(string projectFolder, string targetFile)
    {
        return NormalizeRelativePath(Path.GetRelativePath(projectFolder, targetFile));
    }

    private static string NormalizeRelativePath(string relativePath)
    {
        return (relativePath ?? string.Empty)
            .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
            .TrimStart(Path.DirectorySeparatorChar);
    }

    private sealed class SourceCopyIdentityState
    {
        internal const int CurrentSchemaVersion = 1;

        [JsonPropertyName("schemaVersion")]
        public int SchemaVersion { get; set; } = CurrentSchemaVersion;

        public List<SourceFileCopyIdentity> Files { get; set; } = [];
    }
}
