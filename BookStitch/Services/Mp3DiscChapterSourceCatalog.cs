using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace BookStitch.Services;

public sealed record Mp3DiscPlannedSourceIdentity(
    Guid SourceId,
    string RelativePath,
    string SourceFileName,
    long SourceSizeBytes,
    long SourceLastWriteUtcTicks);

public sealed record Mp3DiscPendingChapterDisc(
    int DiscNumber,
    string Signature,
    IReadOnlyList<Mp3DiscPlannedSourceIdentity> Sources);

public static class Mp3DiscSourceIdentityPlanner
{
    public static IReadOnlyList<Mp3DiscPlannedSourceIdentity> Create(
        string sourceFolder,
        string signature,
        IEnumerable<string> sourceFiles)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFolder);
        ArgumentException.ThrowIfNullOrWhiteSpace(signature);
        ArgumentNullException.ThrowIfNull(sourceFiles);

        return sourceFiles
            .Select(file => CreateSource(sourceFolder, signature, file))
            .OrderBy(source => source.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static Mp3DiscPlannedSourceIdentity CreateSource(
        string sourceFolder,
        string signature,
        string sourceFile)
    {
        var relativePath = NormalizeRelativePath(GetRelativePathSafe(sourceFolder, sourceFile));
        var (sizeBytes, lastWriteUtcTicks) = ReadFileEvidenceSafe(sourceFile);
        return new Mp3DiscPlannedSourceIdentity(
            CreateSourceId(signature, relativePath),
            relativePath,
            Path.GetFileName(sourceFile),
            sizeBytes,
            lastWriteUtcTicks);
    }


    private static string GetRelativePathSafe(string sourceFolder, string sourceFile)
    {
        try
        {
            return Path.GetRelativePath(sourceFolder, sourceFile);
        }
        catch
        {
            return Path.GetFileName(sourceFile);
        }
    }

    private static (long SizeBytes, long LastWriteUtcTicks) ReadFileEvidenceSafe(string sourceFile)
    {
        try
        {
            var fileInfo = new FileInfo(sourceFile);
            return fileInfo.Exists
                ? (fileInfo.Length, fileInfo.LastWriteTimeUtc.Ticks)
                : (0, 0);
        }
        catch
        {
            return (0, 0);
        }
    }

    private static Guid CreateSourceId(string signature, string relativePath)
    {
        var identity = string.Join(
            '\n',
            "mp3-disc-source-v1",
            signature.Trim().ToUpperInvariant(),
            NormalizeRelativePath(relativePath).ToUpperInvariant());
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(identity));
        return new Guid(hash.AsSpan(0, 16));
    }

    internal static string NormalizeRelativePath(string? relativePath)
    {
        return (relativePath ?? string.Empty)
            .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
            .TrimStart(Path.DirectorySeparatorChar);
    }
}

/// <summary>
/// Runtime-only accepted MP3-disc sources that already have stable identities but are not yet
/// present in ImportedDiscs. Entries are staged only when an import is actually starting.
/// </summary>
public sealed class Mp3DiscChapterSourceCatalog
{
    private readonly object _sync = new();
    private readonly Dictionary<int, Mp3DiscPendingChapterDisc> _discs = [];

    public void Stage(
        int discNumber,
        string signature,
        IReadOnlyList<Mp3DiscPlannedSourceIdentity> sources)
    {
        if (discNumber <= 0)
            throw new ArgumentOutOfRangeException(nameof(discNumber));
        ArgumentException.ThrowIfNullOrWhiteSpace(signature);
        ArgumentNullException.ThrowIfNull(sources);

        var staged = new Mp3DiscPendingChapterDisc(
            discNumber,
            signature.Trim(),
            sources.ToArray());

        lock (_sync)
            _discs[discNumber] = staged;
    }

    public void Remove(int discNumber)
    {
        if (discNumber <= 0)
            return;

        lock (_sync)
            _discs.Remove(discNumber);
    }

    public void Clear()
    {
        lock (_sync)
            _discs.Clear();
    }

    public IReadOnlyList<Mp3DiscPendingChapterDisc> Snapshot()
    {
        lock (_sync)
        {
            return _discs.Values
                .OrderBy(disc => disc.DiscNumber)
                .Select(disc => disc with { Sources = disc.Sources.ToArray() })
                .ToArray();
        }
    }
}
