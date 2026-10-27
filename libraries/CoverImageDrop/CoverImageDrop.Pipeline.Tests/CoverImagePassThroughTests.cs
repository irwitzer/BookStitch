using System.IO;
using System.Windows.Media;
using CoverImageDrop.Acquisition;
using CoverImageDrop.Core.Inspection;
using CoverImageDrop.Core.Issues;
using CoverImageDrop.Core.Naming;
using CoverImageDrop.Core.Policy;
using CoverImageDrop.Pipeline.PassThrough;

namespace CoverImageDrop.Pipeline.Tests;

[TestClass]
public sealed class CoverImagePassThroughTests
{
    [TestMethod]
    public async Task ProcessPassThroughAsync_TinyImageIgnoresStandardCoverLimitsAndKeepsBytes()
    {
        var folder = CreateTestFolder();
        try
        {
            var bytes = TestImageFactory.CreateSolidPng(1, 1, Colors.Red);
            var pipeline = new CoverImagePipelineService(new CoverImagePipelineOptions
            {
                Policy = new StandardCoverPolicy
                {
                    MinUsableSquarePixels = 500,
                    MaxInputBytes = 1,
                    MaxTotalPixels = 1
                }
            });

            var result = await pipeline.ProcessPassThroughAsync(
                DropImageData.FromBytes(bytes, "tiny.png"),
                new CoverImagePassThroughOptions
                {
                    TargetFolder = folder,
                    Naming = HostName("tiny-copy")
                });

            Assert.IsTrue(result.IsSuccess, FormatIssues(result.Issues));
            Assert.AreEqual(DetectedImageFormat.Png, result.Format);
            Assert.AreEqual(CoverImagePassThroughFormatSource.Signature, result.FormatSource);
            Assert.AreEqual(CoverImagePassThroughFormatConfidence.Verified, result.FormatConfidence);
            Assert.AreEqual(1, result.PixelWidth);
            Assert.AreEqual(1, result.PixelHeight);
            Assert.AreEqual(".png", result.FileExtension);
            Assert.IsNotNull(result.Persistence);
            CollectionAssert.AreEqual(bytes, await File.ReadAllBytesAsync(result.Persistence.FilePath!));
            Assert.IsFalse(result.Issues.Any(issue => issue.Code is
                CoverImageIssueCode.ImageTooSmall or
                CoverImageIssueCode.InputTooLarge or
                CoverImageIssueCode.TooManyPixels));
        }
        finally
        {
            DeleteTestFolder(folder);
        }
    }

    [TestMethod]
    public async Task ProcessPassThroughFileAsync_PreservesLocalFileByteForByte()
    {
        var folder = CreateTestFolder();
        var sourcePath = Path.Combine(folder, "source.png");
        var targetFolder = Path.Combine(folder, "out");
        try
        {
            var bytes = TestImageFactory.CreateSolidPng(12, 7, Colors.Blue);
            await File.WriteAllBytesAsync(sourcePath, bytes);

            var result = await new CoverImagePipelineService().ProcessPassThroughFileAsync(
                sourcePath,
                new CoverImagePassThroughOptions
                {
                    TargetFolder = targetFolder,
                    Naming = HostName("local-copy")
                });

            Assert.IsTrue(result.IsSuccess, FormatIssues(result.Issues));
            Assert.IsNotNull(result.Source);
            Assert.AreEqual(DropImagePayloadKind.LocalFile, result.Source.PayloadKind);
            Assert.AreEqual(sourcePath, result.Source.LocalFilePath);
            Assert.AreEqual(12, result.PixelWidth);
            Assert.AreEqual(7, result.PixelHeight);
            CollectionAssert.AreEqual(bytes, await File.ReadAllBytesAsync(result.Persistence!.FilePath!));
        }
        finally
        {
            DeleteTestFolder(folder);
        }
    }

    [TestMethod]
    public async Task ProcessPassThroughAsync_MetadataProbeFailure_DoesNotBlockSave()
    {
        var folder = CreateTestFolder();
        try
        {
            var bytes = new byte[] { 1, 2, 3, 4, 5, 6 };
            var result = await new CoverImagePipelineService().ProcessPassThroughAsync(
                DropImageData.FromBytes(bytes, "broken.jpg"),
                new CoverImagePassThroughOptions
                {
                    TargetFolder = folder,
                    Naming = HostName("broken-copy")
                });

            Assert.IsTrue(result.IsSuccess, FormatIssues(result.Issues));
            Assert.AreEqual(DetectedImageFormat.Jpeg, result.Format);
            Assert.AreEqual(CoverImagePassThroughFormatSource.FileExtension, result.FormatSource);
            Assert.AreEqual(CoverImagePassThroughFormatConfidence.Unverified, result.FormatConfidence);
            Assert.IsNull(result.PixelWidth);
            Assert.IsNull(result.PixelHeight);
            Assert.AreEqual(".jpg", result.FileExtension);
            Assert.IsTrue(result.Issues.Any(issue =>
                issue.Code == CoverImageIssueCode.MetadataProbeFailed &&
                issue.Severity == CoverImageIssueSeverity.Info));
            CollectionAssert.AreEqual(bytes, await File.ReadAllBytesAsync(result.Persistence!.FilePath!));
        }
        finally
        {
            DeleteTestFolder(folder);
        }
    }

