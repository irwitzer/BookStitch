using CoverImageDrop.Acquisition;
using CoverImageDrop.Core.Inspection;
using CoverImageDrop.Core.Issues;
using CoverImageDrop.Core.Policy;
using CoverImageDrop.Core.Output;
using CoverImageDrop.Core.Validation;
using CoverImageDrop.Diagnostics;

namespace CoverImageDrop.Pipeline;

public sealed class CoverImagePipelineSession : IDisposable, IAsyncDisposable
{
    private readonly CoverImagePipelineService _service;
    private readonly byte[] _materializedBytes;
    private readonly StandardCoverPolicy _policy;
    private StandardCoverOutputOptions _outputOptions;
    private bool _disposed;

    internal CoverImagePipelineSession(
        CoverImagePipelineService service,
        DropReadResult readResult,
        DropImageData imageData,
        byte[] materializedBytes,
        CoverImageInspectionResult inspection,
        StandardCoverValidationResult validation,
        StandardCoverOutputOptions outputOptions,
        StandardCoverPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(readResult);
        ArgumentNullException.ThrowIfNull(imageData);
        ArgumentNullException.ThrowIfNull(materializedBytes);
        ArgumentNullException.ThrowIfNull(inspection);
        ArgumentNullException.ThrowIfNull(validation);
        ArgumentNullException.ThrowIfNull(outputOptions);
        ArgumentNullException.ThrowIfNull(policy);

        _service = service;
        _materializedBytes = materializedBytes;
        _policy = policy;
        _outputOptions = outputOptions;
        ReadResult = readResult;
        ImageData = imageData;
        Inspection = inspection;
        Validation = validation;
        ProcessingPlan = StandardCoverProcessingPlan.Resolve(
            validation.UsableSquarePixels,
            _outputOptions,
            _policy);
        CropState = CoverImageCropState.CreateCentered(
            inspection.OrientedPixelWidth,
            inspection.OrientedPixelHeight,
            ProcessingPlan.TargetSizePixels).Clamp();
        Issues = BuildIssues(validation, ProcessingPlan);
        SourceDetection = readResult.SourceDetection;
    }

    public DropReadResult ReadResult { get; }
    public DropImageData ImageData { get; }
    public CoverImageInspectionResult Inspection { get; }
    public StandardCoverValidationResult Validation { get; }
    public StandardCoverProcessingPlan ProcessingPlan { get; private set; }
    public CoverImageCropState CropState { get; private set; }
    public IReadOnlyList<CoverImageIssue> Issues { get; private set; }
    public DropSourceDetection SourceDetection { get; private set; } = new(
        DropSourceKind.Unknown,
        DropSourceConfidence.Low,
        []);
    public IReadOnlyList<CoverImageCandidateAttempt> CandidateAttempts { get; private set; } = [];

    public StandardCoverOutputOptions OutputOptions
    {
        get => _outputOptions;
        set
        {
            ThrowIfDisposed();
            ArgumentNullException.ThrowIfNull(value);
            if (_outputOptions == value)
            {
                return;
            }

            _outputOptions = value;
            RefreshProcessingPlan();
        }
    }

    public event EventHandler<CoverImageCropState>? CropStateChanged;

    public CoverImagePreviewResult CreatePreview(
        int maxLongEdgePixels = 1600,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return _service.CreatePreview(_materializedBytes, maxLongEdgePixels, cancellationToken);
    }

    public Task<CoverImagePreviewResult> CreatePreviewAsync(
        int maxLongEdgePixels = 1600,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        return RunStaWorkerAsync(
            () => _service.CreatePreview(
                _materializedBytes,
                maxLongEdgePixels,
                cancellationToken),
            cancellationToken);
    }

    public string BuildDebugReport(
        CoverImageOutputResult? output = null,
        string? error = null)
    {
        ThrowIfDisposed();
        return _service.BuildDebugReport(
            ReadResult,
            CreateDiagnosticContext(error),
            Inspection,
            output,
            error);
    }

    public CoverImagePreviewCropState CreatePreviewCropState(double viewportSize)
    {
        ThrowIfDisposed();
        return CoverImagePreviewCropMapper.CreateFromCrop(
            Inspection.OrientedPixelWidth,
            Inspection.OrientedPixelHeight,
            CropState.Rect,
            viewportSize);
    }

    public void UpdateCropFromPreview(CoverImagePreviewCropState previewState)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(previewState);
        if (previewState.OrientedPixelWidth != Inspection.OrientedPixelWidth ||
            previewState.OrientedPixelHeight != Inspection.OrientedPixelHeight)
        {
            throw new ArgumentException(
                "Preview-Crop-State gehoert nicht zu den orientierten Abmessungen dieser Session.",
                nameof(previewState));
        }

