namespace BookStitch.Services;

public enum DiscDriveIdentityKind
{
    Unavailable,
    StorageSerialNumber,
    PnpDeviceInstance,
    HardwareFingerprint
}

public sealed record DiscDriveInfo(
    string RootPath,
    string DriveLetter,
    bool IsReady,
    string VolumeLabel,
    DiscMediaKind MediaKind = DiscMediaKind.Unknown,
    string DriveName = "",
    string DevicePath = "",
    bool IsChecking = false,
    string StableId = "",
    DiscDriveIdentityKind IdentityKind = DiscDriveIdentityKind.Unavailable,
    string StorageSerialNumber = "",
    string PnpDeviceInstanceId = "",
    string Vendor = "",
    string Product = "",
    string Revision = "",
    string HardwareFingerprint = "")
{
    public string DisplayName => IsChecking
        ? LocalizationText.Get("Disc.Drive.Checking")
        : IsReady
            ? string.IsNullOrWhiteSpace(VolumeLabel) ? LocalizationText.Get("Disc.Drive.MediaInserted") : VolumeLabel
            : LocalizationText.Get("Disc.Drive.NoMediaInserted");

    public string StatusText => IsChecking
        ? LocalizationText.Get("Disc.Drive.ReadingMedia")
        : MediaKind switch
        {
            DiscMediaKind.Empty => LocalizationText.Get("Disc.Drive.Empty"),
            DiscMediaKind.AudioCd => "Audio-CD",
            DiscMediaKind.Mp3Disc => "MP3-CD",
            DiscMediaKind.DataDisc => LocalizationText.Get("Disc.Drive.DataDisc"),
            _ => IsReady ? LocalizationText.Get("Disc.Drive.Ready") : LocalizationText.Get("Disc.Drive.Empty")
        };

    public string DiagnosticDriveName => string.IsNullOrWhiteSpace(DriveName)
        ? LocalizationText.Get("Disc.Drive.OpticalDrive")
        : DriveName;
}
