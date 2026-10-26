using BookStitch.Dialog;

namespace BookStitch.Services;

public sealed record CoverPickerContext(
    ProjectSetupSourceKind SourceKind,
    string? SourceFolder,
    string? ExistingCoverSourcePath,
    string? LegacyFallbackFolder);
