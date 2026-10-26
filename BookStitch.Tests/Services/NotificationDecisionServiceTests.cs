using BookStitch.Models;
using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class NotificationDecisionServiceTests
{
    private readonly NotificationDecisionService _service = new();

    [Fact]
    public void DiscChangeRequiresUserInteraction()
    {
        Assert.True(_service.ShouldNotify(new NotificationEventContext(
            NotificationEvent.DiscChangeRequired,
            RequiresUserInteraction: true)));

        Assert.False(_service.ShouldNotify(new NotificationEventContext(
            NotificationEvent.DiscChangeRequired,
            RequiresUserInteraction: false)));
    }

    [Fact]
    public void CircleDriveAcceptedDiscSuppressesUserAttentionNotification()
    {
        var context = new NotificationEventContext(
            NotificationEvent.DiscChangeRequired,
            RequiresUserInteraction: false,
            CircleDriveSearchActive: true,
            CircleDriveFoundNextDisc: true);

        Assert.False(_service.ShouldNotify(context));
    }

    [Fact]
    public void CircleDriveContextDoesNotSuppressErrors()
    {
        var context = new NotificationEventContext(
            NotificationEvent.Error,
            CircleDriveSearchActive: true,
            CircleDriveFoundNextDisc: true);

        Assert.True(_service.ShouldNotify(context));
    }

    [Fact]
    public void ErrorAndProjectCompletedRemainNotifiable()
    {
        Assert.True(_service.ShouldNotify(new NotificationEventContext(NotificationEvent.Error)));
        Assert.True(_service.ShouldNotify(new NotificationEventContext(NotificationEvent.ProjectCompleted)));
    }
}
