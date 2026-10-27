using System.IO;
using System.Windows.Media;
using CoverImageDrop.Acquisition;
using CoverImageDrop.Core.Issues;
using CoverImageDrop.Core.Normalization;
using CoverImageDrop.Core.Output;
using CoverImageDrop.Core.Policy;

namespace CoverImageDrop.Pipeline.Tests;

[TestClass]
public sealed class CoverImagePipelineServiceTests
{
    [TestMethod]
    public async Task LoadAsync_FromImageDataCreatesUsableSessionWithCenteredCrop()
    {
        var imageData = CreateImageData(TestImageFactory.CreateSolidPng(600, 500, Colors.Red));
        var result = await new CoverImagePipelineService().LoadAsync(imageData);

        Assert.IsTrue(result.IsSuccess, FormatIssues(result.Issues));
        Assert.IsNotNull(result.Session);
        Assert.AreEqual(new CoverImageCropRect(50, 0, 500, 500), result.Session.CropState.Rect);
        Assert.AreEqual(600, result.Session.CropState.OrientedPixelWidth);
        Assert.AreEqual(500, result.Session.CropState.OrientedPixelHeight);
        Assert.AreEqual(1000, result.Session.CropState.TargetSizePixels);
        Assert.AreEqual(500, result.Session.Validation.UsableSquarePixels);
        Assert.IsTrue(result.Issues.Any(issue => issue.Code == CoverImageIssueCode.UpscalingRequired));
    }

    [TestMethod]
    public async Task LoadAsync_RejectsImageBelowStandardCoverMinimum()
    {
        var result = await new CoverImagePipelineService().LoadAsync(
            CreateImageData(TestImageFactory.CreateSolidPng(499, 500, Colors.Red)));

        Assert.IsFalse(result.IsSuccess);
        Assert.IsNull(result.Session);
        Assert.IsNotNull(result.Validation);
        Assert.IsTrue(result.Issues.Any(issue => issue.Code == CoverImageIssueCode.ImageTooSmall));
    }

    [TestMethod]
    public async Task LoadAsync_InvalidImageDataReturnsFailedResultWithStructuredIssue()
    {
        var result = await new CoverImagePipelineService().LoadAsync(CreateImageData([1, 2, 3]));

        Assert.IsFalse(result.IsSuccess);
        Assert.IsNull(result.Session);
        Assert.IsNotEmpty(result.Issues);
        Assert.IsTrue(result.Issues.Any(issue => issue.Severity == CoverImageIssueSeverity.Error));
    }

    [TestMethod]
    public async Task LoadFileAsync_ReadsFilePickerImageAndPreservesLocalPath()
    {
        var filePath = Path.Combine(Path.GetTempPath(), $"CoverImageDropPipeline_{Guid.NewGuid():N}.png");
        try
        {
            await File.WriteAllBytesAsync(filePath, TestImageFactory.CreateSolidPng(500, 500, Colors.Red));

            var result = await new CoverImagePipelineService().LoadFileAsync(filePath);

            Assert.IsTrue(result.IsSuccess, FormatIssues(result.Issues));
            Assert.IsNotNull(result.Session);
            Assert.AreEqual(filePath, result.Session.ImageData.LocalFilePath);
            Assert.AreEqual(Path.GetFileName(filePath), result.Session.ImageData.SourceName);
            Assert.AreEqual("FilePath", result.Session.ReadResult.AcquisitionMethod);
        }
        finally
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
    }

    [TestMethod]
    public async Task LoadFileAsync_MissingFileReturnsFailedResult()
    {
        var filePath = Path.Combine(Path.GetTempPath(), $"CoverImageDropPipeline_missing_{Guid.NewGuid():N}.png");

        var result = await new CoverImagePipelineService().LoadFileAsync(filePath);

        Assert.IsFalse(result.IsSuccess);
        Assert.IsNull(result.Session);
        Assert.IsTrue(result.Issues.Any(issue => issue.Code == CoverImageIssueCode.NoImagePayloadFound));
    }

