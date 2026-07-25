using System.Collections.Concurrent;
using System.IO;

namespace BookStitch.Services;

public sealed record LiveConversionQueueItem(
    string SourcePath,
    string ConvertedPath,
    string PresetName,
    int DiscNumber,
    int TrackNumber);

public sealed record LiveConversionQueueSnapshot(
    int QueuedCount,
    int CompletedCount,
    int SkippedCount,
    int DuplicateCount);

public sealed class LiveConversionQueueService
{
    private readonly ConcurrentQueue<LiveConversionQueueItem> _queue = new();
    private readonly HashSet<string> _knownSources = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _syncRoot = new();

    private int _completedCount;
    private int _skippedCount;
    private int _duplicateCount;

    public int GetLiveWorkerLimit(string parallelJobsInput)
    {
        var requestedJobs = ParseParallelJobs(parallelJobsInput);
        return Math.Clamp(requestedJobs, 1, 40);
    }

    public bool TryEnqueue(LiveConversionQueueItem item)
    {
        if (string.IsNullOrWhiteSpace(item.SourcePath) ||
            string.IsNullOrWhiteSpace(item.ConvertedPath))
        {
            return false;
        }

        if (IsTemporaryOrPartFile(item.SourcePath) ||
            IsTemporaryOrPartFile(item.ConvertedPath))
        {
            return false;
        }

        if (File.Exists(item.ConvertedPath) && new FileInfo(item.ConvertedPath).Length > 0)
        {
            _skippedCount++;
            return false;
        }

        lock (_syncRoot)
        {
            var sourceKey = Path.GetFullPath(item.SourcePath);

            if (!_knownSources.Add(sourceKey))
            {
                _duplicateCount++;
                return false;
            }

            _queue.Enqueue(item);
            return true;
        }
    }

    public bool TryDequeue(out LiveConversionQueueItem item)
    {
        return _queue.TryDequeue(out item!);
    }

    public void MarkCompleted()
    {
        _completedCount++;
    }

    public LiveConversionQueueSnapshot CreateSnapshot()
    {
        return new LiveConversionQueueSnapshot(
            _queue.Count,
            _completedCount,
            _skippedCount,
            _duplicateCount);
    }

    private static int ParseParallelJobs(string value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value.Equals("Auto", StringComparison.OrdinalIgnoreCase))
        {
            return Math.Clamp(Math.Min(8, Environment.ProcessorCount), 1, 40);
        }

        return int.TryParse(value.Trim(), out var parsed)
            ? parsed
            : 2;
    }

    private static bool IsTemporaryOrPartFile(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".part", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".copying", StringComparison.OrdinalIgnoreCase);
    }
}
