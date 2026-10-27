using System.Windows.Media;
using CoverImageDrop.Acquisition;
using CoverImageDrop.Core.Normalization;

namespace CoverImageDrop.Pipeline.Tests;

[TestClass]
public sealed class CoverImagePipelinePreviewSessionTests
{
    [TestMethod]
    public async Task SessionPreview_IsOrientedBoundedPngAndDoesNotChangeCrop()
    {
        var bytes = TestImageFactory.CreateSolidPng(
            2200,
            1600,
            Colors.SteelBlue);
        var load = await new CoverImagePipelineService().LoadAsync(
            DropImageData.FromBytes(bytes, "wide.png"));

        Assert.IsTrue(load.IsSuccess);
        var session = load.Session!;
        var cropBefore = session.CropState;

        var preview = await session.CreatePreviewAsync(800);

        Assert.AreEqual("image/png", preview.MimeType);
        Assert.AreEqual(800, preview.Width);
        Assert.AreEqual(582, preview.Height);
        Assert.AreEqual(cropBefore, session.CropState);

        await session.DisposeAsync();
    }

    [TestMethod]
    public async Task SessionPreviewAsync_ProducesBoundedPreviewOnWorker()
    {
        var bytes = TestImageFactory.CreateSolidPng(
            1800,
            1200,
            Colors.DarkSlateBlue);
        var load = await new CoverImagePipelineService().LoadAsync(
            DropImageData.FromBytes(bytes, "wide.png"));

        Assert.IsTrue(load.IsSuccess);
        var session = load.Session!;

        var preview = await session.CreatePreviewAsync(900);

        Assert.AreEqual("image/png", preview.MimeType);
        Assert.AreEqual(900, preview.Width);
        Assert.AreEqual(600, preview.Height);

        await session.DisposeAsync();
    }

    [TestMethod]
    public async Task PreviewCropState_RoundTripsIntoFixedSessionCrop()
    {
        var bytes = TestImageFactory.CreateSolidPng(
            2200,
            1600,
            Colors.DarkCyan);
        var load = await new CoverImagePipelineService().LoadAsync(
            DropImageData.FromBytes(bytes, "wide.png"));

        Assert.IsTrue(load.IsSuccess);
        var session = load.Session!;
        var centered = session.CreatePreviewCropState(400);
        var moved = CoverImagePreviewCropMapper.MoveBy(
            centered,
            -100,
            0);

        session.UpdateCropFromPreview(moved);

        Assert.AreEqual(moved.CropRect, session.CropState.Rect);
        Assert.AreEqual(
            Math.Min(
                session.Inspection.OrientedPixelWidth,
                session.Inspection.OrientedPixelHeight),
            session.CropState.Rect.Width);
        Assert.AreEqual(
            session.CropState.Rect.Width,
            session.CropState.Rect.Height);

        await session.DisposeAsync();
    }

    [TestMethod]
    public async Task PreviewResize_PreservesExistingSessionCrop()
    {
        var bytes = TestImageFactory.CreateSolidPng(
            2400,
            1600,
            Colors.Teal);
        var load = await new CoverImagePipelineService().LoadAsync(
            DropImageData.FromBytes(bytes, "wide.png"));

        Assert.IsTrue(load.IsSuccess);
        var session = load.Session!;
        session.UpdateCropPosition(500, 0);
        var expected = session.CropState.Rect;

        var small = session.CreatePreviewCropState(320);
        var large = session.CreatePreviewCropState(640);

        Assert.AreEqual(expected, small.CropRect);
        Assert.AreEqual(expected, large.CropRect);
        Assert.AreEqual(-100d, small.OffsetX);
        Assert.AreEqual(-200d, large.OffsetX);

        await session.DisposeAsync();
    }

    [TestMethod]
    public async Task PreviewCropState_FromAnotherImageIsRejected()
    {
        var bytes = TestImageFactory.CreateSolidPng(
            2200,
            1600,
            Colors.DarkCyan);
        var load = await new CoverImagePipelineService().LoadAsync(
            DropImageData.FromBytes(bytes, "wide.png"));

        Assert.IsTrue(load.IsSuccess);
        var session = load.Session!;
        var foreignState = CoverImagePreviewCropMapper.CreateCentered(
            2000,
            1600,
            400);

        Assert.ThrowsExactly<ArgumentException>(() =>
            session.UpdateCropFromPreview(foreignState));

        await session.DisposeAsync();
    }

    [TestMethod]
    public async Task SessionPreviewAndFinalize_RespectCancellationAndDisposedLifetime()
    {
        var bytes = TestImageFactory.CreateSolidPng(
            1600,
            1600,
            Colors.MediumPurple);
        var load = await new CoverImagePipelineService().LoadAsync(
            DropImageData.FromBytes(bytes, "cover.png"));

        Assert.IsTrue(load.IsSuccess);
        var session = load.Session!;
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.ThrowsExactly<OperationCanceledException>(() =>
            session.CreatePreview(
                cancellationToken: cancellation.Token));
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(
            async () =>
                await session.CreatePreviewAsync(
                    cancellationToken: cancellation.Token));
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(
            async () =>
                await session.FinalizeAsync(cancellation.Token));

        await session.DisposeAsync();

        Assert.ThrowsExactly<ObjectDisposedException>(() =>
            session.CreatePreview());
        Assert.ThrowsExactly<ObjectDisposedException>(() =>
            session.CreatePreviewAsync());
        Assert.ThrowsExactly<ObjectDisposedException>(() =>
            session.CreatePreviewCropState(400));
    }
}
