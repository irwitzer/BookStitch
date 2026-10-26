namespace BookStitch.Services;

public enum DiscWaitDialogOutcome
{
    Ready,
    ReadyWithDuplicateOverride,
    Deferred,
    ImportCompleted
}

public readonly record struct DiscWaitCheckRequest(bool AcceptNextDuplicate);
