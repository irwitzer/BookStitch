using CoverImageDrop.Core.Normalization;

namespace CoverImageDrop.Pipeline.Tests;

[TestClass]
public sealed class CoverImagePreviewCropMapperTests
{
    [TestMethod]
    public void CreateCentered_MapsWideImageToCenteredCrop()
    {
        var state = CoverImagePreviewCropMapper.CreateCentered(300, 100, 100);

        Assert.AreEqual(1d, state.Scale);
        Assert.AreEqual(300d, state.DisplayWidth);
        Assert.AreEqual(100d, state.DisplayHeight);
        Assert.AreEqual(-100d, state.OffsetX);
        Assert.AreEqual(0d, state.OffsetY);
        Assert.AreEqual(
            new CoverImageCropRect(100, 0, 100, 100),
            state.CropRect);
    }

    [TestMethod]
    public void CreateCentered_MapsTallImageToCenteredCrop()
    {
        var state = CoverImagePreviewCropMapper.CreateCentered(100, 300, 100);

        Assert.AreEqual(1d, state.Scale);
        Assert.AreEqual(100d, state.DisplayWidth);
        Assert.AreEqual(300d, state.DisplayHeight);
        Assert.AreEqual(0d, state.OffsetX);
        Assert.AreEqual(-100d, state.OffsetY);
        Assert.AreEqual(
            new CoverImageCropRect(0, 100, 100, 100),
            state.CropRect);
    }

    [TestMethod]
    public void CreateCentered_MapsSquareImageToFullCrop()
    {
        var state = CoverImagePreviewCropMapper.CreateCentered(100, 100, 100);

        Assert.AreEqual(1d, state.Scale);
        Assert.AreEqual(0d, state.OffsetX);
        Assert.AreEqual(0d, state.OffsetY);
        Assert.AreEqual(
            new CoverImageCropRect(0, 0, 100, 100),
            state.CropRect);
    }

    [TestMethod]
    public void MoveBy_ClampsOffsetAtImageBounds()
    {
        var state = CoverImagePreviewCropMapper.CreateCentered(300, 100, 100);

        var left = CoverImagePreviewCropMapper.MoveBy(state, -500, 0);
        var right = CoverImagePreviewCropMapper.MoveBy(state, 500, 0);

        Assert.AreEqual(-200d, left.OffsetX);
        Assert.AreEqual(
            new CoverImageCropRect(200, 0, 100, 100),
            left.CropRect);
        Assert.AreEqual(0d, right.OffsetX);
        Assert.AreEqual(
            new CoverImageCropRect(0, 0, 100, 100),
            right.CropRect);
    }

    [TestMethod]
    public void MoveBy_WideImageKeepsFullHeightAndMovesOnlyHorizontally()
    {
        var state = CoverImagePreviewCropMapper.CreateCentered(300, 100, 100);

        var moved = CoverImagePreviewCropMapper.MoveBy(state, -50, 80);

        Assert.AreEqual(-150d, moved.OffsetX);
        Assert.AreEqual(0d, moved.OffsetY);
        Assert.AreEqual(100d, moved.DisplayHeight);
        Assert.AreEqual(
            new CoverImageCropRect(150, 0, 100, 100),
            moved.CropRect);
    }

    [TestMethod]
    public void MoveBy_TallImageKeepsFullWidthAndMovesOnlyVertically()
    {
        var state = CoverImagePreviewCropMapper.CreateCentered(100, 300, 100);

        var moved = CoverImagePreviewCropMapper.MoveBy(state, 80, -50);

        Assert.AreEqual(0d, moved.OffsetX);
        Assert.AreEqual(-150d, moved.OffsetY);
        Assert.AreEqual(100d, moved.DisplayWidth);
        Assert.AreEqual(
            new CoverImageCropRect(0, 150, 100, 100),
            moved.CropRect);
    }

    [TestMethod]
    public void CreateFromOffset_ComputesScaledCropRect()
    {
        var state = CoverImagePreviewCropMapper.CreateFromOffset(
            400,
            200,
            100,
            -50,
            0);

        Assert.AreEqual(0.5d, state.Scale);
        Assert.AreEqual(200d, state.DisplayWidth);
        Assert.AreEqual(100d, state.DisplayHeight);
        Assert.AreEqual(
            new CoverImageCropRect(100, 0, 200, 200),
            state.CropRect);
    }

    [TestMethod]
    public void CreateFromCrop_PreservesExistingCropAcrossViewportResize()
    {
        var crop = new CoverImageCropRect(350, 0, 1600, 1600);

        var small = CoverImagePreviewCropMapper.CreateFromCrop(
            2400,
            1600,
            crop,
            320);
        var large = CoverImagePreviewCropMapper.CreateFromCrop(
            2400,
            1600,
            crop,
            640);

        Assert.AreEqual(crop, small.CropRect);
        Assert.AreEqual(crop, large.CropRect);
        Assert.AreEqual(-70d, small.OffsetX);
        Assert.AreEqual(-140d, large.OffsetX);
    }

    [TestMethod]
    public void CreateFromCrop_RejectsFreeSizedCrop()
    {
        Assert.ThrowsExactly<ArgumentException>(() =>
            CoverImagePreviewCropMapper.CreateFromCrop(
                2400,
                1600,
                new CoverImageCropRect(0, 0, 1200, 1200),
                400));
    }

    [TestMethod]
    public void SquareImage_MoveByHasNoFakeMovement()
    {
        var state = CoverImagePreviewCropMapper.CreateCentered(
            1000,
            1000,
            400);

        var moved = CoverImagePreviewCropMapper.MoveBy(
            state,
            120,
            -90);

        Assert.AreEqual(state, moved);
    }

    [TestMethod]
    public void CreateCentered_RejectsInvalidViewport()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            CoverImagePreviewCropMapper.CreateCentered(100, 100, 0));
    }
}
