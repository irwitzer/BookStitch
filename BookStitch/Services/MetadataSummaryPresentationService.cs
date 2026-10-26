namespace BookStitch.Services;

public static class MetadataSummaryPresentationService
{
    public static MetadataSummaryPresentation Create(
        string? bookTitle,
        string? author,
        string? narrator,
        string? album,
        string? genre)
    {
        var normalizedTitle = bookTitle?.Trim() ?? string.Empty;

        var details = new[] { author, narrator, album, genre }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(4);

        return new MetadataSummaryPresentation(
            string.IsNullOrWhiteSpace(normalizedTitle)
                ? LocalizationText.Get("MainWindow.Metadata.EmptyTitle")
                : normalizedTitle,
            string.Join(" · ", details));
    }
}

public sealed record MetadataSummaryPresentation(
    string Title,
    string Details);
