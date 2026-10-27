using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CoverImageDrop.Diagnostics;

public sealed record SmokeTestAnnotation
{
    public required string TestBrowser { get; init; }

    public required string Verdict { get; init; }

    public DateTimeOffset StartedAt { get; init; }

    public bool AutoFinalize { get; init; }

    public double? AcquisitionMilliseconds { get; init; }

    public double? LoadMilliseconds { get; init; }

    public double? PreviewMilliseconds { get; init; }

    public double? PipelineMilliseconds { get; init; }

    public double? FinalizeMilliseconds { get; init; }

    public double? TotalMilliseconds { get; init; }
}

public static class SmokeTestAnnotationWriter
{
    public static async Task<string> ApplyAsync(
        string diagnosticsFolder,
        SmokeTestAnnotation annotation,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(diagnosticsFolder);
        ArgumentNullException.ThrowIfNull(annotation);
        ArgumentException.ThrowIfNullOrWhiteSpace(annotation.TestBrowser);
        ArgumentException.ThrowIfNullOrWhiteSpace(annotation.Verdict);
        cancellationToken.ThrowIfCancellationRequested();

        var folderPath = Path.GetFullPath(diagnosticsFolder);
        if (!Directory.Exists(folderPath))
        {
            throw new DirectoryNotFoundException(
                $"Diagnostics folder does not exist: {folderPath}");
        }

        var resultPath = Path.Combine(folderPath, "result.json");
        if (!File.Exists(resultPath))
        {
            throw new FileNotFoundException(
                "Smoke-test result.json does not exist.",
                resultPath);
        }

        var json = await File.ReadAllTextAsync(
            resultPath,
            cancellationToken).ConfigureAwait(false);
        JsonObject root;
        try
        {
            root = JsonNode.Parse(json) as JsonObject
                ?? throw new InvalidDataException(
                    "Smoke-test result.json must contain a JSON object.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException(
                "Smoke-test result.json is invalid JSON.",
                ex);
        }

        var baseFolderName =
            (root["SmokeTest"] as JsonObject)?["BaseFolderName"]
                ?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(baseFolderName))
        {
            baseFolderName = Path.GetFileName(folderPath);
        }

        root["SmokeTest"] = new JsonObject
        {
            ["TestBrowser"] = annotation.TestBrowser,
            ["Verdict"] = annotation.Verdict,
            ["StartedAt"] = annotation.StartedAt.ToString("O"),
            ["AnnotatedAt"] = DateTimeOffset.Now.ToString("O"),
            ["AutoFinalize"] = annotation.AutoFinalize,
            ["AcquisitionMilliseconds"] =
                CreateNumber(annotation.AcquisitionMilliseconds),
            ["LoadMilliseconds"] =
                CreateNumber(annotation.LoadMilliseconds),
            ["PreviewMilliseconds"] =
                CreateNumber(annotation.PreviewMilliseconds),
            ["PipelineMilliseconds"] =
                CreateNumber(annotation.PipelineMilliseconds),
            ["FinalizeMilliseconds"] =
                CreateNumber(annotation.FinalizeMilliseconds),
            ["TotalMilliseconds"] =
                CreateNumber(annotation.TotalMilliseconds),
            ["BaseFolderName"] = baseFolderName
        };

        var updatedJson = root.ToJsonString(
            new JsonSerializerOptions
            {
                WriteIndented = true
            });
        var tempPath = resultPath + ".tmp";
        try
        {
            await File.WriteAllTextAsync(
                tempPath,
                updatedJson,
                cancellationToken).ConfigureAwait(false);
            File.Move(tempPath, resultPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }

        var desiredFolderName =
            $"{baseFolderName}_{SanitizeFolderToken(annotation.TestBrowser)}_" +
            $"{SanitizeFolderToken(annotation.Verdict)}";
        var parentFolder = Path.GetDirectoryName(folderPath)
            ?? throw new InvalidOperationException(
                "Diagnostics folder has no parent.");
        var desiredFolderPath = Path.Combine(
            parentFolder,
            desiredFolderName);

        if (string.Equals(
            folderPath,
            desiredFolderPath,
            StringComparison.OrdinalIgnoreCase))
        {
            return folderPath;
        }

        if (Directory.Exists(desiredFolderPath))
        {
            throw new IOException(
                $"Annotated diagnostics folder already exists: {desiredFolderPath}");
        }

        Directory.Move(folderPath, desiredFolderPath);
        return desiredFolderPath;
    }

    private static JsonNode? CreateNumber(double? value)
    {
        return value is double number
            ? JsonValue.Create(Math.Round(number, 2))
            : null;
    }

    private static string SanitizeFolderToken(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(
            value
                .Where(character => !invalid.Contains(character))
                .ToArray())
            .Trim();

        if (cleaned.Length > 40)
        {
            cleaned = cleaned[..40];
        }

        return string.IsNullOrWhiteSpace(cleaned)
            ? "Unknown"
            : cleaned;
    }
}
