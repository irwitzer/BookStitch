using System.Globalization;
using System.Text.RegularExpressions;

namespace BookStitch.Services;

public sealed class ChapterTechnicalNumberingAnalyzer
{
    private static readonly Regex LeadingNumberRegex = new(
        @"^(?<number>[1-9]\d*)(?<separator>\s*[-–—\.:]\s*|\s+)(?<text>.+)$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex AttachedLeadingNumberRegex = new(
        @"^(?<number>[1-9]\d*)(?<text>\p{L}.*)$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex PureNumberRegex = new(
        @"^[1-9]\d*$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex WrappedNumberRegex = new(
        @"^(?<open>[\(\[\{])(?<number>[1-9]\d*)(?<close>[\)\]\}])(?:(?<separator>\s*[-–—\.:]\s*|\s+)(?<text>.+))?$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public IReadOnlyList<ChapterTechnicalNumberingProjection> Analyze(ChapterAnalysisScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);

        var results = scope.Candidates.ToDictionary(
            CandidateKey,
            candidate => new ChapterTechnicalNumberingProjection(
                candidate,
                candidate.Normalization.NormalizedText,
                candidate.Normalization.NormalizedText,
                candidate.Normalization.NormalizedText.Length > 0,
                ChapterTechnicalNumberingEvidence.None),
            StringComparer.Ordinal);

        foreach (var sourceGroup in scope.Candidates.GroupBy(candidate => candidate.Source))
        {
            var ordered = sourceGroup
                .OrderBy(candidate => candidate.Provenance.ScopeOrder)
                .ToArray();
            ApplyFamily(ordered, results);
            ApplyLongNumericIdentifierCandidates(ordered, results);
            ApplyStructuredNumericIdentifierFamily(ordered, results);
        }

        return scope.Candidates.Select(candidate => results[CandidateKey(candidate)]).ToArray();
    }

    private static void ApplyFamily(
        IReadOnlyList<ProjectedChapterCandidate> candidates,
        IDictionary<string, ChapterTechnicalNumberingProjection> results)
    {
        if (candidates.Count < 2)
            return;

        // Parse the whole source family, including candidates whose zero-padded
        // ordinal was already removed by Block 1. Dropping those candidates here
        // would split one technical family exactly at a digit-width boundary.
        var parsed = candidates.Select(Parse).ToArray();
        if (parsed.Any(item => item is null))
            return;

        var values = parsed.Cast<ParsedNumbering>().ToArray();
        var isStrictOrderedFamily = IsStrictlyConsecutive(values) &&
                                    IsScopeOrderConsecutive(values);
        var isBoundarySpanningFamily = IsBoundarySpanningFamily(values);
        var isMixedBoundaryRepresentationFamily = IsMixedBoundaryRepresentationFamily(
            values,
            isBoundarySpanningFamily);
        var hasConsistentKind = values.Select(item => item.Kind).Distinct().Count() == 1;
        var hasConsistentFormat = values
            .Select(item => item.Format)
            .Distinct(StringComparer.Ordinal)
            .Count() == 1;
        if ((!hasConsistentKind && !isMixedBoundaryRepresentationFamily) ||
            (!hasConsistentFormat && !isMixedBoundaryRepresentationFamily) ||
            values.All(item => IsLeadingPunctuationFormat(item.Format)) ||
            (!isStrictOrderedFamily && !isBoundarySpanningFamily) ||
            IsYearLikeSequence(values))
        {
            return;
        }

        var ordinalCorrelated = values.All(IsExplicitlyOrdinalCorrelated);
        if (values.Length == 2 && !ordinalCorrelated && !isBoundarySpanningFamily)
            return;

        foreach (var item in values)
        {
            var isUsable = item.Kind != ChapterTechnicalNumberingKind.PureNumber && item.BaseText.Length > 0;
            var evidence = new ChapterTechnicalNumberingEvidence(
                item.Kind,
                FamilyDetected: true,
                item.Number,
                item.Format,
                ordinalCorrelated,
                ordinalCorrelated
                    ? "Strict consecutive family with matching source ordinal evidence."
                    : isMixedBoundaryRepresentationFamily
                        ? "Consistent numeric family spans the leading-zero normalization boundary and continues as pure numbers after technical marker removal."
                        : isBoundarySpanningFamily
                            ? "Consistent numeric family spans the normalized leading-zero boundary."
                            : "Strict consecutive family with consistent leading structure.");

            results[CandidateKey(item.Candidate)] = new ChapterTechnicalNumberingProjection(
                item.Candidate,
                isUsable ? item.BaseText : string.Empty,
                isUsable ? item.BaseText : string.Empty,
                isUsable,
                evidence);
        }
    }

