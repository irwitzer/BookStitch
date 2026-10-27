using System.IO;
using CoverImageDrop.Acquisition;
using CoverImageDrop.Core.Inspection;
using CoverImageDrop.Core.Issues;
using CoverImageDrop.Core.Naming;
using CoverImageDrop.Pipeline.Persistence;

namespace CoverImageDrop.Pipeline.PassThrough;

internal sealed class CoverImagePassThroughProcessor
{
    private readonly CoverImageInspector _inspector = new();
    private readonly CoverImageFileNameGenerator _fileNameGenerator;
    private readonly CoverImagePersistenceService _persistenceService;

    public CoverImagePassThroughProcessor(TimeProvider? timeProvider = null)
    {
        _fileNameGenerator = new CoverImageFileNameGenerator(timeProvider);
        _persistenceService = new CoverImagePersistenceService(timeProvider);
    }

    public async Task<CoverImagePassThroughResult> ProcessAsync(
        DropReadResult readResult,
        CoverImagePassThroughOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(readResult);
        options ??= new CoverImagePassThroughOptions();
        ValidateOptions(options);
        cancellationToken.ThrowIfCancellationRequested();

        var source = readResult.ImageData;
        if (source is null)
        {
            var issue = new CoverImageIssue(
                CoverImageIssueCode.NoImagePayloadFound,
                CoverImageIssueSeverity.Error);
            return Failed(null, [issue]);
        }

        try
        {
            await using var readable = await source.OpenReadAsync(cancellationToken).ConfigureAwait(false);
            if (!readable.CanRead)
            {
                throw new IOException("The acquired image source is not readable.");
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or InvalidOperationException)
        {
            var issue = new CoverImageIssue(
                CoverImageIssueCode.NoImagePayloadFound,
                CoverImageIssueSeverity.Error,
                new Dictionary<string, object?>
                {
                    ["sourceName"] = source.SourceName,
                    ["localFilePath"] = source.LocalFilePath
                },
                ex.Message);
            return Failed(source, [issue]);
        }

        var inspection = await ProbeMetadataAsync(source, cancellationToken).ConfigureAwait(false);
        var contentType = readResult.DownloadInfo?.ContentType ?? source.ContentType;
        var sourceExtension = TryGetKnownSourceExtension(readResult, source);
        var formatResolution = ResolveFormat(
            inspection?.SignatureFormat ?? DetectedImageFormat.Unknown,
            contentType,
            sourceExtension);
        var format = formatResolution.Format;
        var formatSource = formatResolution.Source;
        var formatConfidence = formatResolution.Confidence;
        var fileExtension = ResolveFileExtension(format, sourceExtension);
        var materializationKind = IsBitmapFallback(readResult)
            ? CoverImagePassThroughMaterializationKind.LosslessPngFromBitmap
            : CoverImagePassThroughMaterializationKind.OriginalEncodedPayload;

        var issues = new List<CoverImageIssue>();
        if (inspection is not null && !inspection.IsSuccess)
        {
            issues.Add(CreateMetadataProbeIssue(inspection));
        }

        if (materializationKind == CoverImagePassThroughMaterializationKind.LosslessPngFromBitmap)
        {
            format = DetectedImageFormat.Png;
            formatSource = CoverImagePassThroughFormatSource.BitmapEncoding;
            formatConfidence = CoverImagePassThroughFormatConfidence.Verified;
            fileExtension = ".png";
            issues.Add(new CoverImageIssue(
                CoverImageIssueCode.BitmapFallbackEncodedAsPng,
                CoverImageIssueSeverity.Info,
                new Dictionary<string, object?>
                {
                    ["acquisitionMethod"] = readResult.AcquisitionMethod
                }));
        }

        int? pixelWidth = inspection is { PixelWidth: > 0 } ? inspection.PixelWidth : null;
        int? pixelHeight = inspection is { PixelHeight: > 0 } ? inspection.PixelHeight : null;
        CoverImagePersistenceResult? persistence = null;
        string? suggestedFileName;

        if (!string.IsNullOrWhiteSpace(options.TargetFilePath))
        {
            suggestedFileName = Path.GetFileName(options.TargetFilePath);
            persistence = await _persistenceService.SaveSourceToPathAsync(
                source,
                options.TargetFilePath,
                options.CollisionPolicy,
                cancellationToken).ConfigureAwait(false);
            issues.AddRange(persistence.Issues);
        }
        else
        {
            var namingOptions = CompleteOriginalFileName(options.Naming, readResult, source);
            var naming = _fileNameGenerator.Generate(fileExtension, namingOptions);
            suggestedFileName = naming.FileName;
            issues.AddRange(naming.Issues);

            if (!string.IsNullOrWhiteSpace(options.TargetFolder))
            {
                var requestedPath = Path.Combine(options.TargetFolder, naming.FileName);
                persistence = await _persistenceService.SaveSourceToPathAsync(
                    source,
                    requestedPath,
                    options.CollisionPolicy,
                    cancellationToken).ConfigureAwait(false);
                issues.AddRange(persistence.Issues);
            }
        }

        var isSuccess = persistence?.IsSuccess ?? true;
        return new CoverImagePassThroughResult(
            isSuccess,
            source,
            materializationKind,
            format,
            pixelWidth,
            pixelHeight,
            fileExtension,
            suggestedFileName,
            persistence,
            issues)
        {
            FormatSource = formatSource,
            FormatConfidence = formatConfidence
        };
    }

    private async Task<CoverImageInspectionResult?> ProbeMetadataAsync(
        DropImageData source,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!string.IsNullOrWhiteSpace(source.LocalFilePath))
        {
            return await _inspector.InspectFileAsync(source.LocalFilePath, cancellationToken).ConfigureAwait(false);
        }

        if (source.TryGetInMemoryBytes(out var bytes))
        {
            return await _inspector.InspectBytesAsync(
                bytes,
                source.SourceName,
                cancellationToken).ConfigureAwait(false);
        }

        return null;
    }

