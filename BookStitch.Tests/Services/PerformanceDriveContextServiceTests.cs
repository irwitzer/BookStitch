using BookStitch.Models;
using BookStitch.Services;
using System;
using System.IO;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class PerformanceDriveContextServiceTests
{
    private readonly PerformanceDriveContextService _service = new();

    [Fact]
    public void Capture_MergesRolesForSameDriveAndUsesStableDriveId()
    {
        var tempPath = Path.GetTempPath();
        var nestedPath = Path.Combine(tempPath, "BookStitch", "Nested");

        var snapshots = _service.Capture(
        [
            new PerformanceDrivePathRole(tempPath, PerformanceDriveRole.Source),
            new PerformanceDrivePathRole(
                nestedPath,
                PerformanceDriveRole.ProjectWorking | PerformanceDriveRole.Output)
        ]);

        var snapshot = Assert.Single(snapshots);
        Assert.Equal(
            PerformanceDriveRole.Source |
            PerformanceDriveRole.ProjectWorking |
            PerformanceDriveRole.Output,
            snapshot.Roles);
        Assert.Equal(
            PerformanceDriveContextService.CreateDriveIdForPath(tempPath),
            snapshot.DriveId);
        Assert.Equal(
            DiscDriveConfigurationService.NormalizeRootPath(tempPath),
            snapshot.DriveRoot);
    }

    [Fact]
    public void CreateDriveIdForPath_NestedPathsOnSameRootAreEqual()
    {
        var root = Path.GetPathRoot(Path.GetTempPath());
        Assert.False(string.IsNullOrWhiteSpace(root));

        var nestedPath = Path.Combine(root!, "Folder", "file.tmp");
        var rootId = PerformanceDriveContextService.CreateDriveIdForPath(root);
        var nestedId = PerformanceDriveContextService.CreateDriveIdForPath(nestedPath);

        Assert.NotEqual(Guid.Empty, rootId);
        Assert.Equal(rootId, nestedId);
    }

    [Fact]
    public void Capture_UsesExistingOpticalDriveAndConfigurationContext()
    {
        var settings = new AppSettings
        {
            DiscDriveOrder =
            [
                new ConfiguredDiscDrive
                {
                    DriveRoot = @"Q:\",
                    DisplayName = @"Q:\ ASUS DRW-24D5MT",
                    DevicePath = @"\Device\CdRom0",
                    IsEnabled = true,
                    Order = 0
                }
            ]
        };

        var opticalDrive = new DiscDriveInfo(
            @"Q:\",
            "Q:",
            IsReady: true,
            VolumeLabel: "AUDIO_CD",
            MediaKind: DiscMediaKind.AudioCd,
            DriveName: "ASUS DRW-24D5MT",
            DevicePath: @"\Device\CdRom0");

        var snapshots = _service.Capture(
            [new PerformanceDrivePathRole(@"Q:\", PerformanceDriveRole.Source)],
            settings,
            [opticalDrive]);

        var snapshot = Assert.Single(snapshots);
        Assert.Equal(
            PerformanceDriveRole.Source | PerformanceDriveRole.OpticalSource,
            snapshot.Roles);
        Assert.Equal("CDRom", snapshot.DriveType);
        Assert.Equal("AUDIO_CD", snapshot.VolumeLabel);
        Assert.Equal("ASUS DRW-24D5MT", snapshot.DeviceModel);
        Assert.Equal("AudioCd", snapshot.MediaType);
        Assert.Equal(@"Q:\ ASUS DRW-24D5MT", snapshot.OpticalDriveDisplayName);
        Assert.Null(snapshot.UserNickname);
    }

    [Fact]
    public void Capture_ConfiguredDisconnectedOpticalDriveIsStillRecognized()
    {
        var settings = new AppSettings
        {
            DiscDriveOrder =
            [
                new ConfiguredDiscDrive
                {
                    DriveRoot = @"R:\",
                    DisplayName = @"R:\ Optisches Testlaufwerk",
                    DevicePath = @"\Device\CdRom9",
                    IsEnabled = true,
                    Order = 0
                }
            ]
        };

        var snapshots = _service.Capture(
            [new PerformanceDrivePathRole(@"R:\", PerformanceDriveRole.Source)],
            settings,
            []);

        var snapshot = Assert.Single(snapshots);
        Assert.Equal(
            PerformanceDriveRole.Source | PerformanceDriveRole.OpticalSource,
            snapshot.Roles);
        Assert.Equal("CDRom", snapshot.DriveType);
        Assert.Equal(@"R:\ Optisches Testlaufwerk", snapshot.OpticalDriveDisplayName);
        Assert.Null(snapshot.UserNickname);
    }

    [Fact]
    public void Capture_IgnoresEmptyPathsAndNoneRoles()
    {
        var snapshots = _service.Capture(
        [
            new PerformanceDrivePathRole(null, PerformanceDriveRole.Source),
            new PerformanceDrivePathRole("", PerformanceDriveRole.Output),
            new PerformanceDrivePathRole(Path.GetTempPath(), PerformanceDriveRole.None)
        ]);

        Assert.Empty(snapshots);
    }

    [Fact]
    public void Capture_UnavailableDriveReturnsBestEffortRootAndRoles()
    {
        var sourcePath = @"Z:\BookStitch\Unavailable";

        var snapshots = _service.Capture(
            [new PerformanceDrivePathRole(sourcePath, PerformanceDriveRole.ProjectWorking)]);

        var snapshot = Assert.Single(snapshots);
        Assert.Equal(
            DiscDriveConfigurationService.NormalizeRootPath(sourcePath),
            snapshot.DriveRoot);
        Assert.Equal(PerformanceDriveRole.ProjectWorking, snapshot.Roles);
        Assert.NotEqual(Guid.Empty, snapshot.DriveId);
    }
}
