namespace BookStitch.Services;

public sealed class ChapterGenericClassifier
{
    private readonly ChapterComparisonKeyBuilder _comparisonKeyBuilder;
    private readonly HashSet<string> _genericKeys;

    public ChapterGenericClassifier()
        : this(new ChapterComparisonKeyBuilder())
    {
    }

    public ChapterGenericClassifier(ChapterComparisonKeyBuilder comparisonKeyBuilder)
    {
        ArgumentNullException.ThrowIfNull(comparisonKeyBuilder);
        _comparisonKeyBuilder = comparisonKeyBuilder;
        _genericKeys = ChapterMarkerCatalog.GenericMarkers
            .Select(comparisonKeyBuilder.Build)
            .Where(key => key.Length > 0)
            .ToHashSet(StringComparer.Ordinal);
    }

    public bool IsGeneric(string? analysisText, string? analysisComparisonKey = null)
    {
        if (string.IsNullOrWhiteSpace(analysisText))
            return false;

        var key = analysisComparisonKey;
        if (string.IsNullOrEmpty(key))
            key = _comparisonKeyBuilder.Build(analysisText);

        return _genericKeys.Contains(key);
    }
}