    private static CoverImageNamingOptions CompleteOriginalFileName(
        CoverImageNamingOptions namingOptions,
        DropReadResult readResult,
        DropImageData source)
    {
        ArgumentNullException.ThrowIfNull(namingOptions);
        if (namingOptions.Strategy != CoverImageNamingStrategy.OriginalFileName ||
            !string.IsNullOrWhiteSpace(namingOptions.OriginalFileName))
        {
            return namingOptions;
        }

        return namingOptions with
        {
            OriginalFileName = TryGetOriginalFileName(readResult, source)
        };
    }

    private static string? TryGetOriginalFileName(DropReadResult readResult, DropImageData source)
    {
        if (source.PayloadKind == DropImagePayloadKind.LocalFile &&
            !string.IsNullOrWhiteSpace(source.LocalFilePath))
        {
            var localName = Path.GetFileName(source.LocalFilePath);
            if (!string.IsNullOrWhiteSpace(localName))
            {
                return localName;
            }
        }

        var downloadUrl = readResult.DownloadInfo?.FinalUrl ??
            readResult.DownloadInfo?.RequestedUrl ??
            source.SourceUrl;
        if (Uri.TryCreate(downloadUrl, UriKind.Absolute, out var uri))
        {
            var urlName = Path.GetFileName(Uri.UnescapeDataString(uri.AbsolutePath));
            if (!string.IsNullOrWhiteSpace(urlName))
            {
                return urlName;
            }
        }

        return string.IsNullOrWhiteSpace(source.SourceName) ? null : source.SourceName;
    }

    private static string? TryGetKnownSourceExtension(DropReadResult readResult, DropImageData source)
    {
        var candidates = new List<string?>
        {
            source.PayloadKind == DropImagePayloadKind.LocalFile ? source.LocalFilePath : null,
            readResult.DownloadInfo?.FinalUrl,
            readResult.DownloadInfo?.RequestedUrl,
            source.SourceUrl,
            source.SourceName
        };

        foreach (var candidate in candidates)
        {
            var extension = TryGetKnownExtension(candidate);
            if (extension is not null)
            {
                return extension;
            }
        }

        return null;
    }

    private static string? TryGetKnownExtension(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return null;
        }

        string path = candidate;
        if (Uri.TryCreate(candidate, UriKind.Absolute, out var uri) &&
            (uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
             uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
             uri.Scheme.Equals(Uri.UriSchemeFile, StringComparison.OrdinalIgnoreCase)))
        {
            path = uri.AbsolutePath;
        }