    private const int LongNumericIdentifierMinimumDigits = 8;

    private static void ApplyLongNumericIdentifierCandidates(
        IReadOnlyList<ProjectedChapterCandidate> candidates,
        IDictionary<string, ChapterTechnicalNumberingProjection> results)
    {
        if (candidates.Count == 0 ||
            candidates[0].Source is not (ChapterCandidateSource.Filename or ChapterCandidateSource.TrackTitle))
        {
            return;
        }

        foreach (var candidate in candidates)
        {
            var tokens = SplitTokens(candidate.Normalization.NormalizedText);
            if (tokens.Length == 0)
                continue;

            var allNumeric = tokens.All(IsUnsignedIntegerToken);
            var hasLongNumericToken = tokens.Any(token =>
                IsUnsignedIntegerToken(token) &&
                token.Length >= LongNumericIdentifierMinimumDigits);
            if (allNumeric && hasLongNumericToken)
            {
                results[CandidateKey(candidate)] = new ChapterTechnicalNumberingProjection(
                    candidate,
                    string.Empty,
                    string.Empty,
                    IsUsable: false,
                    new ChapterTechnicalNumberingEvidence(
                        ChapterTechnicalNumberingKind.LongNumericIdentifier,
                        FamilyDetected: false,
                        RemovedNumber: null,
                        Format: "long-numeric-identifier",
                        OrdinalCorrelated: false,
                        Reason: "Numeric-only source text contains a long technical identifier and no semantic chapter text."));
                continue;
            }

            if (!IsUnsignedIntegerToken(tokens[0]) ||
                tokens[0].Length < LongNumericIdentifierMinimumDigits ||
                tokens.Length < 2 ||
                IsUnsignedIntegerToken(tokens[1]))
            {
                continue;
            }

            var baseText = string.Join(' ', tokens.Skip(1)).Trim();
            if (baseText.Length == 0)
                continue;

            results[CandidateKey(candidate)] = new ChapterTechnicalNumberingProjection(
                candidate,
                baseText,
                baseText,
                IsUsable: true,
                new ChapterTechnicalNumberingEvidence(
                    ChapterTechnicalNumberingKind.LongNumericIdentifier,
                    FamilyDetected: false,
                    RemovedNumber: null,
                    Format: "leading-long-numeric-identifier",
                    OrdinalCorrelated: false,
                    Reason: "Leading long numeric identifier removed before semantic chapter text."));
        }
    }

