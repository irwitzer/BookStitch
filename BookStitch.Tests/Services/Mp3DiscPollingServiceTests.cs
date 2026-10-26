using System.Globalization;
using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class Mp3DiscPollingServiceTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "BookStitch_Mp3DiscPollingServiceTests_" + Guid.NewGuid().ToString("N"));
    private readonly List<string?> _ejectRequests = [];

    [Fact]
    public void CheckDiscSourceForNextImport_WhenSourceIsMissing_ReturnsNotReady()
    {
        var service = CreateService();
        var result = service.CheckDiscSourceForNextImport(Path.Combine(_tempRoot, "missing"), 2, 4, new HashSet<string>());
        Assert.False(result.CanImport);
        Assert.Equal(DiscPollingDisplayState.Waiting, result.DisplayState);
        Assert.Equal(LocalizationText.Format("Disc.Polling.Mp3.SourceNotReady.Dialog", 2, 4), result.DialogText);
        Assert.Equal(LocalizationText.Format("Disc.Polling.Mp3.SourceNotReady.Status", 2), result.StatusText);
    }

    [Fact]
    public void CheckDiscSourceForNextImport_WhenFolderHasNoAudio_ReturnsWaitingForSupportedDisc()
    {
        var sourceFolder = CreateFolder("no-audio");
        File.WriteAllText(Path.Combine(sourceFolder, "readme.txt"), "not audio");
        var result = CreateService().CheckDiscSourceForNextImport(sourceFolder, 1, 2, new HashSet<string>());
        Assert.False(result.CanImport);
        Assert.Equal(DiscPollingDisplayState.Unsupported, result.DisplayState);
        Assert.Equal(LocalizationText.Format("Disc.Polling.Mp3.Unsupported.ManualEject.Dialog", 1, 2), result.DialogText);
        Assert.Equal([sourceFolder], _ejectRequests);
    }

    [Fact]
    public void CheckDiscSourceForNextImport_WhenFolderHasNewAudio_LocalizesAllPresentationTexts()
    {
        WithUiCulture("fr-FR", () =>
        {
            var sourceFolder = CreateFolder("new-disc");
            File.WriteAllText(Path.Combine(sourceFolder, "001.mp3"), "audio");
            var result = CreateService().CheckDiscSourceForNextImport(sourceFolder, 1, 3, new HashSet<string>());
            Assert.True(result.CanImport);
            Assert.Equal(DiscPollingDisplayState.Ready, result.DisplayState);
            Assert.Equal(LocalizationText.Format("Disc.Polling.Mp3.Ready.Dialog", 1, 3), result.DialogText);
            Assert.Equal(LocalizationText.Format("Disc.Polling.Mp3.Ready.Status", 1, 3), result.StatusText);
            Assert.Equal(LocalizationText.Format("Disc.Polling.Mp3.Ready.Progress", 1), result.ProgressText);
        });
    }

    [Theory]
    [InlineData(true, "Disc.Polling.Mp3.Unsupported.Ejected.Dialog", "Disc.Polling.Mp3.Unsupported.Ejected.Status", "Disc.Polling.Mp3.Unsupported.Ejected.Progress")]
    [InlineData(false, "Disc.Polling.Mp3.Unsupported.ManualEject.Dialog", "Disc.Polling.Mp3.Unsupported.ManualEject.Status", "Disc.Polling.Mp3.Unsupported.ManualEject.Progress")]
    public void CheckDiscSourceForNextImport_UnsupportedDisc_PreservesEjectSemanticsAndUsesLocalizedTemplates(
        bool ejectSucceeds, string dialogKey, string statusKey, string progressKey)
    {
        WithUiCulture("en-US", () =>
        {
            var sourceFolder = CreateFolder("unsupported-" + ejectSucceeds);
            File.WriteAllText(Path.Combine(sourceFolder, "readme.txt"), "not audio");
            var result = CreateService(ejectSucceeds: ejectSucceeds).CheckDiscSourceForNextImport(sourceFolder, 2, 3, new HashSet<string>());
            Assert.False(result.CanImport);
            Assert.Equal(DiscPollingDisplayState.Unsupported, result.DisplayState);
            Assert.Equal(LocalizationText.Format(dialogKey, 2, 3), result.DialogText);
            Assert.Equal(LocalizationText.Format(statusKey, 2), result.StatusText);
            Assert.Equal(LocalizationText.Format(progressKey, 2), result.ProgressText);
            Assert.Equal([sourceFolder], _ejectRequests);
        });
    }

    [Fact]
    public void CheckDiscSourceForNextImport_WhenDiscWasAlreadyImported_ReturnsDuplicateDiscMessage()
    {
        var sourceFolder = CreateFolder("duplicate-disc");
        File.WriteAllText(Path.Combine(sourceFolder, "001.mp3"), "audio");
        var importService = new Mp3DiscImportService();
        var analysis = importService.AnalyzeSource(sourceFolder);
        var imported = new HashSet<string> { importService.CreateDiscSignature(sourceFolder, analysis) };
        var result = CreateService(importService).CheckDiscSourceForNextImport(sourceFolder, 2, 3, imported);
        Assert.False(result.CanImport);
        Assert.Equal(DiscPollingDisplayState.Duplicate, result.DisplayState);
        Assert.Equal(LocalizationText.Format("Disc.Polling.Mp3.Duplicate.ManualEject.Dialog", 2, 3), result.DialogText);
        Assert.Equal([sourceFolder], _ejectRequests);
    }

    [Fact]
    public void CheckDiscSourceForNextImport_DuplicateOverrideAcceptsOnlyWhenExplicitlyRequested()
    {
        var sourceFolder = CreateFolder("duplicate-override");
        File.WriteAllText(Path.Combine(sourceFolder, "001.mp3"), "audio");
        var importService = new Mp3DiscImportService();
        var analysis = importService.AnalyzeSource(sourceFolder);
        var signature = importService.CreateDiscSignature(sourceFolder, analysis);
        var imported = new HashSet<string> { signature };
        var service = CreateService(importService);

        var accepted = service.CheckDiscSourceForNextImport(sourceFolder, 2, 3, imported, acceptDuplicate: true);
        var rejectedAgain = service.CheckDiscSourceForNextImport(sourceFolder, 2, 3, imported);

        Assert.True(accepted.CanImport);
        Assert.False(rejectedAgain.CanImport);
        Assert.Equal(DiscPollingDisplayState.Duplicate, rejectedAgain.DisplayState);
        Assert.Equal([signature], imported);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot)) Directory.Delete(_tempRoot, recursive: true);
    }

    private Mp3DiscPollingService CreateService(Mp3DiscImportService? importService = null, bool ejectSucceeds = false) =>
        new(importService ?? new Mp3DiscImportService(), new DiscDriveService(), path => { _ejectRequests.Add(path); return ejectSucceeds; });

    private string CreateFolder(string name)
    {
        var folder = Path.Combine(_tempRoot, name); Directory.CreateDirectory(folder); return folder;
    }

    private static void WithUiCulture(string cultureName, Action action)
    {
        var original = CultureInfo.CurrentUICulture;
        try { CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(cultureName); action(); }
        finally { CultureInfo.CurrentUICulture = original; }
    }
}
