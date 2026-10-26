using BookStitch.Models;
using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class DiscDriveConfigurationServiceTests
{
    private readonly DiscDriveConfigurationService _service = new();

    [Fact]
    public void Reset_rebuilds_current_devices_while_normal_discovery_keeps_disconnected_devices()
    {
        var settings = new AppSettings { ExperimentalDriveRoundEnabled = true };
        _service.Synchronize(settings, [Drive(@"K:\", "serial:OLD"), Drive(@"J:\", "serial:CURRENT")]);
        settings.DiscDriveOrder[0].UserName = "Previous";
        settings.DiscDriveOrder[0].IsEnabled = false;
        var current = new[] { Drive(@"Y:\", "serial:CURRENT", driveName: "Current optical drive") };
        Assert.Equal(2, _service.Synchronize(settings, current).Count);
        var item = Assert.Single(_service.Reset(settings, current));
        Assert.True(item.IsConnected);
        Assert.True(item.Configuration.IsEnabled);
        Assert.Equal(@"Y:\", item.Configuration.DriveRoot);
        Assert.Equal("Current optical drive", item.Configuration.DisplayName);
        Assert.Empty(item.Configuration.UserName);
        Assert.True(settings.ExperimentalDriveRoundEnabled);
        Assert.Single(_service.Synchronize(settings, current));
        Assert.Empty(_service.Reset(settings, []));
        Assert.Empty(settings.DiscDriveOrder);
    }

    [Fact]
    public void Synchronize_SameStableDeviceAtNewRootReusesConfigurationAndPreferences()
    {
        var settings = new AppSettings
        {
            DiscDriveOrder =
            [
                new ConfiguredDiscDrive
                {
                    StableId = "serial:ABC-123", DriveRoot = @"G:\", UserName = "Extern",
                    IsEnabled = false, Order = 0
                }
            ]
        };

        var result = _service.Synchronize(settings, [Drive(@"H:\", "serial:ABC-123")]);

        var item = Assert.Single(result);
        Assert.True(item.IsConnected);
        Assert.Equal(@"H:\", item.Configuration.DriveRoot);
        Assert.Equal("Extern", item.Configuration.UserName);
        Assert.False(item.Configuration.IsEnabled);
        Assert.Equal(0, item.Configuration.Order);
    }

    [Fact]
    public void Synchronize_LegacyRootOnlyConfigurationMigratesWithoutLosingUserState()
    {
        var settings = new AppSettings
        {
            DiscDriveOrder =
            [
                new ConfiguredDiscDrive
                {
                    DriveRoot = @"G:\", UserName = "Archiv", IsEnabled = false, Order = 0
                }
            ]
        };

        var item = Assert.Single(_service.Synchronize(settings, [Drive(@"G:\", "pnp:USBSTOR\\DRIVE-42", DiscDriveIdentityKind.PnpDeviceInstance)]));

        Assert.Equal("pnp:USBSTOR\\DRIVE-42", item.Configuration.StableId);
        Assert.Equal("Archiv", item.Configuration.UserName);
        Assert.False(item.Configuration.IsEnabled);
        Assert.Equal(0, item.Configuration.Order);
    }

    [Fact]
    public void Synchronize_TwoSameModelDrivesWithDifferentReliableIdsRemainSeparate()
    {
        var settings = new AppSettings();
        var result = _service.Synchronize(settings,
        [
            Drive(@"G:\", "serial:ONE", driveName: "ACME DVD"),
            Drive(@"H:\", "serial:TWO", driveName: "ACME DVD")
        ]);

        Assert.Equal(2, result.Count);
        Assert.Equal(2, result.Select(item => item.Configuration.StableId).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void Synchronize_DifferentReliableDeviceAtReusedRootDoesNotClaimOldPreferences()
    {
        var settings = new AppSettings
        {
            DiscDriveOrder =
            [
                new ConfiguredDiscDrive
                {
                    StableId = "serial:OLD", DriveRoot = @"G:\", UserName = "Mein altes Laufwerk",
                    IsEnabled = false, Order = 0
                }
            ]
        };

        var result = _service.Synchronize(settings, [Drive(@"G:\", "serial:NEW")]);

        Assert.Equal(2, result.Count);
        Assert.False(result[0].IsConnected);
        Assert.Equal(DiscDriveConfigurationService.NormalizeUserName("Mein altes Laufwerk"), result[0].Configuration.UserName);
        Assert.True(result[1].IsConnected);
        Assert.Equal("serial:NEW", result[1].Configuration.StableId);
        Assert.Equal(string.Empty, result[1].Configuration.UserName);
    }

    [Fact]
    public void Synchronize_DuplicateOrWeakIdentityNeverMergesSeparatePhysicalRoots()
    {
        var settings = new AppSettings();
        var result = _service.Synchronize(settings,
        [
            Drive(@"G:\", "fingerprint:SAME", DiscDriveIdentityKind.HardwareFingerprint, "ACME DVD", "SAME"),
            Drive(@"H:\", "fingerprint:SAME", DiscDriveIdentityKind.HardwareFingerprint, "ACME DVD", "SAME")
        ]);

        Assert.Equal(2, result.Count);
        Assert.All(result, item => Assert.StartsWith("local:", item.Configuration.StableId, StringComparison.Ordinal));
        Assert.Equal(2, result.Select(item => item.Configuration.StableId).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void Synchronize_WeakIdentityAtReusedRootDoesNotClaimReliableConfiguration()
    {
        var settings = new AppSettings
        {
            DiscDriveOrder =
            [
                new ConfiguredDiscDrive
                {
                    StableId = "serial:KNOWN", DriveRoot = @"G:\", UserName = "Bekannt",
                    IsEnabled = false, Order = 0
                }
            ]
        };

        var result = _service.Synchronize(settings,
            [Drive(@"G:\", "fingerprint:SAME", DiscDriveIdentityKind.HardwareFingerprint, "ACME DVD", "SAME")]);

        Assert.Equal(2, result.Count);
        Assert.False(result[0].IsConnected);
        Assert.Equal("Bekannt", result[0].Configuration.UserName);
        Assert.True(result[1].IsConnected);
        Assert.StartsWith("local:SAME:", result[1].Configuration.StableId, StringComparison.Ordinal);
    }

    [Fact]
    public void Synchronize_UniqueWeakFingerprintUsesControlledFallbackAtNewRoot()
    {
        var settings = new AppSettings
        {
            DiscDriveOrder =
            [
                new ConfiguredDiscDrive
                {
                    StableId = "local:MODEL-A:saved", DriveRoot = @"G:\", HardwareFingerprint = "MODEL-A",
                    UserName = "Einziges Laufwerk", IsEnabled = false, Order = 0
                }
            ]
        };

        var item = Assert.Single(_service.Synchronize(settings,
            [Drive(@"H:\", "fingerprint:MODEL-A", DiscDriveIdentityKind.HardwareFingerprint, "ACME DVD", "MODEL-A")]));

        Assert.True(item.IsConnected);
        Assert.Equal("local:MODEL-A:saved", item.Configuration.StableId);
        Assert.Equal(@"H:\", item.Configuration.DriveRoot);
        Assert.Equal(DiscDriveConfigurationService.NormalizeUserName("Einziges Laufwerk"), item.Configuration.UserName);
        Assert.False(item.Configuration.IsEnabled);
    }

    [Fact]
    public void Synchronize_DifferentWeakFingerprintAtReusedRootDoesNotClaimOldLocalPreferences()
    {
        var settings = new AppSettings
        {
            DiscDriveOrder =
            [
                new ConfiguredDiscDrive
                {
                    StableId = "local:OLD:saved", DriveRoot = @"G:\", HardwareFingerprint = "OLD",
                    UserName = "Altes Laufwerk", IsEnabled = false, Order = 0
                }
            ]
        };

        var result = _service.Synchronize(settings,
            [Drive(@"G:\", "fingerprint:NEW", DiscDriveIdentityKind.HardwareFingerprint, "OTHER DVD", "NEW")]);

        Assert.Equal(2, result.Count);
        Assert.False(result[0].IsConnected);
        Assert.Equal("Altes Laufwerk", result[0].Configuration.UserName);
        Assert.True(result[1].IsConnected);
        Assert.StartsWith("local:NEW:", result[1].Configuration.StableId, StringComparison.Ordinal);
    }

    [Fact]
    public void Synchronize_MissingDriveRemainsStoredAndDisconnected()
    {
        var settings = new AppSettings
        {
            DiscDriveOrder =
            [
                Configured("serial:A", @"A:\", 0),
                Configured("serial:B", @"B:\", 1)
            ]
        };

        var result = _service.Synchronize(settings, [Drive(@"B:\", "serial:B")]);

        Assert.Equal(2, result.Count);
        Assert.False(result[0].IsConnected);
        Assert.True(result[1].IsConnected);
        Assert.Equal("serial:A", result[0].Configuration.StableId);
    }

    [Fact]
    public void IdentityBasedMutationsPreserveTheSelectedConfigurationAcrossRootChanges()
    {
        var settings = new AppSettings
        {
            DiscDriveOrder =
            [
                Configured("serial:A", @"A:\", 0),
                Configured("serial:B", @"B:\", 1),
                Configured("serial:C", @"C:\", 2)
            ]
        };

        _service.MoveDriveToIndex(settings, "serial:B", 0);
        _service.SetEnabled(settings, "serial:B", false);
        _service.RenameDrive(settings, "serial:B", "  Ripper  ");
        _service.Synchronize(settings, [Drive(@"Y:\", "serial:B")]);

        Assert.Equal(["serial:B", "serial:A", "serial:C"], settings.DiscDriveOrder.Select(item => item.StableId));
        var selected = settings.DiscDriveOrder[0];
        Assert.Equal(@"Y:\", selected.DriveRoot);
        Assert.Equal("Ripper", selected.UserName);
        Assert.False(selected.IsEnabled);
        Assert.Equal([0, 1, 2], settings.DiscDriveOrder.Select(item => item.Order));
    }

    [Fact]
    public void Rename_AllowsEmptyNameAndDoesNotUseDriveRootAsKey()
    {
        var settings = new AppSettings { DiscDriveOrder = [Configured("serial:A", @"G:\", 0)] };

        _service.RenameDrive(settings, @"G:\", "wrong key");
        _service.RenameDrive(settings, "serial:A", "   ");

        Assert.Equal(string.Empty, settings.DiscDriveOrder[0].UserName);
    }

    [Fact]
    public void GetActiveConnectedDrives_ReturnsAtMostFiveInSavedIdentityOrder()
    {
        var settings = new AppSettings
        {
            DiscDriveOrder = Enumerable.Range(0, 7)
                .Select(index => Configured($"serial:{index}", $@"{(char)('A' + index)}:\", index))
                .ToList()
        };
        var drives = Enumerable.Range(0, 7)
            .Select(index => Drive($@"{(char)('A' + index)}:\", $"serial:{index}"))
            .ToList();

        var active = _service.GetActiveConnectedDrives(settings, drives);

        Assert.Equal(5, active.Count);
        Assert.Equal(["serial:0", "serial:1", "serial:2", "serial:3", "serial:4"],
            active.Select(item => item.Configuration.StableId));
    }

    private static ConfiguredDiscDrive Configured(string stableId, string root, int order) => new()
    {
        StableId = stableId, DriveRoot = root, DisplayName = "Drive", IsEnabled = true, Order = order
    };

    [Fact]
    public void RenameDrive_TrimsLimitsAndClearsThePersistedUserName()
    {
        var settings = new AppSettings
        {
            DiscDriveOrder =
            [
                new ConfiguredDiscDrive { StableId = "serial:ONE", DriveRoot = @"G:\\", UserName = "Alt", Order = 0 }
            ]
        };

        _service.RenameDrive(settings, "serial:ONE", "   123456789012345678901234567890   ");
        Assert.Equal("12345678901234567890123", settings.DiscDriveOrder[0].UserName);

        _service.Synchronize(settings, [Drive(@"G:\\", "serial:ONE")]);
        Assert.Equal("12345678901234567890123", settings.DiscDriveOrder[0].UserName);

        _service.RenameDrive(settings, "serial:ONE", "   ");
        Assert.Equal(string.Empty, settings.DiscDriveOrder[0].UserName);
    }

    private static DiscDriveInfo Drive(
        string root,
        string stableId,
        DiscDriveIdentityKind kind = DiscDriveIdentityKind.StorageSerialNumber,
        string driveName = "Testlaufwerk",
        string fingerprint = "") =>
        new(root, root.TrimEnd('\\'), false, string.Empty, DiscMediaKind.Empty, driveName,
            DevicePath: @"\Device\CdRom0", StableId: stableId, IdentityKind: kind,
            StorageSerialNumber: kind == DiscDriveIdentityKind.StorageSerialNumber ? stableId["serial:".Length..] : string.Empty,
            PnpDeviceInstanceId: kind == DiscDriveIdentityKind.PnpDeviceInstance ? stableId["pnp:".Length..] : string.Empty,
            Vendor: "ACME", Product: "DVD", Revision: "1.0", HardwareFingerprint: fingerprint);
}
