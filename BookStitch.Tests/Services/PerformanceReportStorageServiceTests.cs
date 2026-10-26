using BookStitch.Models;
using BookStitch.Services;
using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class PerformanceReportStorageServiceTests
{
    [Fact]
    public async Task Journal_IsCreatedUnderSettingsLogsPerformance()
    {
        var root = CreateTemporaryRoot();
        try
        {
            var sessionId = Guid.Parse("11111111-2222-3333-4444-555555555555");
            var startedUtc = new DateTimeOffset(2026, 8, 8, 0, 30, 45, TimeSpan.Zero);

            await using var storage = new PerformanceReportStorageService(root, sessionId, startedUtc);
            await storage.CompleteJournalAsync();

            var expectedRoot = Path.Combine(
                SettingsService.GetSettingsFolder(root),
                "logs",
                "performance");

            Assert.Equal(Path.GetFullPath(expectedRoot), Path.GetFullPath(storage.PerformanceRootFolder));
            Assert.Equal(
                Path.GetFullPath(expectedRoot),
                Path.GetFullPath(Path.GetDirectoryName(storage.SessionFolder)!));
            Assert.Equal(Path.Combine(storage.SessionFolder, "events.jsonl"), storage.JournalFilePath);
            Assert.True(File.Exists(storage.JournalFilePath));
        }
        finally
        {
            DeleteTemporaryRoot(root);
        }
    }

    [Fact]
    public async Task Journal_WritesCamelCaseJsonlWithoutBom()
    {
        var root = CreateTemporaryRoot();
        try
        {
            var sessionId = Guid.NewGuid();
            await using var storage = new PerformanceReportStorageService(
                root,
                sessionId,
                new DateTimeOffset(2026, 8, 8, 1, 0, 0, TimeSpan.Zero));

            Assert.True(storage.TryWriteJournalEvent(new PerformanceJournalEvent
            {
                Event = "session.started",
                SessionId = sessionId,
                Utc = new DateTimeOffset(2026, 8, 8, 1, 0, 0, TimeSpan.Zero),
                Name = "app.startup",
                Outcome = PerformanceOutcome.Incomplete
            }));

            Assert.True(storage.TryWriteJournalEvent(new PerformanceJournalEvent
            {
                Event = "operation.completed",
                SessionId = sessionId,
                OperationId = Guid.NewGuid(),
                Utc = new DateTimeOffset(2026, 8, 8, 1, 0, 1, TimeSpan.Zero),
                Name = "project.resume",
                Outcome = PerformanceOutcome.Success,
                ElapsedMilliseconds = 123.4
            }));

            await storage.CompleteJournalAsync();

            var bytes = await File.ReadAllBytesAsync(storage.JournalFilePath);
            Assert.False(StartsWithUtf8Bom(bytes));

            var lines = await File.ReadAllLinesAsync(storage.JournalFilePath);
            Assert.Equal(2, lines.Length);

            using var first = JsonDocument.Parse(lines[0]);
            Assert.Equal(1, first.RootElement.GetProperty("schemaVersion").GetInt32());
            Assert.Equal("session.started", first.RootElement.GetProperty("event").GetString());
            Assert.Equal("Incomplete", first.RootElement.GetProperty("outcome").GetString());
            Assert.False(first.RootElement.TryGetProperty("exitCode", out _));

            using var second = JsonDocument.Parse(lines[1]);
            Assert.Equal("operation.completed", second.RootElement.GetProperty("event").GetString());
            Assert.Equal("Success", second.RootElement.GetProperty("outcome").GetString());
            Assert.Equal(123.4, second.RootElement.GetProperty("elapsedMilliseconds").GetDouble());
        }
        finally
        {
            DeleteTemporaryRoot(root);
        }
    }

    [Fact]
    public async Task ConcurrentWrites_AreSerializedAsIndependentValidLines()
    {
        var root = CreateTemporaryRoot();
        try
        {
            var sessionId = Guid.NewGuid();
            await using var storage = new PerformanceReportStorageService(
                root,
                sessionId,
                DateTimeOffset.UtcNow,
                flushEventCount: 8);

            Parallel.For(0, 128, index =>
            {
                var accepted = storage.TryWriteJournalEvent(new PerformanceJournalEvent
                {
                    Event = "external.completed",
                    SessionId = sessionId,
                    Utc = DateTimeOffset.UtcNow,
                    Tool = "ffprobe",
                    Value = index
                });

                Assert.True(accepted);
            });

            await storage.CompleteJournalAsync();

            var lines = await File.ReadAllLinesAsync(storage.JournalFilePath);
            Assert.Equal(128, lines.Length);

            foreach (var line in lines)
            {
                using var document = JsonDocument.Parse(line);
                Assert.Equal("external.completed", document.RootElement.GetProperty("event").GetString());
                Assert.Equal("ffprobe", document.RootElement.GetProperty("tool").GetString());
            }
        }
        finally
        {
            DeleteTemporaryRoot(root);
        }
    }

    [Fact]
    public async Task WrongSessionAndWritesAfterCompletion_AreRejected()
    {
        var root = CreateTemporaryRoot();
        try
        {
            var sessionId = Guid.NewGuid();
            await using var storage = new PerformanceReportStorageService(root, sessionId, DateTimeOffset.UtcNow);

            Assert.False(storage.TryWriteJournalEvent(new PerformanceJournalEvent
            {
                Event = "session.started",
                SessionId = Guid.NewGuid(),
                Utc = DateTimeOffset.UtcNow
            }));

            await storage.CompleteJournalAsync();

            Assert.False(storage.TryWriteJournalEvent(new PerformanceJournalEvent
            {
                Event = "session.completed",
                SessionId = sessionId,
                Utc = DateTimeOffset.UtcNow
            }));

            Assert.Empty(await File.ReadAllLinesAsync(storage.JournalFilePath));
        }
        finally
        {
            DeleteTemporaryRoot(root);
        }
    }

    [Fact]
    public async Task StorageFailure_CompletesPromptlyAndRejectsFurtherWrites()
    {
        var root = CreateTemporaryRoot();
        var blockingFile = Path.Combine(root, "blocked-root");

        try
        {
            await File.WriteAllTextAsync(blockingFile, "not a directory");

            var sessionId = Guid.NewGuid();
            await using var storage = new PerformanceReportStorageService(
                blockingFile,
                sessionId,
                DateTimeOffset.UtcNow);

            await storage.CompleteJournalAsync();

            Assert.True(storage.IsFaulted);
            Assert.NotNull(storage.LastError);
            Assert.False(storage.TryWriteJournalEvent(new PerformanceJournalEvent
            {
                Event = "session.completed",
                SessionId = sessionId,
                Utc = DateTimeOffset.UtcNow
            }));
        }
        finally
        {
            DeleteTemporaryRoot(root);
        }
    }

    [Fact]
    public void SessionFolderName_IsDeterministicAndContainsShortSessionId()
    {
        var sessionId = Guid.Parse("a1b2c3d4-1111-2222-3333-444455556666");
        var startedUtc = new DateTimeOffset(2026, 8, 8, 13, 42, 17, TimeSpan.Zero);

        var name = PerformanceReportStorageService.CreateSessionFolderName(startedUtc, sessionId);
        var expectedTimestamp = startedUtc.ToLocalTime().ToString(
            "yyyy.MM.dd HH.mm.ss",
            System.Globalization.CultureInfo.InvariantCulture);

        Assert.Equal($"{expectedTimestamp} - a1b2c3d4", name);
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
            nameof(PerformanceReportStorageServiceTests),
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
            // Test cleanup is best effort; assertions already cover storage semantics.
        }
    }
}