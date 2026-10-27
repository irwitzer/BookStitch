using System.IO;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media.Imaging;

namespace CoverImageDrop.Acquisition;

public sealed partial class DropImageDataReader
{
    private static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromSeconds(15);
    private static readonly HttpClient SharedHttpClient = CreateHttpClient(CreateDefaultHttpHandler());
    private const int MaxRedirects = 5;
    private const int DiagnosticTextLimit = 20_000;
    internal const int MaxDownloadedHtmlCharacters = 4 * 1024 * 1024;
    private const int BitmapFallbackPriority = 1_000;

    private readonly HttpClient _httpClient;
    private readonly TimeSpan _requestTimeout;
    private readonly DropImageAcquisitionSafetyLimits _safetyLimits;

    private static readonly HashSet<string> KnownTextFormats = new(
        StringComparer.OrdinalIgnoreCase)
    {
        DataFormats.Html,
        "HTML Format",
        DataFormats.UnicodeText,
        DataFormats.Text,
        DataFormats.StringFormat,
        "System.String",
        "text/html",
        "text/plain",
        "text/uri-list",
        "text/x-moz-url",
        "text/x-moz-url-data",
        "text/x-moz-url-desc",
        "application/x-moz-file-promise-url",
        "text/_moz_htmlcontext",
        "text/_moz_htmlinfo",
        "UniformResourceLocator",
        "UniformResourceLocatorW"
    };

    public DropImageDataReader()
        : this(
            SharedHttpClient,
            DefaultRequestTimeout,
            DropImageAcquisitionSafetyLimits.Default)
    {
    }

    public DropImageDataReader(DropImageAcquisitionSafetyLimits safetyLimits)
        : this(SharedHttpClient, DefaultRequestTimeout, safetyLimits)
    {
    }

    internal DropImageDataReader(
        HttpMessageHandler httpMessageHandler,
        TimeSpan? requestTimeout = null,
        DropImageAcquisitionSafetyLimits? safetyLimits = null)
        : this(
            CreateHttpClient(httpMessageHandler),
            requestTimeout ?? DefaultRequestTimeout,
            safetyLimits ?? DropImageAcquisitionSafetyLimits.Default)
    {
    }

    private DropImageDataReader(
        HttpClient httpClient,
        TimeSpan requestTimeout,
        DropImageAcquisitionSafetyLimits safetyLimits)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(safetyLimits);
        if (requestTimeout <= TimeSpan.Zero || requestTimeout == Timeout.InfiniteTimeSpan)
        {
            throw new ArgumentOutOfRangeException(nameof(requestTimeout));
        }

        safetyLimits.Validate();

