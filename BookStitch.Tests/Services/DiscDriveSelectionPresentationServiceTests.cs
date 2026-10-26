using BookStitch.Models;
using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class DiscDriveSelectionPresentationServiceTests
{
    [Theory]
    [InlineData(" 1 - Asus ", "H: 1 - Asus")]
    [InlineData("", "H:")]
    [InlineData("   ", "H:")]
    public void FormatStatusDriveLabel_UsesOnlyTheConfiguredUserName(string name, string expected)
    {
        var configured = new[]
        {
            new ConfiguredDiscDrive { DriveRoot = @"H:\", UserName = name, DisplayName = "Hardware model" },
            new ConfiguredDiscDrive { DriveRoot = @"G:\", UserName = "Other drive" }
        };

        Assert.Equal(expected, DiscDriveSelectionPresentationService.FormatStatusDriveLabel(@"h:\", configured));
        Assert.Equal("Z:", DiscDriveSelectionPresentationService.FormatStatusDriveLabel(@"Z:\", configured));
        Assert.Equal(string.Empty, DiscDriveSelectionPresentationService.FormatStatusDriveLabel(null, configured));
    }

    private readonly DiscDriveSelectionPresentationService _service = new();

    [Fact]
    public void Manual_selection_with_DriveCircle_off_can_use_a_drive_disabled_in_the_saved_circle()
    {
        var saved = new[] { new ConfiguredDiscDrive { StableId = "serial:A", DriveRoot = @"V:\", UserName = "Audio", IsEnabled = false } };
        var detected = new[] { Drive(@"V:\", "serial:A") };
        Assert.False(Assert.Single(_service.Create(detected, saved)).IsEnabled);
        var manual = Assert.Single(_service.Create(detected, saved, respectDriveCircleEnabled: false));
        Assert.True(manual.IsEnabled);
        Assert.Equal("Audio", manual.UserName);
        Assert.Equal(manual.DriveInfo.StatusText, manual.StatusText);
        Assert.False(saved[0].IsEnabled);
    }

    [Fact]
    public void Create_UsesStableConfigurationOrderAndProjectsUserNames()
    {
        var configured = new[]
        {
            new ConfiguredDiscDrive { StableId = "serial:B", DriveRoot = @"H:\", UserName = "Zweitlaufwerk", Order = 0 },
            new ConfiguredDiscDrive { StableId = "serial:A", DriveRoot = @"G:\", UserName = "Erstlaufwerk", Order = 1 }
        };
        var detected = new[] { Drive(@"G:\", "serial:A"), Drive(@"H:\", "serial:B") };

        var result = _service.Create(detected, configured);

        Assert.Equal(["serial:B", "serial:A"], result.Select(item => item.DriveInfo.StableId));
        Assert.Equal(["Zweitlaufwerk", "Erstlaufwerk"], result.Select(item => item.UserName));
    }

    [Fact]
    public void Create_AppendsUnconfiguredDetectedDrivesAfterConfiguredEntriesDeterministically()
    {
        var configured = new[]
        {
            new ConfiguredDiscDrive { StableId = "serial:B", DriveRoot = @"H:\", UserName = "Gespeichert", Order = 0 }
        };
        var detected = new[] { Drive(@"I:\", "serial:C"), Drive(@"H:\", "serial:B") };

        var result = _service.Create(detected, configured);

        Assert.Equal("serial:B", result[0].DriveInfo.StableId);
        Assert.Equal("Gespeichert", result[0].UserName);
        Assert.Equal("serial:C", result[1].DriveInfo.StableId);
        Assert.Equal(string.Empty, result[1].UserName);
    }

    [Fact]
    public void Create_MatchesMovedConfiguredDriveByStableIdentityInsteadOfRoot()
    {
        var configured = new[]
        {
            new ConfiguredDiscDrive { StableId = "serial:A", DriveRoot = @"G:\", UserName = "Mobil", Order = 0 }
        };

        var item = Assert.Single(_service.Create([Drive(@"J:\", "serial:A")], configured));

        Assert.Equal(@"J:\", item.DriveRoot);
        Assert.Equal("Mobil", item.UserName);
    }


    [Fact]
    public void Create_KeepsDisabledConfiguredDriveVisibleInConfiguredOrderAndSelectPreferredSkipsIt()
    {
        var configured = new[]
        {
            new ConfiguredDiscDrive { StableId = "serial:B", DriveRoot = @"H:\", UserName = "Aus", Order = 0, IsEnabled = false },
            new ConfiguredDiscDrive { StableId = "serial:A", DriveRoot = @"G:\", UserName = "An", Order = 1, IsEnabled = true }
        };
        var detected = new[] { Drive(@"G:\", "serial:A"), Drive(@"H:\", "serial:B") };

        var result = _service.Create(detected, configured);

        Assert.Equal(["serial:B", "serial:A"], result.Select(item => item.DriveInfo.StableId));
        Assert.False(result[0].IsEnabled);
        Assert.Equal("In Drive Circle deaktiviert", result[0].StatusText);
        Assert.True(result[1].IsEnabled);
        Assert.Same(result[1], DiscDriveSelectionPresentationService.SelectPreferred(result, @"H:\"));
    }

    [Fact]
    public void Create_UsesSavedPresentationForUnprobedShellsWithoutChangingHardwareIdentity()
    {
        var configured = new[]
        {
            new ConfiguredDiscDrive { StableId = "serial:B", DriveRoot = @"H:\", UserName = "Aus", Order = 0, IsEnabled = false },
            new ConfiguredDiscDrive { StableId = "serial:A", DriveRoot = @"G:\", UserName = "An", Order = 1, IsEnabled = true }
        };
        var shells = new[]
        {
            new DiscDriveInfo(@"G:\", "G:", false, string.Empty),
            new DiscDriveInfo(@"H:\", "H:", false, string.Empty)
        };

        var beforeProbe = _service.Create(shells, configured);

        Assert.Equal([@"H:\", @"G:\"], beforeProbe.Select(item => item.DriveRoot));
        Assert.Equal(["Aus", "An"], beforeProbe.Select(item => item.UserName));
        Assert.False(beforeProbe[0].IsEnabled);
        Assert.Equal(@"G:\", DiscDriveSelectionPresentationService.SelectPreferred(beforeProbe, @"H:\")?.DriveRoot);
        Assert.Equal(["serial:B", "serial:A"], configured.Select(item => item.StableId));

        var afterProbe = _service.Create([Drive(@"G:\", "serial:A"), Drive(@"H:\", "serial:B")], configured);
        Assert.Equal(beforeProbe.Select(item => item.DriveRoot), afterProbe.Select(item => item.DriveRoot));
        Assert.Equal(beforeProbe.Select(item => item.IsEnabled), afterProbe.Select(item => item.IsEnabled));
    }

    private static DiscDriveInfo Drive(string root, string stableId) =>
        new(root, root.TrimEnd('\\'), false, string.Empty, DiscMediaKind.Empty, "Optical",
            StableId: stableId, IdentityKind: DiscDriveIdentityKind.StorageSerialNumber,
            StorageSerialNumber: stableId["serial:".Length..], Vendor: "ACME", Product: "DVD");
}
