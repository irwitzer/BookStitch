using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class ProgressStateChangeServiceTests
{
    [Theory]
    [InlineData(0.0, -5.0, 0.0, false)]
    [InlineData(25.0, 25.04, 25.04, false)]
    [InlineData(25.0, 25.05, 25.05, true)]
    [InlineData(95.0, 120.0, 100.0, true)]
    public void CreatePercentChange_NormalizesAndUsesDisplayThreshold(
        double currentValue,
        double incomingValue,
        double expectedValue,
        bool expectedHasChanged)
    {
        var result = ProgressStateChangeService.CreatePercentChange(
            currentValue,
            incomingValue);

        Assert.Equal(expectedValue, result.Value, 5);
        Assert.Equal(expectedHasChanged, result.HasChanged);
    }

    [Theory]
    [InlineData("Accent", "accent", "accent", false)]
    [InlineData("Accent", "Error", "Error", true)]
    public void CreateForegroundChange_UsesCaseInsensitiveComparison(
        string currentValue,
        string incomingValue,
        string expectedValue,
        bool expectedHasChanged)
    {
        var result = ProgressStateChangeService.CreateForegroundChange(
            currentValue,
            incomingValue);

        Assert.Equal(expectedValue, result.Value);
        Assert.Equal(expectedHasChanged, result.HasChanged);
    }

    [Theory]
    [InlineData("", null, "", false)]
    [InlineData("Analyse", "Analyse", "Analyse", false)]
    [InlineData("Analyse", "Export", "Export", true)]
    public void CreateTextChange_NormalizesAndUsesOrdinalComparison(
        string currentValue,
        string? incomingValue,
        string expectedValue,
        bool expectedHasChanged)
    {
        var result = ProgressStateChangeService.CreateTextChange(
            currentValue,
            incomingValue);

        Assert.Equal(expectedValue, result.Value);
        Assert.Equal(expectedHasChanged, result.HasChanged);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    public void CreateIndeterminateChange_DetectsBooleanChanges(
        bool currentValue,
        bool incomingValue,
        bool expectedHasChanged)
    {
        var result = ProgressStateChangeService.CreateIndeterminateChange(
            currentValue,
            incomingValue);

        Assert.Equal(incomingValue, result.Value);
        Assert.Equal(expectedHasChanged, result.HasChanged);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    public void CreateAnalysisActiveChange_DetectsBooleanChanges(
        bool currentValue,
        bool incomingValue,
        bool expectedHasChanged)
    {
        var result = ProgressStateChangeService.CreateAnalysisActiveChange(
            currentValue,
            incomingValue);

        Assert.Equal(incomingValue, result.Value);
        Assert.Equal(expectedHasChanged, result.HasChanged);
    }
}