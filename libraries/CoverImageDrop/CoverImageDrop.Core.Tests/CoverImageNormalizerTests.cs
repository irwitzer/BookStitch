using System.Windows.Media;
using CoverImageDrop.Core.Normalization;
using CoverImageDrop.Core.Output;

namespace CoverImageDrop.Core.Tests;

[TestClass]
public sealed class CoverImageNormalizerTests
{
    private readonly CoverImageNormalizer _normalizer = new();

    [TestMethod]
    public void NormalizeBytes_ProducesOneThousandSquareJpegForSmallUsableSquare()
    {
        var result = _normalizer.NormalizeBytes(TestImageFactory.CreateSolidPng(1200, 1499, Colors.Red));
        var dimensions = TestImageFactory.ReadDimensions(result.Bytes);

        Assert.AreEqual("image/jpeg", result.MimeType);
        Assert.AreEqual(CoverOutputFormat.Jpeg, result.Format);
        Assert.AreEqual(".jpg", result.FileExtension);
        Assert.AreEqual(result.Bytes.LongLength, result.ByteLength);
        Assert.AreEqual(1000, result.Width);
        Assert.AreEqual(1000, result.Height);
        Assert.AreEqual((1000, 1000), dimensions);
    }

    [TestMethod]
    public void NormalizeBytes_ProducesTwoThousandSquareJpegForLargeUsableSquare()
    {
        var result = _normalizer.NormalizeBytes(TestImageFactory.CreateSolidPng(1500, 1600, Colors.Red));
        var dimensions = TestImageFactory.ReadDimensions(result.Bytes);

        Assert.AreEqual("image/jpeg", result.MimeType);
        Assert.AreEqual(2000, result.Width);
        Assert.AreEqual(2000, result.Height);
        Assert.AreEqual((2000, 2000), dimensions);
    }

    [TestMethod]
    public void NormalizeBytes_UsesProvidedCropRect()
    {
        var source = TestImageFactory.CreateTwoColorPng(300, 100, Colors.Red, Colors.Blue);

        var leftCrop = _normalizer.NormalizeBytes(source, new CoverImageCropRect(0, 0, 100, 100));
        var rightCrop = _normalizer.NormalizeBytes(source, new CoverImageCropRect(200, 0, 100, 100));

        var leftPixel = TestImageFactory.ReadCenterPixel(leftCrop.Bytes);
        var rightPixel = TestImageFactory.ReadCenterPixel(rightCrop.Bytes);

        Assert.IsGreaterThan((byte)180, leftPixel.R, $"Expected red crop, got {leftPixel}.");
        Assert.IsGreaterThan((byte)180, rightPixel.B, $"Expected blue crop, got {rightPixel}.");
    }


    [TestMethod]
    public void NormalizeBytes_RejectsNonSquareStandardCoverCrop()
    {
        var source = TestImageFactory.CreateSolidPng(600, 500, Colors.Red);

        Assert.ThrowsExactly<ArgumentException>(() =>
            _normalizer.NormalizeBytes(source, new CoverImageCropRect(0, 0, 500, 400)));
    }

    [TestMethod]
    public void NormalizeBytes_FlattensTransparencyToConfiguredBackgroundBeforeJpegEncoding()
    {
        var result = _normalizer.NormalizeBytes(TestImageFactory.CreateTransparentPng(120, 120));
        var pixel = TestImageFactory.ReadCenterPixel(result.Bytes);

        Assert.IsGreaterThan((byte)240, pixel.R, $"Expected white background, got {pixel}.");
        Assert.IsGreaterThan((byte)240, pixel.G, $"Expected white background, got {pixel}.");
        Assert.IsGreaterThan((byte)240, pixel.B, $"Expected white background, got {pixel}.");
    }

