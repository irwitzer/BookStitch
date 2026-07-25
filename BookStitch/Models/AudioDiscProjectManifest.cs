namespace BookStitch.Models;

public static class AudioDiscProjectManifestVersions
{
    public const string Current = "4";
}

public static class AudioDiscRawReadAddressingVersions
{
    public const int Current = 1;
}

public static class AudioDiscProjectStatus
{
    public const string AwaitingRip = "AwaitingRip";
    public const string Ripping = "Ripping";
    public const string WaitingForDisc = "WaitingForDisc";
    public const string RippingCompleted = "RippingCompleted";
    public const string Canceled = "Canceled";
    public const string Failed = "Failed";
}

public static class AudioDiscExportStatus
{
    public const string NotStarted = "NotStarted";
    public const string Exporting = "Exporting";
    public const string PausedBeforeMerge = "PausedBeforeMerge";
    public const string Completed = "Completed";
    public const string Canceled = "Canceled";
    public const string Failed = "Failed";
}

public static class AudioDiscStatus
{
    public const string Pending = "Pending";
    public const string Ripping = "Ripping";
    public const string Completed = "Completed";
    public const string Failed = "Failed";
}

public static class AudioDiscTrackStatus
{
    public const string Pending = "Pending";
    public const string Ripped = "Ripped";
    public const string Failed = "Failed";
}

public sealed class AudioDiscProjectManifest
{
    public string FormatVersion { get; set; } = AudioDiscProjectManifestVersions.Current;
    public string ProjectType { get; set; } = ProjectManifestTypes.AudioCdProject;
    public string ProjectFolder { get; set; } = "";
    public string PipelineState { get; set; } = ProjectPipelineStateNames.Preparing;
    public string Status { get; set; } = AudioDiscProjectStatus.AwaitingRip;
    public string SourceDriveRoot { get; set; } = "";
    public string SourceDriveName { get; set; } = "";
    public string SourceDriveDevicePath { get; set; } = "";
    public string SourceVolumeLabel { get; set; } = "";
    public string DiscIdentity { get; set; } = "";
    public int TotalDiscs { get; set; }
    public string WorkingFormat { get; set; } = AudioDiscWorkingFormat.Flac.ToString();
    public string ExportPreset { get; set; } = "";
    public string ParallelJobs { get; set; } = "";
    public string OutputExtension { get; set; } = "";
    public string OutputFolder { get; set; } = "";
    public string FileNameTemplate { get; set; } = "";
    public string Title { get; set; } = "";
    public string Author { get; set; } = "";
    public string Album { get; set; } = "";
    public string Narrator { get; set; } = "";
    public string Genre { get; set; } = "";
    public string CoverSourcePath { get; set; } = "";
    public string ProcessedCoverPath { get; set; } = "";
    public DateTime CreatedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }
    public TimeSpan? RipDuration { get; set; }
    public DateTime? CompletedUtc { get; set; }
    public string ErrorMessage { get; set; } = "";
    public string ExportStatus { get; set; } = AudioDiscExportStatus.NotStarted;
    public DateTime? ExportStartedUtc { get; set; }
    public DateTime? ExportCompletedUtc { get; set; }
    public bool HasSuccessfulExport { get; set; }
    public DateTime? LastSuccessfulExportUtc { get; set; }
    public string LastSuccessfulOutputPath { get; set; } = "";
    public string FinalOutputPath { get; set; } = "";
    public string ExportErrorMessage { get; set; } = "";
    public int RawReadAddressingVersion { get; set; }
    public List<AudioDiscProjectManifestDisc> Discs { get; set; } = [];
}

public sealed class AudioDiscProjectManifestDisc
{
    public int DiscNumber { get; set; }
    public string Status { get; set; } = AudioDiscStatus.Pending;
    public string DiscIdentity { get; set; } = "";
    public string SourceDriveRoot { get; set; } = "";
    public string SourceDriveName { get; set; } = "";
    public string SourceDriveDevicePath { get; set; } = "";
    public string SourceVolumeLabel { get; set; } = "";
    public int TrackCount { get; set; }
    public TimeSpan TotalDuration { get; set; }
    public TimeSpan? RipDuration { get; set; }
    public DateTime? CompletedUtc { get; set; }
    public string ErrorMessage { get; set; } = "";
    public List<AudioDiscProjectManifestTrack> Tracks { get; set; } = [];
}

public sealed class AudioDiscProjectManifestTrack
{
    public int GlobalIndex { get; set; }
    public int DiscNumber { get; set; }
    public int TrackNumber { get; set; }
    public string TrackIdentity { get; set; } = "";
    public TimeSpan StartPosition { get; set; }
    public TimeSpan Duration { get; set; }
    public int? SectorOffset { get; set; }
    public int SectorCount { get; set; }
    public string FileName { get; set; } = "";
    public string RelativePath { get; set; } = "";
    public string ChapterTitle { get; set; } = "";
    public string Status { get; set; } = AudioDiscTrackStatus.Pending;
    public DateTime? CompletedUtc { get; set; }
    public long? OutputSizeBytes { get; set; }
    public string ErrorMessage { get; set; } = "";
}
