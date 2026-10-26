using System.Text;
using System.Text.RegularExpressions;

namespace BookStitch.Services;

public sealed class ChapterCandidateNormalizer
{
    private static readonly Regex TechnicalMarkerRegex = new(
        @"(?<![\p{L}\p{N}])(?<marker>" + ChapterMarkerCatalog.TechnicalMarkerRegexAlternation + @")(?!\p{L})",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex PortugueseNumberWordBoundaryRegex = new(
        @"^\s+[-–—:]\s+",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex WordRegex = new(
        @"\G\p{L}+(?:-\p{L}+)*",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex AttachedNumericPrefixRegex = new(
        @"^(?<value>0\d+)(?=\p{L})",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex CompoundNumericPrefixRegex = new(
        @"^\s*(?<value>0\d+(?:\s*[-–—]\s*0\d+)+)(?=$|\s|[\.,:;!?])",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex CompoundNumericSuffixRegex = new(
        @"(?<!\p{N})(?<value>0\d+(?:\s*[-–—]\s*0\d+)+)\s*$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex NumericPrefixRegex = new(
        @"^\s*[\(\[\{]?(?<value>0\d+)[\)\]\}]?(?:\s*(?:[\.,:;!?]|[-–—])+\s*|\s+|$)",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex NumericSuffixRegex = new(
        @"(?:\s+(?:[-–—]\s*)?(?<value>0\d+)|\s*[\(\[\{](?<value>0\d+)[\)\]\}])\s*$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex EmptyBracketsRegex = new(
        @"\(\s*\)|\[\s*\]|\{\s*\}",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex RepeatedDashSeparatorsRegex = new(
        @"(?:\s*[-–—]\s*){2,}",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex LeadingOrphanSeparatorsRegex = new(
        @"^\s*(?:(?:[-–—]|[\.,:;])\s*)+",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex TrailingOrphanDashesRegex = new(
        @"\s*(?:[-–—]\s*)+$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex TrailingSeparatedPunctuationRegex = new(
        @"\s+[\.,:;]+\s*$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex FilenameCopySuffixRegex = new(
        @"\s+-\s+(?<value>" + ChapterMarkerCatalog.FilenameCopySuffixRegexAlternation + @")(?:\s+\(\d+\))?\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex CanonicalRomanNumeralRegex = new(
        @"^M{0,3}(?:CM|CD|D?C{0,3})(?:XC|XL|L?X{0,3})(?:IX|IV|V?I{0,3})$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly IReadOnlyDictionary<string, int> EnglishSmallNumbers =
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["zero"] = 0,
            ["one"] = 1,
            ["two"] = 2,
            ["three"] = 3,
            ["four"] = 4,
            ["five"] = 5,
            ["six"] = 6,
            ["seven"] = 7,
            ["eight"] = 8,
            ["nine"] = 9,
            ["ten"] = 10,
            ["eleven"] = 11,
            ["twelve"] = 12,
            ["thirteen"] = 13,
            ["fourteen"] = 14,
            ["fifteen"] = 15,
            ["sixteen"] = 16,
            ["seventeen"] = 17,
            ["eighteen"] = 18,
            ["nineteen"] = 19
        };

    private static readonly IReadOnlyDictionary<string, int> EnglishTens =
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["twenty"] = 20,
            ["thirty"] = 30,
            ["forty"] = 40,
            ["fifty"] = 50,
            ["sixty"] = 60,
            ["seventy"] = 70,
            ["eighty"] = 80,
            ["ninety"] = 90
        };

    private static readonly IReadOnlyDictionary<string, int> GermanSmallNumbers =
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["null"] = 0,
            ["ein"] = 1,
            ["eins"] = 1,
            ["eine"] = 1,
            ["einen"] = 1,
            ["zwei"] = 2,
            ["drei"] = 3,
            ["vier"] = 4,
            ["fünf"] = 5,
            ["fuenf"] = 5,
            ["sechs"] = 6,
            ["sieben"] = 7,
            ["acht"] = 8,
            ["neun"] = 9,
            ["zehn"] = 10,
            ["elf"] = 11,
            ["zwölf"] = 12,
            ["zwoelf"] = 12,
            ["dreizehn"] = 13,
            ["vierzehn"] = 14,
            ["fünfzehn"] = 15,
            ["fuenfzehn"] = 15,
            ["sechzehn"] = 16,
            ["siebzehn"] = 17,
            ["achtzehn"] = 18,
            ["neunzehn"] = 19
        };

    private static readonly IReadOnlyDictionary<string, int> GermanTens =
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["zwanzig"] = 20,
            ["dreißig"] = 30,
            ["dreissig"] = 30,
            ["vierzig"] = 40,
            ["fünfzig"] = 50,
            ["fuenfzig"] = 50,
            ["sechzig"] = 60,
            ["siebzig"] = 70,
            ["achtzig"] = 80,
            ["neunzig"] = 90
        };

    private readonly ChapterComparisonKeyBuilder _comparisonKeyBuilder;

    public ChapterCandidateNormalizer()
        : this(new ChapterComparisonKeyBuilder())
    {
    }

    public ChapterCandidateNormalizer(ChapterComparisonKeyBuilder comparisonKeyBuilder)
    {
        _comparisonKeyBuilder = comparisonKeyBuilder ?? throw new ArgumentNullException(nameof(comparisonKeyBuilder));
    }

    public ChapterNormalizationResult Normalize(ChapterCandidateSource source, string? rawText)
    {
        return NormalizeCore(source, rawText, preserveCanonicalUserText: false);
    }

    public ChapterNormalizationResult NormalizeUserDefinedCanonical(
        ChapterCandidateSource source,
        string? rawText)
    {
        if (source is not (ChapterCandidateSource.CanonicalTitle or ChapterCandidateSource.CanonicalAlbum))
        {
            throw new ArgumentOutOfRangeException(
                nameof(source),
                source,
                "User-defined canonical normalization is only valid for canonical title and album candidates.");
        }

        return NormalizeCore(source, rawText, preserveCanonicalUserText: true);
    }

    private ChapterNormalizationResult NormalizeCore(
        ChapterCandidateSource source,
        string? rawText,
        bool preserveCanonicalUserText)
    {
        var transforms = new List<ChapterNormalizationTransform>();
        var text = NormalizeUnicode(rawText ?? string.Empty, transforms);
        text = NormalizeWhitespace(text, transforms);

        if (preserveCanonicalUserText)
        {
            return new ChapterNormalizationResult(
                source,
                rawText,
                text,
                _comparisonKeyBuilder.Build(text),
                Array.AsReadOnly(transforms.ToArray()));
        }

        if (source == ChapterCandidateSource.Filename)
            text = ExtractFilename(text, transforms);

        text = RemoveAudioExtensions(text, transforms);

        if (source == ChapterCandidateSource.Filename)
            text = RemoveFilenameCopySuffix(text, transforms);

        if (text.Contains('_', StringComparison.Ordinal))
        {
            text = text.Replace('_', ' ');
            transforms.Add(new(ChapterNormalizationTransformKind.ReplacedUnderscores));
            text = NormalizeWhitespace(text, transforms);
        }

        text = RemoveTechnicalNumbering(text, transforms, out var markerFallback);
        text = RemoveBoundaryNumbering(text, transforms);
        text = CleanupSyntax(text, transforms);
        text = NormalizeWhitespace(text, transforms);

        if (text.Length == 0 && markerFallback is not null)
            text = markerFallback;

        var comparisonKey = _comparisonKeyBuilder.Build(text);

        return new ChapterNormalizationResult(
            source,
            rawText,
            text,
            comparisonKey,
            Array.AsReadOnly(transforms.ToArray()));
    }

    private static string NormalizeUnicode(
        string value,
        ICollection<ChapterNormalizationTransform> transforms)
    {
        string normalized;

        try
        {
            normalized = value.Normalize(NormalizationForm.FormC);
        }
        catch (ArgumentException)
        {
            var repaired = new StringBuilder(value.Length);
            foreach (var rune in value.EnumerateRunes())
                repaired.Append(rune.ToString());

            normalized = repaired.ToString().Normalize(NormalizationForm.FormC);
        }

        if (!string.Equals(value, normalized, StringComparison.Ordinal))
            transforms.Add(new(ChapterNormalizationTransformKind.NormalizedUnicode));

        return normalized;
    }

    private static string RemoveFilenameCopySuffix(
        string value,
        ICollection<ChapterNormalizationTransform> transforms)
    {
        var match = FilenameCopySuffixRegex.Match(value);
        if (!match.Success)
            return value;

        transforms.Add(new(
            ChapterNormalizationTransformKind.RemovedCopySuffix,
            match.Value.Trim()));
        return value[..match.Index].TrimEnd();
    }

    private static string NormalizeWhitespace(
        string value,
        ICollection<ChapterNormalizationTransform> transforms)
    {
        var result = new StringBuilder(value.Length);
        var previousWasWhitespace = false;
        var removedControl = false;

        foreach (var character in value)
        {
            if (char.IsWhiteSpace(character))
            {
                if (result.Length > 0 && !previousWasWhitespace)
                    result.Append(' ');

                previousWasWhitespace = true;
                continue;
            }

            if (char.IsControl(character))
            {
                removedControl = true;
                continue;
            }

            result.Append(character);
            previousWasWhitespace = false;
        }

        var normalized = result.ToString().Trim();
        if (removedControl)
            transforms.Add(new(ChapterNormalizationTransformKind.RemovedControlCharacters));

        if (!string.Equals(value, normalized, StringComparison.Ordinal))
            transforms.Add(new(ChapterNormalizationTransformKind.CollapsedWhitespace));

        return normalized;
    }

    private static string ExtractFilename(
        string value,
        ICollection<ChapterNormalizationTransform> transforms)
    {
        var lastSeparator = value.LastIndexOfAny(['\\', '/']);
        if (lastSeparator < 0)
            return value;

        var filename = value[(lastSeparator + 1)..];
        transforms.Add(new(ChapterNormalizationTransformKind.ExtractedFilename, filename));
        return filename;
    }

    private static string RemoveAudioExtensions(
        string value,
        ICollection<ChapterNormalizationTransform> transforms)
    {
        var text = value;

        while (text.Length > 0)
        {
            var format = AudioInputFormatCatalog.All.FirstOrDefault(candidate =>
                text.EndsWith(candidate.Extension, StringComparison.OrdinalIgnoreCase));

            if (format is null)
                break;

            text = text[..^format.Extension.Length].TrimEnd();
            transforms.Add(new(
                ChapterNormalizationTransformKind.RemovedAudioExtension,
                format.Extension));
        }

        return text;
    }

    private static string RemoveTechnicalNumbering(
        string value,
        ICollection<ChapterNormalizationTransform> transforms,
        out string? markerFallback)
    {
        markerFallback = null;
        if (value.Length == 0)
            return value;

        if (ChapterMarkerCatalog.TryMatchStructuredChapterPrefix(value, out var structuredPrefixLength))
        {
            transforms.Add(new(
                ChapterNormalizationTransformKind.RemovedTechnicalNumbering,
                value[..structuredPrefixLength].Trim()));
            return value[structuredPrefixLength..];
        }

        var spans = new List<TextSpan>();

        foreach (Match markerMatch in TechnicalMarkerRegex.Matches(value))
        {
            var marker = markerMatch.Groups["marker"].Value;
            if (!TryReadTechnicalNumber(value, marker, markerMatch.Index + markerMatch.Length, out var numberSpan))
                continue;

            spans.Add(new(markerMatch.Index, numberSpan.End - markerMatch.Index));
            transforms.Add(new(
                ChapterNormalizationTransformKind.RemovedTechnicalNumbering,
                value[markerMatch.Index..numberSpan.End].Trim()));

            if (!ChapterMarkerCatalog.IsContainerMarker(marker) && markerFallback is null)
                markerFallback = marker;
        }

        if (spans.Count == 0)
            return value;

        var result = new StringBuilder(value);
        foreach (var span in spans.OrderByDescending(span => span.Start))
            result.Remove(span.Start, span.Length);

        return result.ToString();
    }

    private static bool TryReadTechnicalNumber(string value, string marker, int start, out TextSpan numberSpan)
    {
        numberSpan = default;
        var index = start;

        while (index < value.Length && char.IsWhiteSpace(value[index]))
            index++;

        if (index >= value.Length)
            return false;

        if (char.IsDigit(value[index]))
        {
            var end = index + 1;
            while (end < value.Length && char.IsDigit(value[end]))
                end++;

            if (end < value.Length && (char.IsLetterOrDigit(value[end]) || value[end] == '_'))
                return false;

            numberSpan = new(index, end - index);
            return true;
        }

        var tokenEnd = index;
        while (tokenEnd < value.Length && char.IsLetter(value[tokenEnd]))
            tokenEnd++;

        if (tokenEnd > index)
        {
            var token = value[index..tokenEnd];
            if (token.All(character => !char.IsLetter(character) || char.IsUpper(character)) &&
                CanonicalRomanNumeralRegex.IsMatch(token))
            {
                numberSpan = new(index, tokenEnd - index);
                return true;
            }
        }

        return TryReadNumberWords(value, marker, index, out numberSpan);
    }

    private static bool TryReadNumberWords(string value, string marker, int start, out TextSpan numberSpan)
    {
        numberSpan = default;
        var wordEnds = new List<int>();
        var position = start;

        for (var count = 0; count < 8 && position < value.Length; count++)
        {
            var match = WordRegex.Match(value, position);
            if (!match.Success)
                break;

            wordEnds.Add(match.Index + match.Length);
            position = match.Index + match.Length;

            var whitespaceStart = position;
            while (position < value.Length && char.IsWhiteSpace(value[position]))
                position++;

            if (position == whitespaceStart)
                break;
        }

        for (var index = wordEnds.Count - 1; index >= 0; index--)
        {
            var phrase = value[start..wordEnds[index]];
            if (!IsNumberWordPhrase(marker, phrase))
                continue;

            if (ChapterMarkerCatalog.IsPortugueseOnlyChapterNumberWord(marker, phrase) &&
                wordEnds[index] < value.Length &&
                !PortugueseNumberWordBoundaryRegex.IsMatch(value[wordEnds[index]..]))
            {
                continue;
            }

            numberSpan = new(start, wordEnds[index] - start);
            return true;
        }

        return false;
    }

    private static bool IsNumberWordPhrase(string marker, string phrase) =>
        TryParseEnglishNumber(phrase) ||
        TryParseGermanNumber(phrase) ||
        ChapterMarkerCatalog.IsMarkerBoundNumberWord(marker, phrase);

    private static bool TryParseEnglishNumber(string phrase)
    {
        var words = phrase
            .ToLowerInvariant()
            .Replace('-', ' ')
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (words.Length == 0)
            return false;

        var total = 0;
        var current = 0;
        var sawNumber = false;

        foreach (var word in words)
        {
            if (EnglishSmallNumbers.TryGetValue(word, out var small))
            {
                current += small;
                sawNumber = true;
                continue;
            }

            if (EnglishTens.TryGetValue(word, out var tens))
            {
                current += tens;
                sawNumber = true;
                continue;
            }

            if (word == "hundred" && current is >= 1 and <= 9)
            {
                current *= 100;
                sawNumber = true;
                continue;
            }

            if (word == "and" && sawNumber)
                continue;

            return false;
        }

        total += current;
        return sawNumber && total is >= 0 and <= 999 && IsValidEnglishNumberShape(words);
    }

    private static bool IsValidEnglishNumberShape(IReadOnlyList<string> words)
    {
        var hundredIndex = Array.IndexOf(words.ToArray(), "hundred");
        if (hundredIndex >= 0 && hundredIndex != 1)
            return false;

        if (words.Count(word => word == "hundred") > 1 ||
            words.Count(word => word == "and") > 1)
        {
            return false;
        }

        var numericWords = words.Where(word => word != "and" && word != "hundred").ToArray();
        if (numericWords.Length == 0)
            return false;

        var tailStart = hundredIndex >= 0 ? 1 : 0;
        var tail = numericWords.Skip(tailStart).ToArray();
        if (tail.Length > 2)
            return false;

        if (tail.Length == 2)
            return EnglishTens.ContainsKey(tail[0]) && EnglishSmallNumbers.TryGetValue(tail[1], out var unit) && unit is >= 1 and <= 9;

        return true;
    }

    private static bool TryParseGermanNumber(string phrase)
    {
        var compact = phrase
            .ToLowerInvariant()
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal);

        if (compact.Length == 0)
            return false;

        var hundredIndex = compact.IndexOf("hundert", StringComparison.Ordinal);
        if (hundredIndex < 0)
            return TryParseGermanBelowHundred(compact, out _);

        if (compact.IndexOf("hundert", hundredIndex + 7, StringComparison.Ordinal) >= 0)
            return false;

        var prefix = compact[..hundredIndex];
        var suffix = compact[(hundredIndex + 7)..];

        if (!TryParseGermanUnit(prefix.Length == 0 ? "ein" : prefix, out _))
            return false;

        return suffix.Length == 0 || TryParseGermanBelowHundred(suffix, out _);
    }

    private static bool TryParseGermanBelowHundred(string value, out int number)
    {
        if (GermanSmallNumbers.TryGetValue(value, out number) || GermanTens.TryGetValue(value, out number))
            return true;

        var separator = value.IndexOf("und", StringComparison.Ordinal);
        if (separator <= 0 || value.IndexOf("und", separator + 3, StringComparison.Ordinal) >= 0)
            return false;

        var unitText = value[..separator];
        var tensText = value[(separator + 3)..];
        if (!TryParseGermanUnit(unitText, out var unit) || !GermanTens.TryGetValue(tensText, out var tens))
            return false;

        number = tens + unit;
        return true;
    }

    private static bool TryParseGermanUnit(string value, out int unit)
    {
        if (GermanSmallNumbers.TryGetValue(value, out unit) && unit is >= 1 and <= 9)
            return true;

        unit = 0;
        return false;
    }

    private static string RemoveBoundaryNumbering(
        string value,
        ICollection<ChapterNormalizationTransform> transforms)
    {
        var text = value;
        var changed = true;

        while (changed && text.Length > 0)
        {
            changed = false;

            if (TryRemove(AttachedNumericPrefixRegex, ref text, ChapterNormalizationTransformKind.RemovedNumericPrefix, transforms) ||
                TryRemove(CompoundNumericPrefixRegex, ref text, ChapterNormalizationTransformKind.RemovedNumericSequence, transforms) ||
                TryRemove(CompoundNumericSuffixRegex, ref text, ChapterNormalizationTransformKind.RemovedNumericSequence, transforms) ||
                TryRemove(NumericPrefixRegex, ref text, ChapterNormalizationTransformKind.RemovedNumericPrefix, transforms) ||
                TryRemove(NumericSuffixRegex, ref text, ChapterNormalizationTransformKind.RemovedNumericSuffix, transforms))
            {
                changed = true;
                text = text.Trim();
            }
        }

        return text;
    }

    private static bool TryRemove(
        Regex regex,
        ref string value,
        ChapterNormalizationTransformKind kind,
        ICollection<ChapterNormalizationTransform> transforms)
    {
        var match = regex.Match(value);
        if (!match.Success)
            return false;

        var diagnosticValue = match.Groups["value"].Value;
        value = value.Remove(match.Index, match.Length);
        transforms.Add(new(kind, diagnosticValue));
        return true;
    }

    private static string CleanupSyntax(
        string value,
        ICollection<ChapterNormalizationTransform> transforms)
    {
        var text = value;
        var withoutEmptyBrackets = EmptyBracketsRegex.Replace(text, " ");
        if (!string.Equals(text, withoutEmptyBrackets, StringComparison.Ordinal))
        {
            text = withoutEmptyBrackets;
            transforms.Add(new(ChapterNormalizationTransformKind.RemovedEmptyBrackets));
        }

        var cleaned = RepeatedDashSeparatorsRegex.Replace(text, " - ");
        cleaned = LeadingOrphanSeparatorsRegex.Replace(cleaned, string.Empty);
        cleaned = TrailingOrphanDashesRegex.Replace(cleaned, string.Empty);
        cleaned = TrailingSeparatedPunctuationRegex.Replace(cleaned, string.Empty);

        if (!string.Equals(text, cleaned, StringComparison.Ordinal))
            transforms.Add(new(ChapterNormalizationTransformKind.CleanedSeparators));

        return cleaned;
    }

    private readonly record struct TextSpan(int Start, int Length)
    {
        public int End => Start + Length;
    }
}
