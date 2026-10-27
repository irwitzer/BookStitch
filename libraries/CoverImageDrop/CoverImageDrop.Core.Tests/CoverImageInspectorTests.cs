using System.IO;
using System.Windows.Media;
using CoverImageDrop.Core.Inspection;
using CoverImageDrop.Core.Issues;

namespace CoverImageDrop.Core.Tests;

[TestClass]
public sealed class CoverImageInspectorTests
{
    private readonly CoverImageInspector _inspector = new();

    [TestMethod]
    public void InspectBytes_ReportsOnlySourceFactsWithoutStandardCoverOutputDecision()
    {
        var result = _inspector.InspectBytes(TestImageFactory.CreateSolidPng(1499, 1600, Colors.Red));

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(1499, result.PixelWidth);
        Assert.AreEqual(1600, result.PixelHeight);
        Assert.AreEqual(1499, result.OrientedPixelWidth);
        Assert.AreEqual(1600, result.OrientedPixelHeight);
        Assert.IsFalse(typeof(CoverImageInspectionResult).GetProperties().Any(property => property.Name == "TargetSizePixels"));
        Assert.IsFalse(typeof(CoverImageInspectionResult).GetProperties().Any(property => property.Name == "RequiresUpscaling"));
        Assert.IsFalse(typeof(CoverImageInspectionResult).GetProperties().Any(property => property.Name == "UsableSquarePixels"));
    }

    [TestMethod]
    public async Task InspectBytesAsync_ReturnsSameSourceFactsAsSynchronousInspection()
    {
        var bytes = TestImageFactory.CreateSolidPng(
            320,
            240,
            Colors.CornflowerBlue);

        var synchronous = _inspector.InspectBytes(
            bytes,
            "async-test.png");
        var asynchronous = await _inspector.InspectBytesAsync(
            bytes,
            "async-test.png");

        Assert.AreEqual(
            synchronous.SignatureFormat,
            asynchronous.SignatureFormat);
        Assert.AreEqual(
            synchronous.PixelWidth,
            asynchronous.PixelWidth);
        Assert.AreEqual(
            synchronous.PixelHeight,
            asynchronous.PixelHeight);
        Assert.AreEqual(
            synchronous.OrientedPixelWidth,
            asynchronous.OrientedPixelWidth);
        Assert.AreEqual(
            synchronous.OrientedPixelHeight,
            asynchronous.OrientedPixelHeight);
        Assert.AreEqual(
            synchronous.FrameCount,
            asynchronous.FrameCount);
    }

