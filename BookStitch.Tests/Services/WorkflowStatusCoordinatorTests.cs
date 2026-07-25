using BookStitch.Models;
using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class WorkflowStatusCoordinatorTests
{
    [Fact]
    public void Publish_IgnoresStatusFromSupersededOperation()
    {
        var coordinator = new WorkflowStatusCoordinator();
        var firstOperation = coordinator.BeginOperation("project-a");
        var secondOperation = coordinator.BeginOperation("project-b");

        var accepted = coordinator.Publish(firstOperation, new WorkflowStatusSnapshot
        {
            ProjectId = "project-a",
            Error = new WorkflowErrorStatus("Veralteter Fehler")
        });

        Assert.False(accepted);
        Assert.Equal(secondOperation, coordinator.ActiveOperationId);
        Assert.Equal("project-b", coordinator.Snapshot.ProjectId);
        Assert.DoesNotContain("Veralteter Fehler", coordinator.CurrentViewState.TeletextText);
    }

    [Fact]
    public void Update_ForActiveOperation_PublishesFormattedViewState()
    {
        var coordinator = new WorkflowStatusCoordinator();
        var operation = coordinator.BeginOperation("project-a");
        WorkflowStatusViewState? observed = null;
        coordinator.ViewStateChanged += (_, state) => observed = state;

        var accepted = coordinator.Update(operation, snapshot => snapshot with
        {
            ProjectKind = WorkflowProjectKind.Folder,
            ConversionProgress = new ConversionActivityProgress(
                Completed: 7,
                Total: 26,
                Percent: 27,
                ActiveTrackNumbers: [1, 3, 4],
                IsLive: false)
        });

        Assert.True(accepted);
        Assert.NotNull(observed);
        Assert.Equal("Neu konvertieren 7 / 26 AAC 128 kbps", observed.TeletextText);
        Assert.Equal("27 % | 7 / 26 konvertiert | Aktive Jobs: 01, 03, 04", observed.ProgressText);
    }

    [Fact]
    public void EndOperation_PreventsLateUpdatesWithoutClearingLastView()
    {
        var coordinator = new WorkflowStatusCoordinator();
        var operation = coordinator.BeginOperation();
        coordinator.Publish(operation, new WorkflowStatusSnapshot
        {
            IsExportAborted = true
        });

        Assert.True(coordinator.EndOperation(operation));
        Assert.False(coordinator.Update(operation, snapshot => snapshot with
        {
            Error = new WorkflowErrorStatus("Zu spät")
        }));
        Assert.Equal("Export abgebrochen", coordinator.CurrentViewState.TeletextText);
    }
}
