using BookStitch.Models;
using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class PerformanceStatisticsServiceTests
{
    [Fact]
    public void Apply_SeparatesWallClockChildSumThroughputAndRealtimeFactor()
    {
        var parentId = Guid.NewGuid();
        var childId = Guid.NewGuid();
        var report = new PerformanceSessionReport
        {
            Operations =
            [
                new PerformanceOperationReport
                {
                    OperationId = parentId,
                    ElapsedMilliseconds = 2000,
                    ChildOperationIds = [childId],
                    Workload = new PerformanceWorkloadSnapshot
                    {
                        TrackCount = 20,
                        FileCount = 10,
                        InputBytes = 20 * 1024 * 1024,
                        InputAudioDurationMilliseconds = 4000
                    },
                    Phases =
                    [
                        new PerformancePhaseReport
                        {
                            ElapsedMilliseconds = 1500,
                            ExternalProcessAggregates =
                            [new PerformanceExternalProcessAggregate { MaxConcurrency = 4 }]
                        }
                    ]
                },
                new PerformanceOperationReport
                {
                    OperationId = childId,
                    ElapsedMilliseconds = 1000
                }
            ]
        };

        var result = new PerformanceStatisticsService().Apply(report);
        var statistics = result.Operations[0].Statistics!;

        Assert.Equal(2000, statistics.WallClockMilliseconds);
        Assert.Equal(1000, statistics.SumChildOperationElapsedMilliseconds);
        Assert.Equal(1500, statistics.SumPhaseElapsedMilliseconds);
        Assert.Equal(4, statistics.MaxExternalProcessConcurrency);
        Assert.Equal(10, statistics.InputMegabytesPerSecond);
        Assert.Equal(5, statistics.FilesPerSecond);
        Assert.Equal(10, statistics.TracksPerSecond);
        Assert.Equal(0.5, statistics.RealTimeFactor);
    }
}
