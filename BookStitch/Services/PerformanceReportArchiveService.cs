using BookStitch.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace BookStitch.Services;

public sealed record PerformanceJournalReadResult
{
    public IReadOnlyList<PerformanceJournalEvent> Events { get; init; } = [];

    public bool HadIncompleteFinalLine { get; init; }

    public bool IsComplete { get; init; }
}

/// <summary>
/// Persists finalized performance reports, reads crash-tolerant journals and
/// performs best-effort rotation of old performance-session folders.
/// </summary>
public sealed class PerformanceReportArchiveService
{
    public const int DefaultMaximumSessionCount = 50;
    public const int DefaultMaximumSessionAgeDays = 30;

    private static readonly JsonSerializerOptions ReportJsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly JsonSerializerOptions JournalJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };
    private static readonly AtomicJsonStore JsonStore = new();

    public PerformanceReportArchiveService(string projectRootFolder)
    {
        if (string.IsNullOrWhiteSpace(projectRootFolder))
            throw new ArgumentException("Es wurde kein Projektordner übergeben.", nameof(projectRootFolder));

        PerformanceRootFolder = Path.Combine(
            SettingsService.GetSettingsFolder(projectRootFolder),
            "logs",
            "performance");
    }

    public string PerformanceRootFolder { get; }

    public async Task WriteReportsAsync(string sessionFolder, PerformanceSessionReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        if (report.SessionId == Guid.Empty)
            throw new ArgumentException("Der Performance-Bericht enthält keine gültige Session-ID.", nameof(report));

        var normalizedSessionFolder = ValidateSessionFolder(sessionFolder);
        Directory.CreateDirectory(normalizedSessionFolder);

        var jsonPath = Path.Combine(normalizedSessionFolder, "report.json");
        var textPath = Path.Combine(normalizedSessionFolder, "report.txt");

        var sanitizedReport = SanitizeReport(report);
        var text = FormatTextReport(sanitizedReport);

        JsonStore.Save(jsonPath, sanitizedReport, ReportJsonOptions);
        await AtomicWriteTextAsync(textPath, text).ConfigureAwait(false);
    }

    public async Task<PerformanceJournalReadResult> ReadJournalAsync(string journalFilePath)
    {
        if (string.IsNullOrWhiteSpace(journalFilePath))
            throw new ArgumentException("Es wurde kein Journalpfad übergeben.", nameof(journalFilePath));

        var lines = await File.ReadAllLinesAsync(journalFilePath).ConfigureAwait(false);
        var nonEmptyLines = lines
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToArray();

        var events = new List<PerformanceJournalEvent>(nonEmptyLines.Length);
        var hadIncompleteFinalLine = false;

        for (var index = 0; index < nonEmptyLines.Length; index++)
        {
            try
            {
                var journalEvent = JsonSerializer.Deserialize<PerformanceJournalEvent>(
                    nonEmptyLines[index],
                    JournalJsonOptions);

                if (journalEvent is null)
                    throw new JsonException("Die Journalzeile konnte nicht deserialisiert werden.");

                events.Add(journalEvent);
            }
            catch (JsonException) when (index == nonEmptyLines.Length - 1)
            {
                hadIncompleteFinalLine = true;
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException(
                    $"Das Performance-Journal enthält vor der letzten Zeile ungültiges JSON (Zeile {index + 1}).",
                    ex);
            }
        }

        var isComplete = !hadIncompleteFinalLine && events.Any(journalEvent =>
            string.Equals(journalEvent.Event, "session.completed", StringComparison.Ordinal));

        return new PerformanceJournalReadResult
        {
            Events = events,
            HadIncompleteFinalLine = hadIncompleteFinalLine,
            IsComplete = isComplete
        };
    }

    public void CleanupOldSessions(
        DateTimeOffset? nowUtc = null,
        int maximumSessionCount = DefaultMaximumSessionCount,
        int maximumSessionAgeDays = DefaultMaximumSessionAgeDays)
    {
        if (maximumSessionCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumSessionCount));

        if (maximumSessionAgeDays <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumSessionAgeDays));

        try
        {
            if (!Directory.Exists(PerformanceRootFolder))
                return;

            var cutoffUtc = (nowUtc ?? DateTimeOffset.UtcNow)
                .UtcDateTime
                .AddDays(-maximumSessionAgeDays);

            var sessionDirectories = new DirectoryInfo(PerformanceRootFolder)
                .EnumerateDirectories()
                .Where(directory => IsPerformanceSessionFolderName(directory.Name))
                .OrderByDescending(directory => directory.LastWriteTimeUtc)
                .ToList();

            for (var index = 0; index < sessionDirectories.Count; index++)
            {
                var directory = sessionDirectories[index];
                var exceedsCount = index >= maximumSessionCount;
                var exceedsAge = directory.LastWriteTimeUtc < cutoffUtc;

                if (!exceedsCount && !exceedsAge)
                    continue;

                try
                {
                    directory.Delete(recursive: true);
                }
                catch
                {
                    // Rotation is best effort. A locked diagnostic folder can be
                    // retried during a later cleanup without affecting BookStitch.
                }
            }
        }
        catch
        {
            // Diagnostics cleanup must never become an application failure source.
        }
    }

    public static string FormatTextReport(PerformanceSessionReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        var builder = new StringBuilder();
        builder.AppendLine("BookStitch Performance Report");
        builder.AppendLine($"SchemaVersion: {report.SchemaVersion.ToString(CultureInfo.InvariantCulture)}");
        builder.AppendLine($"SessionId: {report.SessionId}");
        builder.AppendLine($"StartReason: {report.StartReason}");
        builder.AppendLine($"StartedUtc: {report.StartedUtc:O}");
        builder.AppendLine($"CompletedUtc: {FormatUtc(report.CompletedUtc)}");
        builder.AppendLine($"ElapsedMilliseconds: {FormatNumber(report.ElapsedMilliseconds)}");
        builder.AppendLine($"Outcome: {report.Outcome}");
        builder.AppendLine($"ApplicationVersion: {ValueOrDash(report.ApplicationVersion)}");
        builder.AppendLine($"BuildIdentifier: {ValueOrDash(report.BuildIdentifier)}");
        builder.AppendLine($"SourceRevision: {ValueOrDash(report.SourceRevision)}");
        builder.AppendLine($"BuildConfiguration: {ValueOrDash(report.BuildConfiguration)}");
        builder.AppendLine($"SelectedParallelJobs: {ValueOrDash(report.SelectedParallelJobs)}");
        builder.AppendLine("SelectedParallelJobsScope: SessionStartSnapshot");

        if (report.Environment is not null)
        {
            builder.AppendLine("Environment:");
            builder.AppendLine($"  OS: {ValueOrDash(report.Environment.OperatingSystem)}");
            builder.AppendLine($"  OSVersion: {ValueOrDash(report.Environment.OperatingSystemVersion)}");
            builder.AppendLine($"  OSArchitecture: {ValueOrDash(report.Environment.OperatingSystemArchitecture)}");
            builder.AppendLine($"  ProcessArchitecture: {ValueOrDash(report.Environment.ProcessArchitecture)}");
            builder.AppendLine($"  DotNetRuntime: {ValueOrDash(report.Environment.DotNetRuntime)}");
            builder.AppendLine($"  CpuModel: {ValueOrDash(report.Environment.CpuModel)}");
            builder.AppendLine($"  LogicalProcessors: {FormatInteger(report.Environment.LogicalProcessorCount)}");
            builder.AppendLine($"  PhysicalCores: {FormatInteger(report.Environment.PhysicalCoreCount)}");
            builder.AppendLine($"  TotalPhysicalMemoryBytes: {FormatInteger(report.Environment.TotalPhysicalMemoryBytes)}");
            builder.AppendLine($"  AvailablePhysicalMemoryBytes: {FormatInteger(report.Environment.AvailablePhysicalMemoryBytes)}");
            builder.AppendLine($"  ProcessWorkingSetBytes: {FormatInteger(report.Environment.ProcessWorkingSetBytes)}");
        }

        AppendTool(builder, "FFmpeg", report.FfmpegContext);
        AppendTool(builder, "FFprobe", report.FfprobeContext);

        builder.AppendLine($"Drives: {report.DriveSnapshots.Count.ToString(CultureInfo.InvariantCulture)}");
        foreach (var drive in report.DriveSnapshots)
        {
            builder.Append("  - Root=").Append(ValueOrDash(drive.DriveRoot))
                .Append(" | Roles=").Append(drive.Roles)
                .Append(" | Type=").Append(ValueOrDash(drive.DriveType))
                .Append(" | VolumeLabel=").Append(ValueOrDash(drive.VolumeLabel))
                .Append(" | UserNickname=").Append(ValueOrDash(drive.UserNickname))
                .Append(" | OpticalDriveDisplayName=").Append(ValueOrDash(drive.OpticalDriveDisplayName))
                .Append(" | DeviceModel=").Append(ValueOrDash(drive.DeviceModel))
                .Append(" | BusType=").Append(ValueOrDash(drive.BusType))
                .AppendLine();
        }

        builder.AppendLine($"Operations: {report.Operations.Count.ToString(CultureInfo.InvariantCulture)}");
        foreach (var operation in report.Operations)
        {
            builder.Append("  - ").Append(operation.Name)
                .Append(" | Outcome=").Append(operation.Outcome)
                .Append(" | ElapsedMs=").Append(FormatNumber(operation.ElapsedMilliseconds))
                .Append(" | ProjectType=").Append(operation.ProjectType)
                .AppendLine();

            var workload = operation.Workload;
            builder.Append("    Workload:")
                .Append(" Tracks=").Append(FormatInteger(workload.TrackCount))
                .Append(" Files=").Append(FormatInteger(workload.FileCount))
                .Append(" Discs=").Append(FormatInteger(workload.DiscCount))
                .Append(" InputBytes=").Append(FormatInteger(workload.InputBytes))
                .Append(" OutputBytes=").Append(FormatInteger(workload.OutputBytes))
                .Append(" Retries=").Append(FormatInteger(workload.RetryCount))
                .AppendLine();

            if (operation.Statistics is not null)
            {
                var statistics = operation.Statistics;
                builder.Append("    Statistics:")
                    .Append(" WallClockMs=").Append(FormatNumber(statistics.WallClockMilliseconds))
                    .Append(" ChildSumMs=").Append(FormatNumber(statistics.SumChildOperationElapsedMilliseconds))
                    .Append(" PhaseSumMs=").Append(FormatNumber(statistics.SumPhaseElapsedMilliseconds))
                    .Append(" MaxConcurrency=").Append(statistics.MaxExternalProcessConcurrency.ToString(CultureInfo.InvariantCulture))
                    .Append(" InputMBps=").Append(FormatNumber(statistics.InputMegabytesPerSecond))
                    .Append(" TracksPerSecond=").Append(FormatNumber(statistics.TracksPerSecond))
                    .Append(" RTF=").Append(FormatNumber(statistics.RealTimeFactor))
                    .AppendLine();
            }

            if (operation.DriveRoleReferences.Count > 0)
            {
                builder.Append("    DriveRoleReferences: ")
                    .AppendJoin(", ", operation.DriveRoleReferences.Select(reference => $"{reference.DriveId}:{reference.Roles}"))
                    .AppendLine();
            }

            AppendMetrics(builder, "    Metrics: ", operation.Metrics);
            foreach (var process in operation.ExternalProcessAggregates)
                AppendExternalProcess(builder, "    External", process);

            foreach (var phase in operation.Phases)
            {
                builder.Append("    Phase: ").Append(phase.Name)
                    .Append(" | Outcome=").Append(phase.Outcome)
                    .Append(" | ElapsedMs=").Append(FormatNumber(phase.ElapsedMilliseconds))
                    .AppendLine();

                AppendMetrics(builder, "      Metrics: ", phase.Metrics);
                foreach (var process in phase.ExternalProcessAggregates)
                    AppendExternalProcess(builder, "      External", process);
            }
        }

        return builder.ToString();
    }

    private static void AppendMetrics(StringBuilder builder, string prefix, IEnumerable<PerformanceMetric> metrics)
    {
        foreach (var metric in metrics.OrderBy(item => item.Name, StringComparer.Ordinal))
            builder.Append(prefix).Append(metric.Name).Append('=').Append(FormatNumber(metric.Value))
                .Append(string.IsNullOrWhiteSpace(metric.Unit) ? string.Empty : " " + metric.Unit).AppendLine();
    }

    private static PerformanceSessionReport SanitizeReport(PerformanceSessionReport report)
    {
        return report with
        {
            Operations = report.Operations.Select(operation => operation with
            {
                FailureMessage = SanitizeText(operation.FailureMessage),
                Phases = operation.Phases.Select(phase => phase with
                {
                    FailureMessage = SanitizeText(phase.FailureMessage)
                }).ToList()
            }).ToList()
        };
    }

    private static string? SanitizeText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return value;

        var sanitized = System.Text.RegularExpressions.Regex.Replace(
            value,
            @"(?i)\b[A-Z]:\\[^\r\n]*",
            "<path>");
        return sanitized;
    }

    private string ValidateSessionFolder(string sessionFolder)
    {
        if (string.IsNullOrWhiteSpace(sessionFolder))
            throw new ArgumentException("Es wurde kein Session-Ordner übergeben.", nameof(sessionFolder));

        var fullRoot = Path.GetFullPath(PerformanceRootFolder);
        var fullSession = Path.GetFullPath(sessionFolder);
        var rootPrefix = fullRoot.EndsWith(Path.DirectorySeparatorChar)
            ? fullRoot
            : fullRoot + Path.DirectorySeparatorChar;

        if (!fullSession.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Der Session-Ordner liegt außerhalb des Performance-Logordners.", nameof(sessionFolder));

        return fullSession;
    }

    private static async Task AtomicWriteTextAsync(string filePath, string content)
    {
        var directory = Path.GetDirectoryName(filePath)
            ?? throw new InvalidOperationException("Für die Berichtsdatei konnte kein Ordner ermittelt werden.");

        Directory.CreateDirectory(directory);

        var temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(filePath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            await File.WriteAllTextAsync(
                temporaryPath,
                content,
                new UTF8Encoding(false)).ConfigureAwait(false);

            File.Move(temporaryPath, filePath, overwrite: true);
        }
        finally
        {
            try
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
            }
            catch
            {
                // A temporary diagnostics file may remain, but report generation
                // itself must not be converted into a second failure.
            }
        }
    }

    private static bool IsPerformanceSessionFolderName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length < 30)
            return false;

        if (!string.Equals(name.Substring(19, 3), " - ", StringComparison.Ordinal))
            return false;

        return DateTime.TryParseExact(
            name[..19],
            "yyyy.MM.dd HH.mm.ss",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out _);
    }

    private static void AppendTool(StringBuilder builder, string name, PerformanceToolSnapshot? tool)
    {
        if (tool is null)
            return;

        builder.Append(name).Append(": Available=").Append(tool.Available)
            .Append(" | Version=").Append(ValueOrDash(tool.Version))
            .Append(" | FileVersion=").Append(ValueOrDash(tool.FileVersion))
            .Append(" | Architecture=").Append(ValueOrDash(tool.Architecture))
            .AppendLine();
    }

    private static void AppendExternalProcess(
        StringBuilder builder,
        string prefix,
        PerformanceExternalProcessAggregate process)
    {
        builder.Append(prefix).Append(": Tool=").Append(ValueOrDash(process.Tool))
            .Append(" | Purpose=").Append(ValueOrDash(process.Purpose))
            .Append(" | Calls=").Append(process.CallCount.ToString(CultureInfo.InvariantCulture))
            .Append(" | Success=").Append(process.SuccessCount.ToString(CultureInfo.InvariantCulture))
            .Append(" | Failed=").Append(process.FailureCount.ToString(CultureInfo.InvariantCulture))
            .Append(" | Cancelled=").Append(process.CancelledCount.ToString(CultureInfo.InvariantCulture))
            .Append(" | SumMs=").Append(process.SumElapsedMilliseconds.ToString("0.###", CultureInfo.InvariantCulture))
            .Append(" | MeanMs=").Append(FormatNumber(process.MeanElapsedMilliseconds))
            .Append(" | MaxConcurrency=").Append(process.MaxConcurrency.ToString(CultureInfo.InvariantCulture))
            .AppendLine();
    }

    private static string FormatUtc(DateTimeOffset? value) =>
        value?.ToString("O", CultureInfo.InvariantCulture) ?? "-";

    private static string FormatNumber(double? value) =>
        value?.ToString("0.###", CultureInfo.InvariantCulture) ?? "-";

    private static string FormatInteger(int? value) =>
        value?.ToString(CultureInfo.InvariantCulture) ?? "-";

    private static string FormatInteger(long? value) =>
        value?.ToString(CultureInfo.InvariantCulture) ?? "-";

    private static string ValueOrDash(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "-" : value.Trim();
}
