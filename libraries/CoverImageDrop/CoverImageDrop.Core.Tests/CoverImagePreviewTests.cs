using System.Windows.Media;
using CoverImageDrop.Core.Normalization;

namespace CoverImageDrop.Core.Tests;

[TestClass]
public sealed class CoverImagePreviewTests
{
    private readonly CoverImageNormalizer _normalizer = new();

    [TestMethod]
    public void CreatePreview_AppliesExifOrientationBeforePreviewEncoding()
    {
        var source = TestImageFactory.CreateJpegWithExifOrientation(120, 80, 6);

        var preview = _normalizer.CreatePreview(source);

        Assert.AreEqual("image/png", preview.MimeType);
        Assert.AreEqual(".png", preview.FileExtension);
        Assert.AreEqual(80, preview.Width);
        Assert.AreEqual(120, preview.Height);
        Assert.AreEqual((80, 120), TestImageFactory.ReadDimensions(preview.Bytes));
    }

    [TestMethod]
    public void CreatePreview_DownscalesLongEdgeWithoutChangingAspectRatioOrUpscaling()
    {
        var large = TestImageFactory.CreateSolidPng(2400, 1200, Colors.DarkSlateBlue);
        var small = TestImageFactory.CreateSolidPng(320, 160, Colors.DarkSlateBlue);

        var downscaled = _normalizer.CreatePreview(large, 600);
        var unchanged = _normalizer.CreatePreview(small, 600);

        Assert.AreEqual(600, downscaled.Width);
        Assert.AreEqual(300, downscaled.Height);
        Assert.AreEqual(320, unchanged.Width);
        Assert.AreEqual(160, unchanged.Height);
    }

    [TestMethod]
    public void CreatePreview_PreservesTransparencyBecausePreviewIsPng()
    {
        var preview = _normalizer.CreatePreview(TestImageFactory.CreateTransparentPng(300, 200));
        var pixel = TestImageFactory.ReadCenterPixel(preview.Bytes);

        Assert.AreEqual((byte)0, pixel.A);
    }

    [TestMethod]
    public void CreatePreview_MultipleFramesReportsSameSourceProcessingWarningAsFinalOutput()
    {
        var preview = _normalizer.CreatePreview(TestImageFactory.CreateTwoFrameTiff(Colors.Red, Colors.Blue));

        Assert.IsTrue(preview.Issues.Any(issue =>
            issue.Code == CoverImageDrop.Core.Issues.CoverImageIssueCode.MultipleFramesFirstFrameUsed));
    }

    [TestMethod]
    public void CreatePreview_PreCanceledTokenIsNotSwallowed()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.ThrowsExactly<OperationCanceledException>(() =>
            _normalizer.CreatePreview(
                TestImageFactory.CreateSolidPng(300, 200, Colors.Red),
                cancellationToken: cancellation.Token));
    }
}
