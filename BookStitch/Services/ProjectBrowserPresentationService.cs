using BookStitch.Models;
using System.Globalization;
using System.IO;
using System.IO.Packaging;
using System.Text;

namespace BookStitch.Services;

public sealed record ProjectBrowserPresentation(
    bool CanLoad,
    string HeaderTitle,
    string Details,
    string LocationDetails,
    Uri CoverUri);

public sealed class ProjectBrowserPresentationService
{
    private readonly ProjectIndexService _projectIndexService;

    public ProjectBrowserPresentationService(
        ProjectIndexService projectIndexService)
    {
        _projectIndexService = projectIndexService ?? throw new ArgumentNullException(nameof(projectIndexService));
    }

    public IReadOnlyList<ProjectIndexItem> ScanSelectableProjects(string workingRootFolder, int retentionDays,
        ICollection<DamagedProjectInfo>? readFailures = null) =>
        _projectIndexService.ScanSelectableProjects(workingRootFolder, retentionDays, readFailures);

    public ProjectBrowserPresentation Create(
        ProjectIndexItem? project,
        string? retentionMode,
        AppearanceMode appearanceMode)
    {
        if (project is null)
        {
            return new ProjectBrowserPresentation(
                false,
                LocalizationText.Get("Project.Browser.NoProjectSelected"),
                LocalizationText.Get("Project.Browser.NoCompleteProjects"),
                string.Empty,
                ResolveProjectCoverImageUri(appearanceMode));
        }

        return new ProjectBrowserPresentation(
            project.CanAttemptLoad,
            BuildProjectHeaderTitle(project),
            BuildDetails(project, retentionMode),
            BuildLocationDetails(project),
            ResolveProjectCoverImageUri(appearanceMode,
                FirstExistingFile(project.ProcessedCoverPath, project.CoverSourcePath)));
    }

    public ProjectDeletionResult DeleteProject(string workingRootFolder, string projectFolder) =>
        _projectIndexService.DeleteProject(workingRootFolder, projectFolder);

    public static Uri ResolveProjectCoverImageUri(AppearanceMode appearanceMode, string? filePath = null)
    {
        if (!string.IsNullOrWhiteSpace(filePath) && File.Exists(filePath))
            return new Uri(filePath, UriKind.Absolute);

        _ = PackUriHelper.UriSchemePack;
        return new Uri(CoverPreviewPresentationService.GetPlaceholderPackUri(appearanceMode), UriKind.Absolute);
    }

    private static string BuildDetails(ProjectIndexItem project, string? retentionMode)
    {
        var builder = new StringBuilder();
        builder.AppendLine(LocalizationText.Get("Project.Browser.Selection"));
        builder.AppendLine(LocalizationText.Get("Project.Browser.NameLabel") + ValueOrPlaceholder(project.DisplayName, LocalizationText.Get("Project.Browser.Untitled")));
        builder.AppendLine(LocalizationText.Get("Project.Browser.CreatedLabel") + FormatDate(project.CreatedUtc));
        builder.AppendLine(LocalizationText.Get("Project.Browser.UpdatedLabel") + FormatDate(project.UpdatedUtc));
        builder.AppendLine("• "+LocalizationText.Format("Integrity.Format", project.ProjectFormatVersion));
        builder.AppendLine(LocalizationText.Get("Project.Browser.StatusLabel") + (project.IsStoppedDiscProject ? LocalizationText.Format("Disc.StopSave.Status", project.ImportedDiscCount, project.TotalDiscs) : FormatStatus(project.Status)));
        if (!project.IsStoppedDiscProject)
            builder.AppendLine(LocalizationText.Get("Project.Browser.CompleteCategory"));
        builder.AppendLine(LocalizationText.Get("Project.Browser.TypeLabel") + FormatProjectType(project.ProjectType));
        builder.AppendLine(LocalizationText.Get("Project.Browser.ExpirationLabel") + ProjectRetentionPolicyService.FormatExpiration(project.ExpiresUtc, retentionMode));
        builder.AppendLine();

        builder.AppendLine(LocalizationText.Get("Project.Browser.MetadataHeading"));
        var displayedTitle = project.Title;
        var displayedAlbum = project.Album;
        builder.AppendLine(LocalizationText.Get("Project.Browser.AuthorLabel") + ValueOrPlaceholder(project.Author, LocalizationText.Get("Project.Browser.Untitled")));
        builder.AppendLine(LocalizationText.Get("Project.Browser.TitleLabel") + ValueOrPlaceholder(displayedTitle, LocalizationText.Get("Project.Browser.Untitled")));
        if (!string.IsNullOrWhiteSpace(displayedAlbum) &&
            !string.Equals(displayedAlbum.Trim(), displayedTitle?.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            builder.AppendLine(LocalizationText.Get("Project.Browser.AlbumLabel") + displayedAlbum.Trim());
        }
        builder.AppendLine(LocalizationText.Get("Project.Browser.NarratorLabel") + ValueOrPlaceholder(project.Narrator, LocalizationText.Get("Project.Browser.NotSet")));
        builder.AppendLine(LocalizationText.Get("Project.Browser.GenreLabel") + ValueOrPlaceholder(project.Genre, LocalizationText.Get("Project.Browser.NotSet")));
        builder.AppendLine();

        if (project.TotalDiscs > 0)
        {
            builder.AppendLine(LocalizationText.Get("Project.Browser.DiscsHeading"));
            builder.AppendLine(LocalizationText.Format("Project.Browser.ImportedDiscs", project.ImportedDiscCount, project.TotalDiscs));
            builder.AppendLine();
        }

        builder.AppendLine(LocalizationText.Get("Project.Browser.ProjectContentHeading"));
        builder.AppendLine(LocalizationText.Get(project.TrackCount > 0
            ? "Project.Browser.TrackListYes" : "Project.Browser.TrackListNo"));
        builder.AppendLine(LocalizationText.Get("Project.Browser.SavedTrackCountLabel") + project.TrackCount.ToString(CultureInfo.InvariantCulture));
        if (!string.IsNullOrWhiteSpace(project.SelectedPreset))
        {
            builder.AppendLine(LocalizationText.Get("Project.Browser.CurrentPresetLabel"));
            builder.AppendLine("  ◦ " + FormatSavedPreset(project, project.SelectedPreset));
        }
        var otherPresets = project.SavedConvertedTrackCounts.Keys
            .Where(preset => !string.Equals(preset, project.SelectedPreset, StringComparison.OrdinalIgnoreCase))
            .OrderBy(preset => preset, StringComparer.CurrentCultureIgnoreCase).ToList();
        if (otherPresets.Count > 0)
        {
            builder.AppendLine(LocalizationText.Get("Project.Browser.OtherPresetsLabel"));
            foreach (var preset in otherPresets)
                builder.AppendLine("  ◦ " + FormatSavedPreset(project, preset));
        }

        return builder.ToString().TrimEnd();
    }

    private static string BuildLocationDetails(ProjectIndexItem project)
    {
        var builder = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(project.OutputFolder))
            builder.AppendLine(LocalizationText.Get("Project.Browser.OutputLocationLabel") + project.OutputFolder);
        builder.AppendLine(LocalizationText.Get("Project.Browser.ProjectFolderLabel") + project.ProjectFolderDisplay);
        return builder.ToString().TrimEnd();
    }

