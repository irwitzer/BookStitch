using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CoverImageDrop.Acquisition;
using CoverImageDrop.Core.Inspection;
using CoverImageDrop.Core.Issues;
using CoverImageDrop.Core.Output;

namespace CoverImageDrop.Diagnostics;

public class CoverImageDiagnosticWriter
{
    private readonly string _rootFolder;
    private readonly CoverImageDiagnosticOptions _options;
    private static int _globalCounter;

    public CoverImageDiagnosticWriter(string rootFolder, CoverImageDiagnosticOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootFolder);

        _rootFolder = Path.GetFullPath(rootFolder);
        _options = options ?? new CoverImageDiagnosticOptions();
        if (_options.DebugReportTextLimit <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "DebugReportTextLimit muss positiv sein.");
        }
    }

    public string RootFolder => _rootFolder;

    public Task<string> SaveAsync(
        string selectedSource,
        DropReadResult readResult,
        CoverImageInspectionResult? inspection,
        CoverImageOutputResult? output,
        string? error,
        CancellationToken cancellationToken = default)
    {
        return SaveAsync(
            selectedSource,
            readResult,
            inspection,
            output,
            error,
            context: null,
            cancellationToken: cancellationToken);
    }

    public async Task<string> SaveAsync(
        string selectedSource,
        DropReadResult readResult,
        CoverImageInspectionResult? inspection,
        CoverImageOutputResult? output,
        string? error,
        CoverImageDiagnosticContext? context,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(selectedSource);
        ArgumentNullException.ThrowIfNull(readResult);
        cancellationToken.ThrowIfCancellationRequested();

        Directory.CreateDirectory(_rootFolder);

        var timestamp = DateTimeOffset.Now;
        var effectiveContext = NormalizeContext(context, selectedSource, readResult);
        var issues = MergeIssues(effectiveContext.Issues, inspection?.Issues, output?.Issues);
        var effectiveError = error ?? effectiveContext.TechnicalException;
        var status = effectiveError is null && issues.All(issue => issue.Severity != CoverImageIssueSeverity.Error)
            ? "success"
            : "failed";

        var folderName = $"{timestamp:yyyyMMdd_HHmmssfff}_{Interlocked.Increment(ref _globalCounter):000}_{SanitizeFileName(selectedSource)}_{status}";
        var folderPath = Path.Combine(_rootFolder, folderName);
        Directory.CreateDirectory(folderPath);

        if (_options.SaveInputBytes && readResult.ImageData is not null)
        {
            await using var input = new FileStream(
                Path.Combine(folderPath, "input.bin"),
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            await readResult.ImageData.CopyToAsync(input, cancellationToken).ConfigureAwait(false);
        }

        if (_options.SaveNormalizedImage && output is not null)
        {
            await File.WriteAllBytesAsync(
                Path.Combine(folderPath, $"normalized{output.FileExtension}"),
                output.Bytes,
                cancellationToken).ConfigureAwait(false);
        }

        if (_options.SaveTextArtifacts)
        {
            foreach (var artifact in readResult.TextArtifacts)
            {
                var artifactPath = Path.Combine(folderPath, $"text_{SanitizeFileName(artifact.Key)}.txt");
                await File.WriteAllTextAsync(artifactPath, artifact.Value, cancellationToken).ConfigureAwait(false);
            }
        }

        var textArtifacts = readResult.TextArtifacts.ToDictionary(
            pair => pair.Key,
            pair => new DiagnosticTextArtifact(
                pair.Value.Length,
                _options.SaveTextArtifacts ? $"text_{SanitizeFileName(pair.Key)}.txt" : null),
            StringComparer.OrdinalIgnoreCase);

        var record = new
        {
            Timestamp = timestamp,
            Profile = effectiveContext.Profile,
            ManualSourceOverride = effectiveContext.ManualSourceOverride,
            SourceDetection = effectiveContext.SourceDetection,
            Acquisition = new
            {
                readResult.InferredSource,
                readResult.AcquisitionMethod,
                readResult.OfferedFormats,
                Candidates = readResult.Candidates.Select(candidate => new
                {
                    candidate.Kind,
                    candidate.Source,
                    candidate.Priority,
                    candidate.Width,
                    candidate.Url,
                    HasEncodedPayload = candidate.SourceData is not null,
                    SourceName = candidate.SourceData?.SourceName
                }).ToArray(),
                CandidateAttempts = effectiveContext.CandidateAttempts,
                readResult.UrlCandidates,
                readResult.DownloadInfo,
                readResult.DownloadAttempts
            },
            Source = new
            {
                SourceName = readResult.ImageData?.SourceName,
                LocalFilePath = readResult.ImageData?.LocalFilePath,
                InputBytes = readResult.ImageData?.Length
            },
            Inspection = inspection,
            Validation = effectiveContext.StandardCoverValidation,
            Processing = new
            {
                effectiveContext.TargetSizeMode,
                effectiveContext.RequestedOutputFormat,
                effectiveContext.TransparencyMode,
                effectiveContext.ResolvedTargetSizePixels,
                effectiveContext.RequiresUpscaling,
                effectiveContext.Crop
            },
            PassThrough =
                string.Equals(
                    effectiveContext.Profile,
                    "PassThrough",
                    StringComparison.OrdinalIgnoreCase)
                    ? new
                    {
                        effectiveContext.PassThroughMaterializationKind,
                        effectiveContext.PassThroughFormat,
                        effectiveContext.PassThroughPixelWidth,
                        effectiveContext.PassThroughPixelHeight,
                        effectiveContext.PassThroughFileExtension
                    }
                    : null,
            Output = output is null
                ? null
                : new
                {
                    output.Width,
                    output.Height,
                    output.Format,
                    output.MimeType,
                    output.FileExtension,
                    output.ByteLength,
                    output.ResolvedTargetSizePixels,
                    output.WasUpscaled,
                    output.JpegQuality,
                    output.JpegMatte
                },
            Naming = new
            {
                effectiveContext.NamingStrategy,
                effectiveContext.FinalFileName
            },
            Persistence = new
            {
                Path = effectiveContext.PersistencePath
            },
            Issues = issues,
            Error = effectiveError,
            TextArtifacts = textArtifacts
        };

        var json = JsonSerializer.Serialize(record, CreateJsonOptions());
        await File.WriteAllTextAsync(
            Path.Combine(folderPath, "result.json"),
            json,
            cancellationToken).ConfigureAwait(false);

        var debugReport = BuildDebugReportCore(
            timestamp,
            selectedSource,
            readResult,
            inspection,
            output,
            effectiveContext,
            issues,
            effectiveError);
        await File.WriteAllTextAsync(
            Path.Combine(folderPath, "debug-report.txt"),
            debugReport,
            cancellationToken).ConfigureAwait(false);

        return folderPath;
    }

    public string BuildDebugReport(
        string selectedSource,
        DropReadResult readResult,
        CoverImageInspectionResult? inspection = null,
        CoverImageOutputResult? output = null,
        string? error = null,
        CoverImageDiagnosticContext? context = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(selectedSource);
        ArgumentNullException.ThrowIfNull(readResult);

        var effectiveContext = NormalizeContext(context, selectedSource, readResult);
        var issues = MergeIssues(effectiveContext.Issues, inspection?.Issues, output?.Issues);
        return BuildDebugReportCore(
            DateTimeOffset.Now,
            selectedSource,
            readResult,
            inspection,
            output,
            effectiveContext,
            issues,
            error ?? effectiveContext.TechnicalException);
    }

    private string BuildDebugReportCore(
        DateTimeOffset timestamp,
        string selectedSource,
        DropReadResult readResult,
        CoverImageInspectionResult? inspection,
        CoverImageOutputResult? output,
        CoverImageDiagnosticContext context,
        IReadOnlyList<CoverImageIssue> issues,
        string? error)
    {
        var builder = new StringBuilder();
        builder.AppendLine("CoverImageDrop Debugbericht");
        builder.AppendLine($"Zeitpunkt: {timestamp:O}");
        builder.AppendLine($"Profil: {context.Profile}");
        builder.AppendLine($"Source Override: {context.ManualSourceOverride ?? selectedSource}");

        var detection = context.SourceDetection ?? readResult.SourceDetection;
        builder.AppendLine($"Automatische Quelle: {detection.Kind}");
        builder.AppendLine($"Confidence: {detection.Confidence}");
        if (detection.Reasons.Count > 0)
        {
            builder.AppendLine("Source-Indikatoren:");
            foreach (var reason in detection.Reasons)
            {
                builder.AppendLine($"- {reason}");
            }
        }

        builder.AppendLine();
        builder.AppendLine($"Acquisition: {readResult.AcquisitionMethod}");
        if (!string.IsNullOrWhiteSpace(readResult.InferredSource))
        {
            builder.AppendLine($"Inferred Source: {readResult.InferredSource}");
        }

        builder.AppendLine("Offered Formats:");
        foreach (var format in readResult.OfferedFormats)
        {
            builder.AppendLine($"- {format}");
        }

        if (readResult.Candidates.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("Kandidaten:");
            foreach (var candidate in readResult.Candidates)
            {
                builder.Append($"- {candidate.Kind} | Priority={candidate.Priority}");
                if (candidate.Width is int width)
                {
                    builder.Append($" | Width={width}");
                }

                builder.Append($" | Source={candidate.Source}");
                if (!string.IsNullOrWhiteSpace(candidate.Url))
                {
                    builder.Append($" | URL={candidate.Url}");
                }

                builder.AppendLine();
            }
        }

        if (context.CandidateAttempts.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("Candidate Attempts:");
            foreach (var attempt in context.CandidateAttempts)
            {
                builder.Append(
                    $"- #{attempt.CandidateIndex} {attempt.Kind} | Priority={attempt.Priority} | " +
                    $"Materialized={attempt.Materialized} | Accepted={attempt.Accepted}");
                if (attempt.WidthHint is int width)
                {
                    builder.Append($" | WidthHint={width}");
                }

                if (!string.IsNullOrWhiteSpace(attempt.Url))
                {
                    builder.Append($" | URL={attempt.Url}");
                }

                if (!string.IsNullOrWhiteSpace(attempt.TechnicalCause))
                {
                    builder.Append($" | Reason={attempt.TechnicalCause}");
                }

                builder.AppendLine();
            }
        }

        if (readResult.DownloadInfo is not null)
        {
            builder.AppendLine();
            builder.AppendLine("Download:");
            builder.AppendLine($"Requested URL: {readResult.DownloadInfo.RequestedUrl}");
            builder.AppendLine($"Final URL: {readResult.DownloadInfo.FinalUrl ?? "-"}");
            builder.AppendLine($"Content-Type: {readResult.DownloadInfo.ContentType ?? "-"}");
            builder.AppendLine($"Content-Length: {readResult.DownloadInfo.ContentLength?.ToString() ?? "-"}");
        }

        if (readResult.DownloadAttempts.Count > 0)
        {
            builder.AppendLine("Download Attempts:");
            foreach (var attempt in readResult.DownloadAttempts)
            {
                builder.AppendLine(
                    $"- {attempt.Url} | Final={attempt.FinalUrl ?? "-"} | " +
                    $"Type={attempt.ContentType ?? "-"} | Length={attempt.ContentLength?.ToString() ?? "-"} | " +
                    $"Accepted={attempt.Accepted} | Error={attempt.Error ?? "-"}");
            }
        }

        if (inspection is not null)
        {
            builder.AppendLine();
            builder.AppendLine("Inspection:");
            builder.AppendLine($"Signatur: {inspection.SignatureFormat}");
            builder.AppendLine($"Decoder: {inspection.DecoderCodec}");
            builder.AppendLine($"Original: {inspection.PixelWidth} x {inspection.PixelHeight}");
            builder.AppendLine($"Orientiert: {inspection.OrientedPixelWidth} x {inspection.OrientedPixelHeight}");
            builder.AppendLine($"Frames: {inspection.FrameCount}");
            builder.AppendLine($"Transparenz moeglich: {inspection.MayContainTransparency}");
        }

        if (context.StandardCoverValidation is not null)
        {
            builder.AppendLine();
            builder.AppendLine("StandardCover Validation:");
            builder.AppendLine($"Valid: {context.StandardCoverValidation.IsValid}");
            builder.AppendLine($"Usable Square: {context.StandardCoverValidation.UsableSquarePixels}");
            builder.AppendLine($"Total Pixels: {context.StandardCoverValidation.TotalPixels}");
        }

        if (string.Equals(
                context.Profile,
                "PassThrough",
                StringComparison.OrdinalIgnoreCase))
        {
            builder.AppendLine();
            builder.AppendLine("PassThrough:");
            builder.AppendLine(
                $"Materialisierung: {context.PassThroughMaterializationKind ?? "-"}");
            builder.AppendLine(
                $"Format: {context.PassThroughFormat ?? "-"}");
            builder.AppendLine(
                $"Pixel: {context.PassThroughPixelWidth?.ToString() ?? "?"} x " +
                $"{context.PassThroughPixelHeight?.ToString() ?? "?"}");
            builder.AppendLine(
                $"Endung: {context.PassThroughFileExtension ?? "-"}");
        }

        if (context.ResolvedTargetSizePixels is not null || context.Crop is not null)
        {
            builder.AppendLine();
            builder.AppendLine("Processing:");
            builder.AppendLine($"Target Mode: {context.TargetSizeMode ?? "-"}");
            builder.AppendLine($"Requested Output: {context.RequestedOutputFormat ?? "-"}");
            builder.AppendLine($"Transparency: {context.TransparencyMode ?? "-"}");
            builder.AppendLine($"Resolved Target: {context.ResolvedTargetSizePixels?.ToString() ?? "-"}");
            builder.AppendLine($"Requires Upscaling: {context.RequiresUpscaling?.ToString() ?? "-"}");
            if (context.Crop is not null)
            {
                builder.AppendLine(
                    $"Crop: X={context.Crop.X}, Y={context.Crop.Y}, " +
                    $"W={context.Crop.Width}, H={context.Crop.Height}");
            }
        }

        if (output is not null)
        {
            builder.AppendLine();
            builder.AppendLine("Output:");
            builder.AppendLine($"Format: {output.Format}");
            builder.AppendLine($"MIME: {output.MimeType}");
            builder.AppendLine($"Groesse: {output.Width} x {output.Height}");
            builder.AppendLine($"Bytes: {output.ByteLength}");
            builder.AppendLine($"Resolved Target: {output.ResolvedTargetSizePixels}");
            builder.AppendLine($"Upscaled: {output.WasUpscaled}");
            builder.AppendLine($"JPEG Quality: {output.JpegQuality?.ToString() ?? "-"}");
            builder.AppendLine($"JPEG Matte: {output.JpegMatte?.ToString() ?? "-"}");
        }

        if (!string.IsNullOrWhiteSpace(context.NamingStrategy) ||
            !string.IsNullOrWhiteSpace(context.FinalFileName) ||
            !string.IsNullOrWhiteSpace(context.PersistencePath))
        {
            builder.AppendLine();
            builder.AppendLine("Naming / Persistence:");
            builder.AppendLine($"Naming Strategy: {context.NamingStrategy ?? "-"}");
            builder.AppendLine($"Final Name: {context.FinalFileName ?? "-"}");
            builder.AppendLine($"Final Path: {context.PersistencePath ?? "-"}");
        }

        if (issues.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("Issues:");
            foreach (var issue in issues)
            {
                builder.Append($"- {issue.Severity}: {issue.Code}");
                if (!string.IsNullOrWhiteSpace(issue.TechnicalCause))
                {
                    builder.Append($" | {issue.TechnicalCause}");
                }

                if (!string.IsNullOrWhiteSpace(issue.SuggestedAction))
                {
                    builder.Append($" | Action={issue.SuggestedAction}");
                }

                builder.AppendLine();
            }
        }

        if (!string.IsNullOrWhiteSpace(error))
        {
            builder.AppendLine();
            builder.AppendLine($"Technischer Fehler: {error}");
        }

        var reportArtifacts = readResult.DiagnosticTextArtifacts.Count > 0
            ? readResult.DiagnosticTextArtifacts
            : readResult.TextArtifacts;
        if (reportArtifacts.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("Text-/HTML-Auszüge:");
            foreach (var artifact in reportArtifacts)
            {
                builder.AppendLine($"[{artifact.Key}] Länge={artifact.Value.Length}");
                builder.AppendLine(CreateReportSnippet(artifact.Value));
            }
        }

        return builder.ToString();
    }

    private string CreateReportSnippet(string value)
    {
        var cleaned = value.Replace('\0', ' ');
        if (cleaned.Length <= _options.DebugReportTextLimit)
        {
            return cleaned;
        }

        return cleaned[.._options.DebugReportTextLimit] + Environment.NewLine + "[... Debugbericht gekuerzt ...]";
    }

    private static CoverImageDiagnosticContext NormalizeContext(
        CoverImageDiagnosticContext? context,
        string selectedSource,
        DropReadResult readResult)
    {
        var effective = context ?? new CoverImageDiagnosticContext();
        return effective with
        {
            ManualSourceOverride = effective.ManualSourceOverride ?? selectedSource,
            SourceDetection = effective.SourceDetection ?? readResult.SourceDetection
        };
    }

    private static IReadOnlyList<CoverImageIssue> MergeIssues(
        IReadOnlyList<CoverImageIssue> contextIssues,
        IReadOnlyList<CoverImageIssue>? inspectionIssues,
        IReadOnlyList<CoverImageIssue>? outputIssues)
    {
        var merged = new List<CoverImageIssue>();
        AddUnique(merged, contextIssues);
        AddUnique(merged, inspectionIssues);
        AddUnique(merged, outputIssues);
        return merged;
    }

    private static void AddUnique(
        ICollection<CoverImageIssue> target,
        IReadOnlyList<CoverImageIssue>? source)
    {
        if (source is null)
        {
            return;
        }

        foreach (var issue in source)
        {
            if (!target.Any(existing =>
                    existing.Code == issue.Code &&
                    existing.Severity == issue.Severity &&
                    string.Equals(existing.TechnicalCause, issue.TechnicalCause, StringComparison.Ordinal)))
            {
                target.Add(issue);
            }
        }
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(value.Select(character => invalid.Contains(character) ? '_' : character).ToArray());
        return string.IsNullOrWhiteSpace(cleaned) ? "unknown" : cleaned;
    }

    private sealed record DiagnosticTextArtifact(int Length, string? File);
}

public sealed class SmokeTestLogger : CoverImageDiagnosticWriter
{
    public SmokeTestLogger(string rootFolder)
        : base(rootFolder)
    {
    }
}
