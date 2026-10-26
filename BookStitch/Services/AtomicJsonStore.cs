using System.IO;
using System.Text;
using System.Text.Json;
using System.Collections.Concurrent;

namespace BookStitch.Services;

internal interface IAtomicJsonStoreFileSystem
{
    Stream CreateNewWriteThroughFile(string path);

    bool FileExists(string path);

    void Move(string sourcePath, string destinationPath);

    void Replace(string sourcePath, string destinationPath);

    void DeleteIfExists(string path);
}

public sealed class AtomicJsonStore
{
    // Shared by every store instance, including transactional restore/migration writers.
    internal sealed class PathState
    {
        internal long Revision;
    }
    internal static StringComparer PathComparer { get; } = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    private static readonly ConcurrentDictionary<string, PathState> Paths = new(PathComparer);
    internal static PathState ForPath(string path) => Paths.GetOrAdd(Path.GetFullPath(path), _ => new());
    private const int BufferSize = 16 * 1024;

    private readonly IAtomicJsonStoreFileSystem _fileSystem;
    private readonly Func<Guid> _guidFactory;

    public AtomicJsonStore()
        : this(new AtomicJsonStoreFileSystem(), Guid.NewGuid)
    {
    }

    internal AtomicJsonStore(
        IAtomicJsonStoreFileSystem fileSystem,
        Func<Guid>? guidFactory = null)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _guidFactory = guidFactory ?? Guid.NewGuid;
    }

    public void Save<T>(
        string filePath,
        T value,
        JsonSerializerOptions? serializerOptions,
        PerformanceMeasurement? measurement = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        var state = ForPath(filePath);
        lock (state)
        {
            SaveCore(filePath, value, serializerOptions, measurement);
            state.Revision++;
        }
    }

    private void SaveCore<T>(string filePath, T value, JsonSerializerOptions? serializerOptions, PerformanceMeasurement? measurement)
    {

        var directory = Path.GetDirectoryName(filePath)
            ?? throw new InvalidOperationException("Für die Zieldatei konnte kein Ordner ermittelt werden.");

        Directory.CreateDirectory(directory);

        var temporaryPath = CreateTemporaryPath(directory, Path.GetFileName(filePath));
        try
        {
            var json = JsonSerializer.Serialize(value, serializerOptions);
            measurement?.SerializedBytes(json);
            using (var stream = _fileSystem.CreateNewWriteThroughFile(temporaryPath))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
            {
                writer.Write(json);
                writer.Flush();
                if (stream is FileStream fileStream)
                    fileStream.Flush(flushToDisk: true);
                else
                    stream.Flush();
            }

            Commit(temporaryPath, filePath);
        }
        finally
        {
            Cleanup(temporaryPath);
        }
    }

    public void RestoreBytes(string filePath, byte[] bytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(bytes);
        var state = ForPath(filePath);
        lock (state)
        {
            RestoreBytesCore(filePath, bytes);
            state.Revision++;
        }
    }

    private void RestoreBytesCore(string filePath, byte[] bytes)
    {
        SafeFileSystemEnumeration.EnsureNoReparseAncestors(filePath);
        var directory = Path.GetDirectoryName(filePath)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = CreateTemporaryPath(directory, Path.GetFileName(filePath));
        try
        {
            using (var stream = _fileSystem.CreateNewWriteThroughFile(temporaryPath))
            {
                stream.Write(bytes);
                if (stream is FileStream fileStream) fileStream.Flush(flushToDisk: true);
                else stream.Flush();
            }
            Commit(temporaryPath, filePath);
        }
        finally { Cleanup(temporaryPath); }
    }

    private void Commit(string temporaryPath, string filePath)
    {
        int[] delays = [25, 50, 100, 200];
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                if (_fileSystem.FileExists(filePath)) _fileSystem.Replace(temporaryPath, filePath);
                else _fileSystem.Move(temporaryPath, filePath);
                return;
            }
            catch (IOException error) when (attempt < delays.Length && IsTransientLock(error))
            {
                Thread.Sleep(delays[attempt]);
            }
        }
    }

    // HRESULT_FROM_WIN32: sharing violation, lock violation, unable to remove replaced file.
    // Access denied, disk full and generic IO failures are deliberately excluded.
    internal static bool IsTransientLock(IOException error) =>
        error.HResult is unchecked((int)0x80070020) or unchecked((int)0x80070021) or unchecked((int)0x80070497);

    private void Cleanup(string path)
    {
        try { _fileSystem.DeleteIfExists(path); }
        catch (Exception error)
        {
            System.Diagnostics.Trace.TraceWarning("JSON temporary-file cleanup failed: {0}", error);
        }
    }

    private string CreateTemporaryPath(string directory, string fileName) =>
        Path.Combine(directory, $"{fileName}.{_guidFactory():N}.tmp");

    private sealed class AtomicJsonStoreFileSystem : IAtomicJsonStoreFileSystem
    {
        public Stream CreateNewWriteThroughFile(string path) =>
            new FileStream(
                path,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                BufferSize,
                FileOptions.WriteThrough);

        public bool FileExists(string path) => File.Exists(path);

        public void Move(string sourcePath, string destinationPath) =>
            File.Move(sourcePath, destinationPath);

        public void Replace(string sourcePath, string destinationPath) =>
            File.Replace(sourcePath, destinationPath, null);

        public void DeleteIfExists(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
                // A stale JSON temp file is preferable to failing a completed save.
            }
        }
    }
}
