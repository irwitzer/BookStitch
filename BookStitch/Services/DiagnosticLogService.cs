using BookStitch.Models;
using System.Globalization;
using System.IO;
using System.Text;

namespace BookStitch.Services;

internal sealed record DiagnosticLogPolicy(
    long MaximumActiveLogBytes,
    long MaximumTotalLogBytes,
    int MaximumLogAgeDays)
{
    public static DiagnosticLogPolicy Default { get; } = new(
        MaximumActiveLogBytes: 10L * 1024 * 1024,
        MaximumTotalLogBytes: 200L * 1024 * 1024,
        MaximumLogAgeDays: 90);
}

public sealed class DiagnosticLogService
{
    private static readonly object ProcessSync = new();
    private static readonly Encoding LogEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    private readonly string _logFolder;
    private readonly DiagnosticLogPolicy _policy;

    public DiagnosticLogService(string dataRootFolder)
        : this(dataRootFolder, DiagnosticLogPolicy.Default)
    {
    }

    internal DiagnosticLogService(string dataRootFolder, DiagnosticLogPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        _policy = policy;
        _logFolder = Path.Combine(SettingsService.GetSettingsFolder(dataRootFolder), "logs");

        try
        {
            lock (ProcessSync)
            {
                Directory.CreateDirectory(_logFolder);
                MigrateLegacyLogNames();
                CleanupOldLogs(LogFilePath);
            }
        }
        catch
        {
            // Logging setup must never interrupt the application workflow.
        }
    }

    public string LogFolder => _logFolder;

    public string LogFilePath => GetDailyLogPath(DateTime.Now);

    public void WriteApplicationEvent(string eventName, string message)
    {
        Append($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {eventName}: {message}{Environment.NewLine}");
    }

    public void WritePerformanceEvent(
        string operationName,
        TimeSpan elapsed,
        string? details = null)
    {
        var elapsedMilliseconds = elapsed.TotalMilliseconds
            .ToString("0.0", CultureInfo.InvariantCulture);
        var detailText = string.IsNullOrWhiteSpace(details)
            ? string.Empty
            : $" | {details.Trim()}";

        Append($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] PERFORMANCE: {operationName} | {elapsedMilliseconds} ms{detailText}{Environment.NewLine}");
    }

    public void WriteTrackScan(
        string context,
        string rootFolder,
        IEnumerable<TrackInfo> tracks,
        IEnumerable<string>? excludedPaths = null)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] TRACK SCAN: {context}");
        builder.AppendLine($"Root: {rootFolder}");

        foreach (var path in excludedPaths ?? [])
            builder.AppendLine($"EXCLUDED | {NormalizePath(path)}");

        foreach (var track in tracks)
        {
            builder.Append("TRACK")
                .Append(" | Index=").Append(track.Index.ToString(CultureInfo.InvariantCulture))
                .Append(" | Disc=").Append(track.DiscNumber?.ToString(CultureInfo.InvariantCulture) ?? "-")
                .Append(" | Track=").Append(track.TrackNumber?.ToString(CultureInfo.InvariantCulture) ?? "-")
                .Append(" | Relative=").Append(track.RelativeFolder)
                .Append(" | File=").Append(track.FileName)
                .Append(" | Source=").Append(NormalizePath(track.FilePath))
                .Append(" | Converted=").Append(NormalizePath(track.PreparedConvertedPath))
                .Append(" | Warning=").Append(track.Warning)
                .AppendLine();
        }

