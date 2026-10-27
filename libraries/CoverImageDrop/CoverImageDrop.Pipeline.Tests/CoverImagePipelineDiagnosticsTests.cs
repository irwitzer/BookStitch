using System.IO;
using System.Text.Json;
using System.Windows.Media;
using CoverImageDrop.Acquisition;

namespace CoverImageDrop.Pipeline.Tests;

[TestClass]
public sealed class CoverImagePipelineDiagnosticsTests
{
    [TestMethod]
    public async Task FinalizeDiagnostics_CarriesProfileCandidateAttemptsProcessingAndCropIntoJson()
    {
        var root = Path.Combine(Path.GetTempPath(), $"CoverImageDropPipelineDiagnostics_{Guid.NewGuid():N}");
        try
        {
            var small = DropImageData.FromBytes(
                TestImageFactory.CreateSolidPng(300, 300, Colors.Red),
                "small.png");
            var large = DropImageData.FromBytes(
                TestImageFactory.CreateSolidPng(1600, 1200, Colors.Blue),
                "large.png");
            var detection = new DropSourceDetection(
                DropSourceKind.Chromium,
                DropSourceConfidence.High,
                ["test-marker"]);
            var analysis = new DropAcquisitionAnalysis(
                detection,
                ["HTML Format"],
                new Dictionary<string, string>(),
                new Dictionary<string, string>(),
                [
                    new DropImageCandidate(
                        DropImageCandidateKind.EncodedDataUrl,
                        "small",
                        5,
                        300,
                        small,
                        null),
                    new DropImageCandidate(
                        DropImageCandidateKind.EncodedDataUrl,
                        "large",
                        10,
                        1600,
                        large,
                        null)
                ],
                []);
            var service = new CoverImagePipelineService(new CoverImagePipelineOptions
            {
                Diagnostics = new CoverImageDiagnosticsOptions
                {
                    Enabled = true,
                    RootFolder = root,
                    SelectedSource = "Auto",
                    SaveInputBytes = false,
                    SaveNormalizedImage = false,
                    SaveTextArtifacts = false
                }
            });

            var load = await service.LoadStandardCoverAsync(analysis);

            Assert.IsTrue(load.IsSuccess);
            Assert.IsNotNull(load.Session);
            Assert.AreEqual(2, load.CandidateAttempts.Count);
            var session = load.Session!;
            session.UpdateCropPosition(200, 0);
            var context = session.CreateDiagnosticContext();
            Assert.AreEqual("StandardCover", context.Profile);
            Assert.AreEqual(DropSourceKind.Chromium, context.SourceDetection?.Kind);
            Assert.AreEqual(2, context.CandidateAttempts.Count);
            Assert.AreEqual(session.CropState.Rect, context.Crop);

            var finalized = await session.FinalizeAsync();

            Assert.IsTrue(finalized.IsSuccess);
            Assert.IsNotNull(finalized.DiagnosticsFolder);
            var jsonPath = Path.Combine(finalized.DiagnosticsFolder!, "result.json");
            using var json = JsonDocument.Parse(await File.ReadAllTextAsync(jsonPath));
            Assert.AreEqual("StandardCover", json.RootElement.GetProperty("Profile").GetString());
            Assert.AreEqual(
                "Chromium",
                json.RootElement.GetProperty("SourceDetection").GetProperty("Kind").GetString());
            Assert.AreEqual(
                2,
                json.RootElement.GetProperty("Acquisition").GetProperty("CandidateAttempts").GetArrayLength());
            Assert.AreEqual(
                session.ProcessingPlan.TargetSizePixels,
                json.RootElement.GetProperty("Processing").GetProperty("ResolvedTargetSizePixels").GetInt32());
            Assert.AreEqual(
                session.CropState.Rect.X,
                json.RootElement.GetProperty("Processing").GetProperty("Crop").GetProperty("X").GetInt32());

            await session.DisposeAsync();
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [TestMethod]
    public async Task CandidateSweepDiagnostics_AllMaterializationFailuresPersistCompleteAttemptHistory()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"CoverImageDropCandidateSweepDiagnostics_{Guid.NewGuid():N}");
        try
        {
            var detection = new DropSourceDetection(
                DropSourceKind.Chromium,
                DropSourceConfidence.High,
                ["test-marker"]);
            var analysis = new DropAcquisitionAnalysis(
                detection,
                ["HTML Format"],
                new Dictionary<string, string>
                {
                    ["HTML Format"] = "<p>diagnostic fixture</p>"
                },
                new Dictionary<string, string>
                {
                    ["HTML Format"] = "<p>diagnostic fixture</p>"
                },
                [
                    new DropImageCandidate(
                        DropImageCandidateKind.DirectUrl,
                        "missing-a",
                        10,
                        null,
                        null,
                        null),
                    new DropImageCandidate(
                        DropImageCandidateKind.HtmlDerivedUrl,
                        "missing-b",
                        20,
                        null,
                        null,
                        null)
                ],
                []);
            var service = new CoverImagePipelineService(new CoverImagePipelineOptions
            {
                Diagnostics = new CoverImageDiagnosticsOptions
                {
                    Enabled = true,
                    RootFolder = root,
                    SelectedSource = "Auto",
                    SaveInputBytes = false,
                    SaveNormalizedImage = false,
                    SaveTextArtifacts = true
                }
            });

            var load = await service.LoadStandardCoverAsync(analysis);

            Assert.IsFalse(load.IsSuccess);
            Assert.AreEqual(2, load.CandidateAttempts.Count);
            Assert.IsNotNull(load.DiagnosticsFolder);

            var jsonPath = Path.Combine(load.DiagnosticsFolder!, "result.json");
            using var json = JsonDocument.Parse(await File.ReadAllTextAsync(jsonPath));
            var attempts = json.RootElement
                .GetProperty("Acquisition")
                .GetProperty("CandidateAttempts");
            Assert.AreEqual(2, attempts.GetArrayLength());
            Assert.AreEqual(
                "missing-a",
                attempts[0].GetProperty("Source").GetString());
            Assert.AreEqual(
                "missing-b",
                attempts[1].GetProperty("Source").GetString());
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [TestMethod]
    public async Task CandidateSweepDiagnostics_BestValidationFailurePersistsAllRejectedCandidates()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"CoverImageDropBestFailureDiagnostics_{Guid.NewGuid():N}");
        try
        {
            var first = DropImageData.FromBytes(
                TestImageFactory.CreateSolidPng(320, 320, Colors.Red),
                "first-small.png");
            var second = DropImageData.FromBytes(
                TestImageFactory.CreateSolidPng(420, 420, Colors.Blue),
                "second-small.png");
            var detection = new DropSourceDetection(
                DropSourceKind.Chromium,
                DropSourceConfidence.High,
                ["test-marker"]);
            var analysis = new DropAcquisitionAnalysis(
                detection,
                ["HTML Format"],
                new Dictionary<string, string>(),
                new Dictionary<string, string>(),
                [
                    new DropImageCandidate(
                        DropImageCandidateKind.EncodedDataUrl,
                        "first-small",
                        5,
                        320,
                        first,
                        null),
                    new DropImageCandidate(
                        DropImageCandidateKind.EncodedDataUrl,
                        "second-small",
                        10,
                        420,
                        second,
                        null)
                ],
                []);
            var service = new CoverImagePipelineService(new CoverImagePipelineOptions
            {
                Diagnostics = new CoverImageDiagnosticsOptions
                {
                    Enabled = true,
                    RootFolder = root,
                    SelectedSource = "Auto",
                    SaveInputBytes = false,
                    SaveNormalizedImage = false,
                    SaveTextArtifacts = false
                }
            });

            var load = await service.LoadStandardCoverAsync(analysis);

            Assert.IsFalse(load.IsSuccess);
            Assert.AreEqual(2, load.CandidateAttempts.Count);
            Assert.IsNotNull(load.DiagnosticsFolder);

            var jsonPath = Path.Combine(load.DiagnosticsFolder!, "result.json");
            using var json = JsonDocument.Parse(await File.ReadAllTextAsync(jsonPath));
            var attempts = json.RootElement
                .GetProperty("Acquisition")
                .GetProperty("CandidateAttempts");
            Assert.AreEqual(2, attempts.GetArrayLength());
            Assert.IsTrue(
                attempts.EnumerateArray().All(attempt =>
                    attempt.GetProperty("Materialized").GetBoolean()));
            Assert.IsTrue(
                attempts.EnumerateArray().All(attempt =>
                    !attempt.GetProperty("Accepted").GetBoolean()));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

}