    [TestMethod]
    public async Task ProcessPassThroughAsync_HtmlDataUrlPayload_RemainsByteIdentical()
    {
        var folder = CreateTestFolder();
        try
        {
            var bytes = TestImageFactory.CreateSolidPng(3, 2, Colors.Green);
            var readResult = CreateReadResult(
                DropImageData.FromBytes(bytes, "Browser-data-URL"),
                "HtmlDataUrl");

            var result = await new CoverImagePipelineService().ProcessPassThroughAsync(
                readResult,
                new CoverImagePassThroughOptions
                {
                    TargetFolder = folder,
                    Naming = HostName("data-url")
                });

            Assert.IsTrue(result.IsSuccess, FormatIssues(result.Issues));
            Assert.AreEqual(CoverImagePassThroughMaterializationKind.OriginalEncodedPayload, result.MaterializationKind);
            Assert.AreEqual(".png", result.FileExtension);
            CollectionAssert.AreEqual(bytes, await File.ReadAllBytesAsync(result.Persistence!.FilePath!));
        }
        finally
        {
            DeleteTestFolder(folder);
        }
    }

    [TestMethod]
    public async Task ProcessPassThroughAsync_UrlDownloadStreamsTemporaryBackingByteForByte()
    {
        var folder = CreateTestFolder();
        var sourcePath = Path.Combine(folder, "download.tmp");
        var targetFolder = Path.Combine(folder, "out");
        try
        {
            var bytes = new byte[] { 9, 7, 5, 3, 1 };
            await File.WriteAllBytesAsync(sourcePath, bytes);
            using var imageData = DropImageData.FromTemporaryFile(
                sourcePath,
                "cdn.example",
                "https://cdn.example/Remote%20Cover",
                "image/webp");
            var readResult = new DropReadResult(
                imageData,
                "UrlDownload",
                "Chromium",
                [],
                new Dictionary<string, string>(),
                [],
                new DropDownloadInfo(
                    "https://cdn.example/asset?id=42",
                    "https://cdn.example/Remote%20Cover",
                    "image/webp",
                    bytes.Length,
                    false),
                []);

            var result = await new CoverImagePipelineService().ProcessPassThroughAsync(
                readResult,
                new CoverImagePassThroughOptions
                {
                    TargetFolder = targetFolder,
                    Naming = new CoverImageNamingOptions
                    {
                        Strategy = CoverImageNamingStrategy.OriginalFileName
                    }
                });

            Assert.IsTrue(result.IsSuccess, FormatIssues(result.Issues));
            Assert.IsNotNull(result.Source);
            Assert.AreEqual(DropImagePayloadKind.TemporaryFile, result.Source.PayloadKind);
            Assert.IsFalse(result.Source.TryGetInMemoryBytes(out _));
            Assert.AreEqual(DetectedImageFormat.WebP, result.Format);
            Assert.AreEqual(CoverImagePassThroughFormatSource.ContentType, result.FormatSource);
            Assert.AreEqual(CoverImagePassThroughFormatConfidence.Unverified, result.FormatConfidence);
            Assert.AreEqual(".webp", result.FileExtension);
            Assert.AreEqual("Remote Cover.webp", result.SuggestedFileName);
            CollectionAssert.AreEqual(bytes, await File.ReadAllBytesAsync(result.Persistence!.FilePath!));
        }
        finally
        {
            DeleteTestFolder(folder);
        }
    }

    [TestMethod]
    public async Task ProcessPassThroughAsync_BitmapFallback_IsMarkedAndStoredAsPng()
    {
        var folder = CreateTestFolder();
        try
        {
            var bytes = TestImageFactory.CreateSolidPng(4, 4, Colors.Purple);
            var readResult = CreateReadResult(
                DropImageData.FromBytes(bytes, "Browser-Bitmap"),
                "Bitmap");

            var result = await new CoverImagePipelineService().ProcessPassThroughAsync(
                readResult,
                new CoverImagePassThroughOptions
                {
                    TargetFolder = folder,
                    Naming = HostName("bitmap")
                });

            Assert.IsTrue(result.IsSuccess, FormatIssues(result.Issues));
            Assert.AreEqual(CoverImagePassThroughMaterializationKind.LosslessPngFromBitmap, result.MaterializationKind);
            Assert.AreEqual(DetectedImageFormat.Png, result.Format);
            Assert.AreEqual(CoverImagePassThroughFormatSource.BitmapEncoding, result.FormatSource);
            Assert.AreEqual(CoverImagePassThroughFormatConfidence.Verified, result.FormatConfidence);
            Assert.AreEqual(".png", result.FileExtension);
            Assert.IsTrue(result.Issues.Any(issue =>
                issue.Code == CoverImageIssueCode.BitmapFallbackEncodedAsPng &&
                issue.Severity == CoverImageIssueSeverity.Info));
            CollectionAssert.AreEqual(bytes, await File.ReadAllBytesAsync(result.Persistence!.FilePath!));
        }
        finally
        {
            DeleteTestFolder(folder);
        }
    }

