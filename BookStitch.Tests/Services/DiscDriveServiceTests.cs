using BookStitch.Services;
using BookStitch.Tests.TestHelpers;
using Xunit;

namespace BookStitch.Tests.Services;

public class DiscDriveServiceTests
{
    private readonly DiscDriveService _service = new();

    [Fact]
    public void ResolveResumeDiscSource_ReturnsFirstExistingCandidateOutsideProjectFolder()
    {
        using var folder = new TemporaryFolder();
        var projectFolder = Path.Combine(folder.Path, "Project");
        var firstSource = Path.Combine(folder.Path, "DiscSource1");
        var secondSource = Path.Combine(folder.Path, "DiscSource2");
        Directory.CreateDirectory(projectFolder);
        Directory.CreateDirectory(firstSource);
        Directory.CreateDirectory(secondSource);

        var result = _service.ResolveResumeDiscSource(
            projectFolder,
            "  ",
            firstSource + "  ",
            secondSource);

        Assert.Equal(firstSource, result);
    }

    [Fact]
    public void ResolveResumeDiscSource_SkipsProjectFolderAndItsChildren()
    {
        using var folder = new TemporaryFolder();
        var projectFolder = Path.Combine(folder.Path, "Project");
        var projectChild = Path.Combine(projectFolder, "CD 01");
        var validSource = Path.Combine(folder.Path, "DiscSource");
        Directory.CreateDirectory(projectChild);
        Directory.CreateDirectory(validSource);

        var result = _service.ResolveResumeDiscSource(
            projectFolder,
            projectFolder,
            projectChild,
            validSource);

        Assert.Equal(validSource, result);
    }

    [Fact]
    public void ResolveResumeDiscSource_WhenNoCandidateIsValid_ReturnsEmptyString()
    {
        using var folder = new TemporaryFolder();
        var projectFolder = Path.Combine(folder.Path, "Project");
        Directory.CreateDirectory(projectFolder);

        var result = _service.ResolveResumeDiscSource(
            projectFolder,
            null,
            "",
            Path.Combine(folder.Path, "Missing"));

        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void DiscDriveInfo_WithVolumeLabel_UsesLabelAsDisplayName()
    {
        var drive = new DiscDriveInfo(@"Y:\", "Y:", true, "Kummer aller Art");

        Assert.Equal("Kummer aller Art", drive.DisplayName);
        Assert.Equal("Bereit", drive.StatusText);
    }

    [Fact]
    public void DiscDriveInfo_WhenEmpty_ReportsEmptyDrive()
    {
        var drive = new DiscDriveInfo(@"G:\", "G:", false, string.Empty);

        Assert.Equal("Kein Datenträger eingelegt", drive.DisplayName);
        Assert.Equal("Laufwerk ist leer", drive.StatusText);
    }
}
