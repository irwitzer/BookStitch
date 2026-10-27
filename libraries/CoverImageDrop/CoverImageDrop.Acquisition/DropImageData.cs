using System.IO;

namespace CoverImageDrop.Acquisition;

public enum DropImagePayloadKind
{
    InMemoryBytes,
    LocalFile,
    TemporaryFile
}

public sealed class DropImageData : IDisposable, IAsyncDisposable
{
    private readonly byte[]? _bytes;
    private readonly TemporaryFileLease? _temporaryFileLease;
    private readonly long? _knownLength;

    private DropImageData(
        byte[]? bytes,
        string sourceName,
        string? localFilePath,
        DropImagePayloadKind payloadKind,
        TemporaryFileLease? temporaryFileLease = null,
        long? knownLength = null,
        string? sourceUrl = null,
        string? contentType = null)
    {
        _bytes = bytes;
        _temporaryFileLease = temporaryFileLease;
        _knownLength = knownLength;
        SourceName = sourceName;
        LocalFilePath = localFilePath;
        PayloadKind = payloadKind;
        SourceUrl = sourceUrl;
        ContentType = contentType;
    }

    public string SourceName { get; }
    public string? LocalFilePath { get; }
    public DropImagePayloadKind PayloadKind { get; }
    public string? SourceUrl { get; }
    public string? ContentType { get; }
    public bool OwnsBackingFile => _temporaryFileLease is not null;
    public bool IsDisposed => _temporaryFileLease?.IsDisposed == true;

    public long? Length
    {
        get
        {
            if (_bytes is not null)
            {
                return _bytes.LongLength;
            }

            if (_knownLength is not null)
            {
                return _knownLength;
            }

            if (LocalFilePath is null)
            {
                return null;
            }

            try
            {
                var fileInfo = new FileInfo(LocalFilePath);
                return fileInfo.Exists ? fileInfo.Length : null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                return null;
            }
        }
    }

    public static DropImageData FromBytes(byte[] bytes, string sourceName)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);

        return new DropImageData(
            bytes,
            sourceName,
            null,
            DropImagePayloadKind.InMemoryBytes,
            knownLength: bytes.LongLength);
    }

    public static DropImageData FromFile(string filePath, string? sourceName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var resolvedSourceName = ResolveSourceName(filePath, sourceName, "LocalImage");
        return new DropImageData(
            null,
            resolvedSourceName,
            filePath,
            DropImagePayloadKind.LocalFile);
    }

    public static DropImageData FromTemporaryFile(
        string filePath,
        string? sourceName = null,
        string? sourceUrl = null,
        string? contentType = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var fullPath = Path.GetFullPath(filePath);
        var fileInfo = new FileInfo(fullPath);
        if (!fileInfo.Exists)
        {
            throw new FileNotFoundException("Temporary image backing file was not found.", fullPath);
        }

        var resolvedSourceName = ResolveSourceName(fullPath, sourceName, "DownloadedImage");
        return new DropImageData(
            null,
            resolvedSourceName,
            fullPath,
            DropImagePayloadKind.TemporaryFile,
            temporaryFileLease: new TemporaryFileLease(fullPath),
            knownLength: fileInfo.Length,
            sourceUrl: sourceUrl,
            contentType: contentType);
    }

    public bool TryGetInMemoryBytes(out byte[] bytes)
    {
        if (_bytes is not null)
        {
            bytes = _bytes;
            return true;
        }

        bytes = [];
        return false;
    }

    public ValueTask<Stream> OpenReadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_bytes is not null)
        {
            return ValueTask.FromResult<Stream>(new MemoryStream(_bytes, writable: false));
        }

        if (LocalFilePath is null)
        {
            throw new InvalidOperationException("The image source has no readable payload backing.");
        }

        if (IsDisposed)
        {
            throw new ObjectDisposedException(nameof(DropImageData));
        }

        var fileShare = _temporaryFileLease is not null
            ? FileShare.Read | FileShare.Delete
            : FileShare.Read;
        Stream stream = new FileStream(
            LocalFilePath,
            FileMode.Open,
            FileAccess.Read,
            fileShare,
            bufferSize: 81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return ValueTask.FromResult(stream);
    }

    public async Task CopyToAsync(Stream destination, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);

        await using var source = await OpenReadAsync(cancellationToken).ConfigureAwait(false);
        await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
    }

    public void Dispose()
    {
        _temporaryFileLease?.Dispose();
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    private static string ResolveSourceName(string filePath, string? sourceName, string fallback)
    {
        var resolvedSourceName = string.IsNullOrWhiteSpace(sourceName)
            ? Path.GetFileName(filePath)
            : sourceName;
        return string.IsNullOrWhiteSpace(resolvedSourceName)
            ? fallback
            : resolvedSourceName;
    }

    private sealed class TemporaryFileLease : IDisposable
    {
        private readonly string _filePath;
        private int _disposed;

        public TemporaryFileLease(string filePath)
        {
            _filePath = filePath;
        }

        ~TemporaryFileLease()
        {
            TryDeleteBackingFile();
        }

        public bool IsDisposed => Volatile.Read(ref _disposed) != 0;

        public void Dispose()
        {
            if (TryDeleteBackingFile())
            {
                GC.SuppressFinalize(this);
            }
        }

        private bool TryDeleteBackingFile()
        {
            if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
            {
                return true;
            }

            try
            {
                if (File.Exists(_filePath))
                {
                    File.Delete(_filePath);
                }

                return true;
            }
            catch (IOException)
            {
                Volatile.Write(ref _disposed, 0);
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                Volatile.Write(ref _disposed, 0);
                return false;
            }
        }
    }
}
