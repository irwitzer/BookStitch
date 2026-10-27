using CoverImageDrop.Acquisition;
using CoverImageDrop.Core.Inspection;
using CoverImageDrop.Core.Issues;
using CoverImageDrop.Core.Normalization;
using CoverImageDrop.Core.Output;
using CoverImageDrop.Core.Validation;
using CoverImageDrop.Diagnostics;
using CoverImageDrop.Pipeline.PassThrough;

namespace CoverImageDrop.Pipeline;

public sealed class CoverImagePipelineService
{
    private readonly CoverImagePipelineOptions _options;
    private readonly CoverImageInspector _inspector;
    private readonly StandardCoverValidator _validator;
    private readonly CoverImageNormalizer _normalizer;
    private readonly CoverImagePassThroughProcessor _passThroughProcessor;
    private readonly DropImageDataReader _acquisitionReader;

    public CoverImagePipelineService(
        CoverImagePipelineOptions? options = null)
    {
        _options = ResolveAndValidateOptions(options);
        _inspector = new CoverImageInspector();
        _validator = new StandardCoverValidator(_options.Policy);
        _normalizer = new CoverImageNormalizer(_options.Policy);
        _passThroughProcessor = new CoverImagePassThroughProcessor();
        _acquisitionReader = new DropImageDataReader(
            _options.AcquisitionSafetyLimits);
    }

    internal CoverImagePipelineService(
        CoverImagePipelineOptions? options,
        DropImageDataReader acquisitionReader)
    {
        ArgumentNullException.ThrowIfNull(acquisitionReader);

        _options = ResolveAndValidateOptions(options);
        _inspector = new CoverImageInspector();
        _validator = new StandardCoverValidator(_options.Policy);
        _normalizer = new CoverImageNormalizer(_options.Policy);
        _passThroughProcessor = new CoverImagePassThroughProcessor();
        _acquisitionReader = acquisitionReader;
    }

    private static CoverImagePipelineOptions ResolveAndValidateOptions(
        CoverImagePipelineOptions? options)
    {
        var resolved = options ?? new CoverImagePipelineOptions();
        ArgumentNullException.ThrowIfNull(resolved.Policy);
        resolved.Policy.Validate();
        return resolved;
    }

    public async Task<CoverImagePassThroughResult> ProcessPassThroughAsync(
        DropReadResult readResult,
        CoverImagePassThroughOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(readResult);
        cancellationToken.ThrowIfCancellationRequested();

        var activeOptions = options ?? new CoverImagePassThroughOptions();
        var result = await _passThroughProcessor.ProcessAsync(
            readResult,
            activeOptions,
            cancellationToken).ConfigureAwait(false);
        var context = CreatePassThroughDiagnosticContext(
            readResult,
            activeOptions,
            result);
        var debugReport = BuildDebugReport(
            readResult,
            context: context);

        string? diagnosticsFolder = null;
        if (_options.Diagnostics.Enabled &&
            !string.IsNullOrWhiteSpace(
                _options.Diagnostics.RootFolder))
        {
            var error = result.IsSuccess
                ? null
                : FormatIssuesForDiagnostics(result.Issues);
            diagnosticsFolder = await SaveDiagnosticsAsync(
                readResult,
                null,
                null,
                error,
                cancellationToken,
                context).ConfigureAwait(false);
        }

        return result with
        {
            DiagnosticsFolder = diagnosticsFolder,
            DebugReport = debugReport
        };
    }

    public Task<CoverImagePassThroughResult> ProcessPassThroughAsync(
        DropImageData imageData,
        CoverImagePassThroughOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(imageData);

        var readResult = new DropReadResult(
            imageData,
            "DirectImageData",
            null,
            [],
            new Dictionary<string, string>(),
            [],
            null,
            []);
        return ProcessPassThroughAsync(
            readResult,
            options,
            cancellationToken);
    }

    public Task<CoverImagePassThroughResult> ProcessPassThroughFileAsync(
        string filePath,
        CoverImagePassThroughOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        cancellationToken.ThrowIfCancellationRequested();

        var imageData = DropImageData.FromFile(filePath);
        return ProcessPassThroughAsync(
            CreateFileReadResult(imageData),
            options,
            cancellationToken);
    }

