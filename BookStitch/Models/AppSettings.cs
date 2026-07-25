namespace BookStitch.Models;

public class AppSettings
{
    public string DefaultGenre { get; set; } = "iBook";

    public string DefaultOutputExtension { get; set; } = ".m4a";

    public string DefaultFileNameTemplate { get; set; } = "{Autor} - {Titel}";

    public bool KeepAlbumLinkedToTitle { get; set; } = true;

    public bool UseLeadingZerosInChapterSuggestions { get; set; } = true;

    public int MetadataPanelAnimationMilliseconds { get; set; } = 550;

    public bool ForceShowFfmpegSetupButton { get; set; } = false;

    public bool ShowDeveloperTab { get; set; } = false;

    public bool ShowPipelineStateDebug { get; set; } = false;
    public bool UseBoxedDiscWaitDialog { get; set; } = false;

    public string? LastSelectedFolder { get; set; }

    public string? LastLocalProjectFolder { get; set; }

    public string? LastDiscSourceFolder { get; set; }

    public string? LastDeveloperAudioDiscTestFolder { get; set; }

    public string? LastDeveloperMp3DiscTestFolder { get; set; }

    public string LastDeveloperDiscTestType { get; set; } = "AudioCd";

    public string? LastSelectedOpticalDrive { get; set; }

    public string? LastCoverFolder { get; set; }

    public int AacBitrateKbps { get; set; } = 128;

    public string SelectedExportPreset { get; set; } = "AAC Stereo 192 kbps";

    public string SelectedParallelJobs { get; set; } = "Auto";

    public string? FfmpegPath { get; set; }

    public string? FfprobePath { get; set; }

    public string? WorkingFolder { get; set; }

    public string? OutputFolder { get; set; }

    public bool AutoEjectDisc { get; set; } = false;

    public string AudioDiscWorkingFormat { get; set; } = "Flac";

    public string SoundProfile { get; set; } = "Important";

    public string FocusProfile { get; set; } = "Standard";

    public int SoundVolumePercent { get; set; } = 65;

    public string SoundLibrary { get; set; } = "Gentle";

    public bool MergeAutomaticallyAfterConversion { get; set; } = false;

    public bool OverwriteFinalOutputWithoutAsking { get; set; } = true;

    public bool KeepComputerAwakeDuringLongOperations { get; set; } = false;

    public int ProjectRetentionDays { get; set; } = 180;

    public int DeleteProjectsOlderThanDays { get; set; } = 180;

    public bool ShowCompletedProjects { get; set; } = true;

    public bool ShowIncompleteProjects { get; set; } = false;

    public string OutputFolderLayout { get; set; } = "AuthorTitleNested";

    public bool TrackGridAutoFitEnabled { get; set; } = true;

    public List<TrackGridColumnLayoutItem> TrackGridColumns { get; set; } = new();

    public bool ExperimentalDriveRoundEnabled { get; set; } = false;

    public bool UsePrivateGenreList { get; set; } = false;

    public List<ConfiguredDiscDrive> DiscDriveOrder { get; set; } = new();
}

public class TrackGridColumnLayoutItem
{
    public string Key { get; set; } = "";

    public int DisplayIndex { get; set; }

    public bool IsVisible { get; set; } = true;

    public double? Width { get; set; }
}

public class ConfiguredDiscDrive
{
    public string DriveRoot { get; set; } = "";

    public string DisplayName { get; set; } = "";

    public string DevicePath { get; set; } = "";

    public bool IsEnabled { get; set; } = true;

    public int Order { get; set; }

    public DateTime? LastSeenUtc { get; set; }
}