    private static void ApplyStructuredNumericIdentifierFamily(
        IReadOnlyList<ProjectedChapterCandidate> candidates,
        IDictionary<string, ChapterTechnicalNumberingProjection> results)
    {
        if (candidates.Count < 2 || candidates[0].Source != ChapterCandidateSource.Filename)
            return;

        var parsed = candidates.Select(ParseStructuredNumericPrefix).ToArray();
        if (parsed.Any(item => item is null))
            return;

        var values = parsed.Cast<StructuredNumericPrefix>().ToArray();
        var numericTokenCount = values[0].NumericTokens.Length;
        if (numericTokenCount < 2 || values.Any(item => item.NumericTokens.Length != numericTokenCount))
            return;

        var hasLongIdentifierPosition = Enumerable.Range(0, numericTokenCount)
            .Any(position => values.All(item =>
                item.NumericTokens[position].Length >= LongNumericIdentifierMinimumDigits));
        if (!hasLongIdentifierPosition)
            return;

        var varyingPositions = Enumerable.Range(0, numericTokenCount)
            .Where(position => values
                .Select(item => item.NumericValues[position])
                .Distinct()
                .Count() > 1)
            .ToArray();
        var ordinalPositions = varyingPositions
            .Where(position => IsStructuredOrdinalPosition(values, position))
            .ToArray();
        if (ordinalPositions.Length != 1)
            return;

        var ordinalPosition = ordinalPositions[0];
        foreach (var position in varyingPositions)
        {
            if (position == ordinalPosition)
                continue;

            // Additional changing numeric fields are only tolerated when they are themselves
            // long machine identifiers. Short unrelated changing fields make the template ambiguous.
            if (!values.All(item =>
                    item.NumericTokens[position].Length >= LongNumericIdentifierMinimumDigits))
            {
                return;
            }
        }

        foreach (var item in values)
        {
            var baseText = item.BaseText.Trim();
            var isUsable = baseText.Length > 0;
            var ordinal = item.NumericValues[ordinalPosition];
            int? removedNumber = ordinal is >= int.MinValue and <= int.MaxValue
                ? (int)ordinal
                : null;
            var trackCorrelated = item.Candidate.Provenance.SourceTrackNumber.HasValue &&
                                  item.Candidate.Provenance.SourceTrackNumber.Value == ordinal;

            results[CandidateKey(item.Candidate)] = new ChapterTechnicalNumberingProjection(
                item.Candidate,
                isUsable ? baseText : string.Empty,
                isUsable ? baseText : string.Empty,
                isUsable,
                new ChapterTechnicalNumberingEvidence(
                    ChapterTechnicalNumberingKind.StructuredNumericIdentifier,
                    FamilyDetected: true,
                    removedNumber,
                    Format: "structured-numeric-prefix",
                    OrdinalCorrelated: trackCorrelated || IsStructuredOrdinalPosition(values, ordinalPosition),
                    Reason: isUsable
                        ? "Filename family contains a long numeric identifier and one proven ordinal field; technical numeric prefix removed."
                        : "Filename family contains only a long numeric identifier template and a proven ordinal field; no semantic chapter text remains."));
        }
    }

    private static StructuredNumericPrefix? ParseStructuredNumericPrefix(ProjectedChapterCandidate candidate)
    {
        var tokens = SplitTokens(candidate.Normalization.NormalizedText);
        if (tokens.Length < 2)
            return null;

        var prefixLength = 0;
        while (prefixLength < tokens.Length && IsUnsignedIntegerToken(tokens[prefixLength]))
            prefixLength++;

        if (prefixLength < 2)
            return null;

        // Preserve a year-like value when it directly precedes semantic text. This keeps titles
        // such as "1984 Roman" intact even when a technical identifier template comes before it.
        if (prefixLength < tokens.Length &&
            prefixLength > 2 &&
            IsYearLikeToken(tokens[prefixLength - 1]))
        {
            prefixLength--;
        }

        var numericTokens = tokens.Take(prefixLength).ToArray();
        var numericValues = new long[numericTokens.Length];
        for (var index = 0; index < numericTokens.Length; index++)
        {
            if (!long.TryParse(
                    numericTokens[index],
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out numericValues[index]))
            {
                return null;
            }
        }

        return new StructuredNumericPrefix(
            candidate,
            numericTokens,
            numericValues,
            string.Join(' ', tokens.Skip(prefixLength)));
    }

