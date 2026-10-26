using BookStitch.Models;

namespace BookStitch.Services;

public static class ProcessingActionPresentation
{
    public static string Format(string? action)
    {
        if (string.IsNullOrWhiteSpace(action))
            return string.Empty;

        var id = ProcessingActionIds.Normalize(action);
        var resourceKey = id switch
        {
            ProcessingActionIds.Pending => "ActionPending",
            ProcessingActionIds.Reuse => "ActionReuse",
            ProcessingActionIds.Convert => "ActionConvert",
            ProcessingActionIds.Inspect => "ActionInspect",
            ProcessingActionIds.Invalid => "ActionInvalid",
            ProcessingActionIds.RipFlac => "ActionRipFlac",
            _ => null
        };
        return resourceKey is null ? action ?? string.Empty : LocalizationText.Get(resourceKey);
    }
}
