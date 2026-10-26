using BookStitch.Models;
using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class MetadataDateParserTests
{
    [Theory]
    [InlineData("2026-01-31", 2026, 1, 31)]
    [InlineData("2026.01.31", 2026, 1, 31)]
    [InlineData("2026/01/31", 2026, 1, 31)]
    [InlineData("2026 01 31", 2026, 1, 31)]
    [InlineData("31.01.2026", 2026, 1, 31)]
    [InlineData("31-01-2026", 2026, 1, 31)]
    [InlineData("31/01/2026", 2026, 1, 31)]
    [InlineData("31 01 2026", 2026, 1, 31)]
    [InlineData("20260131", 2026, 1, 31)]
    [InlineData("31012026", 2026, 1, 31)]
    [InlineData("2026", 2026, null, null)]
    [InlineData("2026-01", 2026, 1, null)]
    [InlineData("2026.01", 2026, 1, null)]
    [InlineData("2026/01", 2026, 1, null)]
    [InlineData("2026 01", 2026, 1, null)]
    public void TryParse_AcceptsDefinedFormats(string text, int year, int? month, int? day)
    {
        Assert.True(MetadataDateParser.TryParse(text, out var parsed));
        Assert.Equal(new MetadataDate(year, month, day), parsed);
    }

    [Theory]
    [InlineData("2026-02-29")]
    [InlineData("31.02.2026")]
    [InlineData("2026-13-01")]
    [InlineData("00.01.2026")]
    [InlineData("2026/01-31")]
    [InlineData("01/31/2026")]
    [InlineData("20261301")]
    [InlineData("31022026")]
    [InlineData("2026-1-31")]
    [InlineData("2026-01-1")]
    [InlineData("")]
    public void TryParse_RejectsInvalidOrUndefinedFormats(string text)
    {
        Assert.False(MetadataDateParser.TryParse(text, out _));
    }

    [Fact]
    public void TryParse_AcceptsLeapDayOnlyForValidCalendarYear()
    {
        Assert.True(MetadataDateParser.TryParse("2024-02-29", out var leapDay));
        Assert.Equal(new MetadataDate(2024, 2, 29), leapDay);
        Assert.False(MetadataDateParser.TryParse("2023-02-29", out _));
    }

    [Theory]
    [InlineData(2026, null, null, "2026")]
    [InlineData(2026, 1, null, "2026.01")]
    [InlineData(2026, 1, 31, "2026.01.31")]
    public void FormatDisplay_UsesNormalizedDottedUiRepresentation(int year, int? month, int? day, string expected)
    {
        Assert.Equal(expected, MetadataDateParser.FormatDisplay(new MetadataDate(year, month, day)));
    }
}
