namespace BookStitch.Services;

public sealed record GenreSelectionDecision(
    string Value,
    bool ShouldApply);

public static class GenreSelectionDecisionService
{
    public static GenreSelectionDecision Create(
        string currentValue,
        string normalizedValue,
        bool isSeparator) =>
        new(
            normalizedValue,
            !isSeparator &&
            !string.Equals(
                currentValue,
                normalizedValue,
                StringComparison.Ordinal));
}