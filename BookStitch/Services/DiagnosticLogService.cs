using BookStitch.Models;
using System.Globalization;
using System.IO;
using System.Text;

namespace BookStitch.Services;

public sealed class DiagnosticLogService
{
    private const int MaximumLogFiles = 20;
    private const int MaximumLogAgeDays = 30;

    private readonly string _logFolder;
    private readonly object _sync = new();

    public DiagnosticLogService(string dataRootFolder)
    {
        _logFolder = Path.Combine(SettingsService.GetSettingsFolder(dataRootFolder), "logs");
        Directory.CreateDirectory(_logFolder);
        MigrateLegacyLogNames();
        CleanupOldLogs();
    }

    public string LogFolder => _logFolder;

    public string LogFilePath => Path.Combine(_logFolder, $"{DateTime.Now:yyyy.MM.dd} - BookStitch.log");

    public void WriteApplicationEvent(string eventName, string message)
    {
        Append($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {eventName}: {message}{Environment.NewLine}");
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
            lock (_sync)
                File.AppendAllText(LogFilePath, text, Encoding.UTF8);
        }
        catch
        {
            // Logging must never interrupt the application workflow.
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

                var targetPath = Path.Combine(_logFolder, $"{date:yyyy.MM.dd} - BookStitch.log");
                if (!File.Exists(targetPath))
                {
                    File.Move(legacyPath, targetPath);
                    continue;
                }

                var legacyText = File.ReadAllText(legacyPath, Encoding.UTF8);
                File.AppendAllText(targetPath, legacyText, Encoding.UTF8);
                File.Delete(legacyPath);
            }
        }
        catch
        {
            // Alte Lognamen bleiben im Zweifel erhalten und können beim nächsten Start erneut migriert werden.
        }
    }

    private void CleanupOldLogs()
    {
        try
        {
            var files = Directory
                .EnumerateFiles(_logFolder, "*.log", SearchOption.TopDirectoryOnly)
                .Where(path => IsBookStitchLogFileName(Path.GetFileName(path)))
                .Select(path => new FileInfo(path))
                .OrderByDescending(file => file.LastWriteTime)
                .ToList();

            var cutoffLocal = DateTime.Now.AddDays(-MaximumLogAgeDays);
            foreach (var file in files.Where((file, index) => index >= MaximumLogFiles || file.LastWriteTime < cutoffLocal))
            {
                try { file.Delete(); }
                catch { /* Einzelne gesperrte Logs werden beim nächsten Start erneut geprüft. */ }
            }
        }
        catch
        {
            // Logrotation darf den Start der Anwendung nicht verhindern.
        }
    }

    private static bool IsBookStitchLogFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return false;

        if (fileName.StartsWith("BookStitch-", StringComparison.OrdinalIgnoreCase))
            return true;

        return fileName.EndsWith(" - BookStitch.log", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;

        try { return Path.GetFullPath(path); }
        catch { return path.Trim(); }
    }
}
