using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class ChapterAnalysisRulesTests
{
    [Theory]
    [InlineData(2, 5, true)]
    [InlineData(1, 3, false)]
    public void HasIndividualSignal_UsesExactFortyPercentBoundary(
        int deviationCount,
        int validCount,
        bool expected)
    {
        Assert.Equal(expected, ChapterAnalysisRules.HasIndividualSignal(deviationCount, validCount));
    }

    [Theory]
    [InlineData(50, 100, false)]
    [InlineData(51, 100, true)]
    public void HasProtectedDominance_RequiresMoreThanFiftyPercent(
        int dominantCount,
        int validCount,
        bool expected)
    {
        Assert.Equal(expected, ChapterAnalysisRules.HasProtectedDominance(dominantCount, validCount));
    }

    [Fact]
    public void HasFuzzyDominance_IncludesExactSixtyPercentBoundary()
    {
        Assert.True(ChapterAnalysisRules.HasFuzzyDominance(3, 5));
        Assert.False(ChapterAnalysisRules.HasFuzzyDominance(59, 100));
    }

    [Theory]
    [InlineData(90, 9)]
    [InlineData(100, 10)]
    [InlineData(110, 11)]
    public void GetMaximumFuzzyAbsorptionCount_UsesExactTenPercentFloor(
        int validCount,
        int expected)
    {
        Assert.Equal(expected, ChapterAnalysisRules.GetMaximumFuzzyAbsorptionCount(validCount));
    }
}
