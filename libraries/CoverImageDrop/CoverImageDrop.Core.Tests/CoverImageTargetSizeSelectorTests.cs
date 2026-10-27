using CoverImageDrop.Core.Normalization;
using CoverImageDrop.Core.Policy;

namespace CoverImageDrop.Core.Tests;

[TestClass]
public sealed class CoverImageTargetSizeSelectorTests
{
    [TestMethod]
    public void SelectTargetSize_Uses1000Below1500()
    {
        Assert.AreEqual(
            1000,
            CoverImageTargetSizeSelector.SelectTargetSize(1));
        Assert.AreEqual(
            1000,
            CoverImageTargetSizeSelector.SelectTargetSize(1499));
    }

    [TestMethod]
    public void SelectTargetSize_Uses2000From1500Through2249()
    {
        Assert.AreEqual(
            2000,
            CoverImageTargetSizeSelector.SelectTargetSize(1500));
        Assert.AreEqual(
            2000,
            CoverImageTargetSizeSelector.SelectTargetSize(2000));
        Assert.AreEqual(
            2000,
            CoverImageTargetSizeSelector.SelectTargetSize(2249));
    }

    [TestMethod]
    public void SelectTargetSize_Uses3000AtAndAbove2250()
    {
        Assert.AreEqual(
            3000,
            CoverImageTargetSizeSelector.SelectTargetSize(2250));
        Assert.AreEqual(
            3000,
            CoverImageTargetSizeSelector.SelectTargetSize(2400));
        Assert.AreEqual(
            3000,
            CoverImageTargetSizeSelector.SelectTargetSize(4000));
    }

    [TestMethod]
    public void SelectTargetSize_UsesConfiguredThreeTierPolicy()
    {
        var policy = StandardCoverPolicy.Default with
        {
            SmallOutputSizePixels = 800,
            LargeOutputSizePixels = 1600,
            LargeOutputMinUsableSquarePixels = 1200,
            PreferredOutputSizePixels = 2400,
            PreferredOutputMinUsableSquarePixels = 1800
        };

        Assert.AreEqual(
            800,
            CoverImageTargetSizeSelector.SelectTargetSize(1199, policy));
        Assert.AreEqual(
            1600,
            CoverImageTargetSizeSelector.SelectTargetSize(1200, policy));
        Assert.AreEqual(
            1600,
            CoverImageTargetSizeSelector.SelectTargetSize(1799, policy));
        Assert.AreEqual(
            2400,
            CoverImageTargetSizeSelector.SelectTargetSize(1800, policy));
    }

    [TestMethod]
    public void SelectTargetSize_ReturnsZeroForInvalidInput()
    {
        Assert.AreEqual(
            0,
            CoverImageTargetSizeSelector.SelectTargetSize(0));
        Assert.AreEqual(
            0,
            CoverImageTargetSizeSelector.SelectTargetSize(-1));
    }
}