    [TestMethod]
    public async Task ProcessPassThroughAsync_OriginalFileName_IsFilledFromLocalSource()
    {
        var folder = CreateTestFolder();
        var sourcePath = Path.Combine(folder, "Original Name.jpeg");
        var targetFolder = Path.Combine(folder, "out");
        try
        {
            var bytes = TestImageFactory.CreateSolidPng(2, 2, Colors.Orange);
            await File.WriteAllBytesAsync(sourcePath, bytes);

            var result = await new CoverImagePipelineService().ProcessPassThroughFileAsync(
                sourcePath,
                new CoverImagePassThroughOptions
                {
                    TargetFolder = targetFolder,
                    Naming = new CoverImageNamingOptions
                    {
                        Strategy = CoverImageNamingStrategy.OriginalFileName
                    }
                });

            Assert.IsTrue(result.IsSuccess, FormatIssues(result.Issues));
            Assert.AreEqual("Original Name.png", result.SuggestedFileName);
            Assert.AreEqual("Original Name.png", result.Persistence!.FileName);
        }
        finally
        {
            DeleteTestFolder(folder);
        }
    }

    [TestMethod]
    public async Task ProcessPassThroughAsync_ConcreteTargetPathBypassesGeneratedName()
    {
        var folder = CreateTestFolder();
        try
        {
            var targetPath = Path.Combine(folder, "host-chosen.custom");
            var bytes = new byte[] { 21, 22, 23 };
            var result = await new CoverImagePipelineService().ProcessPassThroughAsync(
                DropImageData.FromBytes(bytes, "unknown.payload"),
                new CoverImagePassThroughOptions
                {
                    TargetFilePath = targetPath
                });

            Assert.IsTrue(result.IsSuccess, FormatIssues(result.Issues));
            Assert.AreEqual(DetectedImageFormat.Unknown, result.Format);
            Assert.AreEqual(CoverImagePassThroughFormatSource.Unknown, result.FormatSource);
            Assert.AreEqual(CoverImagePassThroughFormatConfidence.Unknown, result.FormatConfidence);
            Assert.AreEqual("host-chosen.custom", result.SuggestedFileName);
            Assert.AreEqual(targetPath, result.Persistence!.FilePath);
            CollectionAssert.AreEqual(bytes, await File.ReadAllBytesAsync(targetPath));
        }
        finally
        {
            DeleteTestFolder(folder);
        }
    }

    [TestMethod]
    public async Task ProcessPassThroughAsync_MissingPayload_ReturnsStructuredFailure()
    {
        var readResult = new DropReadResult(
            null,
            "None",
            null,
            [],
            new Dictionary<string, string>(),
            [],
            null,
            []);

        var result = await new CoverImagePipelineService().ProcessPassThroughAsync(readResult);

        Assert.IsFalse(result.IsSuccess);
        Assert.IsNull(result.Source);
        Assert.IsTrue(result.Issues.Any(issue =>
            issue.Code == CoverImageIssueCode.NoImagePayloadFound &&
            issue.Severity == CoverImageIssueSeverity.Error));
    }

    private static CoverImageNamingOptions HostName(string name)
    {
        return new CoverImageNamingOptions
        {
            Strategy = CoverImageNamingStrategy.HostProvidedName,
            HostProvidedName = name
        };
    }

    private static DropReadResult CreateReadResult(DropImageData imageData, string acquisitionMethod)
    {
        return new DropReadResult(
            imageData,
            acquisitionMethod,
            null,
            [],
            new Dictionary<string, string>(),
            [],
            null,
            []);
    }

    private static string CreateTestFolder()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"CoverImageDrop_PassThrough_{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        return folder;
    }

    private static void DeleteTestFolder(string folder)
    {
        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private static string FormatIssues(IEnumerable<CoverImageIssue> issues)
    {
        return string.Join(
            Environment.NewLine,
            issues.Select(issue => $"{issue.Severity}: {issue.Code} - {issue.TechnicalCause}"));
    }
}
