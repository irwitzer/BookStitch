using CoverImageDrop.Core.Policy;

namespace CoverImageDrop.Pipeline.Tests;

[TestClass]
public sealed class CoverImagePipelinePolicyValidationTests
{
    [TestMethod]
    public void Constructor_DefaultPolicyIsAccepted()
    {
        _ = new CoverImagePipelineService();
    }

    [TestMethod]
    public void Constructor_RejectsNonPositivePolicyValues()
    {
        var cases = new (StandardCoverPolicy Policy, string ParameterName)[]
        {
            (StandardCoverPolicy.Default with { MinUsableSquarePixels = 0 }, nameof(StandardCoverPolicy.MinUsableSquarePixels)),
            (StandardCoverPolicy.Default with { MaxInputBytes = 0 }, nameof(StandardCoverPolicy.MaxInputBytes)),
            (StandardCoverPolicy.Default with { MaxTotalPixels = -1 }, nameof(StandardCoverPolicy.MaxTotalPixels)),
            (StandardCoverPolicy.Default with { SmallOutputSizePixels = 0 }, nameof(StandardCoverPolicy.SmallOutputSizePixels)),
            (StandardCoverPolicy.Default with { LargeOutputSizePixels = 0 }, nameof(StandardCoverPolicy.LargeOutputSizePixels)),
            (StandardCoverPolicy.Default with { LargeOutputMinUsableSquarePixels = 0 }, nameof(StandardCoverPolicy.LargeOutputMinUsableSquarePixels)),
            (StandardCoverPolicy.Default with { PreferredOutputSizePixels = 0 }, nameof(StandardCoverPolicy.PreferredOutputSizePixels)),
            (StandardCoverPolicy.Default with { PreferredOutputMinUsableSquarePixels = 0 }, nameof(StandardCoverPolicy.PreferredOutputMinUsableSquarePixels))
        };

        foreach (var testCase in cases)
        {
            var exception = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
                _ = new CoverImagePipelineService(new CoverImagePipelineOptions
                {
                    Policy = testCase.Policy
                }));

            Assert.AreEqual(testCase.ParameterName, exception.ParamName);
        }
    }

    [TestMethod]
    public void Constructor_RejectsContradictoryLargeOutputSize()
    {
        var policy = StandardCoverPolicy.Default with
        {
            SmallOutputSizePixels = 2000,
            LargeOutputSizePixels = 1000
        };

        var exception = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            _ = new CoverImagePipelineService(new CoverImagePipelineOptions
            {
                Policy = policy
            }));

        Assert.AreEqual(
            nameof(StandardCoverPolicy.LargeOutputSizePixels),
            exception.ParamName);
    }

    [TestMethod]
    public void Constructor_RejectsContradictoryPreferredOutputSize()
    {
        var policy = StandardCoverPolicy.Default with
        {
            LargeOutputSizePixels = 2500,
            PreferredOutputSizePixels = 2000
        };

        var exception = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            _ = new CoverImagePipelineService(new CoverImagePipelineOptions
            {
                Policy = policy
            }));

        Assert.AreEqual(
            nameof(StandardCoverPolicy.PreferredOutputSizePixels),
            exception.ParamName);
    }

    [TestMethod]
    public void Constructor_RejectsLargeThresholdBelowMinimumUsableSquare()
    {
        var policy = StandardCoverPolicy.Default with
        {
            MinUsableSquarePixels = 1000,
            LargeOutputMinUsableSquarePixels = 999
        };

        var exception = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            _ = new CoverImagePipelineService(new CoverImagePipelineOptions
            {
                Policy = policy
            }));

        Assert.AreEqual(
            nameof(StandardCoverPolicy.LargeOutputMinUsableSquarePixels),
            exception.ParamName);
    }

    [TestMethod]
    public void Constructor_RejectsPreferredThresholdBelowLargeThreshold()
    {
        var policy = StandardCoverPolicy.Default with
        {
            LargeOutputMinUsableSquarePixels = 1500,
            PreferredOutputMinUsableSquarePixels = 1499
        };

        var exception = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            _ = new CoverImagePipelineService(new CoverImagePipelineOptions
            {
                Policy = policy
            }));

        Assert.AreEqual(
            nameof(StandardCoverPolicy.PreferredOutputMinUsableSquarePixels),
            exception.ParamName);
    }
}
