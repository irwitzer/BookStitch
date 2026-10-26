using System.Text;

namespace BookStitch.Services;

public sealed record ChapterNearDuplicateGroup(
    string ExactGroupId,
    string ComparisonKey,
    int Count);

public sealed record ChapterNearDuplicateAssignment(
    string ExactGroupId,
    string AnalysisGroupId,
    bool WasAbsorbed,
    int? EditDistance);

public sealed class ChapterNearDuplicateAnalyzer
{
    public IReadOnlyDictionary<string, ChapterNearDuplicateAssignment> Analyze(
        IReadOnlyList<ChapterNearDuplicateGroup> exactGroups,
        int validMeaningfulCount)
    {
        ArgumentNullException.ThrowIfNull(exactGroups);

        var assignments = exactGroups.ToDictionary(
            group => group.ExactGroupId,
            group => new ChapterNearDuplicateAssignment(
                group.ExactGroupId,
                group.ExactGroupId,
                WasAbsorbed: false,
                EditDistance: null),
            StringComparer.Ordinal);

        if (validMeaningfulCount <= 0 || exactGroups.Count < 2)
            return assignments;

        var orderedBySize = exactGroups
            .OrderByDescending(group => group.Count)
            .ThenBy(group => group.ExactGroupId, StringComparer.Ordinal)
            .ToArray();
        var dominant = orderedBySize[0];
        if (!ChapterAnalysisRules.HasFuzzyDominance(dominant.Count, validMeaningfulCount))
            return assignments;

        var absorptionLimit = ChapterAnalysisRules.GetMaximumFuzzyAbsorptionCount(validMeaningfulCount);
        if (absorptionLimit <= 0)
            return assignments;

        var eligible = orderedBySize
            .Skip(1)
            .Select(group => BuildEligible(group, dominant))
            .Where(item => item is not null)
            .Cast<EligibleGroup>()
            .OrderBy(item => item.Distance)
            .ThenBy(item => item.LengthDifference)
            .ThenBy(item => item.Group.ComparisonKey, StringComparer.Ordinal)
            .ThenBy(item => item.Group.ExactGroupId, StringComparer.Ordinal)
            .ToArray();

        var absorbed = 0;
        foreach (var item in eligible)
        {
            if (absorbed + item.Group.Count > absorptionLimit)
                continue;

            assignments[item.Group.ExactGroupId] = new ChapterNearDuplicateAssignment(
                item.Group.ExactGroupId,
                dominant.ExactGroupId,
                WasAbsorbed: true,
                item.Distance);
            absorbed += item.Group.Count;
        }

        return assignments;
    }

    private static EligibleGroup? BuildEligible(
        ChapterNearDuplicateGroup candidate,
        ChapterNearDuplicateGroup dominant)
    {
        var leftRunes = candidate.ComparisonKey.EnumerateRunes().ToArray();
        var rightRunes = dominant.ComparisonKey.EnumerateRunes().ToArray();
        var leftLength = leftRunes.Length;
        var rightLength = rightRunes.Length;
        var shortest = Math.Min(leftLength, rightLength);
        var longest = Math.Max(leftLength, rightLength);
        var lengthDifference = longest - shortest;
        var isCjkPair = leftRunes.Any(IsCjkRune) && rightRunes.Any(IsCjkRune);

        if (shortest < (isCjkPair ? 2 : 5) ||
            lengthDifference > 3 ||
            (longest > 0 && (double)lengthDifference / longest > 0.20))
        {
            return null;
        }

        var maximumDistance = isCjkPair ? 1 : longest <= 12 ? 2 : 3;
        var distance = CalculateEditDistance(leftRunes, rightRunes, maximumDistance);
        return distance <= maximumDistance
            ? new EligibleGroup(candidate, distance, lengthDifference)
            : null;
    }

    private static int CalculateEditDistance(Rune[] left, Rune[] right, int stopAfter)
    {
        if (Math.Abs(left.Length - right.Length) > stopAfter)
            return stopAfter + 1;

        var previous = Enumerable.Range(0, right.Length + 1).ToArray();
        var current = new int[right.Length + 1];

        for (var leftIndex = 1; leftIndex <= left.Length; leftIndex++)
        {
            current[0] = leftIndex;
            for (var rightIndex = 1; rightIndex <= right.Length; rightIndex++)
            {
                var substitutionCost = left[leftIndex - 1] == right[rightIndex - 1] ? 0 : 1;
                current[rightIndex] = Math.Min(
                    Math.Min(current[rightIndex - 1] + 1, previous[rightIndex] + 1),
                    previous[rightIndex - 1] + substitutionCost);
            }

            (previous, current) = (current, previous);
        }

        return previous[right.Length];
    }

    private static bool IsCjkRune(Rune rune) => rune.Value is
        (>= 0x3400 and <= 0x4DBF) or
        (>= 0x4E00 and <= 0x9FFF) or
        (>= 0x3040 and <= 0x30FF) or
        (>= 0xF900 and <= 0xFAFF) or
        (>= 0x20000 and <= 0x323AF);

    private sealed record EligibleGroup(
        ChapterNearDuplicateGroup Group,
        int Distance,
        int LengthDifference);
}
