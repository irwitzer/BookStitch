using BookStitch.Models;
using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class DiscDriveRotationServiceTests
{
    private readonly DiscDriveRotationService _service = new();

    [Fact]
    public void BuildRound_StartsAfterLastProcessedDriveAndWrapsAround()
    {
        var round = _service.BuildRound(
            [
                Item(@"A:\", 0),
                Item(@"B:\", 1),
                Item(@"C:\", 2)
            ],
            @"B:\");

        Assert.Equal([@"C:\", @"A:\", @"B:\"], round);
    }

    [Fact]
    public void BuildRound_SkipsDisabledAndDisconnectedDrives()
    {
        var round = _service.BuildRound(
            [
                Item(@"A:\", 0),
                Item(@"B:\", 1, enabled: false),
                Item(@"C:\", 2, connected: false),
                Item(@"D:\", 3)
            ],
            @"A:\");

        Assert.Equal([@"D:\", @"A:\"], round);
    }

    [Fact]
    public void BuildRound_UsesConfiguredOrderWhenLastDriveIsUnknown()
    {
        var round = _service.BuildRound(
            [
                Item(@"B:\", 0),
                Item(@"A:\", 1)
            ],
            @"Z:\");

        Assert.Equal([@"B:\", @"A:\"], round);
    }

    private static DiscDriveConfigurationItem Item(string root, int order, bool enabled = true, bool connected = true) =>
        new(
            new ConfiguredDiscDrive
            {
                DriveRoot = root,
                DisplayName = root,
                IsEnabled = enabled,
                Order = order
            },
            connected,
            null);
}