    [TestMethod]
    public async Task InspectBytesAsync_HonorsPreCanceledToken()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
            _inspector.InspectBytesAsync(
                TestImageFactory.CreateSolidPng(
                    32,
                    32,
                    Colors.Red),
                cancellationToken: cancellation.Token));
    }

    [TestMethod]
    public void InspectBytes_UsesExifOrientationForOrientedDimensions()
    {
        var result = _inspector.InspectBytes(TestImageFactory.CreateJpegWithExifOrientation(120, 80, 6));

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(120, result.PixelWidth);
        Assert.AreEqual(80, result.PixelHeight);
        Assert.AreEqual(80, result.OrientedPixelWidth);
        Assert.AreEqual(120, result.OrientedPixelHeight);
    }

    [TestMethod]
    public void InspectBytes_ReportsPotentialAlphaAsSourceFactWithoutJpegWarning()
    {
        var result = _inspector.InspectBytes(TestImageFactory.CreateTransparentPng(100, 100));

        Assert.IsTrue(result.IsSuccess);
        Assert.IsTrue(result.MayContainTransparency);
        Assert.IsFalse(result.Issues.Any(issue => issue.Code == CoverImageIssueCode.TransparencyFlattened));
    }

    [TestMethod]
    public void InspectBytes_ReportsFrameCountWithoutApplyingFrameOnePolicy()
    {
        var result = _inspector.InspectBytes(TestImageFactory.CreateTwoFrameTiff(Colors.Red, Colors.Blue));

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(2, result.FrameCount);
        Assert.IsFalse(result.Issues.Any(issue => issue.Code == CoverImageIssueCode.MultipleFramesFirstFrameUsed));
    }

    [TestMethod]
    public void InspectBytes_ReportsEmbeddedColorProfileAsSourceFact()
    {
        var result = _inspector.InspectBytes(TestImageFactory.CreateJpegWithSrgbColorContext(120, 120));

        Assert.IsTrue(result.IsSuccess);
        Assert.IsTrue(result.HasEmbeddedColorProfile);
        Assert.IsGreaterThan(0, result.ColorProfileCount);
    }

    [TestMethod]
    public void InspectBytes_InvalidPayloadReturnsStructuredIssue()
    {
        var result = _inspector.InspectBytes([1, 2, 3]);

        Assert.IsFalse(result.IsSuccess);
        Assert.IsTrue(result.Issues.Any(issue =>
            issue.Severity == CoverImageIssueSeverity.Error &&
            issue.Code == CoverImageIssueCode.UnsupportedImageFormat));
    }

    [TestMethod]
    public void InspectBytes_UsesActualSignatureInsteadOfMisleadingSourceExtension()
    {
        var result = _inspector.InspectBytes(
            TestImageFactory.CreateSolidPng(64, 64, Colors.CornflowerBlue),
            "misleading.jpg");

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(DetectedImageFormat.Png, result.SignatureFormat);
    }

    [TestMethod]
    public void InspectBytes_DoesNotTrustImageExtensionWithoutImagePayload()
    {
        var result = _inspector.InspectBytes([1, 2, 3, 4], "not-an-image.jpg");

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(DetectedImageFormat.Unknown, result.SignatureFormat);
        Assert.IsTrue(result.Issues.Any(issue =>
            issue.Code == CoverImageIssueCode.UnsupportedImageFormat));
    }

    [TestMethod]
    public void SignatureDetector_RecognizesAllExplicitV1InputSignatures()
    {
        var cases = new (byte[] Bytes, DetectedImageFormat Expected)[]
        {
            ([0xFF, 0xD8, 0xFF, 0x00], DetectedImageFormat.Jpeg),
            ([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A], DetectedImageFormat.Png),
            ("GIF87a"u8.ToArray(), DetectedImageFormat.Gif),
            ("GIF89a"u8.ToArray(), DetectedImageFormat.Gif),
            ([0x42, 0x4D, 0x00, 0x00], DetectedImageFormat.Bmp),
            ([0x49, 0x49, 0x2A, 0x00], DetectedImageFormat.Tiff),
            ([0x4D, 0x4D, 0x00, 0x2A], DetectedImageFormat.Tiff),
            ("RIFF0000WEBP"u8.ToArray(), DetectedImageFormat.WebP),
            ([0x00, 0x00, 0x01, 0x00], DetectedImageFormat.Ico)
        };

        foreach (var testCase in cases)
        {
            Assert.AreEqual(
                testCase.Expected,
                ImageFormatSignatureDetector.Detect(testCase.Bytes),
                $"Signature should resolve to {testCase.Expected}.");
        }

        Assert.AreEqual(
            DetectedImageFormat.Unknown,
            ImageFormatSignatureDetector.Detect([1, 2, 3, 4, 5, 6, 7, 8]));
    }

    [TestMethod]
    public void WebPDecodeFailure_IsStructuredAndProvidesStableHostAction()
    {
        var issue = CoverImageInspector.CreateDecodeFailureIssue(
            DetectedImageFormat.WebP,
            new NotSupportedException("Synthetic missing decoder."));

        Assert.AreEqual(CoverImageIssueCode.WebPDecoderUnavailable, issue.Code);
        Assert.AreEqual(CoverImageIssueSeverity.Error, issue.Severity);
        Assert.IsNotNull(issue.Data);
        Assert.AreEqual("WebP", issue.Data["signatureFormat"]);
        Assert.AreEqual("InstallWebPSupport", issue.SuggestedAction);
        Assert.AreEqual("Synthetic missing decoder.", issue.TechnicalCause);
    }

    [TestMethod]
    public void CorruptWebPDecodeFailure_DoesNotClaimDecoderIsMissing()
    {
        var issue = CoverImageInspector.CreateDecodeFailureIssue(
            DetectedImageFormat.WebP,
            new FileFormatException("Synthetic corrupt WebP payload."));

        Assert.AreEqual(CoverImageIssueCode.ImageDecodeFailed, issue.Code);
        Assert.AreEqual(CoverImageIssueSeverity.Error, issue.Severity);
        Assert.IsNotNull(issue.Data);
        Assert.AreEqual("WebP", issue.Data["signatureFormat"]);
        Assert.IsNull(issue.SuggestedAction);
        Assert.AreEqual("Synthetic corrupt WebP payload.", issue.TechnicalCause);
    }

    [TestMethod]
    public void NonWebPDecodeFailure_DoesNotAdvertiseWebPSupportAction()
    {
        var issue = CoverImageInspector.CreateDecodeFailureIssue(
            DetectedImageFormat.Jpeg,
            new InvalidOperationException("Synthetic decode failure."));

        Assert.AreEqual(CoverImageIssueCode.ImageDecodeFailed, issue.Code);
        Assert.IsNotNull(issue.Data);
        Assert.AreEqual("Jpeg", issue.Data["signatureFormat"]);
        Assert.IsNull(issue.SuggestedAction);
    }
}
