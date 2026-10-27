using System.IO;
using System.Text.Json;

namespace CoverImageDrop.Diagnostics.Tests;

[TestClass]
public sealed class SmokeTestAnnotationWriterTests
{
    [TestMethod]
    public async Task ApplyAsync_WritesSmokeMetadataAndRenamesFolder()
    {
        var root = CreateTempRoot();
        try
        {
            var folder = Path.Combine(
                root,
                "20260824_100000_001_Auto_success");
            Directory.CreateDirectory(folder);
            await File.WriteAllTextAsync(
                Path.Combine(folder, "result.json"),
                """
                {
                  "Profile": "StandardCover"
                }
                """);

            var annotated = await SmokeTestAnnotationWriter.ApplyAsync(
                folder,
                new SmokeTestAnnotation
                {
                    TestBrowser = "Chrome",
                    Verdict = "CorrectImage",
                    StartedAt = new DateTimeOffset(
                        2026,
                        8,
                        24,
                        10,
                        0,
                        0,
                        TimeSpan.Zero),
                    AutoFinalize = true,
                    AcquisitionMilliseconds = 12.346,
                    LoadMilliseconds = 45.678,
                    PreviewMilliseconds = 3.21,
                    FinalizeMilliseconds = 8.9,
                    TotalMilliseconds = 70.123
                });

            StringAssert.EndsWith(
                annotated,
                "20260824_100000_001_Auto_success_Chrome_CorrectImage");
            Assert.IsFalse(Directory.Exists(folder));
            Assert.IsTrue(Directory.Exists(annotated));

            using var document = JsonDocument.Parse(
                await File.ReadAllTextAsync(
                    Path.Combine(annotated, "result.json")));
            var result = document.RootElement;

            Assert.AreEqual(
                "StandardCover",
                result.GetProperty("Profile").GetString());
            var smoke = result.GetProperty("SmokeTest");
            Assert.AreEqual(
                "Chrome",
                smoke.GetProperty("TestBrowser").GetString());
            Assert.AreEqual(
                "CorrectImage",
                smoke.GetProperty("Verdict").GetString());
            Assert.IsTrue(
                smoke.GetProperty("AutoFinalize").GetBoolean());
            Assert.AreEqual(
                12.35,
                smoke.GetProperty("AcquisitionMilliseconds").GetDouble(),
                0.001);
            Assert.AreEqual(
                "20260824_100000_001_Auto_success",
                smoke.GetProperty("BaseFolderName").GetString());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public async Task ApplyAsync_ReannotationChangesVerdictWithoutStackingFolderSuffix()
    {
        var root = CreateTempRoot();
        try
        {
            var folder = Path.Combine(
                root,
                "20260824_100000_001_Auto_success");
            Directory.CreateDirectory(folder);
            await File.WriteAllTextAsync(
                Path.Combine(folder, "result.json"),
                """
                {
                  "Profile": "StandardCover"
                }
                """);

            var first = await SmokeTestAnnotationWriter.ApplyAsync(
                folder,
                CreateAnnotation("CorrectImage"));
            var second = await SmokeTestAnnotationWriter.ApplyAsync(
                first,
                CreateAnnotation("WrongImage"));

            StringAssert.EndsWith(
                second,
                "20260824_100000_001_Auto_success_Chrome_WrongImage");
            Assert.IsFalse(second.Contains(
                "CorrectImage_Chrome",
                StringComparison.Ordinal));

            using var document = JsonDocument.Parse(
                await File.ReadAllTextAsync(
                    Path.Combine(second, "result.json")));
            Assert.AreEqual(
                "WrongImage",
                document.RootElement
                    .GetProperty("SmokeTest")
                    .GetProperty("Verdict")
                    .GetString());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static SmokeTestAnnotation CreateAnnotation(string verdict)
    {
        return new SmokeTestAnnotation
        {
            TestBrowser = "Chrome",
            Verdict = verdict,
            StartedAt = DateTimeOffset.UtcNow,
            AutoFinalize = true
        };
    }

    private static string CreateTempRoot()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "CoverImageDrop.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
