using BookStitch.Models;

namespace BookStitch.Services;

public sealed record ConvertedTrackPreparationProgressSnapshot(
    int CompletedCount,
    int TotalCount,
    long CurrentTicks,
    long TotalTicks,
    IReadOnlyList<int> ActiveTrackIndexes);

public sealed class ConvertedTrackPreparationProgressService
{
    private readonly object _syncRoot = new();
    private readonly Dictionary<int, long> _activeProgressTicks = [];
    private readonly int _totalCount;
    private readonly long _totalTicks;
    private int _completedCount;
    private long _completedTicks;

    public ConvertedTrackPreparationProgressService(
        ConvertedTrackResumeState resumeState,
        long totalTicks)
    {
        ArgumentNullException.ThrowIfNull(resumeState);

        _totalCount = Math.Max(0, resumeState.TotalCount);
        _totalTicks = Math.Max(0, totalTicks);
        _completedCount = Math.Clamp(resumeState.ReusableCount, 0, _totalCount);
        _completedTicks = Math.Clamp(resumeState.ReusableDurationTicks, 0, _totalTicks);
    }

    public ConvertedTrackPreparationProgressSnapshot GetSnapshot()
    {
        lock (_syncRoot)
            return CreateSnapshot();
    }


    public ConvertedTrackPreparationProgressSnapshot StartTrack(int trackIndex)
    {
        lock (_syncRoot)
        {
            _activeProgressTicks.TryAdd(trackIndex, 0);
            return CreateSnapshot();
        }
    }

    public ConvertedTrackPreparationProgressSnapshot UpdateActiveTrack(
        int trackIndex,
        long progressTicks,
        long durationTicks)
    {
        lock (_syncRoot)
        {
            _activeProgressTicks[trackIndex] = Math.Clamp(
                progressTicks,
                0,
                Math.Max(0, durationTicks));

            return CreateSnapshot();
        }
    }

    public ConvertedTrackPreparationProgressSnapshot CompleteTrack(
        int trackIndex,
        long durationTicks)
    {
        lock (_syncRoot)
        {
            _activeProgressTicks.Remove(trackIndex);
            _completedCount = Math.Min(_totalCount, _completedCount + 1);
            _completedTicks = Math.Clamp(
                _completedTicks + Math.Max(0, durationTicks),
                0,
                _totalTicks);

            return CreateSnapshot();
        }
    }

    private ConvertedTrackPreparationProgressSnapshot CreateSnapshot()
    {
        var activeTicks = _activeProgressTicks.Values.Sum();
        var currentTicks = Math.Clamp(_completedTicks + activeTicks, 0, _totalTicks);

        return new ConvertedTrackPreparationProgressSnapshot(
            _completedCount,
            _totalCount,
            currentTicks,
            _totalTicks,
            _activeProgressTicks.Keys.OrderBy(index => index).ToArray());
    }
}
