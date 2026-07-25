namespace BookStitch.Models;

public sealed record ExportPlan(
    IReadOnlyList<TrackInfo> TrackSnapshot,
    ExportPreset Preset,
    TimeSpan TotalDuration,
    long TotalTicks,
    int ParallelConversions,
    string ProjectType,
    string SourceFolder,
    string WorkingRootFolder,
    string ProjectWorkFolder,
    string PresetFolder,
    string ConvertedFolder,
    string MergeFolder,
    string ConcatListPath,
    string ChapterMetadataPath,
    string FinalOutputPath,
    string FinalOutputFolder,
    string FinalOutputFileName,
    string FinalPartPath,
    string ManifestPath);
