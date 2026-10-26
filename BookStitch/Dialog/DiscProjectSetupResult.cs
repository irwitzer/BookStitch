namespace BookStitch.Dialog;

public sealed record DiscProjectSetupResult(
    int TotalDiscs,
    string SelectedExportPreset,
    string ParallelJobs,
    string OutputExtension,
    string OutputFolder,
    string BookTitle,
    string Album,
    string Author,
    string Narrator,
    string Genre,
    string FileNameTemplate,
    string CoverSourcePath,
    string ProcessedCoverPath,
    bool AutoMergeAfterConversion);
