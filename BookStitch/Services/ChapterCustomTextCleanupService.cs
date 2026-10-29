using System.IO;
using System.Globalization;
using System.Text.RegularExpressions;

namespace BookStitch.Services;

public sealed class ChapterCustomTextCleanupService
{
    private readonly ChapterCandidateNormalizer _normalizer;
    private static readonly Regex SourceOrdinalPrefix = new(
        @"^\s*(?<number>[1-9]\d*)(?:\s*[-–—.:]\s*|\s+|$)(?<text>.*)$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public ChapterCustomTextCleanupService()
        : this(new ChapterCandidateNormalizer())
    {
    }

    public ChapterCustomTextCleanupService(ChapterCandidateNormalizer normalizer)
    {
        _normalizer = normalizer ?? throw new ArgumentNullException(nameof(normalizer));
    }

    public string CleanTagTitle(string? tagTitle, int? sourceOrdinal = null)
    {
        // Strip a proven source ordinal before marker cleanup, so marker-only
        // titles get the same fallback above and below the zero-padding boundary.
        // Keep this ordering local to the custom tag slot; auto analysis and
        // filename cleanup retain their existing contracts.
        if (sourceOrdinal is > 0 && !string.IsNullOrWhiteSpace(tagTitle))
        {
            var match = SourceOrdinalPrefix.Match(tagTitle.Replace('_', ' '));
            if (match.Success && int.TryParse(match.Groups["number"].Value,
                    NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number == sourceOrdinal)
            {
                tagTitle = match.Groups["text"].Value;
                return _normalizer.Normalize(ChapterCandidateSource.TrackTitle, tagTitle).NormalizedText;
            }
        }

        return Clean(ChapterCandidateSource.TrackTitle, tagTitle, sourceOrdinal);
    }

    public string CleanFileName(string? fileName, int? sourceOrdinal = null)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return string.Empty;

        return Clean(
            ChapterCandidateSource.Filename,
            Path.GetFileName(fileName), sourceOrdinal);
    }

    private string Clean(ChapterCandidateSource source, string? value, int? sourceOrdinal)
    {
        var normalized = _normalizer.Normalize(source, value);
        // Only remove a non-padded number when the source metadata proves its
        // ordinal role. Unrelated title numbers and original slots remain intact.
        if (sourceOrdinal is not > 0 || normalized.AppliedTransforms.Any(transform =>
                transform.Kind == ChapterNormalizationTransformKind.RemovedNumericPrefix))
            return normalized.NormalizedText;
        var match = SourceOrdinalPrefix.Match(normalized.NormalizedText);
        if (!match.Success || !int.TryParse(match.Groups["number"].Value,
                NumberStyles.None, CultureInfo.InvariantCulture, out var number) || number != sourceOrdinal)
            return normalized.NormalizedText;
        return match.Groups["text"].Value.Trim();
    }
}
