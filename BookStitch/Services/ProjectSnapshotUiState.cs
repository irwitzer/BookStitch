namespace BookStitch.Services;

public sealed record ProjectSnapshotUiState(
    string SelectedExportPreset,
    string ParallelJobs,
    string OutputExtension,
    string OutputFolder,
    string FileNameTemplate,
    string BookTitle,
    string Author,
    string Album,
    string Narrator,
    string Genre,
    string CoverSourcePath,
    string ProcessedCoverPath,
    string OutputFileNamePreview);
