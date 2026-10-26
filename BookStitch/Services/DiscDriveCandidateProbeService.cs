namespace BookStitch.Services;

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
                return new DiscDriveCandidateResult(DiscDriveCandidateStatus.Unavailable, driveRoot, DiscMediaKind.Unknown, LocalizationText.Get("Disc.Probe.Disconnected"));

            if (!drive.IsReady || drive.MediaKind == DiscMediaKind.Empty)
                return new DiscDriveCandidateResult(DiscDriveCandidateStatus.Empty, drive.RootPath, drive.MediaKind, LocalizationText.Get("Disc.Probe.Empty"));

            if (!CanAnalyzeMediaKind(drive.MediaKind, expectedKind))
                return new DiscDriveCandidateResult(DiscDriveCandidateStatus.WrongType, drive.RootPath, drive.MediaKind, LocalizationText.Get("Disc.Probe.WrongType"));

            return new DiscDriveCandidateResult(DiscDriveCandidateStatus.Accepted, drive.RootPath, drive.MediaKind);
        }
        catch (Exception ex)
        {
            return new DiscDriveCandidateResult(DiscDriveCandidateStatus.Error, driveRoot, DiscMediaKind.Unknown, ex.Message);
        }
    }


    public async Task<DiscDriveCandidateResult> ProbeTypeAsync(
        string driveRoot,
        DiscMediaKind expectedKind,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var drive = await _discDriveService.ProbeDriveAsync(
                driveRoot,
                operationName: "candidate.probe",
                cancellationToken).ConfigureAwait(false);
            if (drive is null)
                return new DiscDriveCandidateResult(DiscDriveCandidateStatus.Unavailable, driveRoot, DiscMediaKind.Unknown, LocalizationText.Get("Disc.Probe.Disconnected"));

            if (!drive.IsReady || drive.MediaKind == DiscMediaKind.Empty)
                return new DiscDriveCandidateResult(DiscDriveCandidateStatus.Empty, drive.RootPath, drive.MediaKind, LocalizationText.Get("Disc.Probe.Empty"));

            if (!CanAnalyzeMediaKind(drive.MediaKind, expectedKind))
                return new DiscDriveCandidateResult(DiscDriveCandidateStatus.WrongType, drive.RootPath, drive.MediaKind, LocalizationText.Get("Disc.Probe.WrongType"));

            return new DiscDriveCandidateResult(DiscDriveCandidateStatus.Accepted, drive.RootPath, drive.MediaKind);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new DiscDriveCandidateResult(DiscDriveCandidateStatus.Error, driveRoot, DiscMediaKind.Unknown, ex.Message);
        }
    }

    internal static bool CanAnalyzeMediaKind(DiscMediaKind actualKind, DiscMediaKind expectedKind) =>
        actualKind == expectedKind || (expectedKind == DiscMediaKind.Mp3Disc && actualKind == DiscMediaKind.Unknown);

    public DiscDriveCandidateResult MarkDuplicate(string driveRoot, DiscMediaKind mediaKind) =>
        new(DiscDriveCandidateStatus.Duplicate, driveRoot, mediaKind, LocalizationText.Get("Disc.Probe.Duplicate"));
}
