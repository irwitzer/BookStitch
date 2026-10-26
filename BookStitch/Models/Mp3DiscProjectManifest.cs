namespace BookStitch.Models;

public static class Mp3DiscManifestVersions
{
    public const string Current = "2";
    public const int CurrentSchemaVersion = 1;
}

public static class Mp3DiscImportStatus
{
    public const string Completed = "Completed";
}

public sealed class Mp3DiscProjectManifest
{
    public int ProjectFormatVersion { get; set; }
    public string ProjectId { get; set; } = "";
    public ProjectHealth Health { get; set; } = new();
    public bool IsStoppedForResume { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; } = Mp3DiscManifestVersions.CurrentSchemaVersion;

    public string FormatVersion { get; set; } = Mp3DiscManifestVersions.Current;
    public string ProjectType { get; set; } = "Mp3Disc";
    public string ProjectFolder { get; set; } = "";
    public int TargetSampleRateHz { get; set; } = 44100;
    public string PipelineState { get; set; } = ProjectPipelineStateNames.Preparing;
    public string SourceFolder { get; set; } = "";
    public string SourceDriveRoot { get; set; } = "";
    public string SourceDriveName { get; set; } = "";
    public string SourceDriveDevicePath { get; set; } = "";
    public string SourceVolumeLabel { get; set; } = "";
    public int TotalDiscs { get; set; }
    public string ExportPreset { get; set; } = "";
    public string ParallelJobs { get; set; } = "";
    public string OutputExtension { get; set; } = "";
    public string OutputFolder { get; set; } = "";
    public string FileNameTemplate { get; set; } = "";
    public string OutputFileName { get; set; } = "";
    public string Title { get; set; } = "";
    public string Author { get; set; } = "";
    public string Album { get; set; } = "";
    public string Narrator { get; set; } = "";
    public string Genre { get; set; } = "";
    public string CoverSourcePath { get; set; } = "";
    public string ProcessedCoverPath { get; set; } = "";
    public DateTime CreatedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }
    public List<Mp3DiscProjectManifestDisc> ImportedDiscs { get; set; } = [];
}

public sealed class Mp3DiscProjectManifestDisc
{
    public int DiscNumber { get; set; }
    public string Status { get; set; } = Mp3DiscImportStatus.Completed;
    public string Signature { get; set; } = "";
    public string SourcePath { get; set; } = "";
    public string SourceDriveRoot { get; set; } = "";
    public string SourceDriveName { get; set; } = "";
    public string SourceDriveDevicePath { get; set; } = "";
    public string SourceVolumeLabel { get; set; } = "";
    public string LocalFolder { get; set; } = "";
    public int FileCount { get; set; }
    public int CopiedFiles { get; set; }
    public DateTime CompletedUtc { get; set; }
    public List<Mp3DiscProjectManifestSourceFile> SourceFiles { get; set; } = [];
}

public sealed class Mp3DiscProjectManifestSourceFile
{
    public Guid SourceId { get; set; }
    public string RelativePath { get; set; } = "";
    public string SourceFileName { get; set; } = "";
    public long SourceSizeBytes { get; set; }
    public long SourceLastWriteUtcTicks { get; set; }
}
