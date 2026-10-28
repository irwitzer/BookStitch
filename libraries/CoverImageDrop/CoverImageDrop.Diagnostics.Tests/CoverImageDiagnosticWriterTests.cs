using System.IO;
using System.Text.Json;
using CoverImageDrop.Acquisition;
using CoverImageDrop.Core.Inspection;
using CoverImageDrop.Core.Issues;
using CoverImageDrop.Core.Normalization;
using CoverImageDrop.Core.Output;
using CoverImageDrop.Core.Validation;

namespace CoverImageDrop.Diagnostics.Tests;

[TestClass]
public sealed class CoverImageDiagnosticWriterTests
{
    [TestMethod]
    public async Task SaveAsync_WritesRichResultJsonPngArtifactAndDebugReport()
    {
        var root = CreateTempRoot();
        try
        {
            var logger = new CoverImageDiagnosticWriter(root);
            var readResult = CreateReadResult();
            var inspection = CreateInspection();
            var validation = new StandardCoverValidationResult(1000, 1_200_000, []);
            var output = new CoverImageOutputResult(
                [9, 8, 7],
                1000,
                1000,
                CoverOutputFormat.Png,
                "image/png",
                ".png",
                [])
            {
                ResolvedTargetSizePixels = 1000,
                WasUpscaled = false
            };
            var issue = new CoverImageIssue(
                CoverImageIssueCode.UpscalingRequired,
                CoverImageIssueSeverity.Warning,
                SuggestedAction: "ChooseLargerSource");
            var context = new CoverImageDiagnosticContext
            {
                Profile = "StandardCover",
                ManualSourceOverride = "Auto",
                SourceDetection = readResult.SourceDetection,
                CandidateAttempts =
                [
                    new CoverImageDiagnosticCandidateAttempt(
                        0,
                        DropImageCandidateKind.HtmlDerivedUrl,
                        "HTML Format:src",
                        20,
                        320,
                        "https://example.test/cover.jpg",
                        true,
                        true,
                        inspection,
                        validation,
                        [],
                        [],
                        null)
                ],
                StandardCoverValidation = validation,
                TargetSizeMode = "Size1000",
                RequestedOutputFormat = "Png",
                TransparencyMode = "Auto",
                ResolvedTargetSizePixels = 1000,
                RequiresUpscaling = false,
                Crop = new CoverImageCropRect(100, 0, 1000, 1000),
                NamingStrategy = "HostProvidedName",
                FinalFileName = "Mein Cover.png",
                PersistencePath = @"C:\Covers\Mein Cover.png",
                Issues = [issue]
            };

            var folder = await logger.SaveAsync(
                "Auto",
                readResult,
                inspection,
                output,
                null,
                context);

            Assert.IsTrue(Directory.Exists(folder));
            CollectionAssert.AreEqual(
                new byte[] { 1, 2, 3 },
                await File.ReadAllBytesAsync(Path.Combine(folder, "input.bin")));
            CollectionAssert.AreEqual(
                new byte[] { 9, 8, 7 },
                await File.ReadAllBytesAsync(Path.Combine(folder, "normalized.png")));
            Assert.IsFalse(File.Exists(Path.Combine(folder, "normalized.jpg")));
            var debugReportPath = Path.Combine(folder, "debug-report.txt");
            Assert.IsTrue(File.Exists(debugReportPath));
            var debugReport = await File.ReadAllTextAsync(debugReportPath);
            Assert.IsTrue(debugReport.Contains(
                "Action=ChooseLargerSource",
                StringComparison.Ordinal));

            using var json = JsonDocument.Parse(
                await File.ReadAllTextAsync(Path.Combine(folder, "result.json")));
            var rootElement = json.RootElement;
            Assert.AreEqual("StandardCover", rootElement.GetProperty("Profile").GetString());
            Assert.AreEqual("Auto", rootElement.GetProperty("ManualSourceOverride").GetString());
            Assert.AreEqual(
                "Chromium",
                rootElement.GetProperty("SourceDetection").GetProperty("Kind").GetString());
            Assert.AreEqual(
                "High",
                rootElement.GetProperty("SourceDetection").GetProperty("Confidence").GetString());
            Assert.AreEqual(
                1,
                rootElement.GetProperty("Acquisition").GetProperty("Candidates").GetArrayLength());
            Assert.AreEqual(
                1,
                rootElement.GetProperty("Acquisition").GetProperty("CandidateAttempts").GetArrayLength());
            Assert.AreEqual(
                "HostProvidedName",
                rootElement.GetProperty("Naming").GetProperty("NamingStrategy").GetString());
            Assert.AreEqual(
                @"C:\Covers\Mein Cover.png",
                rootElement.GetProperty("Persistence").GetProperty("Path").GetString());
            Assert.AreEqual(
                "Png",
                rootElement.GetProperty("Output").GetProperty("Format").GetString());
            Assert.AreEqual(
                1,
                rootElement.GetProperty("Issues").GetArrayLength());
            Assert.AreEqual(
                "ChooseLargerSource",
                rootElement.GetProperty("Issues")[0].GetProperty("SuggestedAction").GetString());
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [TestMethod]
    public async Task DebugReport_TruncatesOnlyReportSnippetAndNeverEmbedsBinaryPayload()
    {
        var root = CreateTempRoot();
        try
        {
            var longHtml = "<html>" + new string('x', 5_000) + "TAIL-MARKER</html>";
            var readResult = new DropReadResult(
                DropImageData.FromBytes([1, 2, 3, 4], "source.png"),
                "UnitTest",
                "Chromium",
                ["HTML Format"],
                new Dictionary<string, string> { ["HTML Format"] = longHtml },
                [],
                null,
                [])
            {
                DiagnosticTextArtifacts = new Dictionary<string, string>
                {
                    ["HTML Format"] = longHtml
                }
            };
            var logger = new CoverImageDiagnosticWriter(
                root,
                new CoverImageDiagnosticOptions
                {
                    SaveInputBytes = false,
                    SaveNormalizedImage = false,
                    SaveTextArtifacts = true,
                    DebugReportTextLimit = 120
                });

            var report = logger.BuildDebugReport(
                "Auto",
                readResult,
                context: new CoverImageDiagnosticContext { Profile = "StandardCover" });

            Assert.AreEqual(longHtml.Length, readResult.TextArtifacts["HTML Format"].Length);
            Assert.IsTrue(report.Contains("[... Debugbericht gekuerzt ...]", StringComparison.Ordinal));
            Assert.IsFalse(report.Contains("TAIL-MARKER", StringComparison.Ordinal));
            Assert.IsFalse(report.Contains("AQIDBA==", StringComparison.Ordinal));

            var folder = await logger.SaveAsync(
                "Auto",
                readResult,
                inspection: null,
                output: null,
                error: "expected test error",
                context: new CoverImageDiagnosticContext { Profile = "StandardCover" });
            var savedText = await File.ReadAllTextAsync(Path.Combine(folder, "text_HTML Format.txt"));
            Assert.AreEqual(longHtml, savedText);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [TestMethod]
    public async Task SaveAsync_SeparateWriterInstancesProduceDistinctFolders()
    {
        var root = CreateTempRoot();
        try
        {
            var firstReadResult = CreateReadResult();
            var secondReadResult = CreateReadResult();

            var first = await new CoverImageDiagnosticWriter(root).SaveAsync(
                "Auto",
                firstReadResult,
                inspection: null,
                output: null,
                error: null);
            var second = await new CoverImageDiagnosticWriter(root).SaveAsync(
                "Auto",
                secondReadResult,
                inspection: null,
                output: null,
                error: null);

            Assert.AreNotEqual(first, second);
            Assert.IsTrue(Directory.Exists(first));
            Assert.IsTrue(Directory.Exists(second));
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [TestMethod]
    public void Constructor_RejectsNonPositiveDebugReportTextLimit()
    {
        var root = CreateTempRoot();
        try
        {
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
                new CoverImageDiagnosticWriter(
                    root,
                    new CoverImageDiagnosticOptions { DebugReportTextLimit = 0 }));
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    private static DropReadResult CreateReadResult()
    {
        var candidate = new DropImageCandidate(
            DropImageCandidateKind.HtmlDerivedUrl,
            "HTML Format:src",
            20,
            320,
            null,
            "https://example.test/cover.jpg");
        return new DropReadResult(
            DropImageData.FromBytes([1, 2, 3], "source.png"),
            "UrlDownload",
            "Chromium",
            ["HTML Format"],
            new Dictionary<string, string>
            {
                ["HTML Format"] = "<img src=\"https://example.test/cover.jpg\">"
            },
            [new DropUrlCandidate(
                "https://example.test/cover.jpg",
                "HTML Format:src",
                20,
                320,
                DropImageCandidateKind.HtmlDerivedUrl)],
            new DropDownloadInfo(
                "https://example.test/cover.jpg",
                "https://cdn.example.test/cover.jpg",
                "image/png",
                1234,
                false),
            [])
        {
            SourceDetection = new DropSourceDetection(
                DropSourceKind.Chromium,
                DropSourceConfidence.High,
                ["Chromium-spezifisches Dropformat vorhanden."]),
            Candidates = [candidate],
            DiagnosticTextArtifacts = new Dictionary<string, string>
            {
                ["HTML Format"] = "<img src=\"https://example.test/cover.jpg\">"
            }
        };
    }

    private static CoverImageInspectionResult CreateInspection()
    {
        return new CoverImageInspectionResult(
            "source.png",
            DetectedImageFormat.Png,
            "PNG Decoder",
            1200,
            1000,
            1200,
            1000,
            1,
            true,
            []);
    }

    private static string CreateTempRoot()
    {
        return Path.Combine(Path.GetTempPath(), $"CoverImageDropDiagnostics_{Guid.NewGuid():N}");
    }

    private static void DeleteTempRoot(string root)
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