        _httpClient = httpClient;
        _requestTimeout = requestTimeout;
        _safetyLimits = safetyLimits;
    }

    public bool CanRead(IDataObject data)
    {
        ArgumentNullException.ThrowIfNull(data);

        return HasFileDrop(data) ||
               IsDataPresent(data, DataFormats.Bitmap) ||
               IsDataPresent(data, DataFormats.Html) ||
               IsDataPresent(data, "HTML Format") ||
               IsDataPresent(data, "UniformResourceLocatorW") ||
               IsDataPresent(data, "UniformResourceLocator") ||
               IsDataPresent(data, "text/uri-list") ||
               IsDataPresent(data, "text/x-moz-url") ||
               IsDataPresent(data, DataFormats.UnicodeText) ||
               IsDataPresent(data, DataFormats.Text);
    }

    public DropAcquisitionAnalysis Analyze(IDataObject data)
    {
        ArgumentNullException.ThrowIfNull(data);

        var snapshot = CaptureDropSnapshot(data);
        return AnalyzeSnapshot(snapshot, CancellationToken.None);
    }

    public Task<DropAcquisitionAnalysis> AnalyzeAsync(
        IDataObject data,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);
        cancellationToken.ThrowIfCancellationRequested();

        // IDataObject/COM access stays on the caller's STA. Only regex/candidate
        // analysis runs on a worker after a bounded snapshot has been captured.
        var snapshot = CaptureDropSnapshot(data);
        return Task.Run(
            () => AnalyzeSnapshot(snapshot, cancellationToken),
            cancellationToken);
    }

    public DropReadResult CreateDiagnosticResult(
        IDataObject data,
        string acquisitionMethod,
        IReadOnlyList<DropDownloadAttempt>? downloadAttempts = null)
    {
        var analysis = Analyze(data);
        return CreateReadResult(
            null,
            acquisitionMethod,
            analysis,
            null,
            downloadAttempts ?? []);
    }

    public async Task<DropReadResult> ReadAsync(IDataObject data, CancellationToken cancellationToken = default)
    {
        var analysis = await AnalyzeAsync(data, cancellationToken).ConfigureAwait(false);
        var failedDownloadAttempts = Array.Empty<DropDownloadAttempt>();
        DropDownloadException? downloadFailure = null;
        var urlsAttempted = false;

        foreach (var candidate in analysis.Candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (candidate.SourceData is not null)
            {
                return CreateReadResult(
                    candidate.SourceData,
                    GetAcquisitionMethod(candidate.Kind),
                    analysis,
                    null,
                    failedDownloadAttempts);
            }

            if (candidate.DeferredPayload is not null)
            {
                var deferred = await MaterializeDeferredCandidateAsync(
                    analysis,
                    candidate,
                    cancellationToken).ConfigureAwait(false);
                if (deferred.IsSuccess &&
                    deferred.ImageData is not null)
                {
                    return CreateReadResult(
                        deferred.ImageData,
                        GetAcquisitionMethod(candidate.Kind),
                        analysis,
                        null,
                        failedDownloadAttempts);
                }

                continue;
            }

            if (candidate.Url is null || urlsAttempted)
            {
                continue;
            }

            urlsAttempted = true;
            try
            {
                var download = await DownloadFirstUsableCandidateAsync(analysis.UrlCandidates, cancellationToken).ConfigureAwait(false);
                return CreateReadResult(download.SourceData, "UrlDownload", analysis, download.Info, download.Attempts);
            }
            catch (DropDownloadException ex) when (!cancellationToken.IsCancellationRequested)
            {
                downloadFailure = ex;
                failedDownloadAttempts = ex.Attempts.ToArray();
            }
        }

        if (downloadFailure is not null)
        {
            throw downloadFailure;
        }

        return CreateReadResult(null, "None", analysis, null, failedDownloadAttempts);
    }

    public Task<DropCandidateMaterializationResult> MaterializeCandidateAsync(
        DropAcquisitionAnalysis analysis,
        DropImageCandidate candidate,
        CancellationToken cancellationToken = default)
    {
        return MaterializeCandidateAsync(
            analysis,
            candidate,
            variantReference: null,
            cancellationToken: cancellationToken,
            maxDownloadBytes: null);
    }

    public async Task<DropCandidateMaterializationResult> MaterializeCandidateAsync(
        DropAcquisitionAnalysis analysis,
        DropImageCandidate candidate,
        DropImageVariantReference? variantReference,
        CancellationToken cancellationToken = default,
        long? maxDownloadBytes = null)
    {
        ArgumentNullException.ThrowIfNull(analysis);
        ArgumentNullException.ThrowIfNull(candidate);
        if (maxDownloadBytes is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxDownloadBytes));
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (candidate.SourceData is not null)
        {
            var readResult = CreateReadResult(
                candidate.SourceData,
                GetAcquisitionMethod(candidate.Kind),
                analysis,
                null,
                []);
            return new DropCandidateMaterializationResult(candidate, readResult, null);
        }

        if (candidate.DeferredPayload is not null)
        {
            return await MaterializeDeferredCandidateAsync(
                analysis,
                candidate,
                cancellationToken).ConfigureAwait(false);
        }

        if (string.IsNullOrWhiteSpace(candidate.Url))
        {
            var readResult = CreateReadResult(null, "CandidateRejected", analysis, null, []);
            return new DropCandidateMaterializationResult(
                candidate,
                readResult,
                "Kandidat besitzt weder Payload noch URL.");
        }

        var urlCandidate = analysis.UrlCandidates.FirstOrDefault(existing =>
            existing.Url.Equals(candidate.Url, StringComparison.OrdinalIgnoreCase))
            ?? new DropUrlCandidate(
                candidate.Url,
                candidate.Source,
                candidate.Priority,
                candidate.Width,
                candidate.Kind);

        try
        {
            var download = await DownloadSingleUsableCandidateAsync(
                urlCandidate,
                analysis.UrlCandidates,
                variantReference,
                cancellationToken,
                maxDownloadBytes ?? _safetyLimits.MaxRemoteDownloadBytes).ConfigureAwait(false);
            var result = CreateReadResult(
                download.SourceData,
                "UrlDownload",
                analysis,
                download.Info,
                download.Attempts);
            return new DropCandidateMaterializationResult(candidate, result, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (DropDownloadException ex)
        {
            var result = CreateReadResult(
                null,
                "CandidateRejected",
                analysis,
                null,
                ex.Attempts);
            return new DropCandidateMaterializationResult(candidate, result, ex.Message);
        }
    }

    private static HttpMessageHandler CreateDefaultHttpHandler()
    {
        return new HttpClientHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            UseDefaultCredentials = false,
            PreAuthenticate = false,
            AutomaticDecompression = DecompressionMethods.GZip |
                                     DecompressionMethods.Deflate |
                                     DecompressionMethods.Brotli
        };
    }

    private static HttpClient CreateHttpClient(HttpMessageHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        var client = new HttpClient(handler, disposeHandler: false)
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 CoverImageDrop");
        return client;
    }

    private static bool IsDataPresent(
        IDataObject data,
        string format)
    {
        try
        {
            return data.GetDataPresent(format);
        }
        catch (Exception ex) when (IsDropDataAccessException(ex))
        {
            return false;
        }
    }

    private static bool HasFileDrop(IDataObject data)
    {
        try
        {
            return data.GetDataPresent(DataFormats.FileDrop);
        }
        catch (Exception ex) when (IsDropDataAccessException(ex))
        {
            return false;
        }
    }

    private DropDataSnapshot CaptureDropSnapshot(IDataObject data)
    {
        var formats = GetOfferedFormats(data);
        var processingTextArtifacts = ReadTextArtifacts(
            data,
            formats,
            _safetyLimits.MaxTextArtifactBytes);
        var filePath = TryReadFileDrop(data, out var capturedFilePath)
            ? capturedFilePath
            : null;
        var bitmap = TryCaptureBitmap(data, out var capturedBitmap)
            ? capturedBitmap
            : null;

        return new DropDataSnapshot(
            formats,
            processingTextArtifacts,
            filePath,
            bitmap);
    }

    private static DropAcquisitionAnalysis AnalyzeSnapshot(
        DropDataSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var diagnosticTextArtifacts = CreateDiagnosticTextArtifacts(
            snapshot.ProcessingTextArtifacts);
        var sourceDetection = DetectSource(snapshot.Formats);
        var baseUri = TryFindSourceUri(snapshot.ProcessingTextArtifacts.Values);

        cancellationToken.ThrowIfCancellationRequested();
        var urlCandidates = FindUrlCandidates(
            snapshot.ProcessingTextArtifacts,
            allowPageUrls: true,
            baseUri);

        cancellationToken.ThrowIfCancellationRequested();
        var candidates = CollectCandidates(
            snapshot,
            snapshot.ProcessingTextArtifacts,
            urlCandidates);

        return new DropAcquisitionAnalysis(
            sourceDetection,
            snapshot.Formats,
            snapshot.ProcessingTextArtifacts,
            diagnosticTextArtifacts,
            candidates,
            urlCandidates);
    }

    private static string[] GetOfferedFormats(IDataObject data)
    {
        try
        {
            return data.GetFormats(autoConvert: false)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order()
                .ToArray();
        }
        catch (Exception ex) when (IsDropDataAccessException(ex))
        {
            return [];
        }
    }

    private static bool TryReadFileDrop(
        IDataObject data,
        out string filePath)
    {
        try
        {
            if (data.GetDataPresent(DataFormats.FileDrop) &&
                data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files)
            {
                filePath = files[0];
                return true;
            }
        }
        catch (Exception ex) when (IsDropDataAccessException(ex))
        {
        }

        filePath = string.Empty;
        return false;
    }

    private static bool TryCaptureBitmap(
        IDataObject data,
        out BitmapSource? bitmap)
    {
        try
        {
            if (!data.GetDataPresent(DataFormats.Bitmap) ||
                data.GetData(DataFormats.Bitmap) is not BitmapSource source)
            {
                bitmap = null;
                return false;
            }

            if (!source.IsFrozen)
            {
                if (!source.CanFreeze)
                {
                    bitmap = null;
                    return false;
                }

                // Freezing transfers the existing WPF bitmap safely to the
                // later STA materializer without cloning/PNG-encoding it here.
                source.Freeze();
            }

            bitmap = source;
            return true;
        }
        catch (Exception ex) when (
            IsDropDataAccessException(ex) ||
            ex is InvalidOperationException or NotSupportedException)
        {
            bitmap = null;
            return false;
        }
    }

    private static IReadOnlyList<DropImageCandidate> CollectCandidates(
        DropDataSnapshot snapshot,
        IReadOnlyDictionary<string, string> processingTextArtifacts,
        IReadOnlyList<DropUrlCandidate> urlCandidates)
    {
        var candidates = new List<DropImageCandidate>();

        if (!string.IsNullOrWhiteSpace(snapshot.FilePath))
        {
            candidates.Add(new DropImageCandidate(
                DropImageCandidateKind.LocalFile,
                "FileDrop",
                0,
                null,
                DropImageData.FromFile(snapshot.FilePath),
                null));
        }

        AddDataUrlCandidates(candidates, processingTextArtifacts);

        foreach (var urlCandidate in urlCandidates)
        {
            candidates.Add(new DropImageCandidate(
                urlCandidate.Kind,
                urlCandidate.Source,
                urlCandidate.Priority,
                urlCandidate.Width,
                null,
                urlCandidate.Url));
        }

        if (snapshot.Bitmap is not null)
        {
            candidates.Add(new DropImageCandidate(
                DropImageCandidateKind.BitmapFallback,
                "Bitmap",
                BitmapFallbackPriority,
                null,
                null,
                null)
            {
                DeferredPayload = new DropBitmapPayloadReference(snapshot.Bitmap)
            });
        }

        return candidates
            .OrderBy(candidate => candidate.Priority)
            .ThenByDescending(candidate => candidate.Width ?? 0)
            .ThenBy(candidate => candidate.Url?.Length ?? 0)
            .ToArray();
    }

    private static void AddDataUrlCandidates(
        ICollection<DropImageCandidate> candidates,
        IReadOnlyDictionary<string, string> processingTextArtifacts)
    {
        foreach (var artifact in processingTextArtifacts)
        {
            foreach (Match match in DataImageRegex().Matches(artifact.Value))
            {
                var payload = match.Groups["payload"];
                if (!payload.Success || payload.Length == 0)
                {
                    continue;
                }

                candidates.Add(new DropImageCandidate(
                    DropImageCandidateKind.EncodedDataUrl,
                    $"{artifact.Key}:data-url",
                    5,
                    null,
                    null,
                    null)
                {
                    DeferredPayload = new DropDataUrlPayloadReference(
                        artifact.Key,
                        payload.Index,
                        payload.Length)
                });
            }
        }
    }

    private static Dictionary<string, string> ReadTextArtifacts(
        IDataObject data,
        IReadOnlyList<string> formats,
        long maxTextArtifactBytes)
    {
        var artifacts = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var format in formats)
        {
            if (!KnownTextFormats.Contains(format))
            {
                continue;
            }

            var text = TryGetStringData(
                data,
                format,
                maxTextArtifactBytes);
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            artifacts[format] = text;
        }

        return artifacts;
    }

    private static IReadOnlyDictionary<string, string> CreateDiagnosticTextArtifacts(
        IReadOnlyDictionary<string, string> processingTextArtifacts)
    {
        return processingTextArtifacts.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.Length > DiagnosticTextLimit
                ? pair.Value[..DiagnosticTextLimit]
                : pair.Value,
            StringComparer.OrdinalIgnoreCase);
    }

    private static string? TryGetStringData(
        IDataObject data,
        string format,
        long maxTextArtifactBytes)
    {
        try
        {
            if (!data.GetDataPresent(format, autoConvert: false))
            {
                return null;
            }

            var value = data.GetData(format, autoConvert: false);
            return value switch
            {
                string text when IsStringWithinTextBudget(
                    text,
                    maxTextArtifactBytes) => text,
                MemoryStream stream => DecodeBoundedMemoryStream(
                    stream,
                    maxTextArtifactBytes),
                byte[] bytes when bytes.LongLength <= maxTextArtifactBytes =>
                    DecodeString(bytes),
                _ => null
            };
        }
        catch (Exception ex) when (IsDropDataAccessException(ex))
        {
            return null;
        }
    }

    private static bool IsStringWithinTextBudget(
        string text,
        long maxTextArtifactBytes)
    {
        // Strings are UTF-16 in memory. Use that allocation size as the
        // resource budget without performing another full encoding pass.
        return text.Length <= maxTextArtifactBytes / sizeof(char);
    }

    private static string? DecodeBoundedMemoryStream(
        MemoryStream stream,
        long maxTextArtifactBytes)
    {
        if (stream.Length > maxTextArtifactBytes)
        {
            return null;
        }

        if (stream.TryGetBuffer(out var segment))
        {
            return DecodeString(segment.AsSpan());
        }

        return DecodeString(stream.ToArray());
    }

    private async Task<DropCandidateMaterializationResult>
        MaterializeDeferredCandidateAsync(
            DropAcquisitionAnalysis analysis,
            DropImageCandidate candidate,
            CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            DropImageData imageData = candidate.DeferredPayload switch
            {
                DropDataUrlPayloadReference dataUrl =>
                    MaterializeDataUrl(analysis, dataUrl),
                DropBitmapPayloadReference bitmap =>
                    await MaterializeBitmapAsync(
                        bitmap.Bitmap,
                        cancellationToken).ConfigureAwait(false),
                _ => throw new InvalidOperationException(
                    "Unbekannter verzögerter Drop-Payload.")
            };

            var readResult = CreateReadResult(
                imageData,
                GetAcquisitionMethod(candidate.Kind),
                analysis,
                null,
                []);
            return new DropCandidateMaterializationResult(
                candidate,
                readResult,
                null);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (
            ex is FormatException or
            InvalidOperationException or
            NotSupportedException or
            ArgumentException or
            IOException)
        {
            var readResult = CreateReadResult(
                null,
                "CandidateRejected",
                analysis,
                null,
                []);
            return new DropCandidateMaterializationResult(
                candidate,
                readResult,
                ex.Message);
        }
    }

    private DropImageData MaterializeDataUrl(
        DropAcquisitionAnalysis analysis,
        DropDataUrlPayloadReference reference)
    {
        if (!analysis.ProcessingTextArtifacts.TryGetValue(
                reference.ArtifactName,
                out var artifact) ||
            reference.PayloadOffset < 0 ||
            reference.PayloadLength <= 0 ||
            reference.PayloadOffset > artifact.Length - reference.PayloadLength)
        {
            throw new FormatException(
                "Data-URL-Referenz ist nicht mehr gueltig.");
        }

        var payload = artifact.AsSpan(
            reference.PayloadOffset,
            reference.PayloadLength);
        var significantCharacters = 0;
        var padding = 0;
        for (var index = 0; index < payload.Length; index++)
        {
            var value = payload[index];
            if (char.IsWhiteSpace(value))
            {
                continue;
            }

            significantCharacters++;
            padding = value == '='
                ? Math.Min(2, padding + 1)
                : 0;
        }

        if (significantCharacters == 0 ||
            significantCharacters % 4 != 0)
        {
            throw new FormatException(
                "Data-URL enthaelt keinen gueltigen Base64-Payload.");
        }

        var decodedLength =
            (long)(significantCharacters / 4) * 3 - padding;
        if (decodedLength < 0 ||
            decodedLength > _safetyLimits.MaxEmbeddedDataUrlBytes)
        {
            throw new InvalidOperationException(
                $"Data-URL ueberschreitet das konfigurierte Safety-Limit von " +
                $"{_safetyLimits.MaxEmbeddedDataUrlBytes} Bytes.");
        }

        if (decodedLength > int.MaxValue)
        {
            throw new InvalidOperationException(
                "Data-URL ist fuer eine In-Memory-Materialisierung zu gross.");
        }

        var bytes = new byte[(int)decodedLength];
        if (!Convert.TryFromBase64Chars(
                payload,
                bytes,
                out var bytesWritten) ||
            bytesWritten != bytes.Length)
        {
            throw new FormatException(
                "Data-URL enthaelt keinen gueltigen Base64-Payload.");
        }

        return DropImageData.FromBytes(bytes, "Browser-data-URL");
    }

    private async Task<DropImageData> MaterializeBitmapAsync(
        BitmapSource bitmap,
        CancellationToken cancellationToken)
    {
        var pixelCount = checked(
            (long)bitmap.PixelWidth * bitmap.PixelHeight);
        if (pixelCount > _safetyLimits.MaxBitmapPixels)
        {
            throw new InvalidOperationException(
                $"Bitmap-Fallback ueberschreitet das konfigurierte Safety-Limit von " +
                $"{_safetyLimits.MaxBitmapPixels} Pixeln.");
        }

        var bytes = await RunStaWorkerAsync(
            () => EncodeBitmapAsPng(bitmap),
            cancellationToken).ConfigureAwait(false);
        return DropImageData.FromBytes(bytes, "Browser-Bitmap");
    }

    private static byte[] EncodeBitmapAsPng(BitmapSource bitmap)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));

        using var output = new MemoryStream();
        encoder.Save(output);
        return output.ToArray();
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
            Name = "CoverImageDrop.Acquisition.WicWorker"
        };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    private static bool IsDropDataAccessException(Exception exception)
    {
        return exception is COMException or
            ExternalException or
            InvalidOperationException or
            NotSupportedException or
            ArgumentException;
    }

    private sealed record DropDataSnapshot(
        string[] Formats,
        IReadOnlyDictionary<string, string> ProcessingTextArtifacts,
        string? FilePath,
        BitmapSource? Bitmap);

    private static DropSourceDetection DetectSource(IReadOnlyList<string> formats)
    {
        var reasons = new List<string>();
        var hasChromiumMarker = formats.Any(format =>
            format.StartsWith("Chromium ", StringComparison.OrdinalIgnoreCase) ||
            format.StartsWith("chromium/", StringComparison.OrdinalIgnoreCase));
        var hasMozillaMarker = formats.Any(format =>
            format.StartsWith("application/x-moz-", StringComparison.OrdinalIgnoreCase) ||
            format.StartsWith("text/_moz_", StringComparison.OrdinalIgnoreCase) ||
            format.Equals("text/x-moz-url", StringComparison.OrdinalIgnoreCase));

        if (hasChromiumMarker)
        {
            reasons.Add("Chromium-spezifisches Dropformat vorhanden.");
            if (hasMozillaMarker)
            {
                reasons.Add("Mozilla-kompatibles Format wird zusaetzlich angeboten; der Chromium-Marker hat Vorrang.");
            }

            return new DropSourceDetection(DropSourceKind.Chromium, DropSourceConfidence.High, reasons);
        }

        if (hasMozillaMarker)
        {
            reasons.Add("Mozilla-spezifisches Dropformat ohne Chromium-Marker vorhanden.");
            return new DropSourceDetection(DropSourceKind.Firefox, DropSourceConfidence.High, reasons);
        }

        if (formats.Any(format => format.Equals(DataFormats.FileDrop, StringComparison.OrdinalIgnoreCase)))
        {
            reasons.Add("Windows FileDrop vorhanden.");
            return new DropSourceDetection(DropSourceKind.WindowsExplorer, DropSourceConfidence.High, reasons);
        }

        if (formats.Any(format => format.Equals(DataFormats.Html, StringComparison.OrdinalIgnoreCase) ||
                                  format.Equals("HTML Format", StringComparison.OrdinalIgnoreCase) ||
                                  format.Equals("UniformResourceLocatorW", StringComparison.OrdinalIgnoreCase) ||
                                  format.Equals("UniformResourceLocator", StringComparison.OrdinalIgnoreCase)))
        {
            reasons.Add("Browser-typisches HTML-/URL-Format ohne eindeutige Browserfamilie vorhanden.");
            return new DropSourceDetection(DropSourceKind.Unknown, DropSourceConfidence.Medium, reasons);
        }

        reasons.Add("Keine eindeutigen Source-Marker gefunden.");
        return new DropSourceDetection(DropSourceKind.Unknown, DropSourceConfidence.Low, reasons);
    }

    private static Uri? TryFindSourceUri(IEnumerable<string> texts)
    {
        foreach (var text in texts)
        {
            var match = SourceUrlRegex().Match(text);
            if (match.Success && TryCreateHttpUri(match.Groups["url"].Value, out var sourceUri))
            {
                return sourceUri;
            }
        }

        return null;
    }

    private static List<DropUrlCandidate> FindUrlCandidates(
        IReadOnlyDictionary<string, string> textArtifacts,
        bool allowPageUrls,
        Uri? baseUri)
    {
        var candidates = new Dictionary<string, DropUrlCandidate>(StringComparer.OrdinalIgnoreCase);
        foreach (var artifact in textArtifacts)
        {
            var text = WebUtility.HtmlDecode(artifact.Value);
            AddQueryParameterCandidates(candidates, text, artifact.Key);
            AddMetaImageCandidates(candidates, text, artifact.Key, baseUri);
            AddLinkImageCandidates(candidates, text, artifact.Key, baseUri);
            AddImageAttributeCandidates(candidates, text, artifact.Key, baseUri);
            AddSrcSetCandidates(candidates, text, artifact.Key, baseUri);
            AddPlainUrlCandidates(candidates, text, artifact.Key, allowPageUrls);
        }

        return candidates.Values
            .OrderBy(candidate => candidate.Priority)
            .ThenByDescending(candidate => candidate.Width ?? 0)
            .ThenBy(candidate => candidate.Url.Length)
            .ToList();
    }

    private static void AddQueryParameterCandidates(Dictionary<string, DropUrlCandidate> candidates, string text, string artifactName)
    {
        foreach (Match match in UrlRegex().Matches(text))
        {
            if (!TryCreateHttpUri(match.Value, out var uri))
            {
                continue;
            }

            AddKnownImageQueryParameter(candidates, uri, "imgurl", artifactName, 10);
            AddKnownImageQueryParameter(candidates, uri, "mediaurl", artifactName, 10);
            AddKnownImageQueryParameter(candidates, uri, "cdnurl", artifactName, 15);
            AddKnownPageQueryParameter(candidates, uri, "imgrefurl", artifactName, 80);
            AddKnownPageQueryParameter(candidates, uri, "refurl", artifactName, 82);
        }
    }

    private static void AddKnownImageQueryParameter(
        Dictionary<string, DropUrlCandidate> candidates,
        Uri uri,
        string parameterName,
        string artifactName,
        int priority)
    {
        foreach (var value in GetQueryValues(uri, parameterName))
        {
            if (TryCreateHttpUri(value, out var candidateUri))
            {
                AddCandidate(candidates, candidateUri, $"{artifactName}:{parameterName}", priority, null, DropImageCandidateKind.HtmlDerivedUrl);
            }
        }
    }

    private static void AddKnownPageQueryParameter(
        Dictionary<string, DropUrlCandidate> candidates,
        Uri uri,
        string parameterName,
        string artifactName,
        int priority)
    {
        foreach (var value in GetQueryValues(uri, parameterName))
        {
            if (TryCreateHttpUri(value, out var candidateUri) &&
                LooksLikePageUrl(candidateUri))
            {
                AddCandidate(
                    candidates,
                    candidateUri,
                    $"{artifactName}:{parameterName}",
                    priority,
                    null,
                    DropImageCandidateKind.DirectUrl);
            }
        }
    }

    private static void AddImageAttributeCandidates(
        Dictionary<string, DropUrlCandidate> candidates,
        string text,
        string artifactName,
        Uri? baseUri)
    {
        foreach (Match match in ImageAttributeRegex().Matches(text))
        {
            if (TryCreateResourceUri(match.Groups["url"].Value, baseUri, out var uri))
            {
                AddCandidate(candidates, uri, $"{artifactName}:{match.Groups["name"].Value}", 20, TryGetWidth(uri), DropImageCandidateKind.HtmlDerivedUrl);
            }
        }
    }

    private static void AddMetaImageCandidates(
        Dictionary<string, DropUrlCandidate> candidates,
        string text,
        string artifactName,
        Uri? baseUri)
    {
        foreach (Match tagMatch in MetaTagRegex().Matches(text))
        {
            var attributes = ReadAttributes(tagMatch.Value);
            var name = GetAttribute(attributes, "property") ?? GetAttribute(attributes, "name");
            if (name is null ||
                !name.Equals("og:image", StringComparison.OrdinalIgnoreCase) &&
                !name.Equals("og:image:secure_url", StringComparison.OrdinalIgnoreCase) &&
                !name.Equals("twitter:image", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var content = GetAttribute(attributes, "content");
            if (content is not null && TryCreateResourceUri(content, baseUri, out var uri))
            {
                AddCandidate(candidates, uri, $"{artifactName}:{name}", 12, TryGetWidth(uri), DropImageCandidateKind.HtmlDerivedUrl);
            }
        }
    }

    private static void AddLinkImageCandidates(
        Dictionary<string, DropUrlCandidate> candidates,
        string text,
        string artifactName,
        Uri? baseUri)
    {
        foreach (Match tagMatch in LinkTagRegex().Matches(text))
        {
            var attributes = ReadAttributes(tagMatch.Value);
            var rel = GetAttribute(attributes, "rel");
            if (rel is null || !rel.Contains("image_src", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var href = GetAttribute(attributes, "href");
            if (href is not null && TryCreateResourceUri(href, baseUri, out var uri))
            {
                AddCandidate(candidates, uri, $"{artifactName}:image_src", 12, TryGetWidth(uri), DropImageCandidateKind.HtmlDerivedUrl);
            }
        }
    }

    private static void AddSrcSetCandidates(
        Dictionary<string, DropUrlCandidate> candidates,
        string text,
        string artifactName,
        Uri? baseUri)
    {
        foreach (Match match in SrcSetRegex().Matches(text))
        {
            foreach (var part in match.Groups["value"].Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var tokens = part.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (tokens.Length == 0 || !TryCreateResourceUri(tokens[0], baseUri, out var uri))
                {
                    continue;
                }

                var width = tokens.Skip(1)
                    .Select(token => token.EndsWith('w') && int.TryParse(token[..^1], out var parsed) ? parsed : (int?)null)
                    .FirstOrDefault(value => value is not null);
                AddCandidate(candidates, uri, $"{artifactName}:srcset", 25, width ?? TryGetWidth(uri), DropImageCandidateKind.HtmlDerivedUrl);
            }
        }
    }

    private static void AddPlainUrlCandidates(
        Dictionary<string, DropUrlCandidate> candidates,
        string text,
        string artifactName,
        bool allowPageUrls)
    {
        if (IsDiagnosticUrlArtifact(artifactName))
        {
            return;
        }

        foreach (Match match in UrlRegex().Matches(text))
        {
            if (!TryCreateHttpUri(match.Value, out var uri))
            {
                continue;
            }

            if (LooksLikeDirectImageUrl(uri))
            {
                AddCandidate(candidates, uri, artifactName, 30, TryGetWidth(uri), DropImageCandidateKind.DirectUrl);
            }
            else if (allowPageUrls && LooksLikePageUrl(uri))
            {
                AddCandidate(candidates, uri, artifactName, 90, null, DropImageCandidateKind.DirectUrl);
            }
        }
    }

    private static bool IsDiagnosticUrlArtifact(string artifactName)
    {
        return artifactName.Equals("chromium/x-renderer-taint", StringComparison.OrdinalIgnoreCase);
    }

    private static void AddCandidate(
        Dictionary<string, DropUrlCandidate> candidates,
        Uri uri,
        string source,
        int priority,
        int? width,
        DropImageCandidateKind kind)
    {
        var url = uri.ToString();
        if (candidates.TryGetValue(url, out var existing))
        {
            var newIdentityWins = priority < existing.Priority;
            var mergedWidth = existing.Width switch
            {
                null => width,
                _ when width is null => existing.Width,
                _ => Math.Max(existing.Width.Value, width.Value)
            };

            candidates[url] = new DropUrlCandidate(
                url,
                newIdentityWins ? source : existing.Source,
                Math.Min(existing.Priority, priority),
                mergedWidth,
                newIdentityWins ? kind : existing.Kind);
            return;
        }

        candidates[url] = new DropUrlCandidate(url, source, priority, width, kind);
    }

    private static IReadOnlyList<DropUrlCandidate> RankHtmlFallbackCandidates(
        IReadOnlyList<DropUrlCandidate> htmlCandidates,
        IReadOnlyList<DropUrlCandidate> topLevelCandidates,
        DropImageVariantReference? variantReference)
    {
        var candidates = htmlCandidates
            .Where(candidate => !topLevelCandidates.Any(existing =>
                existing.Url.Equals(candidate.Url, StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        if (variantReference is null)
        {
            return candidates;
        }

        if (!TryCreateHttpUri(variantReference.Url, out var referenceUri))
        {
            return [];
        }

        var referenceFamily = GetImageFamilyKey(referenceUri);
        if (string.IsNullOrWhiteSpace(referenceFamily))
        {
            return [];
        }

        return candidates
            .Where(candidate => IsSameImageFamily(candidate.Url, referenceFamily))
            .Where(candidate => !TryGetUrlDimensions(candidate.Url, out var width, out var height) ||
                                variantReference.IsGeometryCompatible(width, height))
            .OrderByDescending(GetEstimatedVariantWidth)
            .ThenBy(candidate => GetVariantAspectDistance(candidate, variantReference))
            .ThenBy(candidate => candidate.Priority)
            .ThenBy(candidate => candidate.Url.Length)
            .ToArray();
    }

    private static bool IsSameImageFamily(string candidateUrl, string referenceFamily)
    {
        return TryCreateHttpUri(candidateUrl, out var candidateUri) &&
               GetImageFamilyKey(candidateUri)
                   .Equals(referenceFamily, StringComparison.OrdinalIgnoreCase);
    }

    private static string GetImageFamilyKey(Uri uri)
    {
        var fileName = Path.GetFileNameWithoutExtension(
            Uri.UnescapeDataString(uri.AbsolutePath));
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return string.Empty;
        }

        var withoutDimensions = ImageDimensionTokenRegex()
            .Replace(fileName, string.Empty);
        var normalized = RepeatedImageSeparatorRegex()
            .Replace(withoutDimensions, "-")
            .Trim('-', '_', '.');

        return normalized.Length >= 6
            ? normalized.ToLowerInvariant()
            : string.Empty;
    }

    private static int GetEstimatedVariantWidth(DropUrlCandidate candidate)
    {
        if (candidate.Width is > 0)
        {
            return candidate.Width.Value;
        }

        return TryGetUrlDimensions(candidate.Url, out var width, out _)
            ? width
            : 0;
    }

    private static double GetVariantAspectDistance(
        DropUrlCandidate candidate,
        DropImageVariantReference variantReference)
    {
        if (!TryGetUrlDimensions(candidate.Url, out var width, out var height))
        {
            return double.MaxValue;
        }

        var referenceAspect = (double)variantReference.PixelWidth / variantReference.PixelHeight;
        var candidateAspect = (double)width / height;
        return Math.Abs(Math.Log(candidateAspect / referenceAspect));
    }

    private static bool TryGetUrlDimensions(
        string url,
        out int width,
        out int height)
    {
        width = 0;
        height = 0;
        if (!TryCreateHttpUri(url, out var uri))
        {
            return false;
        }

        var matches = ImageDimensionRegex().Matches(
            Uri.UnescapeDataString(uri.AbsolutePath));
        if (matches.Count == 0)
        {
            return false;
        }

        var match = matches[^1];
        return int.TryParse(match.Groups["width"].Value, out width) &&
               int.TryParse(match.Groups["height"].Value, out height) &&
               width > 0 &&
               height > 0;
    }

    private async Task<CandidateDownloadResult> DownloadSingleUsableCandidateAsync(
        DropUrlCandidate candidate,
        IReadOnlyList<DropUrlCandidate> topLevelCandidates,
        DropImageVariantReference? variantReference,
        CancellationToken cancellationToken,
        long? maxDownloadBytes)
    {
        Exception? lastException = null;
        var attempts = new List<DropDownloadAttempt>();

        try
        {
            var download = await DownloadAsync(
                new Uri(candidate.Url),
                cancellationToken,
                maxDownloadBytes).ConfigureAwait(false);
            if (download.Info.LooksLikeHtml)
            {
                attempts.Add(ToAttempt(download.Info, accepted: false, error: "HTML-Fallback."));
                string htmlText;
                try
                {
                    htmlText = await ReadDownloadedTextAsync(download.SourceData, cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    download.SourceData.Dispose();
                }

                var htmlBaseUri = TryCreateHttpUri(download.Info.FinalUrl ?? candidate.Url, out var resolvedBaseUri)
                    ? resolvedBaseUri
                    : null;
                var htmlCandidates = FindUrlCandidates(
                    new Dictionary<string, string> { ["downloaded-html"] = htmlText },
                    allowPageUrls: false,
                    htmlBaseUri);
                var fallbackCandidates = RankHtmlFallbackCandidates(
                    htmlCandidates,
                    topLevelCandidates,
                    variantReference);
                if (variantReference is not null && fallbackCandidates.Count == 0)
                {
                    throw new DropDownloadException(
                        "Die Artikelseite enthaelt keine ausreichend verwandte Bildvariante mit passender Geometrie.",
                        null,
                        attempts);
                }

                foreach (var htmlCandidate in fallbackCandidates.Take(10))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    try
                    {
                        var nestedDownload = await DownloadAsync(
                            new Uri(htmlCandidate.Url),
                            cancellationToken,
                            maxDownloadBytes).ConfigureAwait(false);
                        if (nestedDownload.Info.LooksLikeHtml)
                        {
                            attempts.Add(ToAttempt(
                                nestedDownload.Info,
                                accepted: false,
                                error: "HTML statt Bild."));
                            nestedDownload.SourceData.Dispose();
                            continue;
                        }

                        if (!nestedDownload.HasSupportedImageSignature)
                        {
                            attempts.Add(ToAttempt(
                                nestedDownload.Info,
                                accepted: false,
                                error: "Kein unterstuetzter Bildpayload."));
                            nestedDownload.SourceData.Dispose();
                            continue;
                        }

                        attempts.Add(ToAttempt(nestedDownload.Info, accepted: true, error: null));
                        return nestedDownload with { Attempts = attempts };
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or IOException or TimeoutException or TaskCanceledException)
                    {
                        lastException = ex;
                        attempts.Add(new DropDownloadAttempt(
                            htmlCandidate.Url,
                            null,
                            null,
                            null,
                            false,
                            false,
                            ex.Message));
                    }
                }

                throw new DropDownloadException(
                    "Der URL-Kandidat lieferte kein brauchbares Bild.",
                    lastException,
                    attempts);
            }

            if (!download.HasSupportedImageSignature)
            {
                attempts.Add(ToAttempt(
                    download.Info,
                    accepted: false,
                    error: "Kein unterstuetzter Bildpayload."));
                download.SourceData.Dispose();
                throw new DropDownloadException(
                    "Der URL-Kandidat lieferte keinen unterstuetzten Bildpayload.",
                    null,
                    attempts);
            }

            attempts.Add(ToAttempt(download.Info, accepted: true, error: null));
            return download with { Attempts = attempts };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (DropDownloadException)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or IOException or TimeoutException or TaskCanceledException)
        {
            attempts.Add(new DropDownloadAttempt(
                candidate.Url,
                null,
                null,
                null,
                false,
                false,
                ex.Message));
            throw new DropDownloadException(
                "Der URL-Kandidat konnte nicht materialisiert werden.",
                ex,
                attempts);
        }
    }

    private async Task<CandidateDownloadResult> DownloadFirstUsableCandidateAsync(
        IReadOnlyList<DropUrlCandidate> candidates,
        CancellationToken cancellationToken)
    {
        Exception? lastException = null;
        var attempts = new List<DropDownloadAttempt>();

        foreach (var candidate in candidates.Take(20))
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var download = await DownloadSingleUsableCandidateAsync(
                    candidate,
                    candidates,
                    variantReference: null,
                    cancellationToken: cancellationToken,
                    maxDownloadBytes: _safetyLimits.MaxRemoteDownloadBytes).ConfigureAwait(false);
                attempts.AddRange(download.Attempts);
                return download with { Attempts = attempts };
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (DropDownloadException ex)
            {
                lastException = ex.InnerException ?? ex;
                attempts.AddRange(ex.Attempts);
            }
        }

        throw new DropDownloadException(
            "Keine Bild-URL aus den Drop-Kandidaten konnte geladen werden.",
            lastException,
            attempts);
    }

    private async Task<CandidateDownloadResult> DownloadAsync(
        Uri uri,
        CancellationToken cancellationToken,
        long? maxDownloadBytes)
    {
        if (!IsHttpUri(uri))
        {
            throw new InvalidOperationException("Nur HTTP- und HTTPS-Downloads sind erlaubt.");
        }

        if (maxDownloadBytes is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxDownloadBytes));
        }

        var requestedUri = uri;
        var currentUri = uri;
        var redirectCount = 0;
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(_requestTimeout);

        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var request = new HttpRequestMessage(HttpMethod.Get, currentUri);
                using var response = await _httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    timeoutCts.Token).ConfigureAwait(false);

                if (IsRedirectStatusCode(response.StatusCode))
                {
                    if (redirectCount >= MaxRedirects)
                    {
                        throw new InvalidOperationException($"Redirectlimit von {MaxRedirects} wurde ueberschritten.");
                    }

                    var location = response.Headers.Location
                        ?? throw new InvalidOperationException("Redirect ohne Location-Header.");
                    Uri nextUri;
                    try
                    {
                        nextUri = location.IsAbsoluteUri
                            ? location
                            : new Uri(currentUri, location);
                    }
                    catch (UriFormatException ex)
                    {
                        throw new InvalidOperationException("Redirect enthaelt eine ungueltige Location.", ex);
                    }

                    if (!IsHttpUri(nextUri))
                    {
                        throw new InvalidOperationException("Redirect auf ein nicht erlaubtes URL-Schema.");
                    }

                    currentUri = nextUri;
                    redirectCount++;
                    continue;
                }

                response.EnsureSuccessStatusCode();
                var contentLength = response.Content.Headers.ContentLength;
                var contentType = response.Content.Headers.ContentType?.MediaType;
                if (maxDownloadBytes is long declaredLimit &&
                    contentLength is long declaredLength &&
                    declaredLength > declaredLimit)
                {
                    throw new InvalidOperationException(
                        $"Download ueberschreitet das konfigurierte Limit von {declaredLimit} Bytes " +
                        $"(Content-Length: {declaredLength} Bytes).");
                }

                var sourceName = currentUri.Host;
                var tempPath = CreateDownloadTemporaryPath();
                var ownershipTransferred = false;

                try
                {
                    await using var input = await response.Content.ReadAsStreamAsync(timeoutCts.Token).ConfigureAwait(false);
                    await using var output = new FileStream(
                        tempPath,
                        FileMode.CreateNew,
                        FileAccess.Write,
                        FileShare.None,
                        bufferSize: 81920,
                        FileOptions.Asynchronous | FileOptions.SequentialScan);
                    var buffer = new byte[81920];
                    var prefix = new byte[512];
                    var prefixLength = 0;
                    long totalBytes = 0;

                    while (true)
                    {
                        var read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), timeoutCts.Token).ConfigureAwait(false);
                        if (read == 0)
                        {
                            break;
                        }

                        if (maxDownloadBytes is long streamingLimit &&
                            read > streamingLimit - totalBytes)
                        {
                            throw new InvalidOperationException(
                                $"Download ueberschreitet das konfigurierte Limit von {streamingLimit} Bytes.");
                        }

                        if (prefixLength < prefix.Length)
                        {
                            var prefixBytes = Math.Min(read, prefix.Length - prefixLength);
                            buffer.AsSpan(0, prefixBytes).CopyTo(prefix.AsSpan(prefixLength));
                            prefixLength += prefixBytes;
                        }

                        await output.WriteAsync(buffer.AsMemory(0, read), timeoutCts.Token).ConfigureAwait(false);
                        totalBytes += read;
                    }

                    await output.FlushAsync(timeoutCts.Token).ConfigureAwait(false);
                    var prefixBytesForInspection = prefix.AsSpan(0, prefixLength).ToArray();
                    var hasSupportedImageSignature = HasSupportedImageSignature(prefixBytesForInspection);
                    var looksLikeHtml = LooksLikeHtml(prefixBytesForInspection, contentType);
                    var info = new DropDownloadInfo(
                        requestedUri.ToString(),
                        currentUri.ToString(),
                        contentType,
                        contentLength,
                        looksLikeHtml);
                    var sourceData = DropImageData.FromTemporaryFile(
                        tempPath,
                        sourceName,
                        currentUri.ToString(),
                        contentType);
                    ownershipTransferred = true;
                    return new CandidateDownloadResult(
                        sourceData,
                        sourceName,
                        info,
                        hasSupportedImageSignature,
                        []);
                }
                finally
                {
                    if (!ownershipTransferred)
                    {
                        TryDeleteDownloadTemporaryFile(tempPath);
                    }
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException ex)
        {
            throw new TimeoutException($"Der Download hat das Zeitlimit von {_requestTimeout.TotalSeconds:0.###} Sekunden ueberschritten.", ex);
        }
    }

    private static async Task<string> ReadDownloadedTextAsync(
        DropImageData sourceData,
        CancellationToken cancellationToken)
    {
        await using var stream = await sourceData.OpenReadAsync(cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(
            stream,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true,
            bufferSize: 8192,
            leaveOpen: false);
        var builder = new StringBuilder(capacity: 8192);
        var buffer = new char[8192];

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var remaining = MaxDownloadedHtmlCharacters - builder.Length;
            var readLength = Math.Min(buffer.Length, remaining + 1);
            var read = await reader.ReadAsync(
                buffer.AsMemory(0, readLength),
                cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return builder.ToString();
            }

            if (read > remaining)
            {
                throw new InvalidOperationException(
                    $"HTML-Fallback ueberschreitet das Verarbeitungslimit von " +
                    $"{MaxDownloadedHtmlCharacters} Zeichen.");
            }

            builder.Append(buffer, 0, read);
        }
    }

    private static string CreateDownloadTemporaryPath()
    {
        var folder = Path.Combine(Path.GetTempPath(), "CoverImageDrop", "Acquisition");
        Directory.CreateDirectory(folder);
        return Path.Combine(folder, $"download-{Guid.NewGuid():N}.tmp");
    }

    private static void TryDeleteDownloadTemporaryFile(string tempPath)
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

    private static bool IsRedirectStatusCode(HttpStatusCode statusCode)
    {
        var code = (int)statusCode;
        return code is 301 or 302 or 303 or 307 or 308;
    }

    private static bool HasSupportedImageSignature(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        return HasPrefix(bytes, 0xFF, 0xD8, 0xFF) ||
               HasPrefix(bytes, 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A) ||
               HasAsciiPrefix(bytes, "GIF87a") ||
               HasAsciiPrefix(bytes, "GIF89a") ||
               HasAsciiPrefix(bytes, "BM") ||
               HasPrefix(bytes, 0x49, 0x49, 0x2A, 0x00) ||
               HasPrefix(bytes, 0x4D, 0x4D, 0x00, 0x2A) ||
               HasPrefix(bytes, 0x00, 0x00, 0x01, 0x00) ||
               IsWebP(bytes);
    }

    private static bool HasPrefix(byte[] bytes, params byte[] prefix)
    {
        if (bytes.Length < prefix.Length)
        {
            return false;
        }

        for (var index = 0; index < prefix.Length; index++)
        {
            if (bytes[index] != prefix[index])
            {
                return false;
            }
        }

        return true;
    }

    private static bool HasAsciiPrefix(byte[] bytes, string prefix)
    {
        if (bytes.Length < prefix.Length)
        {
            return false;
        }

        for (var index = 0; index < prefix.Length; index++)
        {
            if (bytes[index] != (byte)prefix[index])
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsWebP(byte[] bytes)
    {
        return bytes.Length >= 12 &&
               HasAsciiAt(bytes, 0, "RIFF") &&
               HasAsciiAt(bytes, 8, "WEBP");
    }

    private static bool HasAsciiAt(byte[] bytes, int offset, string value)
    {
        if (offset < 0 || bytes.Length < offset + value.Length)
        {
            return false;
        }

        for (var index = 0; index < value.Length; index++)
        {
            if (bytes[offset + index] != (byte)value[index])
            {
                return false;
            }
        }

        return true;
    }

    private static DropReadResult CreateReadResult(
        DropImageData? imageData,
        string acquisitionMethod,
        DropAcquisitionAnalysis analysis,
        DropDownloadInfo? downloadInfo,
        IReadOnlyList<DropDownloadAttempt> downloadAttempts)
    {
        var diagnosticCandidates = analysis.Candidates
            .Select(candidate => candidate.DeferredPayload is null
                ? candidate
                : candidate with { DeferredPayload = null })
            .ToArray();

        return new DropReadResult(
            imageData,
            acquisitionMethod,
            analysis.SourceDetection.Kind == DropSourceKind.Unknown
                ? null
                : analysis.SourceDetection.DisplayName,
            analysis.OfferedFormats,
            analysis.DiagnosticTextArtifacts,
            analysis.UrlCandidates,
            downloadInfo,
            downloadAttempts)
        {
            SourceDetection = analysis.SourceDetection,
            Candidates = diagnosticCandidates,
            DiagnosticTextArtifacts = analysis.DiagnosticTextArtifacts
        };
    }

    private static string GetAcquisitionMethod(DropImageCandidateKind kind)
    {
        return kind switch
        {
            DropImageCandidateKind.LocalFile => "FileDrop",
            DropImageCandidateKind.EncodedDataUrl => "HtmlDataUrl",
            DropImageCandidateKind.BitmapFallback => "Bitmap",
            _ => "Candidate"
        };
    }

    private static string DecodeString(ReadOnlySpan<byte> bytes)
    {
        return bytes.Length >= 2 && bytes[1] == 0
            ? Encoding.Unicode.GetString(bytes).TrimEnd('\0')
            : Encoding.UTF8.GetString(bytes).TrimEnd('\0');
    }

    private static bool TryCreateResourceUri(string value, Uri? baseUri, out Uri uri)
    {
        value = WebUtility.HtmlDecode(value).Trim().Trim('"', '\'', '<', '>', ')');
        if (TryCreateHttpUri(value, out uri))
        {
            return true;
        }

        if (value.StartsWith("//", StringComparison.Ordinal))
        {
            if (baseUri is not null &&
                Uri.TryCreate(baseUri, value, out var protocolRelativeResolved) &&
                IsHttpUri(protocolRelativeResolved))
            {
                uri = protocolRelativeResolved;
                return true;
            }

            if (TryCreateHttpUri($"https:{value}", out uri))
            {
                return true;
            }
        }

        if (baseUri is not null && Uri.TryCreate(baseUri, value, out var resolved) && IsHttpUri(resolved))
        {
            uri = resolved;
            return true;
        }

        uri = null!;
        return false;
    }

    private static bool TryCreateHttpUri(string value, out Uri uri)
    {
        value = WebUtility.HtmlDecode(value).Trim().Trim('"', '\'', '<', '>', ')');
        return Uri.TryCreate(value, UriKind.Absolute, out uri!) && IsHttpUri(uri);
    }

    private static bool IsHttpUri(Uri uri)
    {
        return (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp) &&
               string.IsNullOrEmpty(uri.UserInfo);
    }

    private static IEnumerable<string> GetQueryValues(Uri uri, string parameterName)
    {
        var query = uri.Query.TrimStart('?');
        foreach (var part in query.Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var pieces = part.Split('=', 2);
            if (pieces.Length != 2 || !pieces[0].Equals(parameterName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            yield return Uri.UnescapeDataString(pieces[1].Replace('+', ' '));
        }
    }

    private static bool LooksLikeDirectImageUrl(Uri uri)
    {
        var path = uri.AbsolutePath;
        return path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".webp", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".gif", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".tif", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".tiff", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".ico", StringComparison.OrdinalIgnoreCase) ||
               uri.Host.Contains("gstatic.com", StringComparison.OrdinalIgnoreCase) ||
               uri.Host.Contains("bing.com", StringComparison.OrdinalIgnoreCase) && path.Contains("/th/", StringComparison.OrdinalIgnoreCase);
    }

    private static int? TryGetWidth(Uri uri)
    {
        foreach (var name in new[] { "w", "width" })
        {
            foreach (var value in GetQueryValues(uri, name))
            {
                if (int.TryParse(value, out var width))
                {
                    return width;
                }
            }
        }

        return null;
    }

    private static bool LooksLikeHtml(byte[] bytes, string? contentType)
    {
        if (HasSupportedImageSignature(bytes))
        {
            return false;
        }

        var text = Encoding.UTF8.GetString(bytes.Take(256).ToArray())
            .TrimStart('\uFEFF', ' ', '\t', '\r', '\n');
        if (text.StartsWith("<!doctype", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("<html", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("<head", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("<body", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return contentType?.Contains("html", StringComparison.OrdinalIgnoreCase) == true &&
               text.StartsWith("<", StringComparison.Ordinal);
    }

    private static DropDownloadAttempt ToAttempt(DropDownloadInfo info, bool accepted, string? error)
    {
        return new DropDownloadAttempt(info.RequestedUrl, info.FinalUrl, info.ContentType, info.ContentLength, info.LooksLikeHtml, accepted, error);
    }

    private static Dictionary<string, string> ReadAttributes(string tag)
    {
        var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in AttributeRegex().Matches(tag))
        {
            attributes[match.Groups["name"].Value] = WebUtility.HtmlDecode(match.Groups["value"].Value);
        }

        return attributes;
    }

    private static string? GetAttribute(IReadOnlyDictionary<string, string> attributes, string name)
    {
        return attributes.TryGetValue(name, out var value) ? value : null;
    }

    private static bool LooksLikePageUrl(Uri uri)
    {
        if (IsClearlyNonImageAsset(uri))
        {
            return false;
        }

        var path = uri.AbsolutePath;
        return string.IsNullOrEmpty(path) ||
               path == "/" ||
               !Path.GetFileName(path).Contains('.', StringComparison.Ordinal);
    }

    private static bool IsClearlyNonImageAsset(Uri uri)
    {
        var path = uri.AbsolutePath;
        return path.EndsWith(".js", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".css", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".woff", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".woff2", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".otf", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".svg", StringComparison.OrdinalIgnoreCase);
    }

    private sealed record CandidateDownloadResult(
        DropImageData SourceData,
        string SourceName,
        DropDownloadInfo Info,
        bool HasSupportedImageSignature,
        IReadOnlyList<DropDownloadAttempt> Attempts);

    [GeneratedRegex("data:image/[^;]+;base64,(?<payload>[A-Za-z0-9+/=\\r\\n]+)", RegexOptions.IgnoreCase)]
    private static partial Regex DataImageRegex();

    [GeneratedRegex("https?://[^\\s\\\"'<>)]*", RegexOptions.IgnoreCase)]
    private static partial Regex UrlRegex();

    [GeneratedRegex("(?im)^SourceURL:(?<url>https?://[^\\r\\n]+)")]
    private static partial Regex SourceUrlRegex();

    [GeneratedRegex("\\b(?<name>src|data-src|data-src-hq|data-lazy-src|data-original)=[\\\"'](?<url>[^\\\"']+)[\\\"']", RegexOptions.IgnoreCase)]
    private static partial Regex ImageAttributeRegex();

    [GeneratedRegex("\\bsrcset=[\\\"'](?<value>[^\\\"']+)[\\\"']", RegexOptions.IgnoreCase)]
    private static partial Regex SrcSetRegex();

    [GeneratedRegex("[-_]\\d{2,5}x\\d{2,5}(?=[-_.]|$)", RegexOptions.IgnoreCase)]
    private static partial Regex ImageDimensionTokenRegex();

    [GeneratedRegex("[-_]{2,}")]
    private static partial Regex RepeatedImageSeparatorRegex();

    [GeneratedRegex("(?<width>\\d{2,5})x(?<height>\\d{2,5})", RegexOptions.IgnoreCase)]
    private static partial Regex ImageDimensionRegex();

    [GeneratedRegex("<meta\\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex MetaTagRegex();

    [GeneratedRegex("<link\\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex LinkTagRegex();

    [GeneratedRegex("(?<name>[A-Za-z_:][-A-Za-z0-9_:.]*)\\s*=\\s*[\\\"'](?<value>[^\\\"']*)[\\\"']", RegexOptions.IgnoreCase)]
    private static partial Regex AttributeRegex();
}
