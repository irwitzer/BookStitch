namespace BookStitch.Models;

public sealed record ExportCheckResult(
    List<TrackInfo> TrackSnapshot,
    string OutputPath,
    List<string> Errors,
    List<string> Warnings);
