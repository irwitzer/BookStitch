using System.IO;
using CoverImageDrop.Core.Issues;
using CoverImageDrop.Core.Output;
using CoverImageDrop.Core.Policy;

namespace CoverImageDrop.Core.Normalization;

internal sealed class CoverImageNormalizer
{
    private readonly StandardCoverPolicy _policy;
    private readonly CoverImageDecoder _decoder = new();
    private readonly CoverImageOrientationService _orientation = new();
    private readonly CoverImageColorManagementService _colorManagement = new();
    private readonly CoverImageCropService _crop = new();
    private readonly CoverImageScaleService _scale = new();
    private readonly CoverImageColorAlphaService _colorAlpha = new();
    private readonly CoverImageEncoder _encoder = new();

    public CoverImageNormalizer(StandardCoverPolicy? policy = null)
    {
        _policy = policy ?? StandardCoverPolicy.Default;
    }

    public async Task<CoverImageOutputResult> NormalizeFileAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var bytes = await File.ReadAllBytesAsync(filePath, cancellationToken).ConfigureAwait(false);
        return NormalizeBytes(bytes, null, cancellationToken);
    }

    public CoverImageOutputResult NormalizeBytes(byte[] bytes)
    {
        return NormalizeBytes(bytes, cropRect: null, CancellationToken.None);
    }

    public CoverImageOutputResult NormalizeBytes(byte[] bytes, CoverImageCropRect? cropRect)
    {
        return NormalizeBytes(bytes, cropRect, CancellationToken.None);
    }

    public CoverImageOutputResult NormalizeBytes(
        byte[] bytes,
        CoverImageCropRect? cropRect,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        cancellationToken.ThrowIfCancellationRequested();

        var decoded = _decoder.Decode(bytes);
        cancellationToken.ThrowIfCancellationRequested();
        var oriented = _orientation.Apply(decoded.FirstFrame);
        cancellationToken.ThrowIfCancellationRequested();
        var colorManaged = _colorManagement.NormalizeToSrgb(oriented, decoded.SourceColorContext);
        if (decoded.SourceProfilePresent && decoded.SourceColorContext is null)
            colorManaged = colorManaged with { SourceProfileDetected = true, TechnicalCause = "Embedded ICC profile could not be loaded." };
        cancellationToken.ThrowIfCancellationRequested();
        var effectiveCrop = _crop.ResolveSquareRect(colorManaged.Bitmap, cropRect);
        var targetSize = CoverImageTargetSizeSelector.SelectTargetSize(effectiveCrop.Width, _policy);

        return NormalizeDecoded(
            decoded.FrameCount,
            colorManaged,
            effectiveCrop,
            new CoverImageNormalizationOptions
            {
                TargetSizePixels = targetSize
            },
            cancellationToken);
    }

    public CoverImageOutputResult NormalizeBytes(
        byte[] bytes,
        CoverImageCropRect? cropRect,
        CoverImageNormalizationOptions options)
    {
        return NormalizeBytes(bytes, cropRect, options, CancellationToken.None);
    }

    public CoverImageOutputResult NormalizeBytes(
        byte[] bytes,
        CoverImageCropRect? cropRect,
        CoverImageNormalizationOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();

        var decoded = _decoder.Decode(bytes);
        cancellationToken.ThrowIfCancellationRequested();
        var oriented = _orientation.Apply(decoded.FirstFrame);
        cancellationToken.ThrowIfCancellationRequested();
        var colorManaged = _colorManagement.NormalizeToSrgb(oriented, decoded.SourceColorContext);
        if (decoded.SourceProfilePresent && decoded.SourceColorContext is null)
            colorManaged = colorManaged with { SourceProfileDetected = true, TechnicalCause = "Embedded ICC profile could not be loaded." };
        cancellationToken.ThrowIfCancellationRequested();
        var effectiveCrop = _crop.ResolveSquareRect(colorManaged.Bitmap, cropRect);
        return NormalizeDecoded(decoded.FrameCount, colorManaged, effectiveCrop, options, cancellationToken);
    }

    public CoverImagePreviewResult CreatePreview(
        byte[] bytes,
        int maxLongEdgePixels = 1600,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (maxLongEdgePixels <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxLongEdgePixels));
        }

        cancellationToken.ThrowIfCancellationRequested();
        var decoded = _decoder.Decode(bytes);
        cancellationToken.ThrowIfCancellationRequested();
        var oriented = _orientation.Apply(decoded.FirstFrame);
        cancellationToken.ThrowIfCancellationRequested();
        var colorManaged = _colorManagement.NormalizeToSrgb(oriented, decoded.SourceColorContext);
        if (decoded.SourceProfilePresent && decoded.SourceColorContext is null)
            colorManaged = colorManaged with { SourceProfileDetected = true, TechnicalCause = "Embedded ICC profile could not be loaded." };
        cancellationToken.ThrowIfCancellationRequested();
        var alphaSafe = _colorAlpha.PrepareForScaling(colorManaged.Bitmap);
        var scaled = _scale.ScaleToFit(alphaSafe, maxLongEdgePixels);
        cancellationToken.ThrowIfCancellationRequested();
        var encoded = _encoder.Encode(scaled, CoverOutputFormat.Png);

        return new CoverImagePreviewResult(
            encoded.Bytes,
            scaled.PixelWidth,
            scaled.PixelHeight,
            BuildSourceProcessingIssues(decoded.FrameCount, colorManaged));
    }

    private CoverImageOutputResult NormalizeDecoded(
        int frameCount,
        CoverImageColorManagementResult colorManaged,
        CoverImageCropRect effectiveCrop,
        CoverImageNormalizationOptions options,
        CancellationToken cancellationToken)
    {
        var targetSize = options.TargetSizePixels
            ?? CoverImageTargetSizeSelector.SelectTargetSize(effectiveCrop.Width, _policy);
        if (targetSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Targetgroesse muss positiv sein.");
        }

        var issues = new List<CoverImageIssue>(BuildSourceProcessingIssues(frameCount, colorManaged));

        cancellationToken.ThrowIfCancellationRequested();
        var cropped = _crop.Crop(colorManaged.Bitmap, effectiveCrop);
        cancellationToken.ThrowIfCancellationRequested();
        var alphaSafe = _colorAlpha.PrepareForScaling(cropped);
        var scaled = _scale.ScaleSquare(alphaSafe, targetSize);
        cancellationToken.ThrowIfCancellationRequested();
        var prepared = _colorAlpha.PrepareForOutput(scaled, options.Format, options.JpegMatte);

        if (prepared.HadTransparency && options.Format == CoverOutputFormat.Jpeg)
        {
            issues.Add(new CoverImageIssue(
                CoverImageIssueCode.TransparencyFlattened,
                CoverImageIssueSeverity.Info,
                new Dictionary<string, object?>
                {
                    ["requestedMatte"] = options.JpegMatte?.ToString() ?? "Auto",
                    ["resolvedMatte"] = prepared.ResolvedJpegMatte?.ToString(),
                    ["edgeBrightnessSamples"] = prepared.AlphaAnalysis.BrightnessSampleCount,
                    ["averageEdgeLuminance"] = prepared.AlphaAnalysis.AverageEdgeLuminance
                }));
        }

        cancellationToken.ThrowIfCancellationRequested();
        var encoded = _encoder.Encode(prepared.Bitmap, options.Format);
        var (mimeType, extension) = options.Format switch
        {
            CoverOutputFormat.Jpeg => ("image/jpeg", ".jpg"),
            CoverOutputFormat.Png => ("image/png", ".png"),
            _ => throw new ArgumentOutOfRangeException(nameof(options.Format), options.Format, "Unbekanntes Outputformat.")
        };

        return new CoverImageOutputResult(
            encoded.Bytes,
            targetSize,
            targetSize,
            options.Format,
            mimeType,
            extension,
            issues)
        {
            ResolvedTargetSizePixels = targetSize,
            WasUpscaled = effectiveCrop.Width < targetSize,
            JpegQuality = encoded.JpegQuality,
            JpegMatte = prepared.ResolvedJpegMatte
        };
    }

    private static IReadOnlyList<CoverImageIssue> BuildSourceProcessingIssues(
        int frameCount,
        CoverImageColorManagementResult colorManaged)
    {
        var issues = new List<CoverImageIssue>();
        if (frameCount > 1)
        {
            issues.Add(new CoverImageIssue(
                CoverImageIssueCode.MultipleFramesFirstFrameUsed,
                CoverImageIssueSeverity.Warning,
                new Dictionary<string, object?>
                {
                    ["frameCount"] = frameCount,
                    ["selectedFrame"] = 1
                }));
        }

        if (colorManaged.SourceProfileDetected && !colorManaged.ConvertedToSrgb)
        {
            issues.Add(new CoverImageIssue(
                CoverImageIssueCode.ColorProfileConversionFailed,
                CoverImageIssueSeverity.Warning,
                new Dictionary<string, object?>
                {
                    ["fallback"] = "AssumeSrgb"
                },
                colorManaged.TechnicalCause));
        }

        return issues;
    }
}
