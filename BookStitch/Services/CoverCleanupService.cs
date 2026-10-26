using System.IO;
using System.Text.Json;

namespace BookStitch.Services;

public sealed class CoverCleanupService
{
    public IReadOnlyList<string> DeleteOldUnreferencedCoverFiles(
        string workingRoot, string coversFolder, DateTime nowUtc, int olderThanDays, CancellationToken cancellationToken = default)
    {
        var failures = new List<string>();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Directory.Exists(coversFolder)) return failures;
            if (!TrackPathService.IsPathInsideFolder(coversFolder, workingRoot))
                throw new IOException("Cover cleanup must remain inside the managed working root.");
            // A partial reference set must never authorize deletion.
            var referenced = CollectReferencedCoverPaths(workingRoot, coversFolder, cancellationToken);
            SafeFileSystemEnumeration.EnsureSafeTree(coversFolder, cancellationToken);
            var files = SafeFileSystemEnumeration.EnumerateFiles(coversFolder, cancellationToken,
                onError: (_, error) => throw error).ToArray();
            if (olderThanDays < 0) throw new ArgumentOutOfRangeException(nameof(olderThanDays));
            var cutoff = nowUtc.AddDays(-olderThanDays);
            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    if (referenced.Contains(Path.GetFullPath(file)) || File.GetLastWriteTimeUtc(file) > cutoff) continue;
                    SafeFileSystemEnumeration.EnsureNoReparseAncestors(file);
                    File.SetAttributes(file, FileAttributes.Normal);
                    cancellationToken.ThrowIfCancellationRequested();
                    File.Delete(file);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    failures.Add(LocalizationText.Format("MainWindow.Cleanup.CoverFileFailed", Path.GetFileName(file), ex.Message));
                }
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            failures.Add(LocalizationText.Format("MainWindow.Cleanup.CoverFailed", ex.Message));
        }
        return failures;
    }

    public HashSet<string> CollectReferencedCoverPaths(string workingRoot, string coversFolder, CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(workingRoot)) throw new DirectoryNotFoundException(workingRoot);
        SafeFileSystemEnumeration.EnsureSafeTree(workingRoot, cancellationToken);
        var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in SafeFileSystemEnumeration.EnumerateFiles(workingRoot, cancellationToken, onError: (_, error) => throw error)
                     .Where(file => Path.GetExtension(file).Equals(".json", StringComparison.OrdinalIgnoreCase)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var input = File.OpenRead(file);
            using var document = JsonDocument.Parse(input);
            Collect(document.RootElement, coversFolder, referenced);
        }
        return referenced;
    }

    private static void Collect(JsonElement element, string coversFolder, ISet<string> referenced)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject()) Collect(property.Value, coversFolder, referenced);
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray()) Collect(item, coversFolder, referenced);
                break;
            case JsonValueKind.String:
                var value = element.GetString();
                if (!string.IsNullOrWhiteSpace(value) && Path.IsPathFullyQualified(value) &&
                    TrackPathService.IsPathInsideFolder(value, coversFolder)) referenced.Add(Path.GetFullPath(value));
                break;
        }
    }
}
