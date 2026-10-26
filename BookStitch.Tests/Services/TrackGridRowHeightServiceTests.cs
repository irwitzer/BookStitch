using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class TrackGridRowHeightServiceTests
{
    [Fact]
    public void Options_expose_six_fixed_levels_from_compact_to_large()
    {
        Assert.Equal(6, TrackGridRowHeightService.Options.Count);
        Assert.Equal(
            new[] { 22d, 24d, 26d, 28d, 30d, 32d },
            TrackGridRowHeightService.Options.Select(option => option.RowHeight).ToArray());
    }

    [Theory]
    [InlineData(-5, 0)]
    [InlineData(0, 0)]
    [InlineData(3, 3)]
    [InlineData(5, 5)]
    [InlineData(99, 5)]
    public void NormalizeLevel_clamps_to_supported_range(int value, int expected)
    {
        Assert.Equal(expected, TrackGridRowHeightService.NormalizeLevel(value));
    }
}
