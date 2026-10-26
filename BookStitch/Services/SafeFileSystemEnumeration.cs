using System.Diagnostics;
using System.IO;
using System.Security;

namespace BookStitch.Services;

/// <summary>Enumerates each directory independently and never follows reparse points.</summary>
public static class SafeFileSystemEnumeration
{
    public static IEnumerable<string> EnumerateFiles(
        string root, CancellationToken cancellationToken = default,
        Action<string, Exception>? onError = null)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.TryPop(out var folder))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsRegularEntry(folder, onError)) continue;
            foreach (var file in FilesInDirectory(folder, cancellationToken, onError))
                yield return file;
            foreach (var child in DirectoriesInDirectory(folder, cancellationToken, onError))
                pending.Push(child);
        }
    }

    public static IEnumerable<string> FilesInDirectory(
        string folder, CancellationToken cancellationToken = default,
        Action<string, Exception>? onError = null, string searchPattern = "*") =>
        IsRegularEntry(folder, onError)
            ? EnumerateSafely(() => Directory.EnumerateFiles(folder, searchPattern), folder, cancellationToken, onError)
                .Where(path => IsRegularEntry(path, onError))
            : [];

    public static IEnumerable<string> DirectoriesInDirectory(
        string folder, CancellationToken cancellationToken = default,
        Action<string, Exception>? onError = null) =>
        IsRegularEntry(folder, onError)
            ? EnumerateSafely(() => Directory.EnumerateDirectories(folder), folder, cancellationToken, onError)
                .Where(path => IsRegularEntry(path, onError))
            : [];

    internal static IEnumerable<string> EnumerateSafely(
        Func<IEnumerable<string>> entries, string folder, CancellationToken cancellationToken,
        Action<string, Exception>? onError = null)
    {
        IEnumerator<string>? iterator = null;
        try
        {
            try { iterator = entries().GetEnumerator(); }
            catch (Exception ex) when (IsFileSystemError(ex)) { Report(folder, ex, onError); }
            if (iterator is null) yield break;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string entry;
                try
                {
                    if (!iterator.MoveNext()) yield break;
                    entry = iterator.Current;
                }
                catch (Exception ex) when (IsFileSystemError(ex))
                {
                    Report(folder, ex, onError);
                    yield break;
                }
                yield return entry;
            }
        }
        finally { iterator?.Dispose(); }
    }

    private static bool IsRegularEntry(string path, Action<string, Exception>? onError)
    {
        try { return (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0; }
        catch (Exception ex) when (IsFileSystemError(ex)) { Report(path, ex, onError); return false; }
    }

    // Destructive callers require a complete tree: skipping an unreadable entry is unsafe.
    public static void EnsureNoReparseAncestors(string path)
    {
        for (var current = Path.GetFullPath(path); !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
        {
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new ProjectPathSafetyException("Managed file operations cannot follow reparse points.");
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }

    public static void EnsureSafeTree(string root, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureNoReparseAncestors(root);
        if (!Directory.Exists(root)) return;
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.TryPop(out var folder))
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureNoReparseAncestors(folder);
            foreach (var entry in Directory.EnumerateFileSystemEntries(folder))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    throw new ProjectPathSafetyException("Managed file operations cannot follow reparse points.");
                if ((attributes & FileAttributes.Directory) != 0) pending.Push(entry);
            }
        }
    }

    internal static bool IsFileSystemError(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or SecurityException;

    private static void Report(string path, Exception ex, Action<string, Exception>? onError)
    {
        if (onError is not null) onError(path, ex);
        else Trace.TraceWarning("File-system scan skipped {0}: {1}", path, ex.Message);
    }
}

public sealed class ProjectPathSafetyException(string message) : IOException(message);
