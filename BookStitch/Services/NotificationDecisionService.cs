using BookStitch.Models;

namespace BookStitch.Services;

public sealed class NotificationDecisionService
{
    public bool ShouldNotify(NotificationEventContext context)
    {
        if (context.CircleDriveSearchActive &&
            context.CircleDriveFoundNextDisc &&
            context.Type is NotificationEvent.DiscChangeRequired or NotificationEvent.UserActionRequired)
        {
            return false;
        }

        if (!context.RequiresUserInteraction &&
            context.Type is NotificationEvent.DiscChangeRequired or NotificationEvent.UserActionRequired)
        {
            return false;
        }

        return context.Type switch
        {
            NotificationEvent.DiscChangeRequired => context.RequiresUserInteraction,
            NotificationEvent.UserActionRequired => context.RequiresUserInteraction,
            NotificationEvent.ProjectCompleted => true,
            NotificationEvent.Warning => context.IsBackgroundOperation || context.RequiresUserInteraction,
            NotificationEvent.Error => context.IsBackgroundOperation || context.RequiresUserInteraction,
            NotificationEvent.Information => context.IsBackgroundOperation,
            _ => false
        };
    }
}
