using BookStitch.Models;
using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace BookStitch.Services;

/// <summary>
/// Owns the crash-tolerant append-only JSONL journal for one performance session.
/// The service is wired into enabled performance sessions and remains diagnostic-only.
/// </summary>
public sealed class PerformanceReportStorageService : IAsyncDisposable
{
    private const int DefaultFlushEventCount = 16;

    private static readonly JsonSerializerOptions JournalJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly Guid _sessionId;
    private readonly int _flushEventCount;
    private readonly Channel<PerformanceJournalEvent> _channel;
    private readonly Task _writerTask;
    private Exception? _lastError;
    private int _completionRequested;
    private int _faulted;

    public PerformanceReportStorageService(
        string projectRootFolder,
        Guid sessionId,
        DateTimeOffset startedUtc,
        int flushEventCount = DefaultFlushEventCount)
    {
        if (string.IsNullOrWhiteSpace(projectRootFolder))
            throw new ArgumentException("Es wurde kein Projektordner übergeben.", nameof(projectRootFolder));

        if (sessionId == Guid.Empty)
            throw new ArgumentException("Es wurde keine gültige Session-ID übergeben.", nameof(sessionId));

        if (flushEventCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(flushEventCount));

        _sessionId = sessionId;
        _flushEventCount = flushEventCount;

        PerformanceRootFolder = Path.Combine(
            SettingsService.GetSettingsFolder(projectRootFolder),
            "logs",
            "performance");

        SessionFolder = Path.Combine(
            PerformanceRootFolder,
            CreateSessionFolderName(startedUtc, sessionId));

        JournalFilePath = Path.Combine(SessionFolder, "events.jsonl");

        _channel = Channel.CreateUnbounded<PerformanceJournalEvent>(
            new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false,
                AllowSynchronousContinuations = false
            });

        _writerTask = WriteLoopAsync();
    }

    public string PerformanceRootFolder { get; }

    public string SessionFolder { get; }

    public string JournalFilePath { get; }

    public bool IsFaulted => Volatile.Read(ref _faulted) != 0;

    public Exception? LastError => _lastError;

    public bool TryWriteJournalEvent(PerformanceJournalEvent journalEvent)
    {
        if (journalEvent is null ||
            journalEvent.SessionId != _sessionId ||
            Volatile.Read(ref _completionRequested) != 0 ||
            IsFaulted)
        {
            return false;
        }

        return _channel.Writer.TryWrite(journalEvent);
    }

    public async ValueTask CompleteJournalAsync()
    {
        if (Interlocked.Exchange(ref _completionRequested, 1) == 0)
            _channel.Writer.TryComplete();

        await _writerTask.ConfigureAwait(false);
    }

    public ValueTask DisposeAsync()
    {
        return CompleteJournalAsync();
    }

    public static string CreateSessionFolderName(DateTimeOffset startedUtc, Guid sessionId)
    {
        if (sessionId == Guid.Empty)
            throw new ArgumentException("Es wurde keine gültige Session-ID übergeben.", nameof(sessionId));

        var timestamp = startedUtc.ToLocalTime().ToString(
            "yyyy.MM.dd HH.mm.ss",
            CultureInfo.InvariantCulture);

        var shortSessionId = sessionId.ToString("N")[..8];
        return $"{timestamp} - {shortSessionId}";
    }

    private async Task WriteLoopAsync()
    {
        try
        {
            Directory.CreateDirectory(SessionFolder);

            await using var stream = new FileStream(
                JournalFilePath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.Read,
                16 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            await using var writer = new StreamWriter(
                stream,
                new UTF8Encoding(false),
                16 * 1024,
                false);

            var pendingSinceFlush = 0;

            while (await _channel.Reader.WaitToReadAsync().ConfigureAwait(false))
            {
                while (_channel.Reader.TryRead(out var journalEvent))
                {
                    var json = JsonSerializer.Serialize(journalEvent, JournalJsonOptions);
                    await writer.WriteLineAsync(json).ConfigureAwait(false);
                    pendingSinceFlush++;

                    if (ShouldFlush(journalEvent, pendingSinceFlush))
                    {
                        await writer.FlushAsync().ConfigureAwait(false);
                        pendingSinceFlush = 0;
                    }
                }
            }

            await writer.FlushAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _lastError = ex;
            Volatile.Write(ref _faulted, 1);

            // Reject future producers and discard only events that are already queued.
            // Never await more data here: storage failure must finish promptly.
            _channel.Writer.TryComplete(ex);
            while (_channel.Reader.TryRead(out _))
            {
            }
        }
    }

    private bool ShouldFlush(PerformanceJournalEvent journalEvent, int pendingSinceFlush)
    {
        if (pendingSinceFlush >= _flushEventCount)
            return true;

        return journalEvent.Event is
            "session.started" or
            "operation.completed" or
            "session.completed";
    }
}