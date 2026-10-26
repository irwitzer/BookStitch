using BookStitch.Models;

namespace BookStitch.Services;

/// <summary>
/// Derives comparable, non-destructive statistics from a completed performance report.
/// Wall-clock and summed child/phase time deliberately remain separate because work may overlap.
/// </summary>
public sealed class PerformanceStatisticsService
{
    private const double BytesPerMegabyte = 1024d * 1024d;

    public PerformanceSessionReport Apply(PerformanceSessionReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        var elapsedByOperationId = report.Operations
            .Where(operation => operation.ElapsedMilliseconds.HasValue)
            .ToDictionary(operation => operation.OperationId, operation => operation.ElapsedMilliseconds!.Value);

        var operations = report.Operations
            .Select(operation => operation with
            {
                Statistics = CreateStatistics(operation, elapsedByOperationId)
            })
            .ToList();

        return report with { Operations = operations };
    }

    private static PerformanceOperationStatistics CreateStatistics(
        PerformanceOperationReport operation,
        IReadOnlyDictionary<Guid, double> elapsedByOperationId)
    {
        var wallClock = Positive(operation.ElapsedMilliseconds);
        var seconds = wallClock.HasValue ? wallClock.Value / 1000d : (double?)null;
        var audioMilliseconds = Positive(operation.Workload.InputAudioDurationMilliseconds);

        return new PerformanceOperationStatistics
        {
            WallClockMilliseconds = wallClock,
            SumChildOperationElapsedMilliseconds = operation.ChildOperationIds
                .Where(elapsedByOperationId.ContainsKey)
                .Sum(id => Math.Max(0d, elapsedByOperationId[id])),
            SumPhaseElapsedMilliseconds = operation.Phases
                .Where(phase => phase.ElapsedMilliseconds.HasValue)
                .Sum(phase => Math.Max(0d, phase.ElapsedMilliseconds!.Value)),
            MaxExternalProcessConcurrency = MaxConcurrency(operation),
            InputMegabytesPerSecond = Rate(operation.Workload.InputBytes, seconds, BytesPerMegabyte),
            OutputMegabytesPerSecond = Rate(operation.Workload.OutputBytes, seconds, BytesPerMegabyte),
            FilesPerSecond = Rate(operation.Workload.FileCount, seconds),
            TracksPerSecond = Rate(operation.Workload.TrackCount, seconds),
            RealTimeFactor = wallClock.HasValue && audioMilliseconds.HasValue
                ? wallClock.Value / audioMilliseconds.Value
                : null
        };
    }

    private static int MaxConcurrency(PerformanceOperationReport operation)
    {
        var operationMax = operation.ExternalProcessAggregates.Count == 0
            ? 0
            : operation.ExternalProcessAggregates.Max(item => item.MaxConcurrency);
        var phaseMax = operation.Phases
            .SelectMany(phase => phase.ExternalProcessAggregates)
            .Select(item => item.MaxConcurrency)
            .DefaultIfEmpty(0)
            .Max();
        return Math.Max(operationMax, phaseMax);
    }

    private static double? Rate(long? count, double? seconds, double divisor = 1d) =>
        count.HasValue && count.Value >= 0 && seconds is > 0d
            ? count.Value / divisor / seconds.Value
            : null;

    private static double? Rate(int? count, double? seconds) =>
        count.HasValue && count.Value >= 0 && seconds is > 0d
            ? count.Value / seconds.Value
            : null;

    private static double? Positive(double? value) =>
        value is > 0d ? value : null;
}
