using BookStitch.Models;
using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class DiscDriveConfigurationServiceV1AutomationTests
{
    private readonly DiscDriveConfigurationService _service = new();

    [Fact]
    public void Synchronize_KeepsMissingConfiguredDriveAndAppendsNewDrive()
    {
        var settings = new AppSettings
        {
            DiscDriveOrder =
            [
                Configured(@"C:\", 0),
                Configured(@"E:\", 1)
            ]
        };

        var items = _service.Synchronize(
            settings,
            [Drive(@"D:\"), Drive(@"C:\")],
            new DateTime(2026, 7, 25, 12, 0, 0, DateTimeKind.Utc));

        Assert.Equal([@"C:\", @"E:\", @"D:\"], settings.DiscDriveOrder.Select(item => item.DriveRoot).ToArray());
        Assert.Equal([true, false, true], items.Select(item => item.IsConnected).ToArray());
        Assert.Equal([0, 1, 2], settings.DiscDriveOrder.Select(item => item.Order).ToArray());
    }

    [Fact]
    public void GetActiveConnectedDrives_UsesOnlyFirstFiveEnabledConnectedDrives()
    {
        var settings = new AppSettings
        {
            DiscDriveOrder = Enumerable.Range(0, 7)
                .Select(index => Configured($@"{(char)('A' + index)}:\", index))
                .ToList()
        };
        var detected = settings.DiscDriveOrder
            .Select(item => Drive(item.DriveRoot))
            .ToList();

        var active = _service.GetActiveConnectedDrives(settings, detected);

        Assert.Equal(DiscDriveConfigurationService.MaximumActiveDrives, active.Count);
        Assert.Equal([@"A:\", @"B:\", @"C:\", @"D:\", @"E:\"], active.Select(item => item.Configuration.DriveRoot).ToArray());
    }

    [Fact]
    public void MoveDrive_SwapsWithNeighborAndRenumbersOrder()
    {
        var settings = new AppSettings
        {
            DiscDriveOrder =
            [
                Configured(@"A:\", 0),
                Configured(@"B:\", 1),
                Configured(@"C:\", 2)
            ]
        };

        _service.MoveDrive(settings, @"B:\", direction: 1);

        Assert.Equal([@"A:\", @"C:\", @"B:\"], settings.DiscDriveOrder.Select(item => item.DriveRoot).ToArray());
        Assert.Equal([0, 1, 2], settings.DiscDriveOrder.Select(item => item.Order).ToArray());
    }

    [Fact]
    public void SetEnabled_ChangesOnlySelectedDrive()
    {
        var settings = new AppSettings
        {
            DiscDriveOrder =
            [
                Configured(@"A:\", 0),
                Configured(@"B:\", 1)
            ]
        };

        _service.SetEnabled(settings, @"B:\", isEnabled: false);

        Assert.True(settings.DiscDriveOrder[0].IsEnabled);
        Assert.False(settings.DiscDriveOrder[1].IsEnabled);
    }

    private static ConfiguredDiscDrive Configured(string root, int order) => new()
    {
        DriveRoot = root,
        DisplayName = root,
        IsEnabled = true,
        Order = order
    };

    private static DiscDriveInfo Drive(string root) => new(
        root,
        root.TrimEnd('\\'),
        IsReady: true,
        VolumeLabel: "Disc",
        MediaKind: DiscMediaKind.Mp3Disc,
        DriveName: "Testlaufwerk");
}
