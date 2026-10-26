using BookStitch.Models;

namespace BookStitch.Services;

public sealed record DiscDriveConfigurationItem(
    ConfiguredDiscDrive Configuration,
    bool IsConnected,
    DiscDriveInfo? DriveInfo);