    public async Task<CoverImagePipelineLoadResult> LoadStandardCoverAsync(
        DropAcquisitionAnalysis analysis,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(analysis);
        cancellationToken.ThrowIfCancellationRequested();

        var candidateAttempts = new List<CoverImageCandidateAttempt>();
        CoverImagePipelineLoadResult? bestFailure = null;
        DropImageVariantReference? variantReference = null;
        var bestFailureScore = int.MinValue;
        var rankedCandidates = analysis.Candidates
            .OrderBy(candidate => candidate.Priority)
            .ThenByDescending(candidate => candidate.Width ?? 0)
            .ToArray();

        for (var index = 0; index < rankedCandidates.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidate = rankedCandidates[index];
            var materialized = await _acquisitionReader.MaterializeCandidateAsync(
                analysis,
                candidate,
                variantReference,
                cancellationToken: cancellationToken,
                maxDownloadBytes: _options.Policy.MaxInputBytes).ConfigureAwait(false);

            if (!materialized.IsSuccess || materialized.ImageData is null)
            {
                candidateAttempts.Add(new CoverImageCandidateAttempt(
                    index,
                    candidate.Kind,
                    candidate.Source,
                    candidate.Priority,
                    candidate.Width,
                    candidate.Url,
                    false,
                    false,
                    null,
                    null,
                    [],
                    materialized.ReadResult.DownloadAttempts,
                    materialized.FailureReason));
                continue;
            }

            var imageData = materialized.ImageData;
            CoverImagePipelineLoadResult candidateResult;
            try
            {
                candidateResult = await LoadCoreAsync(
                    materialized.ReadResult,
                    imageData,
                    cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                imageData.Dispose();
                throw;
            }

            if (candidateResult.IsSuccess &&
                variantReference is not null &&
                ShouldApplyVariantGeometry(candidate) &&
                candidateResult.Inspection is not null &&
                !variantReference.IsGeometryCompatible(
                    candidateResult.Inspection.OrientedPixelWidth,
                    candidateResult.Inspection.OrientedPixelHeight))
            {
                candidateAttempts.Add(new CoverImageCandidateAttempt(
                    index,
                    candidate.Kind,
                    candidate.Source,
                    candidate.Priority,
                    candidate.Width,
                    candidate.Url,
                    true,
                    false,
                    candidateResult.Inspection,
                    candidateResult.Validation,
                    candidateResult.Issues,
                    materialized.ReadResult.DownloadAttempts,
                    "Bildvariante weicht in Ausrichtung oder Seitenverhaeltnis zu stark vom gezogenen Bild ab."));

                await candidateResult.Session!.DisposeAsync().ConfigureAwait(false);
                continue;
            }

            if (candidateResult.IsSuccess)
            {
                candidateAttempts.Add(new CoverImageCandidateAttempt(
                    index,
                    candidate.Kind,
                    candidate.Source,
                    candidate.Priority,
                    candidate.Width,
                    candidate.Url,
                    true,
                    true,
                    candidateResult.Inspection,
                    candidateResult.Validation,
                    candidateResult.Issues,
                    materialized.ReadResult.DownloadAttempts,
                    null));

                candidateResult.Session!.SetAcquisitionDiagnostics(
                    analysis.SourceDetection,
                    candidateAttempts);

                return candidateResult with
                {
                    SourceDetection = analysis.SourceDetection,
                    CandidateAttempts = candidateAttempts.ToArray()
                };
            }

            candidateAttempts.Add(new CoverImageCandidateAttempt(
                index,
                candidate.Kind,
                candidate.Source,
                candidate.Priority,
                candidate.Width,
                candidate.Url,
                true,
                false,
                candidateResult.Inspection,
                candidateResult.Validation,
                candidateResult.Issues,
                materialized.ReadResult.DownloadAttempts,
                FormatIssuesForDiagnostics(candidateResult.Issues)));

            if (variantReference is null &&
                candidate.Kind == DropImageCandidateKind.HtmlDerivedUrl &&
                !string.IsNullOrWhiteSpace(candidate.Url) &&
                candidateResult.Inspection is not null &&
                candidateResult.Issues.Any(issue =>
                    issue.Code == CoverImageIssueCode.ImageTooSmall))
            {
                variantReference = new DropImageVariantReference(
                    candidate.Url,
                    candidateResult.Inspection.OrientedPixelWidth,
                    candidateResult.Inspection.OrientedPixelHeight);
            }

            imageData.Dispose();

            var score = ScoreCandidateFailure(candidateResult);
            if (score > bestFailureScore)
            {
                bestFailure = candidateResult;
                bestFailureScore = score;
            }
        }

        var candidateAttemptSnapshot = candidateAttempts.ToArray();
        if (bestFailure is not null)
        {
            var diagnosticsFolder = await SaveCandidateSweepDiagnosticsAsync(
                analysis,
                candidateAttemptSnapshot,
                bestFailure,
                bestFailure.Issues,
                cancellationToken).ConfigureAwait(false);
            return bestFailure with
            {
                DiagnosticsFolder = diagnosticsFolder,
                SourceDetection = analysis.SourceDetection,
                CandidateAttempts = candidateAttemptSnapshot
            };
        }

        var noPayloadIssue = new CoverImageIssue(
            CoverImageIssueCode.NoImagePayloadFound,
            CoverImageIssueSeverity.Error,
            new Dictionary<string, object?>
            {
                ["candidateCount"] = rankedCandidates.Length
            });
        var noPayloadIssues = new[] { noPayloadIssue };
        var noPayloadDiagnosticsFolder = await SaveCandidateSweepDiagnosticsAsync(
            analysis,
            candidateAttemptSnapshot,
            null,
            noPayloadIssues,
            cancellationToken).ConfigureAwait(false);
        return CoverImagePipelineLoadResult.Failed(
            null,
            null,
            noPayloadIssues,
            noPayloadDiagnosticsFolder) with
        {
            SourceDetection = analysis.SourceDetection,
            CandidateAttempts = candidateAttemptSnapshot
        };
    }

    private static bool ShouldApplyVariantGeometry(DropImageCandidate candidate)
    {
        return candidate.Kind is
            DropImageCandidateKind.DirectUrl or
            DropImageCandidateKind.HtmlDerivedUrl;
    }

    public Task<CoverImagePipelineLoadResult> LoadAsync(
        DropReadResult readResult,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(readResult);

        return LoadCoreAsync(readResult, readResult.ImageData, cancellationToken);
    }

    public Task<CoverImagePipelineLoadResult> LoadAsync(
        DropImageData imageData,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(imageData);

        var readResult = new DropReadResult(
            imageData,
            "DirectImageData",
            null,
            [],
            new Dictionary<string, string>(),
            [],
            null,
            []);
        return LoadCoreAsync(readResult, imageData, cancellationToken);
    }

    public async Task<CoverImagePipelineLoadResult> LoadFileAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        cancellationToken.ThrowIfCancellationRequested();

        var fileInfo = new FileInfo(filePath);
        if (!fileInfo.Exists)
        {
            var issue = new CoverImageIssue(
                CoverImageIssueCode.NoImagePayloadFound,
                CoverImageIssueSeverity.Error,
                new Dictionary<string, object?> { ["filePath"] = filePath },
                "Source file was not found.");
            return await CreateFileFailureAsync(filePath, issue, cancellationToken).ConfigureAwait(false);
        }

        var inputLengthIssue = _validator.ValidateInputLength(fileInfo.Length);
        if (inputLengthIssue is not null)
        {
            return await CreateFileFailureAsync(filePath, inputLengthIssue, cancellationToken).ConfigureAwait(false);
        }

        var imageData = DropImageData.FromFile(filePath, fileInfo.Name);
        var readResult = CreateFileReadResult(imageData);
        return await LoadCoreAsync(readResult, imageData, cancellationToken).ConfigureAwait(false);
    }

