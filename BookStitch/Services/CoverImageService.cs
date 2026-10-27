using CoverImageDrop.Acquisition;
using CoverImageDrop.Core.Inspection;
using CoverImageDrop.Core.Issues;
using CoverImageDrop.Core.Policy;
using CoverImageDrop.Pipeline;
using CoverImageDrop.Pipeline.Persistence;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace BookStitch.Services;

public sealed class CoverImageService
{
    private readonly Func<string?> _getTransparencyMode;

    public CoverImageService(Func<string?>? getTransparencyMode = null)
    {
        _getTransparencyMode = getTransparencyMode ?? (() => CoverTransparencySettingsService.DefaultMode);
    }

    private static readonly SemaphoreSlim ProcessingGate = new(1, 1);
    private readonly CoverImagePipelineService _pipeline = new(new CoverImagePipelineOptions
    {
        Policy = new StandardCoverPolicy
        {
            MinUsableSquarePixels = CoverInputSafety.MinUsableSquarePixels,
            MaxInputBytes = CoverInputSafety.MaxEncodedBytes,
            MaxTotalPixels = CoverInputSafety.MaxPixels,
            MaxDimension = CoverInputSafety.MaxDimension
        },
        AcquisitionSafetyLimits = new DropImageAcquisitionSafetyLimits
        {
            MaxRemoteDownloadBytes = CoverInputSafety.MaxEncodedBytes,
            MaxEmbeddedDataUrlBytes = CoverInputSafety.MaxEncodedBytes,
            MaxTextArtifactBytes = 8 * 1024 * 1024,
            MaxBitmapPixels = CoverInputSafety.MaxPixels
        }
    });
    private readonly CoverImagePersistenceService _persistence = new();
    private readonly DropImageDataReader _dropReader = new(new DropImageAcquisitionSafetyLimits
    {
        MaxRemoteDownloadBytes = CoverInputSafety.MaxEncodedBytes,
        MaxEmbeddedDataUrlBytes = CoverInputSafety.MaxEncodedBytes,
        MaxTextArtifactBytes = 8 * 1024 * 1024,
        MaxBitmapPixels = CoverInputSafety.MaxPixels
    });

    public bool CanReadDrop(System.Windows.IDataObject data) => _dropReader.CanRead(data);

    // Compatibility entry for synchronous project recovery; interactive hosts await the API below.
    public CoverProcessResult CreateProcessedCover(string sourcePath, string outputFolder,
        int warningThresholdPixels = CoverWarningSettingsService.DefaultThresholdPixels) =>
        CreateProcessedCover(sourcePath, outputFolder, null, warningThresholdPixels);

    public CoverProcessResult CreateProcessedCover(string sourcePath, string outputFolder, string? titleHint,
        int warningThresholdPixels = CoverWarningSettingsService.DefaultThresholdPixels) =>
        CreateProcessedCoverAsync(sourcePath, outputFolder, titleHint, warningThresholdPixels).GetAwaiter().GetResult();

