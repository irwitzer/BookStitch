using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class Mp3DiscPollingServiceTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "BookStitch_Mp3DiscPollingServiceTests_" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void CheckDiscSourceForNextImport_WhenSourceIsMissing_ReturnsNotReady()
    {
        var service = CreateService();
        var missingFolder = Path.Combine(_tempRoot, "missing");

        var result = service.CheckDiscSourceForNextImport(missingFolder, discNumber: 2, totalDiscs: 4, new HashSet<string>());

        Assert.False(result.CanImport);
        Assert.Contains("erkennt die eingelegte Disc automatisch", result.DialogText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CD 2", result.StatusText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CheckDiscSourceForNextImport_WhenFolderHasNoAudio_ReturnsWaitingForSupportedDisc()
    {
        var sourceFolder = CreateFolder("no-audio");
        File.WriteAllText(Path.Combine(sourceFolder, "readme.txt"), "not audio");
        var service = CreateService();

        var result = service.CheckDiscSourceForNextImport(sourceFolder, discNumber: 1, totalDiscs: 2, new HashSet<string>());

        Assert.False(result.CanImport);
        Assert.Contains("Keine MP3-CD erkannt. /", result.DialogText, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(DiscPollingDisplayState.Unsupported, result.DisplayState);
        Assert.True(
            result.DialogText.Contains("ausgeworfen", StringComparison.OrdinalIgnoreCase) ||
            result.DialogText.Contains("manuell aus", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CheckDiscSourceForNextImport_WhenFolderHasNewAudio_ReturnsCanImport()
    {
        var sourceFolder = CreateFolder("new-disc");
        File.WriteAllText(Path.Combine(sourceFolder, "001.mp3"), "audio");
        var service = CreateService();

        var result = service.CheckDiscSourceForNextImport(sourceFolder, discNumber: 1, totalDiscs: 3, new HashSet<string>());

        Assert.True(result.CanImport);
        Assert.Contains("Neue MP3-CD erkannt", result.DialogText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Import startet", result.StatusText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CheckDiscSourceForNextImport_WhenDiscWasAlreadyImported_ReturnsDuplicateDiscMessage()
    {
        var sourceFolder = CreateFolder("duplicate-disc");
        File.WriteAllText(Path.Combine(sourceFolder, "001.mp3"), "audio");
        var importService = new Mp3DiscImportService();
        var analysis = importService.AnalyzeSource(sourceFolder);
        var importedSignatures = new HashSet<string> { importService.CreateDiscSignature(sourceFolder, analysis) };
        var service = new Mp3DiscPollingService(importService, new DiscDriveService());

        var result = service.CheckDiscSourceForNextImport(sourceFolder, discNumber: 2, totalDiscs: 3, importedSignatures);

        Assert.False(result.CanImport);
        Assert.Contains("bereits importiert", result.DialogText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Bereits verwendete CD erkannt", result.DialogText, StringComparison.OrdinalIgnoreCase);
        Assert.True(
            result.DialogText.Contains("ausgeworfen", StringComparison.OrdinalIgnoreCase) ||
            result.DialogText.Contains("manuell aus", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("CD 2", result.ProgressText, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(DiscPollingDisplayState.Duplicate, result.DisplayState);
    }

    [Fact]
    public void CheckDiscSourceForNextImport_WhenExpectedShortTestDiscDoesNotMatch_ReturnsUnsupported()
    {
        var sourceFolder = CreateFolder("wrong-short-test-disc");
        File.WriteAllText(Path.Combine(sourceFolder, "001.mp3"), "audio");
        var service = CreateService();

        var result = service.CheckDiscSourceForNextImport(
            sourceFolder,
            discNumber: 1,
            totalDiscs: 2,
            new HashSet<string>(),
            expectedDiscSignature: "different-disc-signature");

        Assert.False(result.CanImport);
        Assert.Equal(DiscPollingDisplayState.Unsupported, result.DisplayState);
        Assert.Contains("gehört nicht zum vorbereiteten Kurztest", result.DialogText, StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    private static Mp3DiscPollingService CreateService()
    {
        return new Mp3DiscPollingService(new Mp3DiscImportService(), new DiscDriveService());
    }

    private string CreateFolder(string name)
    {
        var folder = Path.Combine(_tempRoot, name);
        Directory.CreateDirectory(folder);
        return folder;
    }
}