        var extension = Path.GetExtension(path).ToLowerInvariant();
        return extension switch
        {
            ".jpg" or ".jpeg" or ".png" or ".webp" or ".gif" or ".bmp" or ".tif" or ".tiff" or ".ico" => extension,
            _ => null
        };
    }

    private static PassThroughFormatResolution ResolveFormat(
        DetectedImageFormat signatureFormat,
        string? contentType,
        string? sourceExtension)
    {
        if (signatureFormat != DetectedImageFormat.Unknown)
        {
            return new PassThroughFormatResolution(
                signatureFormat,
                CoverImagePassThroughFormatSource.Signature,
                CoverImagePassThroughFormatConfidence.Verified);
        }

        var contentTypeFormat = FormatFromContentType(contentType);
        if (contentTypeFormat != DetectedImageFormat.Unknown)
        {
            return new PassThroughFormatResolution(
                contentTypeFormat,
                CoverImagePassThroughFormatSource.ContentType,
                CoverImagePassThroughFormatConfidence.Unverified);
        }

        var extensionFormat = FormatFromExtension(sourceExtension);
        if (extensionFormat != DetectedImageFormat.Unknown)
        {
            return new PassThroughFormatResolution(
                extensionFormat,
                CoverImagePassThroughFormatSource.FileExtension,
                CoverImagePassThroughFormatConfidence.Unverified);
        }

        return new PassThroughFormatResolution(
            DetectedImageFormat.Unknown,
            CoverImagePassThroughFormatSource.Unknown,
            CoverImagePassThroughFormatConfidence.Unknown);
    }

    private static DetectedImageFormat FormatFromContentType(string? contentType)
    {
        var normalized = contentType?.Split(';', 2)[0].Trim();
        return normalized?.ToLowerInvariant() switch
        {
            "image/jpeg" or "image/jpg" => DetectedImageFormat.Jpeg,
            "image/png" => DetectedImageFormat.Png,
            "image/gif" => DetectedImageFormat.Gif,
            "image/bmp" or "image/x-ms-bmp" => DetectedImageFormat.Bmp,
            "image/tiff" => DetectedImageFormat.Tiff,
            "image/webp" => DetectedImageFormat.WebP,
            "image/x-icon" or "image/vnd.microsoft.icon" => DetectedImageFormat.Ico,
            _ => DetectedImageFormat.Unknown
        };
    }

    private static DetectedImageFormat FormatFromExtension(string? extension)
    {
        return extension?.ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => DetectedImageFormat.Jpeg,
            ".png" => DetectedImageFormat.Png,
            ".gif" => DetectedImageFormat.Gif,
            ".bmp" => DetectedImageFormat.Bmp,
            ".tif" or ".tiff" => DetectedImageFormat.Tiff,
            ".webp" => DetectedImageFormat.WebP,
            ".ico" => DetectedImageFormat.Ico,
            _ => DetectedImageFormat.Unknown
        };
    }

    private static string ResolveFileExtension(DetectedImageFormat format, string? sourceExtension)
    {
        return format switch
        {
            DetectedImageFormat.Jpeg => ".jpg",
            DetectedImageFormat.Png => ".png",
            DetectedImageFormat.Gif => ".gif",
            DetectedImageFormat.Bmp => ".bmp",
            DetectedImageFormat.Tiff => ".tif",
            DetectedImageFormat.WebP => ".webp",
            DetectedImageFormat.Ico => ".ico",
            _ => sourceExtension ?? ".bin"
        };
    }

    private static bool IsBitmapFallback(DropReadResult readResult)
    {
        return string.Equals(readResult.AcquisitionMethod, "Bitmap", StringComparison.OrdinalIgnoreCase);
    }

    private static CoverImageIssue CreateMetadataProbeIssue(CoverImageInspectionResult inspection)
    {
        var issueCodes = inspection.Issues
            .Select(issue => issue.Code.ToString())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var technicalCauses = inspection.Issues
            .Select(issue => issue.TechnicalCause)
            .Where(cause => !string.IsNullOrWhiteSpace(cause))
            .ToArray();

        return new CoverImageIssue(
            CoverImageIssueCode.MetadataProbeFailed,
            CoverImageIssueSeverity.Info,
            new Dictionary<string, object?>
            {
                ["sourceName"] = inspection.SourceName,
                ["probeIssueCodes"] = issueCodes
            },
            technicalCauses.Length == 0 ? null : string.Join(" | ", technicalCauses));
    }

    private static void ValidateOptions(CoverImagePassThroughOptions options)
    {
        ArgumentNullException.ThrowIfNull(options.Naming);

        if (!string.IsNullOrWhiteSpace(options.TargetFolder) &&
            !string.IsNullOrWhiteSpace(options.TargetFilePath))
        {
            throw new ArgumentException(
                "PassThrough accepts either TargetFolder or TargetFilePath, not both.",
                nameof(options));
        }
    }

    private readonly record struct PassThroughFormatResolution(
        DetectedImageFormat Format,
        CoverImagePassThroughFormatSource Source,
        CoverImagePassThroughFormatConfidence Confidence);

    private static CoverImagePassThroughResult Failed(
        DropImageData? source,
        IReadOnlyList<CoverImageIssue> issues)
    {
        return new CoverImagePassThroughResult(
            false,
            source,
            CoverImagePassThroughMaterializationKind.OriginalEncodedPayload,
            DetectedImageFormat.Unknown,
            null,
            null,
            null,
            null,
            null,
            issues);
    }
}
