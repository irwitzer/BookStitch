using BookStitch.Services;

namespace BookStitch.Dialog;

internal static class ConceptDraftWarningPresentation
{
    internal static string BuildMessage(IEnumerable<string> labels)
    {
        var visibleLabels = labels
            .Where(label => !string.IsNullOrWhiteSpace(label))
            .Distinct(StringComparer.CurrentCulture)
            .ToArray();

        return LocalizationText.Get("Metadata.Dialog.UnconfirmedInputMessage") +
               Environment.NewLine +
               Environment.NewLine +
               string.Join(Environment.NewLine, visibleLabels.Select(label => $"• {label}"));
    }
}