    public async Task<CoverProcessResult> CreateProcessedCoverAsync(string sourcePath, string outputFolder,
        string? titleHint = null, int warningThresholdPixels = CoverWarningSettingsService.DefaultThresholdPixels,
        CancellationToken cancellationToken = default)
    {
        await ProcessingGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var load = await _pipeline.LoadFileAsync(sourcePath, cancellationToken).ConfigureAwait(false);
            return await FinishAsync(load, sourcePath, outputFolder, titleHint, warningThresholdPixels,
                saveSource: false, cancellationToken).ConfigureAwait(false);
        }
        finally { ProcessingGate.Release(); }
    }

    public async Task<CoverProcessResult> CreateDroppedCoverAsync(System.Windows.IDataObject data,
        string outputFolder, string? titleHint = null,
        int warningThresholdPixels = CoverWarningSettingsService.DefaultThresholdPixels,
        CancellationToken cancellationToken = default)
    {
        // Capture OLE/clipboard data on the caller's UI thread, before awaiting the processing gate.
        var analysisTask = _dropReader.AnalyzeAsync(data, cancellationToken);
        var analysis = await analysisTask.ConfigureAwait(false);
        await ProcessingGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var load = await _pipeline.LoadStandardCoverAsync(analysis, cancellationToken).ConfigureAwait(false);
            return await FinishAsync(load, null, outputFolder, titleHint, warningThresholdPixels,
                saveSource: true, cancellationToken).ConfigureAwait(false);
        }
        finally { ProcessingGate.Release(); }
    }

    private async Task<CoverProcessResult> FinishAsync(CoverImagePipelineLoadResult load, string? sourcePath,
        string outputFolder, string? titleHint, int warningThresholdPixels, bool saveSource,
        CancellationToken cancellationToken)
    {
        if (!load.IsSuccess || load.Session is null)
            throw CreateInputException(load.Issues);
        await using var session = load.Session;
        session.OutputOptions = session.OutputOptions with
        { Transparency = CoverTransparencySettingsService.ResolveMode(_getTransparencyMode()) };
        var final = await session.FinalizeAsync(cancellationToken).ConfigureAwait(false);
        if (!final.IsSuccess || final.Output is null)
            throw CreateInputException(final.Issues);
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(outputFolder);
        if (saveSource)
        {
            // Browser temporary leases must never become persistent project source references.
            using var input = await session.ImageData.OpenReadAsync(cancellationToken).ConfigureAwait(false);
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(input, cancellationToken).ConfigureAwait(false))[..12].ToLowerInvariant();
            var extension = session.Inspection.SignatureFormat.ToString().ToLowerInvariant();
            if (extension == "jpeg") extension = "jpg";
            if (extension == "unknown") extension = "img";
            var savedSource = await _persistence.SaveSourceToPathAsync(session.ImageData,
                Path.Combine(outputFolder, $"source_{hash}.{extension}"),
                cancellationToken: cancellationToken).ConfigureAwait(false);
            if (!savedSource.IsSuccess) throw new IOException(LocalizationText.Get("Disc.Setup.CoverFailed.Message"));
            sourcePath = savedSource.FilePath;
        }
        var target = Path.Combine(outputFolder, CoverFileNamePolicy.BuildManagedFileName(BuildShortHash(sourcePath!), titleHint));
        var saved = await _persistence.SaveOutputToPathAsync(final.Output, target,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!saved.IsSuccess) throw new IOException(LocalizationText.Get("Disc.Setup.CoverFailed.Message"));
        return new CoverProcessResult(sourcePath!, saved.FilePath!, session.Inspection.PixelWidth,
            session.Inspection.PixelHeight, final.Output.Width,
            BuildQualityWarning(session.Inspection.PixelWidth, session.Inspection.PixelHeight, warningThresholdPixels, final.Output.Width))
        { Issues = final.Issues };
    }

    private static Exception CreateInputException(IReadOnlyList<CoverImageIssue> issues)
    {
        var key = issues.Any(i => i.Code == CoverImageIssueCode.ImageTooSmall) ? "Cover.InputTooSmall"
            : issues.Any(i => i.Code is CoverImageIssueCode.InputTooLarge or CoverImageIssueCode.TooManyPixels)
                ? "Cover.InputTooLarge" : "Cover.InputInvalid";
        return new InvalidDataException(LocalizationText.Get(key));
    }

    private static string BuildQualityWarning(
        int originalWidth,
        int originalHeight,
        int warningThresholdPixels, int targetSize)
    {
        var threshold = CoverWarningSettingsService.NormalizeThresholdPixels(warningThresholdPixels);
        if (threshold == CoverWarningSettingsService.Disabled)
            return "";

        var shortestSide = Math.Min(originalWidth, originalHeight);

        if (shortestSide >= threshold)
            return "";

        return LocalizationText.Format("Cover.QualityWarning", originalWidth, originalHeight, targetSize);
    }

    private static string BuildShortHash(string sourcePath)
    {
        var fileInfo = new FileInfo(sourcePath);
        var raw = $"{fileInfo.FullName}|{fileInfo.Length}|{fileInfo.LastWriteTimeUtc.Ticks}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(bytes)[..12].ToLowerInvariant();
    }

    public CoverProcessedAssetInfo? TryGetProcessedCoverInfo(string? processedPath)
    {
        if (string.IsNullOrWhiteSpace(processedPath) || !File.Exists(processedPath))
            return null;

        try
        {
            CoverInputSafety.ValidateFile(processedPath);
            var info = new CoverImageInspector().InspectFileAsync(processedPath).GetAwaiter().GetResult();
            if (!info.IsSuccess) return null;
            return new CoverProcessedAssetInfo(Path.GetFileName(processedPath),
                info.SignatureFormat == DetectedImageFormat.Jpeg ? "JPEG" : info.SignatureFormat.ToString().ToUpperInvariant(),
                info.PixelWidth, info.PixelHeight, new FileInfo(processedPath).Length);
        }
        catch (Exception)
        {
            return null;
        }
    }

    public string EnsureManagedCoverForTitle(string? processedPath, string? title)
    {
        if (string.IsNullOrWhiteSpace(processedPath) || !File.Exists(processedPath) ||
            !CoverFileNamePolicy.TryGetManagedHash(processedPath, out var hash))
        {
            return processedPath ?? string.Empty;
        }

        var folder = Path.GetDirectoryName(processedPath) ?? string.Empty;
        var desired = Path.Combine(folder, CoverFileNamePolicy.BuildManagedFileName(hash, title));
        if (Path.GetFullPath(desired).Equals(Path.GetFullPath(processedPath), StringComparison.OrdinalIgnoreCase))
            return processedPath;

        var candidate = desired;
        if (File.Exists(candidate))
        {
            if (FilesAreIdentical(processedPath, candidate))
                return candidate;

            var stem = Path.GetFileNameWithoutExtension(desired);
            string? availableCandidate = null;
            for (var suffix = 2; suffix <= 100; suffix++)
            {
                candidate = Path.Combine(folder, $"{stem}_{suffix}.jpg");
                if (!File.Exists(candidate))
                {
                    availableCandidate = candidate;
                    break;
                }
                if (FilesAreIdentical(processedPath, candidate))
                    return candidate;
            }

            if (availableCandidate is null)
                throw new IOException("Für das verwaltete Cover konnte kein kollisionsfreier Dateiname gebildet werden.");
            candidate = availableCandidate;
        }

        File.Copy(processedPath, candidate, overwrite: false);
        return candidate;
    }

    private static bool FilesAreIdentical(string left, string right)
    {
        var leftInfo = new FileInfo(left);
        var rightInfo = new FileInfo(right);
        if (leftInfo.Length != rightInfo.Length)
            return false;
        using var leftStream = File.OpenRead(left);
        using var rightStream = File.OpenRead(right);
        return SHA256.HashData(leftStream).AsSpan().SequenceEqual(SHA256.HashData(rightStream));
    }


}

public sealed record CoverProcessedAssetInfo(
    string FileName,
    string Format,
    int Width,
    int Height,
    long FileSizeBytes);

public sealed record CoverProcessResult(
    string SourcePath,
    string ProcessedJpegPath,
    int OriginalWidth,
    int OriginalHeight,
    int TargetSize,
    string Warning)
{
    public IReadOnlyList<CoverImageIssue> Issues { get; init; } = [];
}
