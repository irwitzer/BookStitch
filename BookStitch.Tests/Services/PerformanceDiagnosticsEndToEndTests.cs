using BookStitch.Models;
using BookStitch.Services;
using System.Text.Json;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class PerformanceDiagnosticsEndToEndTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EnabledSession_WritesCompleteJournalAndFinalReportsWithStatistics(bool pause)
    {
        var root = CreateTemporaryRoot();
        try
        {
            var sessionId = Guid.Parse("12345678-1234-5678-9abc-def012345678");
            var startedUtc = new DateTimeOffset(2026, 8, 8, 9, 0, 0, TimeSpan.Zero);
            var time = new ManualTimeProvider(startedUtc);
            await using var storage = new PerformanceReportStorageService(
                root,
                sessionId,
                startedUtc,
                flushEventCount: 1);
            var trace = new PerformanceTraceService(
                enabled: true,
                timeProvider: time,
                journalEventSink: journalEvent => { storage.TryWriteJournalEvent(journalEvent); });

            Assert.True(trace.TryStartSession(
                new PerformanceSessionStartContext
                {
                    ApplicationVersion = "1.0.1",
                    BuildConfiguration = "Debug"
                },
                startedTimestamp: time.GetTimestamp(),
                startedUtc: startedUtc,
                sessionId: sessionId));

            trace.SetSessionContext(
                new PerformanceEnvironmentSnapshot { OperatingSystem = "Windows Test" },
                new PerformanceToolSnapshot { Available = true, Version = "ffmpeg-test" },
                new PerformanceToolSnapshot { Available = true, Version = "ffprobe-test" },
                []);

            var operation = trace.StartOperation(
                "export.run",
                projectType: PerformanceProjectType.LocalFolder,
                workload: new PerformanceWorkloadSnapshot
                {
                    ProjectType = PerformanceProjectType.LocalFolder,
                    TrackCount = 4,
                    FileCount = 4,
                    InputBytes = 8 * 1024 * 1024,
                    InputAudioDurationMilliseconds = 8000
                });
            var phase = trace.StartPhase(operation.OperationId, "export.convert");
            trace.RecordMetric(operation.OperationId, "RequestedParallelJobs", 20, "jobs");
            trace.RecordPhaseMetric(phase.PhaseId, "EffectiveWorkerLimit", 4, "workers");
            var external = trace.StartExternalProcess(
                operation.OperationId,
                phase.PhaseId,
                "ffmpeg",
                "export.convert");

            time.Advance(TimeSpan.FromSeconds(2));
            external.Complete(0);
            if (pause) { phase.Pause(); operation.Pause(); }
            else { phase.Complete(); operation.Complete(); }

            var report = trace.CompleteSession(PerformanceOutcome.Success);
            Assert.NotNull(report);
            report = new PerformanceStatisticsService().Apply(report!);

            await storage.CompleteJournalAsync();
            var archive = new PerformanceReportArchiveService(root);
            await archive.WriteReportsAsync(storage.SessionFolder, report);

            Assert.True(File.Exists(storage.JournalFilePath));
            Assert.True(File.Exists(Path.Combine(storage.SessionFolder, "report.json")));
            Assert.True(File.Exists(Path.Combine(storage.SessionFolder, "report.txt")));

            var journal = await archive.ReadJournalAsync(storage.JournalFilePath);
            Assert.True(journal.IsComplete);
            Assert.Contains(journal.Events, item => item.Event == "session.started");
            Assert.Contains(journal.Events, item => item.Event == "session.completed");
            Assert.Contains(journal.Events, item => item.Event == "external.completed");

            using var json = JsonDocument.Parse(
                await File.ReadAllTextAsync(Path.Combine(storage.SessionFolder, "report.json")));
            var operationsJson = json.RootElement.GetProperty("operations");
            Assert.Equal(1, operationsJson.GetArrayLength());
            var operationJson = operationsJson[0];
            Assert.Equal(pause ? "Paused" : "Success", operationJson.GetProperty("outcome").GetString());
            Assert.Equal(pause ? "Paused" : "Success", operationJson.GetProperty("phases")[0].GetProperty("outcome").GetString());
            var text = await File.ReadAllTextAsync(Path.Combine(storage.SessionFolder, "report.txt"));
            Assert.Contains("RequestedParallelJobs=20 jobs", text);
            Assert.Contains("EffectiveWorkerLimit=4 workers", text);
            Assert.Equal("export.run", operationJson.GetProperty("name").GetString());
            Assert.Equal(2d, operationJson.GetProperty("statistics").GetProperty("tracksPerSecond").GetDouble());
            Assert.Equal(0.25d, operationJson.GetProperty("statistics").GetProperty("realTimeFactor").GetDouble());
            Assert.Equal(1, operationJson.GetProperty("statistics").GetProperty("maxExternalProcessConcurrency").GetInt32());
        }
        finally
        {
            DeleteTemporaryRoot(root);
        }
    }

    private static string CreateTemporaryRoot()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "BookStitch.Tests",
            nameof(PerformanceDiagnosticsEndToEndTests),
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

    private sealed class ManualTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private long _timestamp;
        private DateTimeOffset _utcNow = utcNow;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => _timestamp;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan elapsed)
        {
            _timestamp += elapsed.Ticks;
            _utcNow += elapsed;
        }
    }
}