    private async Task<CoverImagePipelineLoadResult> LoadCoreAsync(
        DropReadResult readResult,
        DropImageData? imageData,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (imageData is null)
        {
            var issue = new CoverImageIssue(
                CoverImageIssueCode.NoImagePayloadFound,
                CoverImageIssueSeverity.Error);
            var failureIssues = new[] { issue };
            var diagnosticsFolder = await SaveDiagnosticsAsync(
                readResult,
                null,
                null,
                FormatIssuesForDiagnostics(failureIssues),
                cancellationToken).ConfigureAwait(false);
            return CoverImagePipelineLoadResult.Failed(null, null, failureIssues, diagnosticsFolder);
        }

        if (imageData.Length is long knownLength)
        {
            var inputLengthIssue = _validator.ValidateInputLength(knownLength);
            if (inputLengthIssue is not null)
            {
                var failureIssues = new[] { inputLengthIssue };
                var diagnosticsFolder = await SaveDiagnosticsAsync(
                    readResult,
                    null,
                    null,
                    FormatIssuesForDiagnostics(failureIssues),
                    cancellationToken).ConfigureAwait(false);
                return CoverImagePipelineLoadResult.Failed(null, null, failureIssues, diagnosticsFolder);
            }
        }

        byte[] materializedBytes;
        try
        {
            var materialized = await MaterializeStandardCoverBytesAsync(imageData, cancellationToken).ConfigureAwait(false);
            if (materialized.Issue is not null)
            {
                var failureIssues = new[] { materialized.Issue };
                var diagnosticsFolder = await SaveDiagnosticsAsync(
                    readResult,
                    null,
                    null,
                    FormatIssuesForDiagnostics(failureIssues),
                    cancellationToken).ConfigureAwait(false);
                return CoverImagePipelineLoadResult.Failed(null, null, failureIssues, diagnosticsFolder);
            }

            materializedBytes = materialized.Bytes!;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            var issue = new CoverImageIssue(
                CoverImageIssueCode.NoImagePayloadFound,
                CoverImageIssueSeverity.Error,
                new Dictionary<string, object?> { ["sourceName"] = imageData.SourceName },
                ex.Message);
            var failureIssues = new[] { issue };
            var diagnosticsFolder = await SaveDiagnosticsAsync(
                readResult,
                null,
                null,
                FormatIssuesForDiagnostics(failureIssues),
                cancellationToken).ConfigureAwait(false);
            return CoverImagePipelineLoadResult.Failed(null, null, failureIssues, diagnosticsFolder);
        }

        var inspection = await _inspector.InspectBytesAsync(
            materializedBytes,
            imageData.SourceName,
            cancellationToken).ConfigureAwait(false);
        var validation = _validator.Validate(
            inspection,
            materializedBytes.LongLength);
        if (!validation.IsValid)
        {
            var diagnosticsFolder = await SaveDiagnosticsAsync(
                readResult,
                inspection,
                null,
                FormatIssuesForDiagnostics(validation.Issues),
                cancellationToken).ConfigureAwait(false);
            return CoverImagePipelineLoadResult.Failed(inspection, validation, validation.Issues, diagnosticsFolder);
        }

        var session = new CoverImagePipelineSession(
            this,
            readResult,
            imageData,
            materializedBytes,
            inspection,
            validation,
            _options.StandardCoverOutput,
            _options.Policy);
        return CoverImagePipelineLoadResult.Success(session, session.Issues);
    }

