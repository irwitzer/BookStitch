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
