namespace BookStitch.Models;

public sealed record NotificationEventContext(
    NotificationEvent Type,
    string ProjectType = "",
    bool RequiresUserInteraction = true,
    bool IsBackgroundOperation = true,
    string Source = "",
    string Reason = "",
    bool CircleDriveSearchActive = false,
    bool CircleDriveFoundNextDisc = false)
{
    public static NotificationEventContext FromEvent(NotificationEvent type) => new(type);
}
