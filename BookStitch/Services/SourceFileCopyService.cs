using System.IO;
using System.Security.Cryptography;

namespace BookStitch.Services;

/// <summary>Copies and fingerprints the source in one pass before publishing the complete file.</summary>
public static class SourceFileCopyService
{
    private const int BufferSize = 128 * 1024;

    public static Task<string> CopyAtomicallyAsync(
        string sourceFile, string targetFile, CancellationToken cancellationToken, Action? sourceReadStarted = null) =>
        CopyAtomicallyCoreAsync(sourceFile, targetFile, cancellationToken,
            path => new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                BufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan), sourceReadStarted: sourceReadStarted);

    internal static async Task<string> CopyAtomicallyCoreAsync(
        string sourceFile, string targetFile, CancellationToken cancellationToken, Func<string, Stream> openSource,
        Action<string>? deleteTemporaryFile = null, Action? sourceReadStarted = null)
    {
        var copyingPath = targetFile + ".copying";
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            SafeFileSystemEnumeration.EnsureNoReparseAncestors(targetFile);
            SafeFileSystemEnumeration.EnsureNoReparseAncestors(copyingPath);
            CleanupTemporaryCopy(targetFile, deleteTemporaryFile);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using (var source = openSource(sourceFile))
            await using (var target = new FileStream(copyingPath, FileMode.CreateNew, FileAccess.Write,
                             FileShare.None, BufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                var buffer = new byte[BufferSize];
                var readStarted = false;
                int read;
                while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) != 0)
                {
                    if (!readStarted)
                    {
                        readStarted = true;
                        sourceReadStarted?.Invoke();
                    }
                    cancellationToken.ThrowIfCancellationRequested();
                    hash.AppendData(buffer, 0, read);
                    await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                }
                await target.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            cancellationToken.ThrowIfCancellationRequested();
            SafeFileSystemEnumeration.EnsureNoReparseAncestors(targetFile);
            SafeFileSystemEnumeration.EnsureNoReparseAncestors(copyingPath);
            if (File.Exists(targetFile))
            {
                var attributes = File.GetAttributes(targetFile);
                if ((attributes & FileAttributes.ReadOnly) != 0 && (attributes & FileAttributes.ReparsePoint) == 0)
                    File.SetAttributes(targetFile, attributes & ~FileAttributes.ReadOnly);
            }
            File.Move(copyingPath, targetFile, overwrite: true);
            return Convert.ToHexString(hash.GetHashAndReset());
        }
        finally
        {
            CleanupTemporaryCopy(targetFile, deleteTemporaryFile);
        }
    }

    // Only the exact temporary sibling of a validated acquisition target is owned
    // by BookStitch; never scan Originals for user filenames resembling temp files.
    internal static void CleanupTemporaryCopy(string targetFile, Action<string>? deleteTemporaryFile = null)
    {
        var path = targetFile + ".copying";
        try
        {
            SafeFileSystemEnumeration.EnsureNoReparseAncestors(path);
            if (File.Exists(path)) (deleteTemporaryFile ?? File.Delete)(path);
        }
        catch (Exception error) when (SafeFileSystemEnumeration.IsFileSystemError(error))
        {
            System.Diagnostics.Trace.TraceWarning("Source temporary-file cleanup deferred: {0}: {1}", path, error);
        }
    }
}
