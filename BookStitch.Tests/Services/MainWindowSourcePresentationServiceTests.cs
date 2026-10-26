using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class MainWindowSourcePresentationServiceTests
{
    [Fact]
    public void Create_WithoutTracks_HidesStaleSourceAndUsesPrimaryLaunchStyle()
    {
        var result = MainWindowSourcePresentationService.Create(
            0,
            @"C:\OldProject",
            "Old display",
            @"C:\OldSource",
            selectedFolderIsCdDrive: false);

        Assert.False(result.HasProjectTracks);
        Assert.Equal("PrimaryButtonStyle", result.LaunchButtonStyleResourceKey);
        Assert.Empty(result.DisplayText);
        Assert.Empty(result.OpenPath);
    }

    [Fact]
    public void Create_WithFolderProject_UsesFolderNameAndSecondaryLaunchStyle()
    {
        var result = MainWindowSourcePresentationService.Create(
            2,
            @"C:\Audiobooks\Example Book",
            displayOverride: null,
            openPathOverride: null,
            selectedFolderIsCdDrive: false);

        Assert.True(result.HasProjectTracks);
        Assert.Equal("SecondaryButtonStyle", result.LaunchButtonStyleResourceKey);
        Assert.Equal("Example Book", result.DisplayText);
        Assert.Equal(@"C:\Audiobooks\Example Book", result.OpenPath);
    }

    [Fact]
    public void Create_WithExplicitPresentation_UsesDisplayAndOpenPathOverrides()
    {
        var result = MainWindowSourcePresentationService.Create(
            1,
            @"C:\Project",
            "MP3-CD 2",
            @"D:\ImportedDisc",
            selectedFolderIsCdDrive: false);

        Assert.Equal("MP3-CD 2", result.DisplayText);
        Assert.Equal(@"D:\ImportedDisc", result.OpenPath);
    }

    [Fact]
    public void Create_WithCdDrive_UsesReadableDriveLabel()
    {
        var result = MainWindowSourcePresentationService.Create(
            1,
            @"G:\",
            displayOverride: null,
            openPathOverride: null,
            selectedFolderIsCdDrive: true);

        Assert.Equal("CD-Laufwerk G", result.DisplayText);
        Assert.Equal(@"G:\", result.OpenPath);
    }
}
