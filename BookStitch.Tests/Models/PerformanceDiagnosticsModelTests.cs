using BookStitch.Models;
using System.Reflection;
using System.Text.Json;
using Xunit;

namespace BookStitch.Tests.Models;

public sealed class PerformanceDiagnosticsModelTests
{
    [Fact]
    public void Defaults_AreSafeForIncompleteDiagnostics()
    {
        var session = new PerformanceSessionReport();
        var workload = new PerformanceWorkloadSnapshot();

        Assert.Equal(1, session.SchemaVersion);
        Assert.Equal(PerformanceOutcome.Incomplete, session.Outcome);
        Assert.Equal(PerformanceSessionStartReason.ApplicationStartup, session.StartReason);
        Assert.Empty(session.Operations);
        Assert.Empty(session.DriveSnapshots);

        Assert.Equal(PerformanceProjectType.Unknown, workload.ProjectType);
        Assert.Null(workload.TrackCount);
        Assert.Null(workload.InputBytes);
        Assert.Null(workload.InputAudioDurationMilliseconds);
    }

    [Fact]
    public void Hierarchy_PreservesSessionOperationPhaseAndChildRelationships()
    {
        var sessionId = Guid.NewGuid();
        var parentOperationId = Guid.NewGuid();
        var childOperationId = Guid.NewGuid();
        var phaseId = Guid.NewGuid();

        var parentOperation = new PerformanceOperationReport
        {
            OperationId = parentOperationId,
            SessionId = sessionId,
            Name = "project.resume",
            ChildOperationIds = [childOperationId],
            Phases =
            [
                new PerformancePhaseReport
                {
                    PhaseId = phaseId,
                    OperationId = parentOperationId,
                    Name = "audio.probe"
                }
            ]
        };

        var childOperation = new PerformanceOperationReport
        {
            OperationId = childOperationId,
            SessionId = sessionId,
            ParentOperationId = parentOperationId,
            Name = "project.resume.child"
        };

        var session = new PerformanceSessionReport
        {
            SessionId = sessionId,
            Operations = [parentOperation, childOperation]
        };

        Assert.Equal(parentOperationId, session.Operations[1].ParentOperationId);
        Assert.Contains(childOperationId, session.Operations[0].ChildOperationIds);
        Assert.Equal(phaseId, session.Operations[0].Phases[0].PhaseId);
    }

    [Fact]
    public void JsonRoundTrip_PreservesComparisonAndDriveContext()
    {
        var driveId = Guid.NewGuid();
        var session = new PerformanceSessionReport
        {
            SessionId = Guid.NewGuid(),
            StartedUtc = new DateTimeOffset(2026, 8, 8, 1, 30, 0, TimeSpan.Zero),
            ApplicationVersion = "1.0.1",
            BuildIdentifier = "local-release",
            SourceRevision = "abcdef1234567890",
            BuildConfiguration = "Release",
            SelectedParallelJobs = "Auto",
            DriveSnapshots =
            [
                new PerformanceDriveSnapshot
                {
                    DriveId = driveId,
                    DriveRoot = "D:\\",
                    Roles = PerformanceDriveRole.Source | PerformanceDriveRole.ProjectWorking,
                    DriveType = "Fixed",
                    VolumeLabel = "Audio",
                    DeviceModel = "Example NVMe",
                    BusType = "NVMe",
                    UserNickname = "Arbeitslaufwerk"
                }
            ],
            Operations =
            [
                new PerformanceOperationReport
                {
                    OperationId = Guid.NewGuid(),
                    Name = "project.resume",
                    ProjectType = PerformanceProjectType.Resume,
                    Workload = new PerformanceWorkloadSnapshot
                    {
                        ProjectType = PerformanceProjectType.Resume,
                        TrackCount = 288
                    },
                    ExternalProcessAggregates =
                    [
                        new PerformanceExternalProcessAggregate
                        {
                            Tool = "ffprobe",
                            CallCount = 288,
                            SuccessCount = 288,
                            SumElapsedMilliseconds = 13200,
                            MeanElapsedMilliseconds = 45.8,
                            MaxConcurrency = 8
                        }
                    ]
                }
            ]
        };

        var json = JsonSerializer.Serialize(session);
        var restored = JsonSerializer.Deserialize<PerformanceSessionReport>(json);

        Assert.NotNull(restored);
        Assert.Equal("abcdef1234567890", restored!.SourceRevision);
        Assert.Equal("Release", restored.BuildConfiguration);
        Assert.Equal("Audio", restored.DriveSnapshots[0].VolumeLabel);
        Assert.Equal("Arbeitslaufwerk", restored.DriveSnapshots[0].UserNickname);
        Assert.Equal(288, restored.Operations[0].Workload.TrackCount);
        Assert.Equal(8, restored.Operations[0].ExternalProcessAggregates[0].MaxConcurrency);
    }

    [Fact]
    public void DriveModel_ContainsNoSerialNumberProperty()
    {
        var forbiddenProperty = typeof(PerformanceDriveSnapshot)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .FirstOrDefault(name => name.Contains("Serial", StringComparison.OrdinalIgnoreCase));

        Assert.Null(forbiddenProperty);
    }

    [Fact]
    public void DriveRoles_CanRepresentMultipleRolesOnOneDrive()
    {
        var roles = PerformanceDriveRole.Source | PerformanceDriveRole.ProjectWorking;

        Assert.True(roles.HasFlag(PerformanceDriveRole.Source));
        Assert.True(roles.HasFlag(PerformanceDriveRole.ProjectWorking));
        Assert.False(roles.HasFlag(PerformanceDriveRole.Output));
    }
}