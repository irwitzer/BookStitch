namespace BookStitch.Models;

public static class ExportWorkManifestVersions
{
    public const string Current = "3";
}

public sealed class ExportWorkManifest
{
    public string FormatVersion { get; set; } = ExportWorkManifestVersions.Current;
    public string ProjectType { get; set; } = ProjectManifestTypes.FolderProject;
    public string ProjectId { get; set; } = "";
    public string AppVersion { get; set; } = "";
    public string ProjectWorkFolder { get; set; } = "";
    public string SourceFolder { get; set; } = "";
    public string SelectedPreset { get; set; } = "";
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

    public ExportManifestExportSettings Export { get; set; } = new();
    public ExportManifestBookMetadata Metadata { get; set; } = new();
    public ExportManifestState State { get; set; } = new();
    public ExportManifestResume Resume { get; set; } = new();

    public List<ExportWorkManifestDisc> Discs { get; set; } = [];
    public List<ExportWorkManifestTrack> Tracks { get; set; } = [];
    public List<ExportWorkManifestEvent> History { get; set; } = [];
}

public static class ProjectManifestTypes
{
    public const string FolderProject = "FolderProject";
    public const string Mp3DiscProject = "Mp3DiscProject";
    public const string AudioCdProject = "AudioCdProject";
}

public static class ProjectManifestStatuses
{
    public const string Preparing = ProjectPipelineStateNames.Preparing;
    public const string AcquiringSources = ProjectPipelineStateNames.AcquiringSources;
    public const string Converting = ProjectPipelineStateNames.Converting;
    public const string ReviewBeforeMerge = ProjectPipelineStateNames.ReviewBeforeMerge;
    public const string Merging = ProjectPipelineStateNames.Merging;
    public const string Completed = ProjectPipelineStateNames.Completed;

    // Nur zum Lesen bestehender Manifeste. Neue Schreibvorgänge verwenden ausschließlich die sechs Zustände oben.
    public const string LegacyCreated = "Created";
    public const string LegacyImporting = "Importing";
    public const string LegacyReady = "Ready";
    public const string LegacyExporting = "Exporting";
    public const string LegacyCanceled = "Canceled";
    public const string LegacyFailed = "Failed";

    // Quellkompatibilität für bestehenden Code und Tests. Nicht für neue Schreibvorgänge verwenden.
    public const string Created = LegacyCreated;
    public const string Importing = LegacyImporting;
    public const string Ready = LegacyReady;
    public const string Exporting = LegacyExporting;
    public const string Canceled = LegacyCanceled;
    public const string Failed = LegacyFailed;
}

public static class ProjectManifestTrackStatuses
{
    public const string Pending = "Pending";
    public const string Reading = "Reading";
    public const string Ripping = "Ripping";
    public const string Converting = "Converting";
    public const string Copied = "Copied";
    public const string Converted = "Converted";
    public const string Skipped = "Skipped";
    public const string Failed = "Failed";
    public const string Canceled = "Canceled";
}

public static class ProjectManifestDiscStatuses
{
    public const string Expected = "Expected";
    public const string Inserted = "Inserted";
    public const string Importing = "Importing";
    public const string Imported = "Imported";
    public const string PartiallyImported = "PartiallyImported";
    public const string Failed = "Failed";
    public const string Canceled = "Canceled";
    public const string Missing = "Missing";
}

public sealed class ExportManifestExportSettings
{
    public string SelectedPreset { get; set; } = "";
    public string OutputFolder { get; set; } = "";
    public string OutputFileName { get; set; } = "";
    public string OutputExtension { get; set; } = "";
    public string ParallelJobs { get; set; } = "";
}

public sealed class ExportManifestBookMetadata
{
    public string Title { get; set; } = "";
    public string Author { get; set; } = "";
    public string Album { get; set; } = "";
    public string Narrator { get; set; } = "";
    public string Genre { get; set; } = "";
    public string CoverSourcePath { get; set; } = "";
    public string ProcessedCoverPath { get; set; } = "";
}

public sealed class ExportManifestState
{
    public string Status { get; set; } = ProjectManifestStatuses.Preparing;
    public string LastSuccessfulStep { get; set; } = "";
    public int LastStartedTrackIndex { get; set; }
    public int LastCompletedTrackIndex { get; set; }
    public DateTime? CancelRequestedUtc { get; set; }
    public DateTime? LastErrorUtc { get; set; }
    public string LastErrorSummary { get; set; } = "";
    public List<string> ManualMergeReviewCompletedPresets { get; set; } = [];
}

public sealed class ExportManifestResume
{
    public bool CanResume { get; set; }
    public string Reason { get; set; } = "";
    public DateTime? LastCleanupUtc { get; set; }
    public bool DirtyShutdownDetected { get; set; }
}

public sealed class ExportWorkManifestDisc
{
    public int DiscIndex { get; set; }
    public string DiscId { get; set; } = "";
    public string VolumeLabel { get; set; } = "";
    public DateTime? DetectedUtc { get; set; }
    public string Status { get; set; } = ProjectManifestDiscStatuses.Expected;
    public int TrackCount { get; set; }
    public int ImportedTrackCount { get; set; }
    public string LastError { get; set; } = "";
}

public sealed class ExportWorkManifestTrack
{
    public int TrackIndex { get; set; }
    public string SourcePath { get; set; } = "";
    public string SourceFileName { get; set; } = "";
    public long SourceSizeBytes { get; set; }
    public long SourceLastWriteUtcTicks { get; set; }
    public string SourceCodec { get; set; } = "";
    public string SourceExtension { get; set; } = "";
    public string Duration { get; set; } = "";
    public long? DurationTicks { get; set; }
    public string ChapterTitle { get; set; } = "";
    public string Action { get; set; } = "";
    public string Preset { get; set; } = "";
    public string Status { get; set; } = ProjectManifestTrackStatuses.Pending;
    public string ConvertedPath { get; set; } = "";
    public long ConvertedSizeBytes { get; set; }
    public long ConvertedLastWriteUtcTicks { get; set; }
    public DateTime? StartedUtc { get; set; }
    public DateTime? CompletedUtc { get; set; } = DateTime.UtcNow;
    public string LastError { get; set; } = "";
}

public sealed class ExportWorkManifestEvent
{
    public DateTime Utc { get; set; } = DateTime.UtcNow;
    public string Event { get; set; } = "";
    public string Message { get; set; } = "";
    public int? TrackIndex { get; set; }
    public int? DiscIndex { get; set; }
}
