using Xunit;
using BookStitch.Services;

namespace BookStitch.Tests.Services;

public sealed class NaturalStringComparerTests
{
    private readonly NaturalStringComparer _comparer = new();

    [Fact]
    public void Sorts_Numbers_Naturally_Inside_Text()
    {
        var values = new[]
        {
            "Track 10",
            "Track 2",
            "Track 1"
        };

        Array.Sort(values, _comparer);

        Assert.Equal(["Track 1", "Track 2", "Track 10"], values);
    }

    [Fact]
    public void Sorts_Disc_And_Track_Numbers_Naturally()
    {
        var values = new[]
        {
            "CD 10 Track 1",
            "CD 2 Track 10",
            "CD 2 Track 2",
            "CD 1 Track 9"
        };

        Array.Sort(values, _comparer);

        Assert.Equal(
            [
                "CD 1 Track 9",
                "CD 2 Track 2",
                "CD 2 Track 10",
                "CD 10 Track 1"
            ],
            values);
    }

    [Fact]
    public void Sorts_Number_Suffixes_In_A_Predictable_Order()
    {
        var values = new[]
        {
            "003B Kapitel",
            "003A Kapitel",
            "003 Kapitel"
        };

        Array.Sort(values, _comparer);

        Assert.Equal(["003 Kapitel", "003A Kapitel", "003B Kapitel"], values);
    }

    [Fact]
    public void Trims_Outer_Whitespace_For_Comparison()
    {
        var result = _comparer.Compare(" Track 2 ", "Track 10");

        Assert.True(result < 0);
    }

    [Fact]
    public void Uses_Stable_Fallback_For_Numerically_Equal_Values()
    {
        Assert.NotEqual(0, _comparer.Compare("3", "03"));
        Assert.NotEqual(0, _comparer.Compare("03", "003"));
    }
}