    private static string BuildProjectHeaderTitle(ProjectIndexItem project)
    {
        var title = ValueOrPlaceholder(project.Title, project.DisplayName);
        var author = ValueOrPlaceholder(project.Author, LocalizationText.Get("Project.Browser.UnknownAuthor"));
        return title + " - " + author;
    }

    private static string ValueOrPlaceholder(string? value, string placeholder) =>
        string.IsNullOrWhiteSpace(value) ? placeholder : value.Trim();

    private static string FormatDate(DateTime value)
    {
        if (value == default)
            return LocalizationText.Get("Project.Browser.UnknownDate");
        var local = value.Kind == DateTimeKind.Local ? value : value.ToLocalTime();
        return local.ToString("dd.MM.yyyy HH:mm", CultureInfo.CurrentCulture);
    }

    private static string FormatProjectType(string? projectType) => projectType switch
    {
        ProjectManifestTypes.Mp3DiscProject => LocalizationText.Get("Project.Type.Mp3Disc"),
        ProjectManifestTypes.AudioCdProject => LocalizationText.Get("Project.Type.AudioDisc"),
        ProjectManifestTypes.FolderProject => LocalizationText.Get("Project.Type.Folder"),
        "Mp3Disc" => LocalizationText.Get("Project.Type.Mp3Import"),
        _ => string.IsNullOrWhiteSpace(projectType) ? LocalizationText.Get("Project.Browser.Unknown") : projectType
    };

    private static string FormatStatus(string? status) => status switch
    {
        ProjectPipelineStateNames.Preparing => LocalizationText.Get("Project.Status.Preparing"),
        ProjectPipelineStateNames.AcquiringSources => LocalizationText.Get("Project.Status.AcquiringSources"),
        ProjectPipelineStateNames.Converting => LocalizationText.Get("Project.Status.Converting"),
        ProjectPipelineStateNames.ReviewBeforeMerge => LocalizationText.Get("Project.Status.ReviewBeforeMerge"),
        ProjectPipelineStateNames.Merging => LocalizationText.Get("Project.Status.Merging"),
        ProjectPipelineStateNames.Completed => LocalizationText.Get("Project.Status.Completed"),
        ProjectManifestStatuses.LegacyCreated => LocalizationText.Get("Project.Status.Preparing"),
        ProjectManifestStatuses.LegacyImporting => LocalizationText.Get("Project.Status.AcquiringSources"),
        ProjectManifestStatuses.LegacyReady => LocalizationText.Get("Project.Status.ReviewBeforeMerge"),
        ProjectManifestStatuses.LegacyExporting => LocalizationText.Get("Project.Status.ConversionInterrupted"),
        ProjectManifestStatuses.LegacyCanceled => LocalizationText.Get("Project.Status.Canceled"),
        ProjectManifestStatuses.LegacyFailed => LocalizationText.Get("Project.Status.Failed"),
        AudioDiscProjectStatus.AwaitingRip => LocalizationText.Get("Project.Status.Preparing"),
        AudioDiscProjectStatus.Ripping => LocalizationText.Get("Project.Status.AcquiringSources"),
        AudioDiscProjectStatus.WaitingForDisc => LocalizationText.Get("Project.Status.WaitingForAudioDisc"),
        AudioDiscProjectStatus.RippingCompleted => LocalizationText.Get("Project.Status.ReviewBeforeMerge"),
        AudioDiscExportStatus.PausedBeforeMerge => LocalizationText.Get("Project.Status.ReviewBeforeMerge"),
        _ => string.IsNullOrWhiteSpace(status) ? LocalizationText.Get("Project.Browser.Unknown") : status
    };

    private static string FormatSavedPreset(ProjectIndexItem project, string preset) =>
        project.SavedConvertedTrackCounts.TryGetValue(preset, out var count)
            ? preset + ": " + LocalizationText.Format("Project.Browser.SavedConvertedCount", count)
            : preset;

    private static string FirstExistingFile(params string?[] candidates)
    {
        foreach (var candidate in candidates)
            if (!string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate))
                return candidate;
        return string.Empty;
    }
}
