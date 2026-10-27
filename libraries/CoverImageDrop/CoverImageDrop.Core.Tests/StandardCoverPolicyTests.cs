using CoverImageDrop.Core.Policy;

namespace CoverImageDrop.Core.Tests;

[TestClass]
public sealed class StandardCoverPolicyTests
{
    [TestMethod]
    public void Default_UsesV1StandardCoverLimitsAndOutputSizes()
    {
        var policy = StandardCoverPolicy.Default;

        Assert.AreEqual(500, policy.MinUsableSquarePixels);
        Assert.AreEqual(80L * 1024 * 1024, policy.MaxInputBytes);
        Assert.AreEqual(80_000_000L, policy.MaxTotalPixels);
        Assert.AreEqual(1000, policy.SmallOutputSizePixels);
        Assert.AreEqual(2000, policy.LargeOutputSizePixels);
        Assert.AreEqual(1500, policy.LargeOutputMinUsableSquarePixels);
    }

    [TestMethod]
    public void Policy_DoesNotCarryLegacyPerDimensionOrTransparencyRules()
    {
        var propertyNames = typeof(StandardCoverPolicy)
            .GetProperties()
            .Select(property => property.Name)
            .ToArray();

        Assert.IsFalse(propertyNames.Contains("MaxPixelWidth"));
        Assert.IsFalse(propertyNames.Contains("MaxPixelHeight"));
        Assert.IsFalse(propertyNames.Contains("TransparencyBackground"));
        Assert.IsFalse(propertyNames.Contains("JpegQuality"));
    }
}
