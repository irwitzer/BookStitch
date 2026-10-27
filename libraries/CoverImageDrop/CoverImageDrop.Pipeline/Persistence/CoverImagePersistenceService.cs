using CoverImageDrop.Acquisition;
using CoverImageDrop.Core.Issues;
using CoverImageDrop.Core.Naming;
using CoverImageDrop.Core.Output;

namespace CoverImageDrop.Pipeline.Persistence;

public sealed class CoverImagePersistenceService
{
    private const int BufferSize = 81920;
    private readonly CoverImageFileNameGenerator _fileNameGenerator;

    public CoverImagePersistenceService(TimeProvider? timeProvider = null)
    {
        _fileNameGenerator = new CoverImageFileNameGenerator(timeProvider);
    }

    public Task<CoverImagePersistenceResult> SaveSourceAsync(
        DropImageData source,
        string targetFolder,
        string fileExtension,
        CoverImageNamingOptions? namingOptions = null,
        CoverImageCollisionPolicy collisionPolicy = CoverImageCollisionPolicy.GenerateUniqueName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetFolder);

        var naming = _fileNameGenerator.Generate(fileExtension, namingOptions);
        var targetPath = Path.Combine(targetFolder, naming.FileName);

        return PersistAsync(
            targetPath,
            collisionPolicy,
            naming.Issues,
            async (destination, token) =>
            {
                await source.CopyToAsync(destination, token).ConfigureAwait(false);
            },
            cancellationToken);
    }

    public Task<CoverImagePersistenceResult> SaveOutputAsync(
        CoverImageOutputResult output,
        string targetFolder,
        CoverImageNamingOptions? namingOptions = null,
        CoverImageCollisionPolicy collisionPolicy = CoverImageCollisionPolicy.GenerateUniqueName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetFolder);

        var naming = _fileNameGenerator.Generate(output.FileExtension, namingOptions);
        var targetPath = Path.Combine(targetFolder, naming.FileName);
        var initialIssues = naming.Issues.Concat(output.Issues).ToArray();

        return PersistAsync(
            targetPath,
            collisionPolicy,
            initialIssues,
            async (destination, token) =>
            {
                await destination.WriteAsync(output.Bytes.AsMemory(), token).ConfigureAwait(false);
            },
            cancellationToken);
    }

    public Task<CoverImagePersistenceResult> SaveSourceToPathAsync(
        DropImageData source,
        string targetFilePath,
        CoverImageCollisionPolicy collisionPolicy = CoverImageCollisionPolicy.GenerateUniqueName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ValidateTargetFilePath(targetFilePath);

        return PersistAsync(
            targetFilePath,
            collisionPolicy,
            Array.Empty<CoverImageIssue>(),
            async (destination, token) =>
            {
                await source.CopyToAsync(destination, token).ConfigureAwait(false);
            },
            cancellationToken);
    }

    public Task<CoverImagePersistenceResult> SaveOutputToPathAsync(
        CoverImageOutputResult output,
        string targetFilePath,
        CoverImageCollisionPolicy collisionPolicy = CoverImageCollisionPolicy.GenerateUniqueName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(output);
        ValidateTargetFilePath(targetFilePath);

        return PersistAsync(
            targetFilePath,
            collisionPolicy,
            output.Issues,
            async (destination, token) =>
            {
                await destination.WriteAsync(output.Bytes.AsMemory(), token).ConfigureAwait(false);
            },
            cancellationToken);
    }

    private static async Task<CoverImagePersistenceResult> PersistAsync(
        string requestedTargetPath,
        CoverImageCollisionPolicy collisionPolicy,
        IReadOnlyList<CoverImageIssue> initialIssues,
        Func<FileStream, CancellationToken, Task> writeAsync,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(initialIssues);
        ArgumentNullException.ThrowIfNull(writeAsync);

        string? tempPath = null;
        string? requestedFullPath = null;

        try
        {
            requestedFullPath = Path.GetFullPath(requestedTargetPath);
            var targetDirectory = Path.GetDirectoryName(requestedFullPath);
            if (string.IsNullOrWhiteSpace(targetDirectory))
            {
                throw new IOException("A target directory could not be resolved.");
            }

            var requestedFileName = Path.GetFileName(requestedFullPath);
            if (requestedFileName.Length > CoverImageFileNameGenerator.MaxFileNameCharacters)
            {
                return CreateFailure(
                    requestedFullPath,
                    initialIssues,
                    $"Target file name exceeds the Windows component limit of {CoverImageFileNameGenerator.MaxFileNameCharacters} characters.");
            }

            Directory.CreateDirectory(targetDirectory);

            if (collisionPolicy == CoverImageCollisionPolicy.FailIfExists &&
                File.Exists(requestedFullPath))
            {
                return CreateFailure(
                    requestedFullPath,
                    initialIssues,
                    "Target file already exists.");
            }

            tempPath = Path.Combine(
                targetDirectory,
                $".coverimagedrop-{Guid.NewGuid():N}.tmp");

            long bytesWritten;
            await using (var destination = new FileStream(
                tempPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                BufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await writeAsync(destination, cancellationToken).ConfigureAwait(false);
                await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
                bytesWritten = destination.Length;
            }

            cancellationToken.ThrowIfCancellationRequested();

            return collisionPolicy switch
            {
                CoverImageCollisionPolicy.GenerateUniqueName => MoveToUniqueTarget(
                    tempPath,
                    requestedFullPath,
                    bytesWritten,
                    initialIssues),
                CoverImageCollisionPolicy.Overwrite => MoveWithOverwrite(
                    tempPath,
                    requestedFullPath,
                    bytesWritten,
                    initialIssues),
                CoverImageCollisionPolicy.FailIfExists => MoveWithoutOverwrite(
                    tempPath,
                    requestedFullPath,
                    bytesWritten,
                    initialIssues),
                _ => throw new ArgumentOutOfRangeException(
                    nameof(collisionPolicy),
                    collisionPolicy,
                    "Unknown collision policy.")
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            return CreateFailure(
                requestedFullPath ?? requestedTargetPath,
                initialIssues,
                ex.Message);
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(tempPath))
            {
                TryDeleteTemporaryFile(tempPath);
            }
        }
    }

    private static CoverImagePersistenceResult MoveToUniqueTarget(
        string tempPath,
        string requestedFullPath,
        long bytesWritten,
        IReadOnlyList<CoverImageIssue> initialIssues)
    {
        var candidatePath = requestedFullPath;
        var suffix = 2;

        while (true)
        {
            try
            {
                File.Move(tempPath, candidatePath);

                if (string.Equals(candidatePath, requestedFullPath, StringComparison.OrdinalIgnoreCase))
                {
                    return CoverImagePersistenceResult.Success(
                        candidatePath,
                        bytesWritten,
                        false,
                        initialIssues);
                }

                var issues = AppendCollisionIssue(initialIssues, requestedFullPath, candidatePath);
                return CoverImagePersistenceResult.Success(
                    candidatePath,
                    bytesWritten,
                    true,
                    issues);
            }
            catch (IOException) when (File.Exists(candidatePath))
            {
                candidatePath = BuildCollisionPath(requestedFullPath, suffix);
                suffix++;
            }
        }
    }

    private static CoverImagePersistenceResult MoveWithOverwrite(
        string tempPath,
        string requestedFullPath,
        long bytesWritten,
        IReadOnlyList<CoverImageIssue> initialIssues)
    {
        File.Move(tempPath, requestedFullPath, overwrite: true);
        return CoverImagePersistenceResult.Success(
            requestedFullPath,
            bytesWritten,
            false,
            initialIssues);
    }

    private static CoverImagePersistenceResult MoveWithoutOverwrite(
        string tempPath,
        string requestedFullPath,
        long bytesWritten,
        IReadOnlyList<CoverImageIssue> initialIssues)
    {
        try
        {
            File.Move(tempPath, requestedFullPath);
            return CoverImagePersistenceResult.Success(
                requestedFullPath,
                bytesWritten,
                false,
                initialIssues);
        }
        catch (IOException) when (File.Exists(requestedFullPath))
        {
            return CreateFailure(
                requestedFullPath,
                initialIssues,
                "Target file already exists.");
        }
    }

    private static string BuildCollisionPath(string requestedFullPath, int suffix)
    {
        var directory = Path.GetDirectoryName(requestedFullPath)
            ?? throw new IOException("A target directory could not be resolved.");
        var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(requestedFullPath);
        var extension = Path.GetExtension(requestedFullPath);
        var suffixText = $" ({suffix})";
        var maxBaseNameLength =
            CoverImageFileNameGenerator.MaxFileNameCharacters -
            extension.Length -
            suffixText.Length;
        if (maxBaseNameLength <= 0)
        {
            throw new IOException(
                "The target extension and collision suffix exceed the Windows file-name component limit.");
        }

        if (fileNameWithoutExtension.Length > maxBaseNameLength)
        {
            fileNameWithoutExtension = fileNameWithoutExtension[..maxBaseNameLength]
                .TrimEnd(' ', '.');
        }

        if (fileNameWithoutExtension.Length == 0)
        {
            throw new IOException(
                "The target file name cannot be shortened enough for collision resolution.");
        }

        return Path.Combine(
            directory,
            $"{fileNameWithoutExtension}{suffixText}{extension}");
    }

    private static IReadOnlyList<CoverImageIssue> AppendCollisionIssue(
        IReadOnlyList<CoverImageIssue> initialIssues,
        string requestedPath,
        string finalPath)
    {
        return initialIssues
            .Append(new CoverImageIssue(
                CoverImageIssueCode.FileNameAdjusted,
                CoverImageIssueSeverity.Info,
                new Dictionary<string, object?>
                {
                    ["Reason"] = "CollisionResolved",
                    ["RequestedFileName"] = Path.GetFileName(requestedPath),
                    ["FinalFileName"] = Path.GetFileName(finalPath)
                }))
            .ToArray();
    }

    private static CoverImagePersistenceResult CreateFailure(
        string? filePath,
        IReadOnlyList<CoverImageIssue> initialIssues,
        string technicalCause)
    {
        var issues = initialIssues
            .Append(new CoverImageIssue(
                CoverImageIssueCode.PersistenceFailed,
                CoverImageIssueSeverity.Error,
                string.IsNullOrWhiteSpace(filePath)
                    ? null
                    : new Dictionary<string, object?> { ["filePath"] = filePath },
                technicalCause))
            .ToArray();

        return CoverImagePersistenceResult.Failed(filePath, issues);
    }

    private static void ValidateTargetFilePath(string targetFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetFilePath);

        if (string.IsNullOrWhiteSpace(Path.GetFileName(targetFilePath)))
        {
            throw new ArgumentException(
                "Target file path must include a file name.",
                nameof(targetFilePath));
        }
    }

    private static void TryDeleteTemporaryFile(string tempPath)
    {
        try
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}