    private static bool IsStructuredOrdinalPosition(
        IReadOnlyList<StructuredNumericPrefix> values,
        int position)
    {
        var trackCorrelated = values.All(item =>
            item.Candidate.Provenance.SourceTrackNumber.HasValue &&
            item.Candidate.Provenance.SourceTrackNumber.Value == item.NumericValues[position]);
        if (trackCorrelated)
            return true;

        for (var index = 1; index < values.Count; index++)
        {
            if (values[index].NumericValues[position] != values[index - 1].NumericValues[position] + 1 ||
                values[index].Candidate.Provenance.ScopeOrder !=
                values[index - 1].Candidate.Provenance.ScopeOrder + 1)
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsYearLikeToken(string token) =>
        token.Length == 4 &&
        int.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out var value) &&
        value is >= 1800 and <= 2199;

    private static bool IsUnsignedIntegerToken(string token) =>
        token.Length > 0 && token.All(char.IsDigit);

    private static string[] SplitTokens(string text) =>
        text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static ParsedNumbering? Parse(ProjectedChapterCandidate candidate)
    {
        var text = candidate.Normalization.NormalizedText;
        var removedPrefix = candidate.Normalization.AppliedTransforms
            .FirstOrDefault(transform =>
                transform.Kind == ChapterNormalizationTransformKind.RemovedNumericPrefix &&
                int.TryParse(transform.Value, NumberStyles.None, CultureInfo.InvariantCulture, out _));
        if (removedPrefix is not null &&
            int.TryParse(removedPrefix.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var removedNumber))
        {
            var baseText = text.Trim();
            var kind = baseText.Length == 0
                ? ChapterTechnicalNumberingKind.PureNumber
                : ChapterTechnicalNumberingKind.LeadingNumber;
            var removedFormat = kind == ChapterTechnicalNumberingKind.PureNumber
                ? ResolveRemovedPureNumberFormat(candidate, removedPrefix.Value!)
                : ResolveRemovedPrefixFormat(candidate, removedPrefix.Value!);

            return new ParsedNumbering(
                candidate,
                kind,
                removedNumber,
                removedFormat,
                baseText,
                WasBoundaryNumberRemoved: true);
        }

        var wrapped = ParseWrappedNumber(candidate, text);
        if (wrapped is not null)
            return wrapped;

        if (PureNumberRegex.IsMatch(text) && int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var pure))
        {
            return new ParsedNumbering(
                candidate,
                ChapterTechnicalNumberingKind.PureNumber,
                pure,
                "pure",
                string.Empty,
                WasBoundaryNumberRemoved: false);
        }

        var match = LeadingNumberRegex.Match(text);
        if (match.Success &&
            int.TryParse(match.Groups["number"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var number))
        {
            var format = ResolveSeparatorFormat(
                match.Groups["separator"].Value,
                "leading");

            return new ParsedNumbering(
                candidate,
                ChapterTechnicalNumberingKind.LeadingNumber,
                number,
                format,
                match.Groups["text"].Value.Trim(),
                WasBoundaryNumberRemoved: false);
        }

        var attached = AttachedLeadingNumberRegex.Match(text);
        if (!attached.Success ||
            !int.TryParse(attached.Groups["number"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out number))
        {
            return null;
        }

        return new ParsedNumbering(
            candidate,
            ChapterTechnicalNumberingKind.LeadingNumber,
            number,
            "leading-attached",
            attached.Groups["text"].Value.Trim(),
            WasBoundaryNumberRemoved: false);
    }

    private static ParsedNumbering? ParseWrappedNumber(
        ProjectedChapterCandidate candidate,
        string text)
    {
        var match = WrappedNumberRegex.Match(text);
        if (!match.Success ||
            !AreMatchingWrappers(
                match.Groups["open"].Value[0],
                match.Groups["close"].Value[0]) ||
            !int.TryParse(
                match.Groups["number"].Value,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var number))
        {
            return null;
        }

        var wrapper = WrapperName(match.Groups["open"].Value[0]);
        var baseText = match.Groups["text"].Success
            ? match.Groups["text"].Value.Trim()
            : string.Empty;

        if (baseText.Length == 0)
        {
            return new ParsedNumbering(
                candidate,
                ChapterTechnicalNumberingKind.PureNumber,
                number,
                "pure-" + wrapper,
                string.Empty,
                WasBoundaryNumberRemoved: false);
        }

        return new ParsedNumbering(
            candidate,
            ChapterTechnicalNumberingKind.LeadingNumber,
            number,
            ResolveSeparatorFormat(
                match.Groups["separator"].Value,
                "leading-" + wrapper),
            baseText,
            WasBoundaryNumberRemoved: false);
    }

    private static string ResolveRemovedPureNumberFormat(
        ProjectedChapterCandidate candidate,
        string removedPrefix)
    {
        var raw = RawLeaf(candidate);
        return TryResolveWrappedRawPrefix(
            raw,
            removedPrefix,
            out _,
            out var wrapper)
            ? "pure-" + wrapper
            : "pure";
    }

    private static string ResolveRemovedPrefixFormat(
        ProjectedChapterCandidate candidate,
        string removedPrefix)
    {
        var raw = RawLeaf(candidate);

        if (TryResolveWrappedRawPrefix(
                raw,
                removedPrefix,
                out var wrappedPrefixLength,
                out var wrapper))
        {
            var prefix = "leading-" + wrapper;
            if (raw.Length <= wrappedPrefixLength)
                return prefix + "-space";

            return ResolveRawSeparatorFormat(
                raw[wrappedPrefixLength..],
                prefix);
        }

        if (!raw.StartsWith(removedPrefix, StringComparison.Ordinal) || raw.Length == removedPrefix.Length)
            return "leading-space";

        return ResolveRawSeparatorFormat(
            raw[removedPrefix.Length..],
            "leading");
    }

    private static string RawLeaf(ProjectedChapterCandidate candidate)
    {
        var raw = candidate.Normalization.RawText?.TrimStart() ?? string.Empty;
        var lastPathSeparator = raw.LastIndexOfAny(['\\', '/']);
        return lastPathSeparator >= 0
            ? raw[(lastPathSeparator + 1)..]
            : raw;
    }

    private static bool TryResolveWrappedRawPrefix(
        string raw,
        string numberText,
        out int prefixLength,
        out string wrapper)
    {
        foreach (var candidate in new[]
                 {
                     (Open: '(', Close: ')', Name: "parentheses"),
                     (Open: '[', Close: ']', Name: "brackets"),
                     (Open: '{', Close: '}', Name: "braces")
                 })
        {
            var prefix = candidate.Open + numberText + candidate.Close;
            if (!raw.StartsWith(prefix, StringComparison.Ordinal))
                continue;

            prefixLength = prefix.Length;
            wrapper = candidate.Name;
            return true;
        }

        prefixLength = 0;
        wrapper = string.Empty;
        return false;
    }

    private static string ResolveSeparatorFormat(
        string separator,
        string prefix)
    {
        if (separator.Any(character => character is '-' or '–' or '—'))
            return prefix + "-dash";

        if (separator.Any(character => character is '.' or ':'))
            return prefix + "-punctuation";

        return prefix + "-space";
    }

    private static string ResolveRawSeparatorFormat(
        string remainder,
        string prefix)
    {
        // The normalized branch recognizes the whole separator (" - "), not
        // merely its first space. Use the same structure for removed padded
        // ordinals, otherwise 099 - Title and 100 - Title become different families.
        var separator = remainder.TrimStart();
        return (separator.Length > 0 ? separator[0] : ' ') switch
        {
            '-' or '–' or '—' => prefix + "-dash",
            '.' or ':' => prefix + "-punctuation",
            '_' => prefix + "-space",
            _ when char.IsWhiteSpace(remainder[0]) => prefix + "-space",
            _ => prefix + "-attached"
        };
    }

    private static bool AreMatchingWrappers(char open, char close) =>
        (open, close) is
            ('(', ')') or
            ('[', ']') or
            ('{', '}');

    private static string WrapperName(char open) =>
        open switch
        {
            '(' => "parentheses",
            '[' => "brackets",
            '{' => "braces",
            _ => throw new ArgumentOutOfRangeException(nameof(open), open, "Unsupported numeric wrapper.")
        };

    private static bool IsLeadingPunctuationFormat(string format) =>
        format.StartsWith("leading", StringComparison.Ordinal) &&
        format.EndsWith("-punctuation", StringComparison.Ordinal);

    private static bool IsStrictlyConsecutive(IReadOnlyList<ParsedNumbering> values)
    {
        for (var index = 1; index < values.Count; index++)
        {
            if (values[index].Number != values[index - 1].Number + 1)
                return false;
        }

        return true;
    }

    private static bool IsYearLikeSequence(IReadOnlyList<ParsedNumbering> values) =>
        values.All(item => item.Number is >= 1800 and <= 2199);

    private static bool IsBoundarySpanningFamily(IReadOnlyList<ParsedNumbering> values)
    {
        var removed = values.Where(item => item.WasBoundaryNumberRemoved).ToArray();
        var preserved = values.Where(item => !item.WasBoundaryNumberRemoved).ToArray();
        if (removed.Length < 2 || preserved.Length == 0)
            return false;

        var numbers = values
            .Select(item => item.Number)
            .Distinct()
            .Order()
            .ToArray();
        for (var index = 1; index < numbers.Length; index++)
        {
            if (numbers[index] != numbers[index - 1] + 1)
                return false;
        }

        return preserved.Min(item => item.Number) == removed.Max(item => item.Number) + 1;
    }

    private static bool IsMixedBoundaryRepresentationFamily(
        IReadOnlyList<ParsedNumbering> values,
        bool isBoundarySpanningFamily)
    {
        if (!isBoundarySpanningFamily)
            return false;

        var removed = values.Where(item => item.WasBoundaryNumberRemoved).ToArray();
        var preserved = values.Where(item => !item.WasBoundaryNumberRemoved).ToArray();

        // Normalization deliberately removes zero-padded boundary ordinals such as 099.
        // When the remaining text is only a technical marker (for example "Kapitel 32"),
        // the pre-boundary candidates retain that marker as a generic fallback while 100+
        // normalize to pure numbers. Treat those two representations as one proven family.
        if (removed.Any(item => item.Kind != ChapterTechnicalNumberingKind.LeadingNumber) ||
            preserved.Any(item => item.Kind != ChapterTechnicalNumberingKind.PureNumber))
        {
            return false;
        }

        if (preserved.Any(item => !string.Equals(item.Format, "pure", StringComparison.Ordinal)))
            return false;

        return removed
            .Select(item => item.Format)
            .Distinct(StringComparer.Ordinal)
            .Count() == 1;
    }

    private static bool IsScopeOrderConsecutive(IReadOnlyList<ParsedNumbering> values)
    {
        for (var index = 1; index < values.Count; index++)
        {
            if (values[index].Candidate.Provenance.ScopeOrder !=
                values[index - 1].Candidate.Provenance.ScopeOrder + 1)
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsExplicitlyOrdinalCorrelated(ParsedNumbering item) =>
        item.Candidate.Provenance.SourceTrackNumber == item.Number ||
        item.Candidate.Provenance.EmbeddedOrdinal == item.Number;

    private static string CandidateKey(ProjectedChapterCandidate candidate) =>
        candidate.Provenance.EffectiveChapterId + ":" + candidate.Source;

    private sealed record StructuredNumericPrefix(
        ProjectedChapterCandidate Candidate,
        string[] NumericTokens,
        long[] NumericValues,
        string BaseText);

    private sealed record ParsedNumbering(
        ProjectedChapterCandidate Candidate,
        ChapterTechnicalNumberingKind Kind,
        int Number,
        string Format,
        string BaseText,
        bool WasBoundaryNumberRemoved);
}
