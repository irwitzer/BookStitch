namespace BookStitch.Dialog;

public enum ProjectSetupSourceKind
{
    Folder,
    Mp3Disc,
    AudioDisc
}

public sealed record ProjectSetupDialogRequest(
    ProjectSetupSourceKind SourceKind,
    string WindowTitle,
    string SourceInformation,
    string Instruction,
    int DefaultDiscCount,
    int MinimumDiscs,
    int MaximumDiscs,
    IReadOnlyList<string> ExportPresets,
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
    string CoverPreviewSource,
    string CoverWorkFolder,
    bool AutoMergeAfterConversion,
    bool KeepAlbumLinkedToTitle,
    bool UsePrivateGenreList = false,
    string SourceFolder = "",
    int? MaxSourceBitrateKbps = null,
    string LastCoverFolder = "");

public sealed record ProjectSetupDialogGlobalSettings(
    bool AutoMergeAfterConversion,
    bool KeepAlbumLinkedToTitle,
    string OutputExtension,
    string FileNameTemplate);
