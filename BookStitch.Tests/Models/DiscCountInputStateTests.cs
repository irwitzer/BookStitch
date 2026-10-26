using BookStitch.Models;
using Xunit;

namespace BookStitch.Tests.Models;

public sealed class DiscCountInputStateTests
{
    [Theory]
    [InlineData(1, true)]
    [InlineData(2, false)]
    [InlineData(12, false)]
    [InlineData(13, true)]
    [InlineData(99, true)]
    public void Confirmation_uses_valid_count_boundaries(int count, bool expected)
    {
        Assert.Equal(expected, new DiscCountInputState(count, 1, 99).RequiresStartConfirmation);
    }

    [Theory]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("100")]
    [InlineData("abc")]
    public void Invalid_text_cannot_be_confirmed(string text)
    {
        var state = new DiscCountInputState(1, 1, 99) { Text = text };
        Assert.False(state.TryGetValue(out _));
        Assert.False(state.RequiresStartConfirmation);
    }

    [Fact]
    public void Stepping_clamps_and_notifications_publish_only_changed_valid_counts()
    {
        var state = new DiscCountInputState(2, 1, 3);
        var values = new List<int>();
        state.ValueChanged += values.Add;
        state.Step(1);
        state.Step(1);
        state.Step(-1);
        state.Text = "";
        state.Step(1);
        Assert.Equal(new[] { 3, 2, 2 }, values);
        Assert.Equal("2", state.Text);
        Assert.False(state.AcceptsInput("4"));
        Assert.False(state.AcceptsInput("22a"));
        Assert.True(state.AcceptsInput(""));
    }
}