    [TestMethod]
    public void NormalizeBytes_UsesOnlyFirstFrame()
    {
        var source = TestImageFactory.CreateTwoFrameTiff(Colors.Red, Colors.Blue);
        var result = _normalizer.NormalizeBytes(source);
        var pixel = TestImageFactory.ReadCenterPixel(result.Bytes);

        Assert.IsGreaterThan((byte)180, pixel.R, $"Expected first TIFF frame, got {pixel}.");
        Assert.IsLessThan((byte)80, pixel.B, $"Expected first TIFF frame, got {pixel}.");
    }
    [TestMethod]
    public void NormalizeBytes_ExplicitPngTargetPreservesTransparencyAndReportsMetadata()
    {
        var result = _normalizer.NormalizeBytes(
            TestImageFactory.CreateTransparentPng(600, 500),
            new CoverImageCropRect(50, 0, 500, 500),
            new CoverImageNormalizationOptions
            {
                TargetSizePixels = 1000,
                Format = CoverOutputFormat.Png
            });
        var pixel = TestImageFactory.ReadCenterPixel(result.Bytes);

        Assert.AreEqual(CoverOutputFormat.Png, result.Format);
        Assert.AreEqual("image/png", result.MimeType);
        Assert.AreEqual(".png", result.FileExtension);
        Assert.AreEqual(1000, result.ResolvedTargetSizePixels);
        Assert.IsTrue(result.WasUpscaled);
        Assert.IsNull(result.JpegQuality);
        Assert.IsNull(result.JpegMatte);
        Assert.AreEqual((byte)0, pixel.A);
    }

    [TestMethod]
    public void NormalizeBytes_BlackJpegMatteIsAppliedAndReported()
    {
        var result = _normalizer.NormalizeBytes(
            TestImageFactory.CreateTransparentPng(500, 500),
            cropRect: null,
            options: new CoverImageNormalizationOptions
            {
                TargetSizePixels = 1000,
                Format = CoverOutputFormat.Jpeg,
                JpegMatte = CoverImageMatte.Black
            });
        var pixel = TestImageFactory.ReadCenterPixel(result.Bytes);

        Assert.AreEqual(CoverImageMatte.Black, result.JpegMatte);
        Assert.AreEqual(94, result.JpegQuality);
        Assert.IsLessThan((byte)20, pixel.R);
        Assert.IsLessThan((byte)20, pixel.G);
        Assert.IsLessThan((byte)20, pixel.B);
    }

    [TestMethod]
    public void NormalizeBytes_OpaqueAlphaCapableInputDoesNotReportTransparencyFlattening()
    {
        var result = _normalizer.NormalizeBytes(
            TestImageFactory.CreateSolidPng(500, 500, Colors.DarkSlateGray),
            cropRect: null,
            options: new CoverImageNormalizationOptions
            {
                TargetSizePixels = 1000,
                Format = CoverOutputFormat.Jpeg
            });

        Assert.IsNull(result.JpegMatte);
        Assert.IsFalse(result.Issues.Any(issue => issue.Code == CoverImageDrop.Core.Issues.CoverImageIssueCode.TransparencyFlattened));
    }

    [TestMethod]
    public void NormalizeBytes_AutoMatteUsesWhiteForDarkTransparentEdge()
    {
        var result = _normalizer.NormalizeBytes(
            TestImageFactory.CreateHalfTransparentPng(500, 500, Colors.Black),
            cropRect: null,
            options: new CoverImageNormalizationOptions
            {
                TargetSizePixels = 1000,
                Format = CoverOutputFormat.Jpeg
            });

        Assert.AreEqual(CoverImageMatte.White, result.JpegMatte);
        Assert.IsTrue(result.Issues.Any(issue => issue.Code == CoverImageDrop.Core.Issues.CoverImageIssueCode.TransparencyFlattened));
    }

    [TestMethod]
    public void NormalizeBytes_AutoMatteUsesBlackForLightTransparentEdge()
    {
        var result = _normalizer.NormalizeBytes(
            TestImageFactory.CreateHalfTransparentPng(500, 500, Colors.White),
            cropRect: null,
            options: new CoverImageNormalizationOptions
            {
                TargetSizePixels = 1000,
                Format = CoverOutputFormat.Jpeg
            });

        Assert.AreEqual(CoverImageMatte.Black, result.JpegMatte);
    }

    [TestMethod]
    public void NormalizeBytes_AutoMatteFallsBackToWhiteForAmbiguousTransparentEdge()
    {
        var result = _normalizer.NormalizeBytes(
            TestImageFactory.CreateHalfTransparentPng(500, 500, Color.FromRgb(128, 128, 128)),
            cropRect: null,
            options: new CoverImageNormalizationOptions
            {
                TargetSizePixels = 1000,
                Format = CoverOutputFormat.Jpeg
            });

        Assert.AreEqual(CoverImageMatte.White, result.JpegMatte);
    }

