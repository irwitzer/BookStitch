using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class ChapterNearDuplicateAnalyzerTests
{
    private readonly ChapterNearDuplicateAnalyzer _analyzer = new();

    [Fact]
    public void Analyze_AbsorbsSmallTypoGroupsIntoExactDominance()
    {
        var groups = new[]
        {
            Group("a", "der stein der weisen", 94),
            Group("b", "der stein der wiesen", 3),
            Group("c", "der stein der weisn", 3)
        };

        var result = _analyzer.Analyze(groups, 100);

        Assert.False(result["a"].WasAbsorbed);
        Assert.Equal("a", result["b"].AnalysisGroupId);
        Assert.Equal("a", result["c"].AnalysisGroupId);
    }

    [Fact]
    public void Analyze_RequiresSixtyPercentExactDominance()
    {
        var result = _analyzer.Analyze(
            [Group("a", "dominant title", 59), Group("b", "dominant titel", 41)],
            100);

        Assert.False(result["b"].WasAbsorbed);
    }

    [Fact]
    public void Analyze_ExactSixtyPercentDominanceAllowsBoundedAbsorption()
    {
        var result = _analyzer.Analyze(
            [
                Group("a", "chapter title", 60),
                Group("b", "chapter titla", 10),
                Group("c", "different value", 30)
            ],
            100);

        Assert.True(result["b"].WasAbsorbed);
        Assert.Equal("a", result["b"].AnalysisGroupId);
    }

    [Fact]
    public void Analyze_EnforcesTenPercentCapDeterministically()
    {
        var groups = new[]
        {
            Group("a", "chapter title", 80),
            Group("b", "chapter titla", 6),
            Group("c", "chapter titlb", 6),
            Group("d", "different value", 8)
        };

        var first = _analyzer.Analyze(groups, 100);
        var second = _analyzer.Analyze(groups.Reverse().ToArray(), 100);

        Assert.Equal(1, first.Values.Count(item => item.WasAbsorbed));
        Assert.Equal(
            first.OrderBy(item => item.Key).Select(item => item.Value),
            second.OrderBy(item => item.Key).Select(item => item.Value));
    }

    [Theory]
    [InlineData(9, true)]
    [InlineData(10, true)]
    [InlineData(11, false)]
    public void Analyze_TenPercentBoundaryLimitsWholeGroupAbsorption(
        int typoCount,
        bool expectedAbsorbed)
    {
        var result = _analyzer.Analyze(
            [
                Group("a", "chapter title", 100 - typoCount),
                Group("b", "chapter titla", typoCount)
            ],
            100);

        Assert.Equal(expectedAbsorbed, result["b"].WasAbsorbed);
    }

    [Theory]
    [InlineData("abcd", "abce")]
    [InlineData("montagmorgen", "montagabend")]
    public void Analyze_DoesNotMergeShortOrSemanticallyDifferentText(string dominant, string candidate)
    {
        var result = _analyzer.Analyze(
            [Group("a", dominant, 90), Group("b", candidate, 10)],
            100);

        Assert.False(result["b"].WasAbsorbed);
    }

    [Fact]
    public void Analyze_HandlesShortCjkTitlesConservativelyByUnicodeScalar()
    {
        var close = _analyzer.Analyze(
            [Group("a", "星空物语集", 90), Group("b", "星空物語集", 10)],
            100);
        var different = _analyzer.Analyze(
            [Group("a", "星空", 90), Group("b", "深海", 10)],
            100);

        Assert.True(close["b"].WasAbsorbed);
        Assert.False(different["b"].WasAbsorbed);
    }

    private static ChapterNearDuplicateGroup Group(string id, string key, int count) => new(id, key, count);
}
