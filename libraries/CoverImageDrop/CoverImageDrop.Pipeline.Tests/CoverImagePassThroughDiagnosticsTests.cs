using System.IO;
using System.Windows.Media;
using CoverImageDrop.Acquisition;
using CoverImageDrop.Core.Naming;
using CoverImageDrop.Pipeline.PassThrough;

namespace CoverImageDrop.Pipeline.Tests;

[TestClass]
public sealed class CoverImagePassThroughDiagnosticsTests
{
    [TestMethod]
    public async Task PassThrough_DiagnosticsReportCarriesNamingPersistenceWithoutDuplicatingPayload()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"CoverImageDropPassThroughDiagnostics_{Guid.NewGuid():N}");
        var target = Path.Combine(root, "target");
        var diagnostics = Path.Combine(root, "diagnostics");
        Directory.CreateDirectory(target);

        try
        {
            var bytes = TestImageFactory.CreateSolidPng(
                120,
                80,
                Colors.Goldenrod);
            var source = DropImageData.FromBytes(
                bytes,
                "source.png");
            var readResult = new DropReadResult(
                source,
                "UnitTest",
                "Explorer",
                ["FileDrop"],
                new Dictionary<string, string>(),
                [],
                null,
                [])
            {
                SourceDetection = new DropSourceDetection(
                    DropSourceKind.WindowsExplorer,
                    DropSourceConfidence.High,
                    ["Windows FileDrop vorhanden."])
            };
            var service = new CoverImagePipelineService(
                new CoverImagePipelineOptions
                {
                    Diagnostics = new CoverImageDiagnosticsOptions
                    {
                        Enabled = true,
                        RootFolder = diagnostics,
                        SelectedSource = "Auto",
                        SaveInputBytes = false,
                        SaveNormalizedImage = true,
                        SaveTextArtifacts = true
                    }
                });

            var result = await service.ProcessPassThroughAsync(
                readResult,
                new CoverImagePassThroughOptions
                {
                    Naming = new CoverImageNamingOptions
                    {
                        Strategy =
                            CoverImageNamingStrategy.HostProvidedName,
                        HostProvidedName = "Diagnostic Cover"
                    },
                    TargetFolder = target
                });

            Assert.IsTrue(result.IsSuccess);
            Assert.IsTrue(result.WasPersisted);
            Assert.IsNotNull(result.Persistence);
            Assert.IsNotNull(result.DiagnosticsFolder);
            Assert.IsNotNull(result.DebugReport);
            Assert.IsTrue(
                result.DebugReport.Contains(
                    "Profil: PassThrough",
                    StringComparison.Ordinal));
            Assert.IsTrue(
                result.DebugReport.Contains(
                    "Naming Strategy: HostProvidedName",
                    StringComparison.Ordinal));
            Assert.IsTrue(
                result.DebugReport.Contains(
                    result.Persistence!.FilePath!,
                    StringComparison.Ordinal));
            Assert.IsTrue(
                File.Exists(
                    Path.Combine(
                        result.DiagnosticsFolder!,
                        "debug-report.txt")));
            Assert.IsTrue(
                File.Exists(
                    Path.Combine(
                        result.DiagnosticsFolder!,
                        "result.json")));
            Assert.IsFalse(
                File.Exists(
                    Path.Combine(
                        result.DiagnosticsFolder!,
                        "input.bin")));
            CollectionAssert.AreEqual(
                bytes,
                await File.ReadAllBytesAsync(
                    result.Persistence.FilePath!));

            source.Dispose();
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