    internal CoverImageOutputResult Normalize(
        byte[] materializedBytes,
        CoverImageCropState cropState,
        StandardCoverProcessingPlan processingPlan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(materializedBytes);
        ArgumentNullException.ThrowIfNull(processingPlan);
        cancellationToken.ThrowIfCancellationRequested();

        CoverImageMatte? matte = processingPlan.Transparency switch
        {
            JpegTransparencyMode.Auto => null,
            JpegTransparencyMode.White => CoverImageMatte.White,
            JpegTransparencyMode.Black => CoverImageMatte.Black,
            _ => throw new ArgumentOutOfRangeException(
                nameof(processingPlan),
                processingPlan.Transparency,
                "Unbekannter Transparency-Modus.")
        };
        return _normalizer.NormalizeBytes(
            materializedBytes,
            cropState.Rect,
            new CoverImageNormalizationOptions
            {
                TargetSizePixels = processingPlan.TargetSizePixels,
                Format = processingPlan.Format,
                JpegMatte = matte
            },
            cancellationToken);
    }

    internal CoverImagePreviewResult CreatePreview(
        byte[] materializedBytes,
        int maxLongEdgePixels,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(materializedBytes);
        return _normalizer.CreatePreview(materializedBytes, maxLongEdgePixels, cancellationToken);
    }

    public string BuildDebugReport(
        DropReadResult readResult,
        CoverImageDiagnosticContext? context = null,
        CoverImageInspectionResult? inspection = null,
        CoverImageOutputResult? output = null,
        string? error = null)
    {
        ArgumentNullException.ThrowIfNull(readResult);

        var diagnostics = _options.Diagnostics;
        var writerOptions = new CoverImageDiagnosticOptions
        {
            SaveInputBytes = false,
            SaveNormalizedImage = false,
            SaveTextArtifacts = false,
            DebugReportTextLimit = diagnostics.DebugReportTextLimit
        };
        var rootFolder = string.IsNullOrWhiteSpace(
            diagnostics.RootFolder)
            ? Path.GetTempPath()
            : diagnostics.RootFolder;
        var writer = new CoverImageDiagnosticWriter(
            rootFolder,
            writerOptions);
        var effectiveContext = (context ??
            new CoverImageDiagnosticContext
            {
                Profile = "Unknown",
                SourceDetection = readResult.SourceDetection
            }) with
        {
            ManualSourceOverride =
                context?.ManualSourceOverride ??
                diagnostics.SelectedSource,
            SourceDetection =
                context?.SourceDetection ??
                readResult.SourceDetection
        };

        return writer.BuildDebugReport(
            diagnostics.SelectedSource,
            readResult,
            inspection,
            output,
            error,
            effectiveContext);
    }

