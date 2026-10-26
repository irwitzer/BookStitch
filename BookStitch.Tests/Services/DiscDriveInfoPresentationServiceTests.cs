using BookStitch.Models;
using BookStitch.Services;
using System.Globalization;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class DiscDriveInfoPresentationServiceTests
{
    [Fact]
    public void Saved_configuration_waits_for_diagnosis_without_claiming_a_connection_or_failed_detection()
    {
        var config = new ConfiguredDiscDrive { DriveRoot = @"G:\", UserName = "Archive" };
        var result = DiscDriveInfoPresentationService.Create(config, null, false, connectionVerified: false);
        Assert.Contains(LocalizationText.Get("Disc.Drive.Checking"), result.Body);
        Assert.DoesNotContain(LocalizationText.Get("Disc.Drive.Disconnected"), result.Body);
        Assert.DoesNotContain(LocalizationText.Get("Disc.Drive.NoFurtherInformation"), result.Body);
    }

    [Fact]
    public void Create_UsesSpanishPresentationWithoutComparingLocalizedLabels()
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("es-ES");
            var result = DiscDriveInfoPresentationService.Create(
                new ConfiguredDiscDrive { DriveRoot = @"G:\" }, null, isConnected: false);

            Assert.Contains("No se pudo obtener más información", result.Body);
            Assert.Contains("Estado: Desconectado", result.Body);
            Assert.DoesNotContain("Gerät", result.Body);
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    [Fact]
    public void Create_ProjectsOnlyReadableDeviceInformationAndCurrentStatus()
    {
        var configuration = new ConfiguredDiscDrive
        {
            StableId = "serial:SECRET",
            DriveRoot = @"H:\",
            DevicePath = @"\\Device\\CdRom0",
            StorageSerialNumber = "SECRET",
            PnpDeviceInstanceId = "PCI\\SECRET",
            HardwareFingerprint = "SECRET-FP",
            Vendor = "LG",
            Product = "GP57EB40",
            Revision = "1.00"
        };
        var drive = new DiscDriveInfo(@"H:\", "H:", false, string.Empty, DiscMediaKind.Empty,
            "LG GP57EB40", DevicePath: configuration.DevicePath, StableId: configuration.StableId,
            StorageSerialNumber: configuration.StorageSerialNumber, PnpDeviceInstanceId: configuration.PnpDeviceInstanceId,
            Vendor: configuration.Vendor, Product: configuration.Product, Revision: configuration.Revision,
            HardwareFingerprint: configuration.HardwareFingerprint);

        var result = DiscDriveInfoPresentationService.Create(configuration, drive, isConnected: true);

        Assert.Equal("Laufwerk H:", result.Title);
        Assert.Contains("Hersteller: LG", result.Body, StringComparison.Ordinal);
        Assert.Contains("Modell: GP57EB40", result.Body, StringComparison.Ordinal);
        Assert.Contains("Revision: 1.00", result.Body, StringComparison.Ordinal);
        Assert.Contains("Status: Kein Datenträger eingelegt", result.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("SECRET", result.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("Device", result.Body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Create_UsesStableFallbackWhenNoReadableHardwareInformationExists()
    {
        var configuration = new ConfiguredDiscDrive { DriveRoot = @"G:\" };
        var result = DiscDriveInfoPresentationService.Create(configuration, driveInfo: null, isConnected: false);

        Assert.Contains("Weitere Geräteinformationen konnten nicht erkannt werden.", result.Body, StringComparison.Ordinal);
        Assert.Contains("Status: Nicht verbunden", result.Body, StringComparison.Ordinal);
    }
}
