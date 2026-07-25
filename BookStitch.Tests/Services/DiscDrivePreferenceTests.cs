using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class DiscDrivePreferenceTests
{
    [Fact]
    public void SelectPreferredDrive_ReturnsStoredDrive_WhenItStillExists()
    {
        var drives = new[]
        {
            new DiscDriveInfo(@"G:\", "G:", true, "Audio CD", DiscMediaKind.AudioCd),
            new DiscDriveInfo(@"Y:\", "Y:", true, "Book", DiscMediaKind.Mp3Disc)
        };

        var selected = DiscDriveService.SelectPreferredDrive(drives, @"Y:\");

        Assert.NotNull(selected);
        Assert.Equal(@"Y:\", selected.RootPath);
    }

    [Fact]
    public void SelectPreferredDrive_FallsBackToFirstReadyDrive_WhenStoredDriveIsMissing()
    {
        var drives = new[]
        {
            new DiscDriveInfo(@"G:\", "G:", false, string.Empty, DiscMediaKind.Empty),
            new DiscDriveInfo(@"Y:\", "Y:", true, "Book", DiscMediaKind.Mp3Disc)
        };

        var selected = DiscDriveService.SelectPreferredDrive(drives, @"Z:\");

        Assert.NotNull(selected);
        Assert.Equal(@"Y:\", selected.RootPath);
    }

    [Theory]
    [InlineData(DiscMediaKind.AudioCd, "Audio-CD")]
    [InlineData(DiscMediaKind.Mp3Disc, "MP3-CD")]
    [InlineData(DiscMediaKind.DataDisc, "Daten-CD")]
    [InlineData(DiscMediaKind.Empty, "Laufwerk ist leer")]
    public void StatusText_UsesFriendlyMediaType(DiscMediaKind mediaKind, string expected)
    {
        var drive = new DiscDriveInfo(@"G:\", "G:", mediaKind != DiscMediaKind.Empty, "Test", mediaKind);

        Assert.Equal(expected, drive.StatusText);
    }
    [Fact]
    public void CheckingDrive_UsesImmediateLoadingLabels()
    {
        var drive = new DiscDriveInfo(
            @"G:\",
            "G:",
            false,
            string.Empty,
            DiscMediaKind.Unknown,
            IsChecking: true);

        Assert.Equal("Wird geprüft …", drive.DisplayName);
        Assert.Equal("Datenträger wird gelesen …", drive.StatusText);
    }

}