    private static CoverImageDiagnosticContext
        CreatePassThroughDiagnosticContext(
            DropReadResult readResult,
            CoverImagePassThroughOptions options,
            CoverImagePassThroughResult result)
    {
        return new CoverImageDiagnosticContext
        {
            Profile = "PassThrough",
            SourceDetection = readResult.SourceDetection,
            PassThroughMaterializationKind =
                result.MaterializationKind.ToString(),
            PassThroughFormat = result.Format.ToString(),
            PassThroughPixelWidth = result.PixelWidth,
            PassThroughPixelHeight = result.PixelHeight,
            PassThroughFileExtension = result.FileExtension,
            NamingStrategy = options.Naming.Strategy.ToString(),
            FinalFileName =
                result.Persistence?.FileName ??
                result.SuggestedFileName,
            PersistencePath = result.Persistence?.FilePath,
            Issues = result.Issues
        };
    }

    private async Task<string?> SaveCandidateSweepDiagnosticsAsync(
        DropAcquisitionAnalysis analysis,
        IReadOnlyList<CoverImageCandidateAttempt> candidateAttempts,
        CoverImagePipelineLoadResult? bestFailure,
        IReadOnlyList<CoverImageIssue> issues,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(analysis);
        ArgumentNullException.ThrowIfNull(candidateAttempts);
        ArgumentNullException.ThrowIfNull(issues);

        var readResult = CreateCandidateSweepDiagnosticReadResult(
            analysis,
            candidateAttempts);
        var context = CreateCandidateSweepDiagnosticContext(
            analysis,
            candidateAttempts,
            bestFailure,
            issues);

        return await SaveDiagnosticsAsync(
            readResult,
            bestFailure?.Inspection,
            null,
            FormatIssuesForDiagnostics(issues),
            cancellationToken,
            context).ConfigureAwait(false);
    }

    private static DropReadResult CreateCandidateSweepDiagnosticReadResult(
        DropAcquisitionAnalysis analysis,
        IReadOnlyList<CoverImageCandidateAttempt> candidateAttempts)
    {
        var downloadAttempts = candidateAttempts
            .SelectMany(attempt => attempt.DownloadAttempts)
            .ToArray();

        return new DropReadResult(
            null,
            "StandardCoverCandidateSweep",
            analysis.SourceDetection.DisplayName,
            analysis.OfferedFormats,
            analysis.DiagnosticTextArtifacts,
            analysis.UrlCandidates,
            null,
            downloadAttempts)
        {
            SourceDetection = analysis.SourceDetection,
            Candidates = analysis.Candidates,
            DiagnosticTextArtifacts = analysis.DiagnosticTextArtifacts
        };
    }

    private CoverImageDiagnosticContext CreateCandidateSweepDiagnosticContext(
        DropAcquisitionAnalysis analysis,
        IReadOnlyList<CoverImageCandidateAttempt> candidateAttempts,
        CoverImagePipelineLoadResult? bestFailure,
        IReadOnlyList<CoverImageIssue> issues)
    {
        return new CoverImageDiagnosticContext
        {
            Profile = "StandardCover",
            SourceDetection = analysis.SourceDetection,
            CandidateAttempts = candidateAttempts
                .Select(attempt => new CoverImageDiagnosticCandidateAttempt(
                    attempt.CandidateIndex,
                    attempt.Kind,
                    attempt.Source,
                    attempt.Priority,
                    attempt.WidthHint,
                    attempt.Url,
                    attempt.Materialized,
                    attempt.Accepted,
                    attempt.Inspection,
                    attempt.Validation,
                    attempt.Issues,
                    attempt.DownloadAttempts,
                    attempt.TechnicalCause))
                .ToArray(),
            StandardCoverValidation = bestFailure?.Validation,
            TargetSizeMode = _options.StandardCoverOutput.TargetSize.ToString(),
            RequestedOutputFormat = _options.StandardCoverOutput.Format.ToString(),
            TransparencyMode = _options.StandardCoverOutput.Transparency.ToString(),
            Issues = issues
        };
    }

