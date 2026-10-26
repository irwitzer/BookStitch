namespace BookStitch.Services;

public sealed record AudioDiscLiveConversionSessionSnapshot(
    int AcceptedCount,
    int CompletedCount,
    int FailedCount,
    int CanceledCount,
    int DuplicateCount,
    int ReusedCount,
    IReadOnlyList<int>? ActiveTrackNumbers = null,
    int ExistingConvertedCount = 0)
{
    public int ConvertedCount => ExistingConvertedCount > 0
        ? ExistingConvertedCount + CompletedCount
        : CompletedCount + ReusedCount;
    public int FinishedCount => CompletedCount + ReusedCount + FailedCount + CanceledCount;
    public int PendingCount => Math.Max(0, AcceptedCount - FinishedCount);
}