        builder.AppendLine();
        Append(builder.ToString());
    }

    public void WriteError(string context, Exception exception)
    {
        Append($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] ERROR: {context}{Environment.NewLine}{exception}{Environment.NewLine}{Environment.NewLine}");
    }

    private void Append(string text)
    {
        try
        {
            lock (ProcessSync)
            {
                Directory.CreateDirectory(_logFolder);
                var activeLogPath = GetDailyLogPath(DateTime.Now);
                RotateActiveLogIfNeeded(activeLogPath, LogEncoding.GetByteCount(text));
                File.AppendAllText(activeLogPath, text, LogEncoding);
                CleanupOldLogs(activeLogPath);
            }
        }
        catch
        {
            // Logging must never interrupt the application workflow.
        }
    }

    private string GetDailyLogPath(DateTime date) =>
        Path.Combine(_logFolder, $"{date:yyyy.MM.dd} - BookStitch.log");

    private void RotateActiveLogIfNeeded(string activeLogPath, long appendByteCount)
    {
        try
        {
            if (!File.Exists(activeLogPath))
                return;

            var currentLength = new FileInfo(activeLogPath).Length;
            if (currentLength <= 0 || currentLength + appendByteCount <= _policy.MaximumActiveLogBytes)
                return;

            var datePrefix = Path.GetFileName(activeLogPath)[..10];
            var sequence = 1;
            string archivePath;
            do
            {
                archivePath = Path.Combine(
                    _logFolder,
                    $"{datePrefix} - BookStitch - {sequence:000}.log");
                sequence++;
            }
            while (File.Exists(archivePath));

            File.Move(activeLogPath, archivePath);
        }
        catch
        {
            // If rotation fails, keep appending to the active file rather than losing the log entry.
        }
    }

    private void MigrateLegacyLogNames()
    {
        try
        {
            foreach (var legacyPath in Directory.EnumerateFiles(_logFolder, "BookStitch-????-??-??.log", SearchOption.TopDirectoryOnly))
            {
                var fileName = Path.GetFileNameWithoutExtension(legacyPath);
                var datePart = fileName["BookStitch-".Length..];
                if (!DateTime.TryParseExact(
                        datePart,
                        "yyyy-MM-dd",
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.None,
                        out var date))
                {
                    continue;
                }

                var targetPath = GetDailyLogPath(date);
                if (!File.Exists(targetPath))
                {
                    File.Move(legacyPath, targetPath);
                    continue;
                }

                var legacyText = File.ReadAllText(legacyPath, LogEncoding);
                File.AppendAllText(targetPath, legacyText, LogEncoding);
                File.Delete(legacyPath);
            }
        }
        catch
        {
            // Alte Lognamen bleiben im Zweifel erhalten und können beim nächsten Start erneut migriert werden.
        }
    }

    private void CleanupOldLogs(string activeLogPath)
    {
        try
        {
            var activeFullPath = Path.GetFullPath(activeLogPath);
            var cutoffLocal = DateTime.Now.AddDays(-_policy.MaximumLogAgeDays);

            foreach (var file in EnumerateManagedLogFiles()
                         .Where(file =>
                             !IsSamePath(file.FullName, activeFullPath) &&
                             file.LastWriteTime < cutoffLocal)
                         .ToList())
            {
                TryDelete(file);
            }

            TrimTotalLogVolume(activeFullPath);
        }
        catch
        {
            // Logrotation must never prevent application startup or logging.
        }
    }

    private void TrimTotalLogVolume(string activeFullPath)
    {
        var files = EnumerateManagedLogFiles()
            .OrderBy(file => file.LastWriteTime)
            .ThenBy(file => file.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var totalBytes = files.Sum(file => file.Length);
        if (totalBytes <= _policy.MaximumTotalLogBytes)
            return;

        foreach (var file in files)
        {
            if (totalBytes <= _policy.MaximumTotalLogBytes)
                break;
            if (IsSamePath(file.FullName, activeFullPath))
                continue;

            var length = file.Length;
            if (TryDelete(file))
                totalBytes -= length;
        }
    }

    private List<FileInfo> EnumerateManagedLogFiles() => Directory
        .EnumerateFiles(_logFolder, "*.log", SearchOption.TopDirectoryOnly)
        .Where(path => IsBookStitchLogFileName(Path.GetFileName(path)))
        .Select(path => new FileInfo(path))
        .ToList();

    private static bool TryDelete(FileInfo file)
    {
        try
        {
            file.Delete();
            return true;
        }
        catch
        {
            // Locked logs are retried on a later cleanup pass.
            return false;
        }
    }

    private static bool IsSamePath(string left, string right) =>
        string.Equals(
            Path.GetFullPath(left),
            Path.GetFullPath(right),
            StringComparison.OrdinalIgnoreCase);

    internal static bool IsBookStitchLogFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return false;

        if (fileName.StartsWith("BookStitch-", StringComparison.OrdinalIgnoreCase))
            return true;

        if (!fileName.EndsWith(".log", StringComparison.OrdinalIgnoreCase) || fileName.Length < 10)
            return false;

        if (!DateTime.TryParseExact(
                fileName[..10],
                "yyyy.MM.dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out _))
        {
            return false;
        }

        var stem = Path.GetFileNameWithoutExtension(fileName);
        if (stem.EndsWith(" - BookStitch", StringComparison.OrdinalIgnoreCase))
            return true;

        const string rotatedMarker = " - BookStitch - ";
        var markerIndex = stem.IndexOf(rotatedMarker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex != 10)
            return false;

        return int.TryParse(
            stem[(markerIndex + rotatedMarker.Length)..],
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out var sequence) && sequence > 0;
    }

    private static string NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;

        try { return Path.GetFullPath(path); }
        catch { return path.Trim(); }
    }
}
