namespace BookStitch.Services;

public enum ChapterNormalizationTransformKind
{
    NormalizedUnicode,
    RemovedControlCharacters,
    CollapsedWhitespace,
    ExtractedFilename,
    RemovedAudioExtension,
    RemovedCopySuffix,
    ReplacedUnderscores,
    RemovedTechnicalNumbering,
    RemovedNumericPrefix,
    RemovedNumericSuffix,
    RemovedNumericSequence,
    RemovedEmptyBrackets,
    CleanedSeparators
}

public sealed record ChapterNormalizationTransform(
    ChapterNormalizationTransformKind Kind,
    string? Value = null);

public sealed record ChapterNormalizationResult(
    ChapterCandidateSource Source,
    string? RawText,
    string NormalizedText,
    string ComparisonKey,
    IReadOnlyList<ChapterNormalizationTransform> AppliedTransforms);