        UpdateCropPosition(previewState.CropRect.X, previewState.CropRect.Y);
    }

    public void UpdateCropPosition(int x, int y)
    {
        ThrowIfDisposed();
        SetCropState(CropState.MoveTo(x, y));
    }

    public void ResetCropToCenter()
    {
        ThrowIfDisposed();
        SetCropState(CoverImageCropState.CreateCentered(
            Inspection.OrientedPixelWidth,
            Inspection.OrientedPixelHeight,
            ProcessingPlan.TargetSizePixels));
    }

    public async Task<CoverImagePipelineFinalizeResult> FinalizeAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        var cropState = CropState;
        var processingPlan = ProcessingPlan;
        var sessionIssues = Issues.ToArray();
        var diagnosticContext = CreateDiagnosticContext();

        try
        {
            var output = await RunStaWorkerAsync(
                () => _service.Normalize(
                    _materializedBytes,
                    cropState,
                    processingPlan,
                    cancellationToken),
                cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            var diagnosticsFolder = await _service.SaveDiagnosticsAsync(
                ReadResult,
                Inspection,
                output,
                null,
                cancellationToken,
                diagnosticContext).ConfigureAwait(false);
            return new CoverImagePipelineFinalizeResult(
                true,
                output,
                MergeFinalIssues(sessionIssues, output.Issues),
                diagnosticsFolder);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or NotSupportedException or ArgumentException)
        {
            var issue = new CoverImageIssue(
                CoverImageIssueCode.ImageDecodeFailed,
                CoverImageIssueSeverity.Error,
                TechnicalCause: ex.Message);
            var diagnosticsFolder = await _service.SaveDiagnosticsAsync(
                ReadResult,
                Inspection,
                null,
                ex.Message,
                cancellationToken,
                diagnosticContext with
                {
                    TechnicalException = ex.Message
                }).ConfigureAwait(false);
            return CoverImagePipelineFinalizeResult.Failed([issue], diagnosticsFolder);
        }
    }

    public CoverImageDiagnosticContext CreateDiagnosticContext(string? technicalException = null)
    {
        ThrowIfDisposed();

        return new CoverImageDiagnosticContext
        {
            Profile = "StandardCover",
            SourceDetection = SourceDetection,
            CandidateAttempts = CandidateAttempts
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
            StandardCoverValidation = Validation,
            TargetSizeMode = OutputOptions.TargetSize.ToString(),
            RequestedOutputFormat = OutputOptions.Format.ToString(),
            TransparencyMode = OutputOptions.Transparency.ToString(),
            ResolvedTargetSizePixels = ProcessingPlan.TargetSizePixels,
            RequiresUpscaling = ProcessingPlan.RequiresUpscaling,
            Crop = CropState.Rect,
            Issues = Issues,
            TechnicalException = technicalException
        };
    }

    internal void SetAcquisitionDiagnostics(
        DropSourceDetection sourceDetection,
        IReadOnlyList<CoverImageCandidateAttempt> candidateAttempts)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(sourceDetection);
        ArgumentNullException.ThrowIfNull(candidateAttempts);

        SourceDetection = sourceDetection;
        CandidateAttempts = candidateAttempts.ToArray();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        ImageData.Dispose();
        GC.SuppressFinalize(this);
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    private void RefreshProcessingPlan()
    {
        var nextPlan = StandardCoverProcessingPlan.Resolve(
            Validation.UsableSquarePixels,
            _outputOptions,
            _policy);
        ProcessingPlan = nextPlan;
        Issues = BuildIssues(Validation, nextPlan);

        var nextCropState = CropState with
        {
            TargetSizePixels = nextPlan.TargetSizePixels
        };
        SetCropState(nextCropState);
    }

    private static IReadOnlyList<CoverImageIssue> BuildIssues(
        StandardCoverValidationResult validation,
        StandardCoverProcessingPlan processingPlan)
    {
        var issues = new List<CoverImageIssue>(validation.Issues);
        if (processingPlan.RequiresUpscaling)
        {
            issues.Add(new CoverImageIssue(
                CoverImageIssueCode.UpscalingRequired,
                CoverImageIssueSeverity.Warning,
                new Dictionary<string, object?>
                {
                    ["usableSquarePixels"] = validation.UsableSquarePixels,
                    ["targetSizePixels"] = processingPlan.TargetSizePixels,
                    ["targetSizeMode"] = processingPlan.TargetSizeMode.ToString()
                }));
        }

        return issues;
    }

    private static IReadOnlyList<CoverImageIssue> MergeFinalIssues(
        IReadOnlyList<CoverImageIssue> sessionIssues,
        IReadOnlyList<CoverImageIssue> outputIssues)
    {
        var merged = new List<CoverImageIssue>(sessionIssues);
        var existingCodes = sessionIssues
            .Select(issue => issue.Code)
            .ToHashSet();

        foreach (var issue in outputIssues)
        {
            if (existingCodes.Add(issue.Code))
            {
                merged.Add(issue);
            }
        }

        return merged;
    }

    private void SetCropState(CoverImageCropState cropState)
    {
        var clamped = cropState.Clamp();
        if (CropState == clamped)
        {
            return;
        }

        CropState = clamped;
        CropStateChanged?.Invoke(this, CropState);
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
            Name = "CoverImageDrop.WicWorker"
        };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(CoverImagePipelineSession));
        }
    }
}
