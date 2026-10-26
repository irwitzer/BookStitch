using System.Globalization;
using System.Text;

namespace BookStitch.Services;

public sealed class ChapterComparisonKeyBuilder
{
    private static readonly HashSet<char> IgnorableEdgeCharacters =
    [
        '.', ',', ':', ';', '!', '?',
        '"', '„', '“', '”', '‚', '‘', '’',
        '。', '，', '！', '？', '：', '；'
    ];

    public string Build(string? normalizedText)
    {
        if (string.IsNullOrWhiteSpace(normalizedText))
            return string.Empty;

        var text = NormalizeForComparison(normalizedText);
        text = TrimIgnoredEdges(text);

        return text.ToLowerInvariant();
    }

    private static string NormalizeForComparison(string value)
    {
        string normalized;

        try
        {
            normalized = value.Normalize(NormalizationForm.FormKC);
        }
        catch (ArgumentException)
        {
            var repaired = new StringBuilder(value.Length);
            foreach (var rune in value.EnumerateRunes())
                repaired.Append(rune.ToString());

            normalized = repaired.ToString().Normalize(NormalizationForm.FormKC);
        }

        var result = new StringBuilder(normalized.Length);
        var previousWasWhitespace = false;

        foreach (var character in normalized)
        {
            if (char.IsWhiteSpace(character))
            {
                if (result.Length > 0 && !previousWasWhitespace)
                    result.Append(' ');

                previousWasWhitespace = true;
                continue;
            }

            if (char.IsControl(character))
                continue;

            result.Append(character is '–' or '—' ? '-' : character);
            previousWasWhitespace = false;
        }

        return result.ToString().Trim();
    }

    private static string TrimIgnoredEdges(string value)
    {
        var text = value.Trim();
        var changed = true;

        while (changed && text.Length > 0)
        {
            changed = false;

            var start = 0;
            var end = text.Length;

            while (start < end && IgnorableEdgeCharacters.Contains(text[start]))
                start++;

            while (end > start && IgnorableEdgeCharacters.Contains(text[end - 1]))
                end--;

            if (start > 0 || end < text.Length)
            {
                text = text[start..end].Trim();
                changed = true;
            }

            if (TryRemoveOuterBracketWrapper(text, out var inner))
            {
                text = inner.Trim();
                changed = true;
            }
        }

        return text;
    }

    private static bool TryRemoveOuterBracketWrapper(string value, out string inner)
    {
        inner = value;
        if (value.Length < 3)
            return false;

        var closing = value[0] switch
        {
            '(' => ')',
            '[' => ']',
            '{' => '}',
            '「' => '」',
            '『' => '』',
            '《' => '》',
            _ => '\0'
        };

        if (closing == '\0' || value[^1] != closing || !OuterPairEnclosesWholeValue(value, value[0], closing))
            return false;

        var candidate = value[1..^1];
        if (string.IsNullOrWhiteSpace(candidate))
            return false;

        inner = candidate;
        return true;
    }

    private static bool OuterPairEnclosesWholeValue(string value, char opening, char closing)
    {
        var depth = 0;

        for (var index = 0; index < value.Length; index++)
        {
            if (value[index] == opening)
                depth++;
            else if (value[index] == closing)
                depth--;

            if (depth == 0 && index < value.Length - 1)
                return false;

            if (depth < 0)
                return false;
        }

        return depth == 0;
    }
}
