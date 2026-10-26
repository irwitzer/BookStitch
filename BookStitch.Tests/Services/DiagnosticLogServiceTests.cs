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
        Assert.StartsWith(service.LogFolder, service.LogFilePath, StringComparison.OrdinalIgnoreCase);
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
    [Fact]
    public void WritePerformanceEvent_CreatesReadableTimingEntry()
    {
        using var folder = new TemporaryFolder();
        var service = new DiagnosticLogService(folder.Path);

        service.WritePerformanceEvent(
            "STARTUP",
            TimeSpan.FromMilliseconds(125.5),
            "Tracks=42");

        var text = File.ReadAllText(service.LogFilePath);
        Assert.Contains("PERFORMANCE: STARTUP", text);
        Assert.Contains("125.5 ms", text);
        Assert.Contains("Tracks=42", text);
    }


    [Fact]
    public void DefaultPolicy_UsesTenMiBActiveTwoHundredMiBTotalAndNinetyDaysWithoutFileCountLimit()
    {
        var policy = DiagnosticLogPolicy.Default;

        Assert.Equal(10L * 1024 * 1024, policy.MaximumActiveLogBytes);
        Assert.Equal(200L * 1024 * 1024, policy.MaximumTotalLogBytes);
        Assert.Equal(90, policy.MaximumLogAgeDays);
        Assert.DoesNotContain(
            typeof(DiagnosticLogPolicy).GetProperties(),
            property => property.Name == "MaximumLogFiles");
    }

    [Fact]
    public void Constructor_DoesNotTrimYoungSmallLogsByFileCount()
    {
        using var folder = new TemporaryFolder();
        var logFolder = Path.Combine(folder.Path, "software-settings", "logs");
        Directory.CreateDirectory(logFolder);

        var expectedPaths = new List<string>();
        for (var index = 1; index <= 25; index++)
        {
            var path = Path.Combine(logFolder, $"{DateTime.Now.AddDays(-index):yyyy.MM.dd} - BookStitch.log");
            File.WriteAllText(path, $"log-{index}");
            File.SetLastWriteTime(path, DateTime.Now.AddDays(-index));
            expectedPaths.Add(path);
        }

        _ = new DiagnosticLogService(folder.Path);

        Assert.All(expectedPaths, path => Assert.True(File.Exists(path), path));
    }

    [Fact]
    public void Constructor_RemovesManagedLogsOlderThanNinetyDays()
    {
        using var folder = new TemporaryFolder();
        var logFolder = Path.Combine(folder.Path, "software-settings", "logs");
        Directory.CreateDirectory(logFolder);
        var recentPath = Path.Combine(logFolder, $"{DateTime.Now.AddDays(-89):yyyy.MM.dd} - BookStitch.log");
        var expiredPath = Path.Combine(logFolder, $"{DateTime.Now.AddDays(-91):yyyy.MM.dd} - BookStitch.log");
        File.WriteAllText(recentPath, "recent");
        File.WriteAllText(expiredPath, "expired");
        File.SetLastWriteTime(recentPath, DateTime.Now.AddDays(-89));
        File.SetLastWriteTime(expiredPath, DateTime.Now.AddDays(-91));

        _ = new DiagnosticLogService(folder.Path);

        Assert.True(File.Exists(recentPath));
        Assert.False(File.Exists(expiredPath));
    }

    [Fact]
    public void WriteApplicationEvent_RotatesActiveDailyLogBeforeNextAppend()
    {
        using var folder = new TemporaryFolder();
        var policy = new DiagnosticLogPolicy(
            MaximumActiveLogBytes: 180,
            MaximumTotalLogBytes: 10_000,
            MaximumLogAgeDays: 90);
        var service = new DiagnosticLogService(folder.Path, policy);

        service.WriteApplicationEvent("FIRST", new string('A', 140));
        service.WriteApplicationEvent("SECOND", "after rotation");

        var archivePath = Path.Combine(
            service.LogFolder,
            $"{DateTime.Now:yyyy.MM.dd} - BookStitch - 001.log");
        Assert.True(File.Exists(archivePath));
        Assert.Contains("FIRST", File.ReadAllText(archivePath), StringComparison.Ordinal);
        Assert.True(File.Exists(service.LogFilePath));
        var activeText = File.ReadAllText(service.LogFilePath);
        Assert.Contains("SECOND", activeText, StringComparison.Ordinal);
        Assert.DoesNotContain("FIRST", activeText, StringComparison.Ordinal);
    }

    [Fact]
    public void RotatedDailyLogName_IsRecognizedAsBookStitchLog()
    {
        Assert.True(DiagnosticLogService.IsBookStitchLogFileName("2026.09.12 - BookStitch - 001.log"));
        Assert.True(DiagnosticLogService.IsBookStitchLogFileName("2026.09.12 - BookStitch.log"));
        Assert.False(DiagnosticLogService.IsBookStitchLogFileName("2026.09.12 - Other - 001.log"));
    }

    [Fact]
    public void Constructor_TrimsManagedLogsByTotalVolumeWithoutDeletingActiveDailyLog()
    {
        using var folder = new TemporaryFolder();
        var logFolder = Path.Combine(folder.Path, "software-settings", "logs");
        Directory.CreateDirectory(logFolder);
        var activePath = Path.Combine(logFolder, $"{DateTime.Now:yyyy.MM.dd} - BookStitch.log");
        var newerArchive = Path.Combine(logFolder, $"{DateTime.Now.AddDays(-1):yyyy.MM.dd} - BookStitch - 001.log");
        var oldestArchive = Path.Combine(logFolder, $"{DateTime.Now.AddDays(-2):yyyy.MM.dd} - BookStitch - 001.log");
        File.WriteAllText(activePath, new string('A', 90));
        File.WriteAllText(newerArchive, new string('B', 90));
        File.WriteAllText(oldestArchive, new string('C', 90));
        File.SetLastWriteTime(newerArchive, DateTime.Now.AddHours(-2));
        File.SetLastWriteTime(oldestArchive, DateTime.Now.AddHours(-3));

        var policy = new DiagnosticLogPolicy(
            MaximumActiveLogBytes: 1_000,
            MaximumTotalLogBytes: 180,
            MaximumLogAgeDays: 90);
        _ = new DiagnosticLogService(folder.Path, policy);

        Assert.True(File.Exists(activePath));
        Assert.True(File.Exists(newerArchive));
        Assert.False(File.Exists(oldestArchive));
    }

    [Fact]
    public void MultipleInstances_CanAppendAndRotateWithoutLosingEntries()
    {
        using var folder = new TemporaryFolder();
        var policy = new DiagnosticLogPolicy(
            MaximumActiveLogBytes: 400,
            MaximumTotalLogBytes: 1_000_000,
            MaximumLogAgeDays: 90);
        var first = new DiagnosticLogService(folder.Path, policy);
        var second = new DiagnosticLogService(folder.Path, policy);

        Parallel.For(0, 20, index =>
        {
            var service = index % 2 == 0 ? first : second;
            service.WriteApplicationEvent($"PARALLEL-{index:00}", new string((char)('A' + (index % 26)), 80));
        });

        var combined = string.Concat(
            Directory.EnumerateFiles(first.LogFolder, "*.log")
                .Where(path => DiagnosticLogService.IsBookStitchLogFileName(Path.GetFileName(path)))
                .Select(File.ReadAllText));
        for (var index = 0; index < 20; index++)
            Assert.Contains($"PARALLEL-{index:00}", combined, StringComparison.Ordinal);
    }
}
