namespace BookStitch.Models;

public sealed class ProjectResumePlan
{
    public string ProjectFolder { get; set; } = "";
    public string ProjectType { get; set; } = ProjectManifestTypes.FolderProject;
    public string DisplayName { get; set; } = "";
    public string Status { get; set; } = "";
    public bool CanResume { get; set; }
    public bool CanEditTrackOrder { get; set; }
    public bool CanContinueDiscImport { get; set; }
    public bool HasSuccessfulExport { get; set; }
    public int? NextMissingDiscNumber { get; set; }
    public int TotalDiscs { get; set; }
    public int ImportedDiscCount { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }
    public string SourceFolder { get; set; } = "";
    public string OutputFolder { get; set; } = "";
    public string OutputFileName { get; set; } = "";
    public string OutputExtension { get; set; } = "";
    public string FileNameTemplate { get; set; } = "";
    public string ParallelJobs { get; set; } = "";
    public string SelectedPreset { get; set; } = "";
    public string BookTitle { get; set; } = "";
    public string Author { get; set; } = "";
    public string Album { get; set; } = "";
    public string Narrator { get; set; } = "";
    public string Genre { get; set; } = "";
    public string CoverSourcePath { get; set; } = "";
    public string ProcessedCoverPath { get; set; } = "";
    public List<ProjectResumeTrackItem> Tracks { get; set; } = [];
}

public sealed class ProjectResumeTrackItem
{
    public int TrackIndex { get; set; }
    public int? DiscNumber { get; set; }
    public int? TrackNumber { get; set; }
    public string SourcePath { get; set; } = "";
    public string SourceFileName { get; set; } = "";
    public string RelativeFolder { get; set; } = "";
    public string ChapterTitle { get; set; } = "";
    public string Duration { get; set; } = "";
    public long? DurationTicks { get; set; }
    public string Action { get; set; } = "";
    public string Preset { get; set; } = "";
    public string Status { get; set; } = "";
    public string ConvertedPath { get; set; } = "";
}
