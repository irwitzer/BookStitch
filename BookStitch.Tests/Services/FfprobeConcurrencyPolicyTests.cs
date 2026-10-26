using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class FfprobeConcurrencyPolicyTests
{
    private readonly FfprobeConcurrencyPolicy _policy = new();

    [Theory]
    [InlineData(1, 40, 1)]
    [InlineData(2, 40, 2)]
    [InlineData(10, 1, 3)]
    [InlineData(10, 2, 3)]
    [InlineData(10, 3, 3)]
    [InlineData(10, 7, 7)]
    [InlineData(10, 8, 8)]
    [InlineData(10, 10, 10)]
    [InlineData(10, 12, 10)]
    [InlineData(1300, 40, 10)]
    public void GetEffectiveConcurrency_ClampsRequestedConcurrency(
        int jobCount,
        int requestedConcurrency,
        int expected)
    {
        Assert.Equal(expected, _policy.GetEffectiveConcurrency(jobCount, requestedConcurrency));
    }

    [Fact]
    public void GetRequestedConcurrency_AutoUsesProcessorCount()
    {
        Assert.Equal(Environment.ProcessorCount, _policy.GetRequestedConcurrency("Auto"));
    }
}
