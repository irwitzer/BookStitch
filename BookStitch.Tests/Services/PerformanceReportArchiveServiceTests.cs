using BookStitch.Models;
using BookStitch.Services;
using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class PerformanceReportArchiveServiceTests
{
    [Fact]
    public void Text_report_renders_paused_and_stable_operation_and_phase_metrics()
    {
        var trace = new PerformanceTraceService(true);
        trace.TryStartSession(new());
        var operation = trace.StartOperation("disc.mp3.import");
        var phase = trace.StartPhase(operation.OperationId, "file.copy");
        trace.RecordMetric(operation.OperationId, "WorkerLimit", 20, "workers");
        trace.RecordPhaseMetric(phase.PhaseId, "InputBytes", 2048, "bytes");
        phase.Pause();
        operation.Pause();
        var report = trace.CompleteSession(PerformanceOutcome.Success)!;
        var text = PerformanceReportArchiveService.FormatTextReport(report);
        Assert.Contains("disc.mp3.import | Outcome=Paused", text);
        Assert.Contains("Phase: file.copy | Outcome=Paused", text);
        Assert.Contains("WorkerLimit=20 workers", text);
        Assert.Contains("InputBytes=2048 bytes", text);
    }

    [Fact]
    public async Task Reports_AreWrittenAsSchemaVersionedJsonAndHumanReadableText()
    {
        var root = CreateTemporaryRoot();
        try
        {
            var sessionId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
            var startedUtc = new DateTimeOffset(2026, 8, 8, 5, 0, 0, TimeSpan.Zero);
            var sessionFolder = Path.Combine(
                SettingsService.GetSettingsFolder(root),
                "logs",
                "performance",
                PerformanceReportStorageService.CreateSessionFolderName(startedUtc, sessionId));

            var report = new PerformanceSessionReport
            {
                SessionId = sessionId,
                StartedUtc = startedUtc,
                CompletedUtc = startedUtc.AddSeconds(2),
                ElapsedMilliseconds = 2000,
                Outcome = PerformanceOutcome.Success,
                ApplicationVersion = "1.0.1",
                SourceRevision = "63f3075e0ea4bcbbc4b71597136c66d1b628e079",
                BuildConfiguration = "Debug",
                Environment = new PerformanceEnvironmentSnapshot
                {
                    OperatingSystem = "Windows Test",
                    OperatingSystemVersion = "10.0-test",
                    OperatingSystemArchitecture = "X64",
                    ProcessArchitecture = "X64",
                    DotNetRuntime = ".NET 10",
                    LogicalProcessorCount = 16,
                    PhysicalCoreCount = 8,
                    CpuModel = "Test CPU",
                    TotalPhysicalMemoryBytes = 34359738368,
                    AvailablePhysicalMemoryBytes = 17179869184,
                    ProcessWorkingSetBytes = 268435456
                },
                DriveSnapshots =
                [
                    new PerformanceDriveSnapshot
                    {
                        DriveId = Guid.NewGuid(),
                        DriveRoot = "E:\\",
                        Roles = PerformanceDriveRole.OpticalSource,
                        DriveType = "CDRom",
                        VolumeLabel = "AUDIO_CD",
                        OpticalDriveDisplayName = "ASUS DRW",
                        UserNickname = "Unteres Laufwerk"
                    }
                ],
                Operations =
                [
                    new PerformanceOperationReport
                    {
                        OperationId = Guid.NewGuid(),
                        SessionId = sessionId,
                        Name = "disc.audio.rip",
                        ProjectType = PerformanceProjectType.AudioCd,
                        Outcome = PerformanceOutcome.Success,
                        ElapsedMilliseconds = 1500,
                        FailureMessage = @"Failed C:\Users\Alice\Books\secret.m4b",
                        Statistics = new PerformanceOperationStatistics
                        {
                            WallClockMilliseconds = 1500,
                            TracksPerSecond = 8,
                            MaxExternalProcessConcurrency = 2
                        },
                        Workload = new PerformanceWorkloadSnapshot
                        {
                            ProjectType = PerformanceProjectType.AudioCd,
                            TrackCount = 12,
                            RetryCount = 1
                        },
                        ExternalProcessAggregates =
                        [
                            new PerformanceExternalProcessAggregate
                            {
                                Tool = "ffmpeg",
                                CallCount = 12,
                                SuccessCount = 12,
                                SumElapsedMilliseconds = 900,
                                MeanElapsedMilliseconds = 75,
                                MaxConcurrency = 2
                            }
                        ]
                    }
                ]
            };

            var archive = new PerformanceReportArchiveService(root);
            await archive.WriteReportsAsync(sessionFolder, report);

            var jsonPath = Path.Combine(sessionFolder, "report.json");
            var textPath = Path.Combine(sessionFolder, "report.txt");

            Assert.True(File.Exists(jsonPath));
            Assert.True(File.Exists(textPath));
            Assert.False(StartsWithUtf8Bom(await File.ReadAllBytesAsync(jsonPath)));
            Assert.False(StartsWithUtf8Bom(await File.ReadAllBytesAsync(textPath)));

            using var json = JsonDocument.Parse(await File.ReadAllTextAsync(jsonPath));
            Assert.Equal(1, json.RootElement.GetProperty("schemaVersion").GetInt32());
            Assert.Equal("Success", json.RootElement.GetProperty("outcome").GetString());
            Assert.Equal("AUDIO_CD", json.RootElement.GetProperty("driveSnapshots")[0].GetProperty("volumeLabel").GetString());

            var text = await File.ReadAllTextAsync(textPath);
            Assert.Contains("BookStitch Performance Report", text);
            Assert.Contains("SchemaVersion: 1", text);
            Assert.Contains("OSArchitecture: X64", text);
            Assert.Contains("AvailablePhysicalMemoryBytes: 17179869184", text);
            Assert.Contains("ProcessWorkingSetBytes: 268435456", text);
            Assert.Contains("VolumeLabel=AUDIO_CD", text);
            Assert.Contains("UserNickname=Unteres Laufwerk", text);
            Assert.Contains("disc.audio.rip", text);
            Assert.Contains("Tool=ffmpeg", text);
            Assert.Contains("Statistics:", text);
            Assert.Contains("TracksPerSecond=8", text);
            Assert.DoesNotContain("secret.m4b", await File.ReadAllTextAsync(jsonPath));
        }
        finally
        {
            DeleteTemporaryRoot(root);
        }
    }

    [Fact]
    public async Task JournalReader_IgnoresOnlyIncompleteFinalLineAndMarksSessionIncomplete()
    {
        var root = CreateTemporaryRoot();
        try
        {
            var archive = new PerformanceReportArchiveService(root);
            var sessionId = Guid.NewGuid();
            var journalPath = Path.Combine(root, "events.jsonl");
            var sessionIdText = sessionId.ToString();

            var content =
                $"{{\"schemaVersion\":1,\"event\":\"session.started\",\"sessionId\":\"{sessionIdText}\",\"utc\":\"2026-08-08T05:00:00+00:00\"}}\n" +
                $"{{\"schemaVersion\":1,\"event\":\"operation.completed\",\"sessionId\":\"{sessionIdText}\",\"utc\":\"2026-08-08T05:00:01+00:00\"}}\n" +
                "{\"schemaVersion\":1,\"event\":";

            await File.WriteAllTextAsync(journalPath, content, new UTF8Encoding(false));

            var result = await archive.ReadJournalAsync(journalPath);

            Assert.Equal(2, result.Events.Count);
            Assert.True(result.HadIncompleteFinalLine);
            Assert.False(result.IsComplete);
        }
        finally
        {
            DeleteTemporaryRoot(root);
        }
    }

    [Fact]
    public async Task JournalReader_RejectsInvalidJsonBeforeFinalLine()
    {
        var root = CreateTemporaryRoot();
        try
        {
            var archive = new PerformanceReportArchiveService(root);
            var journalPath = Path.Combine(root, "events.jsonl");
            var content =
                "{not-json}\n" +
                "{\"schemaVersion\":1,\"event\":\"session.completed\",\"sessionId\":\"11111111-2222-3333-4444-555555555555\",\"utc\":\"2026-08-08T05:00:01+00:00\"}";

            await File.WriteAllTextAsync(journalPath, content, new UTF8Encoding(false));

            await Assert.ThrowsAsync<InvalidDataException>(() => archive.ReadJournalAsync(journalPath));
        }
        finally
        {
            DeleteTemporaryRoot(root);
        }
    }

    [Fact]
    public async Task JournalReader_DetectsCompletedSession()
    {
        var root = CreateTemporaryRoot();
        try
        {
            var archive = new PerformanceReportArchiveService(root);
            var journalPath = Path.Combine(root, "events.jsonl");
            var sessionId = Guid.NewGuid();
            var sessionIdText = sessionId.ToString();
            var content =
                $"{{\"schemaVersion\":1,\"event\":\"session.started\",\"sessionId\":\"{sessionIdText}\",\"utc\":\"2026-08-08T05:00:00+00:00\"}}\n" +
                $"{{\"schemaVersion\":1,\"event\":\"session.completed\",\"sessionId\":\"{sessionIdText}\",\"utc\":\"2026-08-08T05:00:02+00:00\"}}";

            await File.WriteAllTextAsync(journalPath, content, new UTF8Encoding(false));

            var result = await archive.ReadJournalAsync(journalPath);

            Assert.Equal(2, result.Events.Count);
            Assert.False(result.HadIncompleteFinalLine);
            Assert.True(result.IsComplete);
        }
        finally
        {
            DeleteTemporaryRoot(root);
        }
    }

    [Fact]
    public void CleanupOldSessions_AppliesCountAndAgeLimitsButPreservesUnrelatedFolders()
    {
        var root = CreateTemporaryRoot();
        try
        {
            var archive = new PerformanceReportArchiveService(root);
            Directory.CreateDirectory(archive.PerformanceRootFolder);

            var now = new DateTimeOffset(2026, 8, 8, 5, 0, 0, TimeSpan.Zero);
            var newest = CreateSessionDirectory(archive.PerformanceRootFolder, "2026.08.08 04.00.00 - 11111111", now.UtcDateTime.AddHours(-1));
            var second = CreateSessionDirectory(archive.PerformanceRootFolder, "2026.08.07 04.00.00 - 22222222", now.UtcDateTime.AddDays(-1));
            var overCount = CreateSessionDirectory(archive.PerformanceRootFolder, "2026.08.06 04.00.00 - 33333333", now.UtcDateTime.AddDays(-2));
            var tooOld = CreateSessionDirectory(archive.PerformanceRootFolder, "2026.06.01 04.00.00 - 44444444", now.UtcDateTime.AddDays(-68));
            var unrelated = Path.Combine(archive.PerformanceRootFolder, "do-not-delete");
            Directory.CreateDirectory(unrelated);

            archive.CleanupOldSessions(now, maximumSessionCount: 2, maximumSessionAgeDays: 30);

            Assert.True(Directory.Exists(newest));
            Assert.True(Directory.Exists(second));
            Assert.False(Directory.Exists(overCount));
            Assert.False(Directory.Exists(tooOld));
            Assert.True(Directory.Exists(unrelated));
        }
        finally
        {
            DeleteTemporaryRoot(root);
        }
    }

    [Fact]
    public async Task WriteReports_RejectsSessionFolderOutsidePerformanceRoot()
    {
        var root = CreateTemporaryRoot();
        try
        {
            var archive = new PerformanceReportArchiveService(root);
            var report = new PerformanceSessionReport
            {
                SessionId = Guid.NewGuid(),
                StartedUtc = DateTimeOffset.UtcNow
            };

            var outsideFolder = Path.Combine(root, "outside");

            await Assert.ThrowsAsync<ArgumentException>(() => archive.WriteReportsAsync(outsideFolder, report));
        }
        finally
        {
            DeleteTemporaryRoot(root);
        }
    }

    private static string CreateSessionDirectory(string root, string name, DateTime lastWriteTimeUtc)
    {
        var path = Path.Combine(root, name);
        Directory.CreateDirectory(path);
        Directory.SetLastWriteTimeUtc(path, lastWriteTimeUtc);
        return path;
    }

    private static bool StartsWithUtf8Bom(byte[] bytes)
    {
        return bytes.Length >= 3 &&
               bytes[0] == 0xEF &&
               bytes[1] == 0xBB &&
               bytes[2] == 0xBF;
    }

    private static string CreateTemporaryRoot()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "BookStitch.Tests",
            nameof(PerformanceReportArchiveServiceTests),
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(root);
        return root;
    }

    private static void DeleteTemporaryRoot(string root)
    {
        try
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
        catch
        {
            // Test cleanup is best effort only.
        }
    }
}
