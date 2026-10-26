namespace BookStitch.Dialog;

public enum ProjectSetupSourceKind
{
    Folder,
    Mp3Disc,
    AudioDisc
}

public readonly record struct ProjectSetupModeProfile(
    bool ShowsDiscCount,
    bool ShowsProjectFolder)
{
    public bool UsesDiscPresentation => ShowsDiscCount;

    public static ProjectSetupModeProfile FromSourceKind(ProjectSetupSourceKind sourceKind) => sourceKind switch
    {
        ProjectSetupSourceKind.Folder => new(ShowsDiscCount: false, ShowsProjectFolder: true),
        ProjectSetupSourceKind.Mp3Disc => new(ShowsDiscCount: true, ShowsProjectFolder: false),
        ProjectSetupSourceKind.AudioDisc => new(ShowsDiscCount: true, ShowsProjectFolder: false),
        _ => throw new ArgumentOutOfRangeException(nameof(sourceKind), sourceKind, null)
    };
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
    bool UsePrivateGenreList = false,
    string SourceFolder = "",
    int? MaxSourceBitrateKbps = null,
    string LastCoverFolder = "",
    bool ConfirmDiscCountOnStart = false,
    bool IsDiscResume = false,
    bool IsContinuation = false,
    IReadOnlyList<string>? GenreOptions = null);

public sealed record ProjectSetupDialogGlobalSettings(
    bool AutoMergeAfterConversion,
    string OutputExtension,
    string FileNameTemplate,
    string OutputFolder);
