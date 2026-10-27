using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CoverImageDrop.Core.Imaging;
using CoverImageDrop.Core.Issues;

namespace CoverImageDrop.Core.Inspection;

public sealed class CoverImageInspector
{
    public Task<CoverImageInspectionResult> InspectFileAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        cancellationToken.ThrowIfCancellationRequested();

        return RunStaWorkerAsync(
            () => InspectFileCore(filePath),
            cancellationToken);
    }

    public Task<CoverImageInspectionResult> InspectBytesAsync(
        byte[] bytes,
        string sourceName = "Bilddaten",
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        cancellationToken.ThrowIfCancellationRequested();

        return RunStaWorkerAsync(
            () => InspectBytes(bytes, sourceName),
            cancellationToken);
    }

    public CoverImageInspectionResult InspectBytes(
        byte[] bytes,
        string sourceName = "Bilddaten")
    {
        ArgumentNullException.ThrowIfNull(bytes);

        using var stream = new MemoryStream(bytes, writable: false);
        return InspectStream(stream, sourceName);
    }

    private static CoverImageInspectionResult InspectFileCore(string filePath)
    {
        var fileInfo = new FileInfo(filePath);
        if (!fileInfo.Exists)
        {
            return CreateFailed(
                Path.GetFileName(filePath),
                DetectedImageFormat.Unknown,
                CoverImageIssueCode.NoImagePayloadFound,
                "Source file was not found.");
        }

        try
        {
            using var stream = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);
            return InspectStream(stream, fileInfo.Name);
        }
        catch (Exception ex) when (
            ex is IOException or
            UnauthorizedAccessException or
            NotSupportedException)
        {
            return CreateFailed(
                fileInfo.Name,
                DetectedImageFormat.Unknown,
                CoverImageIssueCode.ImageDecodeFailed,
                ex.Message);
        }
    }

    private static CoverImageInspectionResult InspectStream(Stream stream, string sourceName)
    {
        var signatureFormat = DetectSignature(stream);
        var issues = new List<CoverImageIssue>();

        try
        {
            stream.Position = 0;
            if (signatureFormat == DetectedImageFormat.WebP)
            {
                if (stream.Length > 32L * 1024 * 1024) throw new InvalidDataException("WebP input exceeds the resource limit.");
                using var payload = new MemoryStream();
                stream.CopyTo(payload);
                var info = BundledWebPDecoder.Read(payload.ToArray(), decode: false);
                var swaps = CoverImageBitmapOrientation.SwapsDimensions(info.Orientation);
                return CreateResult(sourceName, signatureFormat, "Bundled libwebp 1.6.0", info.Width, info.Height,
                    swaps ? info.Height : info.Width, swaps ? info.Width : info.Height, info.Frames,
                    mayContainTransparency: true, colorProfileCount: info.Icc.Length > 0 ? 1 : 0);
            }
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnDemand);
            var frame = decoder.Frames.FirstOrDefault();

            if (frame is null)
            {
                issues.Add(CreateIssue(
                    CoverImageIssueCode.ImageDecodeFailed,
                    CoverImageIssueSeverity.Error,
                    "Image decoder returned no readable frame."));
                return CreateResult(sourceName, signatureFormat, decoder.CodecInfo.FriendlyName, issues: issues);
            }

            var width = frame.PixelWidth;
            var height = frame.PixelHeight;
            if (width <= 0 || height <= 0)
            {
                issues.Add(CreateIssue(
                    CoverImageIssueCode.ImageDecodeFailed,
                    CoverImageIssueSeverity.Error,
                    "Image decoder returned invalid pixel dimensions.",
                    new Dictionary<string, object?>
                    {
                        ["pixelWidth"] = width,
                        ["pixelHeight"] = height
                    }));
            }

            var orientation = CoverImageBitmapOrientation.TryReadExifOrientation(frame.Metadata as BitmapMetadata);
            var swapsDimensions = CoverImageBitmapOrientation.SwapsDimensions(orientation);
            var orientedWidth = swapsDimensions ? height : width;
            var orientedHeight = swapsDimensions ? width : height;
            var colorProfileCount = TryGetColorProfileCount(frame, decoder);

            return CreateResult(
                sourceName,
                signatureFormat,
                decoder.CodecInfo.FriendlyName,
                width,
                height,
                orientedWidth,
                orientedHeight,
                decoder.Frames.Count,
                MayContainTransparency(frame.Format),
                issues,
                colorProfileCount);
        }
        catch (Exception ex) when (ex is FileFormatException or NotSupportedException or IOException or InvalidOperationException or ArgumentException or OperationCanceledException or System.ComponentModel.Win32Exception or System.Runtime.InteropServices.COMException)
        {
            issues.Add(CreateDecodeFailureIssue(signatureFormat, ex));
            return CreateResult(sourceName, signatureFormat, issues: issues);
        }
    }

    internal static CoverImageIssue CreateDecodeFailureIssue(
        DetectedImageFormat signatureFormat,
        Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var issueCode = signatureFormat switch
        {
            DetectedImageFormat.WebP when exception is NotSupportedException =>
                CoverImageIssueCode.WebPDecoderUnavailable,
            DetectedImageFormat.Unknown => CoverImageIssueCode.UnsupportedImageFormat,
            _ => CoverImageIssueCode.ImageDecodeFailed
        };
        IReadOnlyDictionary<string, object?>? data = signatureFormat == DetectedImageFormat.Unknown
            ? null
            : new Dictionary<string, object?>
            {
                ["signatureFormat"] = signatureFormat.ToString()
            };
        var suggestedAction = issueCode == CoverImageIssueCode.WebPDecoderUnavailable
            ? "InstallWebPSupport"
            : null;

        return new CoverImageIssue(
            issueCode,
            CoverImageIssueSeverity.Error,
            data,
            exception.Message,
            suggestedAction);
    }

    private static DetectedImageFormat DetectSignature(Stream stream)
    {
        if (!stream.CanSeek)
        {
            return DetectedImageFormat.Unknown;
        }

        var originalPosition = stream.Position;
        Span<byte> header = stackalloc byte[16];
        var read = stream.Read(header);
        stream.Position = originalPosition;
        return ImageFormatSignatureDetector.Detect(header[..read]);
    }

    private static int TryGetColorProfileCount(BitmapFrame frame, BitmapDecoder decoder)
    {
        try
        {
            var frameCount = frame.ColorContexts?.Count ?? 0;
            if (frameCount > 0)
            {
                return frameCount;
            }
        }
        catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException)
        {
        }

        try
        {
            return decoder.ColorContexts?.Count ?? 0;
        }
        catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException)
        {
            return 0;
        }
    }

    private static Task<T> RunStaWorkerAsync<T>(
        Func<T> action,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(action);
        cancellationToken.ThrowIfCancellationRequested();

        var completion = new TaskCompletionSource<T>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                completion.TrySetResult(action());
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                completion.TrySetCanceled(cancellationToken);
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
        })
        {
            IsBackground = true,
            Name = "CoverImageDrop.Inspection.WicWorker"
        };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    private static CoverImageInspectionResult CreateFailed(
        string sourceName,
        DetectedImageFormat signatureFormat,
        CoverImageIssueCode issueCode,
        string technicalCause)
    {
        return CreateResult(
            sourceName,
            signatureFormat,
            issues:
            [
                CreateIssue(issueCode, CoverImageIssueSeverity.Error, technicalCause)
            ]);
    }

    private static CoverImageInspectionResult CreateResult(
        string sourceName,
        DetectedImageFormat signatureFormat,
        string decoderCodec = "",
        int pixelWidth = 0,
        int pixelHeight = 0,
        int orientedPixelWidth = 0,
        int orientedPixelHeight = 0,
        int frameCount = 0,
        bool mayContainTransparency = false,
        IReadOnlyList<CoverImageIssue>? issues = null,
        int colorProfileCount = 0)
    {
        return new CoverImageInspectionResult(
            sourceName,
            signatureFormat,
            decoderCodec,
            pixelWidth,
            pixelHeight,
            orientedPixelWidth,
            orientedPixelHeight,
            frameCount,
            mayContainTransparency,
            issues ?? [])
        {
            ColorProfileCount = colorProfileCount
        };
    }

    private static CoverImageIssue CreateIssue(
        CoverImageIssueCode code,
        CoverImageIssueSeverity severity,
        string? technicalCause = null,
        IReadOnlyDictionary<string, object?>? data = null)
    {
        return new CoverImageIssue(code, severity, data, technicalCause);
    }

    private static bool MayContainTransparency(PixelFormat format)
    {
        return format == PixelFormats.Bgra32 ||
               format == PixelFormats.Pbgra32 ||
               format == PixelFormats.Prgba64 ||
               format == PixelFormats.Rgba64 ||
               format == PixelFormats.Indexed1 ||
               format == PixelFormats.Indexed2 ||
               format == PixelFormats.Indexed4 ||
               format == PixelFormats.Indexed8;
    }
}