    [TestMethod]
    public async Task LoadFileAsync_RejectsOversizedFileBeforeReadingPayload()
    {
        var filePath = Path.Combine(Path.GetTempPath(), $"CoverImageDropPipeline_large_{Guid.NewGuid():N}.bin");
        try
        {
            await using (var stream = new FileStream(filePath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.SetLength(StandardCoverPolicy.Default.MaxInputBytes + 1);
            }

            var result = await new CoverImagePipelineService().LoadFileAsync(filePath);

            Assert.IsFalse(result.IsSuccess);
            Assert.IsNull(result.Inspection);
            Assert.IsTrue(result.Issues.Any(issue => issue.Code == CoverImageIssueCode.InputTooLarge));
        }
        finally
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
    }

    [TestMethod]
    public async Task UpdateCropPosition_ClampsPositionToOrientedImageBounds()
    {
        var session = await LoadSessionAsync(TestImageFactory.CreateSolidPng(600, 500, Colors.Red));

        session.UpdateCropPosition(550, -20);

        Assert.AreEqual(new CoverImageCropRect(100, 0, 500, 500), session.CropState.Rect);
    }

    [TestMethod]
    public async Task UpdateCropPosition_RaisesCropStateChangedWhenPositionChanges()
    {
        var session = await LoadSessionAsync(TestImageFactory.CreateSolidPng(600, 500, Colors.Red));
        CoverImageCropState? changedState = null;
        session.CropStateChanged += (_, state) => changedState = state;

        session.UpdateCropPosition(100, 0);

        Assert.IsNotNull(changedState);
        Assert.AreEqual(new CoverImageCropRect(100, 0, 500, 500), changedState.Rect);
    }

    [TestMethod]
    public async Task CropState_ClampRestoresFixedMaximumSquareSize()
    {
        var session = await LoadSessionAsync(TestImageFactory.CreateSolidPng(600, 500, Colors.Red));

        var tampered = session.CropState with
        {
            Rect = new CoverImageCropRect(25, 30, 200, 300)
        };

        var clamped = tampered.Clamp();

        Assert.AreEqual(new CoverImageCropRect(25, 0, 500, 500), clamped.Rect);
    }

    [TestMethod]
    public async Task ResetCropToCenter_RestoresDefaultCrop()
    {
        var session = await LoadSessionAsync(TestImageFactory.CreateSolidPng(600, 500, Colors.Red));
        session.UpdateCropPosition(100, 0);

        session.ResetCropToCenter();

        Assert.AreEqual(new CoverImageCropRect(50, 0, 500, 500), session.CropState.Rect);
    }

    [TestMethod]
    public async Task FinalizeAsync_ProducesJpegWithTargetSize()
    {
        var session = await LoadSessionAsync(TestImageFactory.CreateSolidPng(1500, 1600, Colors.Red));

        var result = await session.FinalizeAsync();

        Assert.IsTrue(result.IsSuccess);
        Assert.IsNotNull(result.Output);
        Assert.AreEqual(2000, result.Output.Width);
        Assert.AreEqual(2000, result.Output.Height);
        Assert.AreEqual("image/jpeg", result.Output.MimeType);
        Assert.AreEqual(CoverOutputFormat.Jpeg, result.Output.Format);
        Assert.AreEqual(".jpg", result.Output.FileExtension);
    }

    [TestMethod]
    public async Task FinalizeAsync_UsesManualCrop()
    {
        var session = await LoadSessionAsync(TestImageFactory.CreateTwoColorPng(1500, 500, Colors.Red, Colors.Blue));
        session.UpdateCropPosition(1000, 0);

        var result = await session.FinalizeAsync();
        var pixel = TestImageFactory.ReadCenterPixel(result.Output!.Bytes);

        Assert.IsGreaterThan((byte)180, pixel.B, $"Expected blue crop, got {pixel}.");
    }

    [TestMethod]
    public async Task LoadAsync_Size1000OverrideResolvesProcessingPlanWithoutUpscaling()
    {
        var service = new CoverImagePipelineService(new CoverImagePipelineOptions
        {
            StandardCoverOutput = new StandardCoverOutputOptions
            {
                TargetSize = CoverTargetSizeMode.Size1000
            }
        });

        var result = await service.LoadAsync(
            CreateImageData(TestImageFactory.CreateSolidPng(1500, 1600, Colors.Red)));

        Assert.IsTrue(result.IsSuccess, FormatIssues(result.Issues));
        Assert.IsNotNull(result.Session);
        Assert.AreEqual(1000, result.Session.ProcessingPlan.TargetSizePixels);
        Assert.AreEqual(CoverTargetSizeMode.Size1000, result.Session.ProcessingPlan.TargetSizeMode);
        Assert.IsFalse(result.Session.ProcessingPlan.RequiresUpscaling);
        Assert.AreEqual(1000, result.Session.CropState.TargetSizePixels);
        Assert.IsFalse(result.Issues.Any(issue => issue.Code == CoverImageIssueCode.UpscalingRequired));
    }

    [TestMethod]
    public async Task LoadAsync_Size2000OverrideReportsUpscaling()
    {
        var service = new CoverImagePipelineService(new CoverImagePipelineOptions
        {
            StandardCoverOutput = new StandardCoverOutputOptions
            {
                TargetSize = CoverTargetSizeMode.Size2000
            }
        });

        var result = await service.LoadAsync(
            CreateImageData(TestImageFactory.CreateSolidPng(1000, 1000, Colors.Red)));

        Assert.IsTrue(result.IsSuccess, FormatIssues(result.Issues));
        Assert.IsNotNull(result.Session);
        Assert.AreEqual(2000, result.Session.ProcessingPlan.TargetSizePixels);
        Assert.AreEqual(CoverTargetSizeMode.Size2000, result.Session.ProcessingPlan.TargetSizeMode);
        Assert.IsTrue(result.Session.ProcessingPlan.RequiresUpscaling);
        Assert.IsTrue(result.Issues.Any(issue => issue.Code == CoverImageIssueCode.UpscalingRequired));
    }

    [TestMethod]
    public async Task LoadAsync_Size3000OverrideReportsUpscaling()
    {
        var service = new CoverImagePipelineService(new CoverImagePipelineOptions
        {
            StandardCoverOutput = new StandardCoverOutputOptions
            {
                TargetSize = CoverTargetSizeMode.Size3000
            }
        });

        var result = await service.LoadAsync(
            CreateImageData(
                TestImageFactory.CreateSolidPng(
                    2000,
                    2000,
                    Colors.Red)));

        Assert.IsTrue(result.IsSuccess, FormatIssues(result.Issues));
        Assert.IsNotNull(result.Session);
        Assert.AreEqual(
            3000,
            result.Session.ProcessingPlan.TargetSizePixels);
        Assert.AreEqual(
            CoverTargetSizeMode.Size3000,
            result.Session.ProcessingPlan.TargetSizeMode);
        Assert.IsTrue(result.Session.ProcessingPlan.RequiresUpscaling);
        Assert.IsTrue(
            result.Issues.Any(
                issue => issue.Code ==
                    CoverImageIssueCode.UpscalingRequired));
    }

    [TestMethod]
    public async Task FinalizeAsync_AutoPreferredTierProduces3000()
    {
        var session = await LoadSessionAsync(
            TestImageFactory.CreateSolidPng(
                2250,
                2250,
                Colors.DarkGreen));

        Assert.AreEqual(
            3000,
            session.ProcessingPlan.TargetSizePixels);
        Assert.AreEqual(
            CoverTargetSizeMode.Auto,
            session.ProcessingPlan.TargetSizeMode);
        Assert.IsTrue(session.ProcessingPlan.RequiresUpscaling);

        var result = await session.FinalizeAsync();

        Assert.IsTrue(result.IsSuccess, FormatIssues(result.Issues));
        Assert.IsNotNull(result.Output);
        Assert.AreEqual(3000, result.Output.Width);
        Assert.AreEqual(3000, result.Output.Height);
        Assert.AreEqual(3000, result.Output.ResolvedTargetSizePixels);
        Assert.IsTrue(result.Output.WasUpscaled);
    }

    [TestMethod]
    public async Task FinalizeAsync_PngOptionUsesResolvedPlanAndGenericOutputContract()
    {
        var service = new CoverImagePipelineService(new CoverImagePipelineOptions
        {
            StandardCoverOutput = new StandardCoverOutputOptions
            {
                TargetSize = CoverTargetSizeMode.Size1000,
                Format = CoverOutputFormat.Png
            }
        });
        var load = await service.LoadAsync(
            CreateImageData(TestImageFactory.CreateSolidPng(1500, 1600, Colors.Green)));
        Assert.IsTrue(load.IsSuccess, FormatIssues(load.Issues));

        var result = await load.Session!.FinalizeAsync();

        Assert.IsTrue(result.IsSuccess);
        Assert.IsNotNull(result.Output);
        Assert.AreEqual(CoverOutputFormat.Png, result.Output.Format);
        Assert.AreEqual("image/png", result.Output.MimeType);
        Assert.AreEqual(".png", result.Output.FileExtension);
        Assert.AreEqual(1000, result.Output.Width);
        Assert.AreEqual(1000, result.Output.ResolvedTargetSizePixels);
        Assert.IsFalse(result.Output.WasUpscaled);
        Assert.IsNull(result.Output.JpegQuality);
        Assert.IsNull(result.Output.JpegMatte);
    }


    [TestMethod]
    public async Task FinalizeAsync_AutoTransparencyResolvesBrightEdgeToBlackMatte()
    {
        var service = new CoverImagePipelineService(new CoverImagePipelineOptions
        {
            StandardCoverOutput = new StandardCoverOutputOptions
            {
                TargetSize = CoverTargetSizeMode.Size1000,
                Format = CoverOutputFormat.Jpeg,
                Transparency = JpegTransparencyMode.Auto
            }
        });
        var load = await service.LoadAsync(
            CreateImageData(TestImageFactory.CreateHalfTransparentPng(500, 500, Colors.White)));
        Assert.IsTrue(load.IsSuccess, FormatIssues(load.Issues));

        var result = await load.Session!.FinalizeAsync();

        Assert.IsTrue(result.IsSuccess, FormatIssues(result.Issues));
        Assert.IsNotNull(result.Output);
        Assert.AreEqual(CoverImageDrop.Core.Output.CoverImageMatte.Black, result.Output.JpegMatte);
        Assert.IsTrue(result.Issues.Any(issue => issue.Code == CoverImageIssueCode.TransparencyFlattened));
    }

    [TestMethod]
    public async Task FinalizeAsync_ExplicitWhiteTransparencyOverridesAutoMatte()
    {
        var service = new CoverImagePipelineService(new CoverImagePipelineOptions
        {
            StandardCoverOutput = new StandardCoverOutputOptions
            {
                TargetSize = CoverTargetSizeMode.Size1000,
                Format = CoverOutputFormat.Jpeg,
                Transparency = JpegTransparencyMode.White
            }
        });
        var load = await service.LoadAsync(
            CreateImageData(TestImageFactory.CreateHalfTransparentPng(500, 500, Colors.White)));
        Assert.IsTrue(load.IsSuccess, FormatIssues(load.Issues));

        var result = await load.Session!.FinalizeAsync();

        Assert.IsTrue(result.IsSuccess, FormatIssues(result.Issues));
        Assert.IsNotNull(result.Output);
        Assert.AreEqual(CoverImageDrop.Core.Output.CoverImageMatte.White, result.Output.JpegMatte);
    }

    [TestMethod]
    public async Task Diagnostics_AreDisabledByDefault()
    {
        var session = await LoadSessionAsync(TestImageFactory.CreateSolidPng(500, 500, Colors.Red));

        var result = await session.FinalizeAsync();

        Assert.IsTrue(result.IsSuccess);
        Assert.IsNull(result.DiagnosticsFolder);
    }

    [TestMethod]
    public async Task Diagnostics_WriteArtifactsWhenEnabled()
    {
        var root = Path.Combine(Path.GetTempPath(), $"CoverImageDropPipeline_{Guid.NewGuid():N}");
        try
        {
            var service = new CoverImagePipelineService(new CoverImagePipelineOptions
            {
                Diagnostics = new CoverImageDiagnosticsOptions
                {
                    Enabled = true,
                    RootFolder = root,
                    SelectedSource = "UnitTest"
                }
            });
            var load = await service.LoadAsync(CreateImageData(TestImageFactory.CreateSolidPng(500, 500, Colors.Red)));
            Assert.IsTrue(load.IsSuccess, FormatIssues(load.Issues));
            var session = load.Session!;

            var result = await session.FinalizeAsync();

            Assert.IsTrue(result.IsSuccess);
            Assert.IsNotNull(result.DiagnosticsFolder);
            Assert.IsTrue(File.Exists(Path.Combine(result.DiagnosticsFolder, "input.bin")));
            Assert.IsTrue(File.Exists(Path.Combine(result.DiagnosticsFolder, "normalized.jpg")));
            Assert.IsTrue(File.Exists(Path.Combine(result.DiagnosticsFolder, "result.json")));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static async Task<CoverImagePipelineSession> LoadSessionAsync(byte[] bytes)
    {
        var result = await new CoverImagePipelineService().LoadAsync(CreateImageData(bytes));
        Assert.IsTrue(result.IsSuccess, FormatIssues(result.Issues));
        return result.Session!;
    }

    private static string FormatIssues(IEnumerable<CoverImageIssue> issues)
    {
        return string.Join(Environment.NewLine, issues.Select(issue => $"{issue.Severity}: {issue.Code}"));
    }

    private static DropImageData CreateImageData(byte[] bytes)
    {
        return DropImageData.FromBytes(bytes, "test.png");
    }
}
