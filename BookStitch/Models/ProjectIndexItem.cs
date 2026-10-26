using BookStitch.Services;
using System.Globalization;

namespace BookStitch.Models;

public sealed class ProjectIndexItem
{
    public ProjectHealthStatus HealthStatus { get; set; }
    public IReadOnlyList<IntegrityFinding> IntegrityFindings { get; set; } = [];
    public string WorkingRoot { get; set; } = "";
    public string ProjectFolderDisplay => ProjectIntegrityPresentation.ShortProjectPath(ProjectFolder, WorkingRoot);
    public bool SafetyBlocked => IntegrityFindings.Any(f=>f.IssueCode==IntegrityIssueCode.UnsafeReparsePoint);
    public bool ShowProjectFolder => SafetyBlocked || HealthStatus is ProjectHealthStatus.RebuildRequired or ProjectHealthStatus.Damaged ||
        IntegrityFindings.Any(f => !f.WasRepaired && f.IssueCode is IntegrityIssueCode.UnknownProjectFile or IntegrityIssueCode.StaleManagedPath);
    public bool IsVisibleInProjectBrowser => IsSelectableProject || HealthStatus is ProjectHealthStatus.Damaged or ProjectHealthStatus.RebuildRequired;
    public bool CanAttemptLoad => !SafetyBlocked && (IsSelectableProject || HealthStatus is ProjectHealthStatus.Damaged or ProjectHealthStatus.RebuildRequired);
    public bool CanRebuild => HealthStatus==ProjectHealthStatus.RebuildRequired && !SafetyBlocked;
    public string HealthMessage => SafetyBlocked ? LocalizationText.Get("Integrity.SafetyBlocked") : HealthStatus==ProjectHealthStatus.RebuildRequired ? LocalizationText.Get("Integrity.RebuildRequired") : HealthStatus == ProjectHealthStatus.Damaged ? LocalizationText.Get("Integrity.Damaged")
        : HealthStatus == ProjectHealthStatus.Warning ? LocalizationText.Get("Integrity.ProjectWarning") : "";
    public int ProjectFormatVersion { get; set; }
    public bool NeedsUpgrade => ProjectFormatVersion < ProjectFormat.CurrentProjectFormatVersion;
    public string ProjectFolder { get; set; } = "";
    public string ProjectType { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Status { get; set; } = "";
    public bool CanResume { get; set; }
    public bool IsSelectableProject { get; set; }
    public bool IsCompletedProject { get; set; }
    public bool IsStoppedDiscProject { get; set; }
    public bool IsExpired { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }
    public DateTime ExpiresUtc { get; set; }
    public string SourceFolder { get; set; } = "";
    public string OutputFolder { get; set; } = "";
    public string OutputFileName { get; set; } = "";
    public string Title { get; set; } = "";
    public string Author { get; set; } = "";
    public string Album { get; set; } = "";
    public string Narrator { get; set; } = "";
    public string Genre { get; set; } = "";
    public int TotalDiscs { get; set; }
    public int ImportedDiscCount { get; set; }
    public string PrimaryManifestPath { get; set; } = "";
    // In-memory preview summary, populated from manifests already read by the index.
    // It describes saved state, never the validity of audio files on disk.
    public int TrackCount { get; set; }
    public string SelectedPreset { get; set; } = "";
    public string CoverSourcePath { get; set; } = "";
    public string ProcessedCoverPath { get; set; } = "";
    public IReadOnlyDictionary<string, int> SavedConvertedTrackCounts { get; set; } = new Dictionary<string, int>();

    public string ListDisplayPrefix =>
        BuildListName() + " | " + FormatListDate(CreatedUtc);

    public string ListDisplayState => ResolveListState();

    public string ListDisplayName =>
        ListDisplayPrefix + " | " + ListDisplayState;

    public override string ToString()
    {
        return ListDisplayName;
    }

    private string BuildListName()
    {
        var author = ResolveListAuthor();
        var title = string.IsNullOrWhiteSpace(Title) ? ResolveListTitleFromDisplayName() : Title.Trim();

        if (string.IsNullOrWhiteSpace(title))
            title = LocalizationText.Get("Project.Index.Untitled");

        return FormatListAuthor(author) + " - " + title;
    }

    private string ResolveListState()
    {
        if (SafetyBlocked) return LocalizationText.Get("Integrity.SafetyBlocked");
        if (HealthStatus == ProjectHealthStatus.RebuildRequired) return LocalizationText.Get("Integrity.RebuildRequired");
        if (HealthStatus == ProjectHealthStatus.Damaged) return LocalizationText.Get("Integrity.Defect");
        if (IsStoppedDiscProject)
            return LocalizationText.Format("Disc.StopSave.Status", ImportedDiscCount, TotalDiscs);
        if (IsCompletedProject)
            return LocalizationText.Get("Project.Index.Completed");

        return LocalizationText.Get(IsSelectableProject ? "Project.Index.ExportReady" : "Project.Index.InProgress");
    }

    private string ResolveListAuthor()
    {
        if (!string.IsNullOrWhiteSpace(Author))
            return Author.Trim();

        if (!string.IsNullOrWhiteSpace(DisplayName))
        {
            var separatorIndex = DisplayName.IndexOf(" - ", StringComparison.Ordinal);
            if (separatorIndex > 0)
                return DisplayName[..separatorIndex].Trim();
        }

        return LocalizationText.Get("Project.Index.Unknown");
    }

    private string ResolveListTitleFromDisplayName()
    {
        if (string.IsNullOrWhiteSpace(DisplayName))
            return "";

        var displayName = DisplayName.Trim();
        var separatorIndex = displayName.IndexOf(" - ", StringComparison.Ordinal);
        return separatorIndex >= 0
            ? displayName[(separatorIndex + 3)..].Trim()
            : displayName;
    }

    private static string FormatListAuthor(string author)
    {
        const int maxLength = 18;
        const string ellipsis = "...";

        var normalized = string.IsNullOrWhiteSpace(author) ? LocalizationText.Get("Project.Index.Unknown") : author.Trim();
        return normalized.Length > maxLength
            ? normalized[..(maxLength - ellipsis.Length)] + ellipsis
            : normalized;
    }

    private static string FormatListDate(DateTime value)
    {
        if (value == default)
            return LocalizationText.Get("Project.Index.DateUnknown");

        var local = value.Kind == DateTimeKind.Local ? value : value.ToLocalTime();
        return local.ToString("dd.MM.yyyy HH:mm", CultureInfo.CurrentCulture);
    }


}