    [TestMethod]
    public void NormalizeBytes_PngTransparencyDoesNotReportFlatteningOrMatte()
    {
        var result = _normalizer.NormalizeBytes(
            TestImageFactory.CreateHalfTransparentPng(500, 500, Colors.White),
            cropRect: null,
            options: new CoverImageNormalizationOptions
            {
                TargetSizePixels = 1000,
                Format = CoverOutputFormat.Png
            });

        Assert.IsNull(result.JpegMatte);
        Assert.IsNull(result.JpegQuality);
        Assert.IsFalse(result.Issues.Any(issue => issue.Code == CoverImageDrop.Core.Issues.CoverImageIssueCode.TransparencyFlattened));
        Assert.AreEqual((byte)0, TestImageFactory.ReadCenterPixel(
            _normalizer.NormalizeBytes(
                TestImageFactory.CreateTransparentPng(500, 500),
                cropRect: null,
                options: new CoverImageNormalizationOptions
                {
                    TargetSizePixels = 1000,
                    Format = CoverOutputFormat.Png
                }).Bytes).A);
    }

    [TestMethod]
    public void NormalizeBytes_MultipleFramesReportsFirstFrameWarning()
    {
        var result = _normalizer.NormalizeBytes(TestImageFactory.CreateTwoFrameTiff(Colors.Red, Colors.Blue));

        Assert.IsTrue(result.Issues.Any(issue =>
            issue.Code == CoverImageDrop.Core.Issues.CoverImageIssueCode.MultipleFramesFirstFrameUsed &&
            issue.Severity == CoverImageDrop.Core.Issues.CoverImageIssueSeverity.Warning));
    }

    [TestMethod]
    public void NormalizeBytes_EmbeddedSrgbProfileUsesColorManagedPathWithoutWarning()
    {
        var result = _normalizer.NormalizeBytes(TestImageFactory.CreateJpegWithSrgbColorContext(500, 500));

        Assert.IsFalse(result.Issues.Any(issue =>
            issue.Code == CoverImageDrop.Core.Issues.CoverImageIssueCode.ColorProfileConversionFailed));
        Assert.AreEqual((1000, 1000), TestImageFactory.ReadDimensions(result.Bytes));
    }

    [TestMethod]
    public void NormalizeBytes_StripsSourceExifCameraAndCommentMetadata()
    {
        var source = TestImageFactory.CreateJpegWithSensitiveMetadata(600, 500);
        var sourceMetadata = TestImageFactory.ReadMetadata(source);

        Assert.IsNotNull(sourceMetadata);
        Assert.IsTrue(TestImageFactory.MetadataContainsQuery(source, "/app1/ifd/{ushort=270}"));
        Assert.IsTrue(TestImageFactory.MetadataContainsQuery(source, "/app1/ifd/{ushort=271}"));
        Assert.IsTrue(TestImageFactory.MetadataContainsQuery(source, "/app1/ifd/{ushort=272}"));
        Assert.IsTrue(TestImageFactory.MetadataContainsQuery(source, "/app1/ifd/{ushort=274}"));

        var result = _normalizer.NormalizeBytes(source);
        var outputMetadata = TestImageFactory.ReadMetadata(result.Bytes);

        Assert.IsFalse(TestImageFactory.MetadataContainsQuery(result.Bytes, "/app1/ifd/{ushort=270}"));
        Assert.IsFalse(TestImageFactory.MetadataContainsQuery(result.Bytes, "/app1/ifd/{ushort=271}"));
        Assert.IsFalse(TestImageFactory.MetadataContainsQuery(result.Bytes, "/app1/ifd/{ushort=272}"));
        Assert.IsFalse(TestImageFactory.MetadataContainsQuery(result.Bytes, "/app1/ifd/{ushort=274}"));
        Assert.IsTrue(outputMetadata is null || string.IsNullOrWhiteSpace(outputMetadata.Comment));
        Assert.IsTrue(outputMetadata is null || string.IsNullOrWhiteSpace(outputMetadata.CameraManufacturer));
        Assert.IsTrue(outputMetadata is null || string.IsNullOrWhiteSpace(outputMetadata.CameraModel));
    }

    [TestMethod]
    public void LowLevelNormalizationTypes_AreNotExportedFromCoreAssembly()
    {
        var exportedTypeNames = typeof(CoverImageOutputResult).Assembly
            .GetExportedTypes()
            .Select(type => type.FullName)
            .ToHashSet(StringComparer.Ordinal);

        Assert.IsFalse(exportedTypeNames.Contains(
            "CoverImageDrop.Core.Normalization.CoverImageNormalizer"));
        Assert.IsFalse(exportedTypeNames.Contains(
            "CoverImageDrop.Core.Normalization.CoverImageNormalizationOptions"));
    }
}
