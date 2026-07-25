namespace BookStitch.Models;

public sealed record ExportPlanRequest(
    IReadOnlyList<TrackInfo> TrackSnapshot,
    string SourceFolder,
    string WorkingRootFolder,
    string FinalOutputPath,
    string SelectedExportPreset,
    int ParallelConversions,
    string? ProjectWorkFolderOverride = null,
    string ProjectType = ProjectManifestTypes.FolderProject,
    string? Author = null,
    string? BookTitle = null);
