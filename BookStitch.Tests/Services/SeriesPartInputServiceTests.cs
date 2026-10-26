using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class SeriesPartInputServiceTests
{
    [Theory]
    [InlineData("4,5", "4.5")]
    [InlineData("4,5B", "4.5B")]
    [InlineData("12,7A", "12.7A")]
    [InlineData("6,6,3", "6.6.3")]
    [InlineData("Text, Value", "Text, Value")]
    public void NormalizePartText_ReplacesOnlyDigitDelimitedCommas(string input, string expected)
    {
        Assert.Equal(expected, SeriesPartInputService.NormalizePartText(input));
    }

    [Theory]
    [InlineData("0", "0")]
    [InlineData("1.5", "1.5")]
    [InlineData("1,50", "1.5")]
    [InlineData("01.50", "1.5")]
    public void TryNormalizeNumeric_NormalizesSimpleNonNegativeNumericParts(string input, string expected)
    {
        Assert.True(SeriesPartInputService.TryNormalizeNumeric(input, out var normalized));
        Assert.Equal(expected, normalized);
    }

}
