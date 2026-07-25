using BookStitch.Models;
using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class DiscDriveConfigurationServiceTests
{
    private readonly DiscDriveConfigurationService _service = new();

    [Fact]
    public void Synchronize_KeepsMissingDriveButMarksItDisconnected()
    {
        var settings = new AppSettings
        {
            DiscDriveOrder =
            [
                new ConfiguredDiscDrive { DriveRoot = @"A:\", DisplayName = "A", IsEnabled = true, Order = 0 },
                new ConfiguredDiscDrive { DriveRoot = @"B:\", DisplayName = "B", IsEnabled = true, Order = 1 }
            ]
        };

        var result = _service.Synchronize(settings, [Drive(@"B:\")], new DateTime(2026, 7, 25, 12, 0, 0, DateTimeKind.Utc));

        Assert.Equal(2, result.Count);
        Assert.False(result[0].IsConnected);
        Assert.True(result[1].IsConnected);
        Assert.Equal(@"A:\", result[0].Configuration.DriveRoot);
        Assert.Equal(@"B:\", result[1].Configuration.DriveRoot);
    }

    [Fact]
    public void Synchronize_AppendsNewDriveAtEnd()
    {
        var settings = new AppSettings
        {
            DiscDriveOrder =
            [
                new ConfiguredDiscDrive { DriveRoot = @"B:\", DisplayName = "B", IsEnabled = true, Order = 0 }
            ]
        };

        var result = _service.Synchronize(settings, [Drive(@"B:\"), Drive(@"C:\")]);

        Assert.Equal([@"B:\", @"C:\"], result.Select(item => item.Configuration.DriveRoot).ToArray());
        Assert.Equal([0, 1], result.Select(item => item.Configuration.Order).ToArray());
    }

    [Fact]
    public void GetActiveConnectedDrives_ReturnsAtMostFiveEnabledConnectedDrives()
    {
        var settings = new AppSettings
        {
            DiscDriveOrder = Enumerable.Range(0, 7)
                .Select(index => new ConfiguredDiscDrive
                {
                    DriveRoot = $@"{(char)('A' + index)}:\",
                    DisplayName = "Drive",
                    IsEnabled = true,
                    Order = index
                })
                .ToList()
        };
        var drives = Enumerable.Range(0, 7)
            .Select(index => Drive($@"{(char)('A' + index)}:\"))
            .ToList();

        var active = _service.GetActiveConnectedDrives(settings, drives);

        Assert.Equal(5, active.Count);
        Assert.Equal([@"A:\", @"B:\", @"C:\", @"D:\", @"E:\"], active.Select(item => item.Configuration.DriveRoot).ToArray());
    }

    private static DiscDriveInfo Drive(string root) =>
        new(root, root.TrimEnd('\\'), IsReady: false, VolumeLabel: string.Empty, MediaKind: DiscMediaKind.Empty, DriveName: "Testlaufwerk");
}
