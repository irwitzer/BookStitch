using BookStitch.Services;
using BookStitch.Tests.TestHelpers;
using System.IO;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class DiagnosticLogServiceTests
{
    [Fact]
    public void Constructor_UsesSoftwareSettingsLogsFolder()
    {
        using var folder = new TemporaryFolder();
        var service = new DiagnosticLogService(folder.Path);

        Assert.Equal(
            Path.Combine(folder.Path, "software-settings", "logs"),
            service.LogFolder);
        Assert.True(service.LogFilePath.StartsWith(service.LogFolder, StringComparison.OrdinalIgnoreCase));
        Assert.Equal($"{DateTime.Now:yyyy.MM.dd} - BookStitch.log", Path.GetFileName(service.LogFilePath));
    }


    [Fact]
    public void Constructor_MigratesLegacyDailyLogName()
    {
        using var folder = new TemporaryFolder();
        var logFolder = Path.Combine(folder.Path, "software-settings", "logs");
        Directory.CreateDirectory(logFolder);
        var legacyPath = Path.Combine(logFolder, "BookStitch-2026-07-18.log");
        File.WriteAllText(legacyPath, "Legacy");

        _ = new DiagnosticLogService(folder.Path);

        var migratedPath = Path.Combine(logFolder, "2026.07.18 - BookStitch.log");
        Assert.False(File.Exists(legacyPath));
        Assert.True(File.Exists(migratedPath));
        Assert.Contains("Legacy", File.ReadAllText(migratedPath));
    }

    [Fact]
    public void WriteApplicationEvent_CreatesReadableDailyLog()
    {
        using var folder = new TemporaryFolder();
        var service = new DiagnosticLogService(folder.Path);

        service.WriteApplicationEvent("TEST", "Nachricht");

        Assert.True(File.Exists(service.LogFilePath));
        var text = File.ReadAllText(service.LogFilePath);
        Assert.Contains("TEST", text);
        Assert.Contains("Nachricht", text);
    }
}
