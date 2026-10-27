using CoverImageDrop.Core.Normalization;

namespace CoverImageDrop.Core.Tests;

[TestClass]
public sealed class CoverImageCropRectTests
{
    [TestMethod]
    public void CreateCenteredSquare_ReturnsCenteredSquareForWideImage()
    {
        var crop = CoverImageCropRect.CreateCenteredSquare(1200, 800);

        Assert.AreEqual(new CoverImageCropRect(200, 0, 800, 800), crop);
    }

    [TestMethod]
    public void CreateCenteredSquare_ReturnsCenteredSquareForTallImage()
    {
        var crop = CoverImageCropRect.CreateCenteredSquare(800, 1200);

        Assert.AreEqual(new CoverImageCropRect(0, 200, 800, 800), crop);
    }

    [TestMethod]
    public void CreateCenteredSquare_ReturnsFullSquareForSquareImage()
    {
        var crop = CoverImageCropRect.CreateCenteredSquare(1000, 1000);

        Assert.AreEqual(new CoverImageCropRect(0, 0, 1000, 1000), crop);
    }

    [TestMethod]
    public void ClampTo_KeepsRectangularCropInsideImageBounds()
    {
        var crop = new CoverImageCropRect(950, -20, 400, 250).ClampTo(1000, 800);

        Assert.AreEqual(new CoverImageCropRect(600, 0, 400, 250), crop);
    }

    [TestMethod]
    public void ClampTo_ReducesOversizedDimensionsIndependently()
    {
        var crop = new CoverImageCropRect(100, 100, 1200, 900).ClampTo(1000, 800);

        Assert.AreEqual(new CoverImageCropRect(0, 0, 1000, 800), crop);
    }

    [TestMethod]
    public void IsSquare_DistinguishesSquareAndRectangularGeometry()
    {
        Assert.IsTrue(new CoverImageCropRect(0, 0, 500, 500).IsSquare);
        Assert.IsFalse(new CoverImageCropRect(0, 0, 500, 400).IsSquare);
    }
}
