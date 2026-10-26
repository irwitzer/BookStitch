using System.Text.RegularExpressions;

namespace BookStitch.Services;

public sealed record SeriesAutoSplitState(
    string OriginalName,
    string SplitName,
    string ExtractedPart);

public sealed record SeriesEntryDraftResolution(
    string Name,
    string Part,
    SeriesAutoSplitState? AutoSplitState = null);

public sealed partial class SeriesEntryDraftResolver
{
    public SeriesEntryDraftResolution LoadStructured(string? name, string? part) =>
        new(Clean(name), NormalizePart(part));

    public SeriesEntryDraftResolution LoadStructuredForEdit(string? name, string? part)
    {
        var cleanName = Clean(name);
        var cleanPart = NormalizePart(part);
        if (TryRemoveDuplicateSuffix(cleanName, cleanPart, out var deduplicatedName))
            cleanName = deduplicatedName;

        return new SeriesEntryDraftResolution(cleanName, cleanPart);
    }

    public SeriesEntryDraftResolution ResolveUserNameConfirmation(string? name, string? currentPart)
    {
        var cleanName = Clean(name);
        var cleanPart = NormalizePart(currentPart);

        if (!TryExtractAutomaticSuffix(cleanName, out var splitName, out var extractedPart))
            return new SeriesEntryDraftResolution(cleanName, cleanPart);

        return new SeriesEntryDraftResolution(
            splitName,
            extractedPart,
            new SeriesAutoSplitState(cleanName, splitName, extractedPart));
    }

    public SeriesEntryDraftResolution ResolveForCommit(
        string? name,
        string? part,
        SeriesAutoSplitState? autoSplitState,
        bool allowAutomaticSplit)
    {
        var cleanName = Clean(name);
        var cleanPart = NormalizePart(part);

        if (autoSplitState is not null &&
            string.Equals(cleanName, autoSplitState.SplitName, StringComparison.Ordinal))
        {
            if (cleanPart.Length == 0)
                return new SeriesEntryDraftResolution(autoSplitState.OriginalName, string.Empty);

            return new SeriesEntryDraftResolution(cleanName, cleanPart);
        }

        if (cleanPart.Length > 0 &&
            TryRemoveDuplicateSuffix(cleanName, cleanPart, out var deduplicatedName))
        {
            return new SeriesEntryDraftResolution(deduplicatedName, cleanPart);
        }

        if (cleanPart.Length == 0 &&
            allowAutomaticSplit &&
            TryExtractAutomaticSuffix(cleanName, out var splitName, out var extractedPart))
        {
            return new SeriesEntryDraftResolution(splitName, extractedPart);
        }

        return new SeriesEntryDraftResolution(cleanName, cleanPart);
    }

    private static bool TryExtractAutomaticSuffix(
        string name,
        out string splitName,
        out string extractedPart)
    {
        splitName = string.Empty;
        extractedPart = string.Empty;

        if (!TryGetLastToken(name, out var prefix, out var token) ||
            !AutomaticPositionSuffix().IsMatch(token))
        {
            return false;
        }

        splitName = prefix;
        extractedPart = NormalizePart(token);
        return true;
    }

    private static bool TryRemoveDuplicateSuffix(
        string name,
        string part,
        out string deduplicatedName)
    {
        deduplicatedName = string.Empty;
        if (part.Length == 0 ||
            !TryGetLastToken(name, out var prefix, out var token) ||
            !AreSemanticallyEqual(token, part))
        {
            return false;
        }

        deduplicatedName = prefix;
        return true;
    }

    private static bool AreSemanticallyEqual(string left, string right)
    {
        var normalizedLeft = NormalizePart(left);
        var normalizedRight = NormalizePart(right);

        if (SeriesPartInputService.TryNormalizeNumeric(normalizedLeft, out var leftNumeric) &&
            SeriesPartInputService.TryNormalizeNumeric(normalizedRight, out var rightNumeric))
        {
            return string.Equals(leftNumeric, rightNumeric, StringComparison.Ordinal);
        }

        return string.Equals(
            normalizedLeft,
            normalizedRight,
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryGetLastToken(
        string value,
        out string prefix,
        out string token)
    {
        prefix = string.Empty;
        token = string.Empty;

        var source = Clean(value);
        if (source.Length == 0)
            return false;

        var separator = source.LastIndexOfAny([' ', '\t', '\r', '\n']);
        if (separator <= 0 || separator >= source.Length - 1)
            return false;

        prefix = source[..separator].TrimEnd();
        token = source[(separator + 1)..].Trim();
        return prefix.Length > 0 && token.Length > 0;
    }

    private static string NormalizePart(string? value) =>
        SeriesPartInputService.NormalizePartText(value);

    private static string Clean(string? value) => (value ?? string.Empty).Trim();

    [GeneratedRegex(@"^\d+(?:[.,]\d+){0,2}(?:[A-Za-z]+)?$", RegexOptions.CultureInvariant)]
    private static partial Regex AutomaticPositionSuffix();
}
