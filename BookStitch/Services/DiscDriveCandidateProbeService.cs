namespace BookStitch.Services;

public enum DiscDriveCandidateStatus
{
    Accepted,
    Unavailable,
    Empty,
    WrongType,
    Duplicate,
    Error
}

public sealed record DiscDriveCandidateResult(
    DiscDriveCandidateStatus Status,
    string DriveRoot,
    DiscMediaKind MediaKind,
    string Message = "")
{
    public bool IsAccepted => Status == DiscDriveCandidateStatus.Accepted;
}

public sealed class DiscDriveCandidateProbeService
{
    private readonly DiscDriveService _discDriveService;

    public DiscDriveCandidateProbeService(DiscDriveService discDriveService)
    {
        _discDriveService = discDriveService ?? throw new ArgumentNullException(nameof(discDriveService));
    }

    public DiscDriveCandidateResult ProbeType(string driveRoot, DiscMediaKind expectedKind)
    {
        try
        {
            var drive = _discDriveService.GetDriveInfoForPath(driveRoot);
            if (drive is null)
                return new DiscDriveCandidateResult(DiscDriveCandidateStatus.Unavailable, driveRoot, DiscMediaKind.Unknown, "Laufwerk nicht verbunden.");

            if (!drive.IsReady || drive.MediaKind == DiscMediaKind.Empty)
                return new DiscDriveCandidateResult(DiscDriveCandidateStatus.Empty, drive.RootPath, drive.MediaKind, "Kein Datenträger eingelegt.");

            if (drive.MediaKind != expectedKind)
                return new DiscDriveCandidateResult(DiscDriveCandidateStatus.WrongType, drive.RootPath, drive.MediaKind, "Falscher Datenträgertyp.");

            return new DiscDriveCandidateResult(DiscDriveCandidateStatus.Accepted, drive.RootPath, drive.MediaKind);
        }
        catch (Exception ex)
        {
            return new DiscDriveCandidateResult(DiscDriveCandidateStatus.Error, driveRoot, DiscMediaKind.Unknown, ex.Message);
        }
    }

    public DiscDriveCandidateResult MarkDuplicate(string driveRoot, DiscMediaKind mediaKind) =>
        new(DiscDriveCandidateStatus.Duplicate, driveRoot, mediaKind, "Datenträger wurde bereits importiert.");
}