    internal async Task<string?> SaveDiagnosticsAsync(
        DropReadResult readResult,
        CoverImageInspectionResult? inspection,
        CoverImageOutputResult? output,
        string? error,
        CancellationToken cancellationToken,
        CoverImageDiagnosticContext? context = null)
    {
        var diagnostics = _options.Diagnostics;
        if (!diagnostics.Enabled || string.IsNullOrWhiteSpace(diagnostics.RootFolder))
        {
            return null;
        }

        var writerOptions = new CoverImageDiagnosticOptions
        {
            SaveInputBytes = diagnostics.SaveInputBytes,
            SaveNormalizedImage = diagnostics.SaveNormalizedImage,
            SaveTextArtifacts = diagnostics.SaveTextArtifacts,
            DebugReportTextLimit = diagnostics.DebugReportTextLimit
        };
        var writer = new CoverImageDiagnosticWriter(diagnostics.RootFolder, writerOptions);
        var effectiveContext = (context ?? new CoverImageDiagnosticContext
        {
            Profile = "StandardCover",
            SourceDetection = readResult.SourceDetection
        }) with
        {
            ManualSourceOverride = context?.ManualSourceOverride ?? diagnostics.SelectedSource,
            SourceDetection = context?.SourceDetection ?? readResult.SourceDetection
        };

        return await writer.SaveAsync(
                diagnostics.SelectedSource,
                readResult,
                inspection,
                output,
                error,
                effectiveContext,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<(byte[]? Bytes, CoverImageIssue? Issue)> MaterializeStandardCoverBytesAsync(
        DropImageData imageData,
        CancellationToken cancellationToken)
    {
        if (imageData.TryGetInMemoryBytes(out var inMemoryBytes))
        {
            var inputLengthIssue = _validator.ValidateInputLength(inMemoryBytes.LongLength);
            return inputLengthIssue is null
                ? (inMemoryBytes, null)
                : (null, inputLengthIssue);
        }

        await using var source = await imageData.OpenReadAsync(cancellationToken).ConfigureAwait(false);
        using var output = imageData.Length is long length && length <= int.MaxValue
            ? new MemoryStream((int)length)
            : new MemoryStream();
        var buffer = new byte[81920];
        long totalBytes = 0;

        while (true)
        {
            var read = await source.ReadAsync(
                buffer.AsMemory(0, buffer.Length),
                cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            totalBytes += read;
            var inputLengthIssue = _validator.ValidateInputLength(totalBytes);
            if (inputLengthIssue is not null)
            {
                return (null, inputLengthIssue);
            }

            await output.WriteAsync(
                buffer.AsMemory(0, read),
                cancellationToken).ConfigureAwait(false);
        }

        return (output.ToArray(), null);
    }

    private async Task<CoverImagePipelineLoadResult> CreateFileFailureAsync(
        string filePath,
        CoverImageIssue issue,
        CancellationToken cancellationToken)
    {
        var readResult = CreateFileReadResult(null);
        var issues = new[] { issue };
        var diagnosticsFolder = await SaveDiagnosticsAsync(
            readResult,
            null,
            null,
            FormatIssuesForDiagnostics(issues),
            cancellationToken).ConfigureAwait(false);
        return CoverImagePipelineLoadResult.Failed(null, null, issues, diagnosticsFolder);
    }

    private static DropReadResult CreateFileReadResult(DropImageData? imageData)
    {
        return new DropReadResult(
            imageData,
            "FilePath",
            "FilePath",
            ["FilePath"],
            new Dictionary<string, string>(),
            [],
            null,
            []);
    }

    private static int ScoreCandidateFailure(CoverImagePipelineLoadResult result)
    {
        var score = 0;
        if (result.Inspection is not null)
        {
            score += 10;
        }

        if (result.Validation is not null)
        {
            score += 20;
        }

        score += result.Issues.Count(issue => issue.Severity == CoverImageIssueSeverity.Error) * 2;
        if (result.Issues.Any(issue => issue.Code == CoverImageIssueCode.ImageTooSmall))
        {
            score += 5;
        }

        return score;
    }

    private static string FormatIssuesForDiagnostics(IEnumerable<CoverImageIssue> issues)
    {
        return string.Join(
            Environment.NewLine,
            issues.Select(issue => string.IsNullOrWhiteSpace(issue.TechnicalCause)
                ? $"{issue.Severity}: {issue.Code}"
                : $"{issue.Severity}: {issue.Code} - {issue.TechnicalCause}"));
    }
}
