using BookStitch.Models;
using BookStitch.Models.TrackOrdering;
using BookStitch.Services.TrackOrdering;
using System.IO;

namespace BookStitch.Services;

public sealed class ProjectResumeLoadWorkflowService
{
    private readonly AudioDiscProjectService _audioDiscProjectService;
    private readonly TrackListStateService _trackListStateService;
    private readonly SourceMetadataSnapshotStore _sourceMetadataSnapshotStore;
    private readonly ProjectMetadataStore _projectMetadataStore;

    public ProjectResumeLoadWorkflowService(
        AudioDiscProjectService audioDiscProjectService,
        TrackListStateService trackListStateService,
        SourceMetadataSnapshotStore? sourceMetadataSnapshotStore = null,
        ProjectMetadataStore? projectMetadataStore = null)
    {
        _audioDiscProjectService = audioDiscProjectService;
        _trackListStateService = trackListStateService;
        _sourceMetadataSnapshotStore = sourceMetadataSnapshotStore ?? new SourceMetadataSnapshotStore();
        _projectMetadataStore = projectMetadataStore ?? new ProjectMetadataStore();
    }

    public ProjectResumeLoadResult Prepare(ProjectResumePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        try
        {
            DiscProjectStopSaveService.ValidateResumeSources(plan);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or System.Text.Json.JsonException or PersistentJsonSchemaException)
        {
            return ProjectResumeLoadResult.Failed(ex.Message);
        }

        var isMp3DiscProject = string.Equals(
            plan.ProjectType,
            ProjectManifestTypes.Mp3DiscProject,
            StringComparison.OrdinalIgnoreCase);
        var isAudioDiscProject = string.Equals(
            plan.ProjectType,
            ProjectManifestTypes.AudioCdProject,
            StringComparison.OrdinalIgnoreCase);

        var audioDiscManifest = isAudioDiscProject
            ? _audioDiscProjectService.TryLoad(plan.ProjectFolder)
            : null;

        if (isAudioDiscProject && audioDiscManifest is null)
        {
            return ProjectResumeLoadResult.Failed(
                LocalizationText.Get("Resume.Load.AudioManifestInvalid"));
        }

        var currentFolderPath = isMp3DiscProject
            ? plan.ProjectFolder
            : plan.SourceFolder;
        if (string.IsNullOrWhiteSpace(currentFolderPath) || !Directory.Exists(currentFolderPath))
            currentFolderPath = plan.ProjectFolder;

        var canonicalMetadataPath = ProjectFolderLayout.GetProjectMetadataPath(plan.ProjectFolder);
        var canonicalMetadata = File.Exists(canonicalMetadataPath)
            ? _projectMetadataStore.LoadOrCreate(plan.ProjectFolder)
            : null;

        var sourceMetadataById = _sourceMetadataSnapshotStore
            .LoadAll(plan.ProjectFolder)
            .Where(snapshot => snapshot.SourceId != Guid.Empty)
            .GroupBy(snapshot => snapshot.SourceId)
            .ToDictionary(group => group.Key, group => group.First());

        var tracks = plan.Tracks
            .OrderBy(track => track.TrackIndex)
            .Select(track => BuildTrackInfo(
                plan,
                track,
                sourceMetadataById.GetValueOrDefault(track.SourceId)))
            .ToList();
        var projectOrderType = isAudioDiscProject
            ? TrackOrderProjectType.AudioDisc
            : isMp3DiscProject
                ? TrackOrderProjectType.Mp3Disc
                : TrackOrderProjectType.LocalFolder;
        var confirmedContexts = isAudioDiscProject
            ? TrackOrderConfirmedContextFactory.CreateAudioDiscContexts(tracks)
            : isMp3DiscProject
                ? TrackOrderConfirmedContextFactory.CreateMp3DiscContexts(tracks)
                : null;
        tracks = _trackListStateService.Apply(plan.ProjectFolder, tracks, projectOrderType, confirmedContexts);
        new TrackPreparedStateRefreshService().RefreshForCurrentPreset(tracks, plan.ProjectFolder, plan.SelectedPreset);

        var awaitingAudioDiscRip = !plan.IsMergePaused && isAudioDiscProject &&
            audioDiscManifest is not null &&
            !_audioDiscProjectService.IsProjectRipCompleted(audioDiscManifest);
        var pipelineState = plan.IsMergePaused ? ProjectPipelineState.Merging : awaitingAudioDiscRip || (isMp3DiscProject && plan.CanContinueDiscImport)
            ? ProjectPipelineState.AcquiringSources
            : plan.IsStoppedDiscProject
                ? ProjectPipelineState.Converting
                : plan.HasSuccessfulExport
                ? ProjectPipelineState.Completed
                : ProjectPipelineState.ReviewBeforeMerge;
        var waitingForManualMergeReview = pipelineState == ProjectPipelineState.ReviewBeforeMerge;

        var needsMp3DiscImport = isMp3DiscProject && plan.CanContinueDiscImport;
        var (statusText, progressText) = BuildLoadStatus(
            plan,
            awaitingAudioDiscRip,
            waitingForManualMergeReview,
            pipelineState == ProjectPipelineState.Completed);
        var loadStatusSnapshot = BuildLoadStatusSnapshot(
            plan,
            tracks,
            isMp3DiscProject,
            isAudioDiscProject,
            needsMp3DiscImport,
            awaitingAudioDiscRip,
            pipelineState);

        return new ProjectResumeLoadResult
        {
            Success = true,
            IsMp3DiscProject = isMp3DiscProject,
            IsAudioDiscProject = isAudioDiscProject,
            NeedsMp3DiscImport = needsMp3DiscImport,
            AudioDiscManifest = audioDiscManifest,
            IsAudioDiscProjectAwaitingRip = awaitingAudioDiscRip,
            IsWaitingForManualMergeReview = waitingForManualMergeReview || pipelineState == ProjectPipelineState.Completed,
            IsCompletedProject = pipelineState == ProjectPipelineState.Completed,
            PipelineState = pipelineState,
            CurrentFolderPath = currentFolderPath,
            SelectedFolder = string.IsNullOrWhiteSpace(plan.SourceFolder) ? plan.ProjectFolder : plan.SourceFolder,
            CanonicalMetadata = canonicalMetadata,
            Tracks = tracks,
            StatusText = statusText,
            ProgressText = progressText,
            LoadStatusSnapshot = loadStatusSnapshot
        };
    }


    private static WorkflowStatusSnapshot? BuildLoadStatusSnapshot(
        ProjectResumePlan plan,
        IReadOnlyList<TrackInfo> tracks,
        bool isMp3DiscProject,
        bool isAudioDiscProject,
        bool needsMp3DiscImport,
        bool awaitingAudioDiscRip,
        ProjectPipelineState pipelineState)
    {
        if (needsMp3DiscImport || awaitingAudioDiscRip)
            return null;

        if (pipelineState is not ProjectPipelineState.ReviewBeforeMerge and not ProjectPipelineState.Completed)
            return null;

        var projectKind = isAudioDiscProject
            ? WorkflowProjectKind.AudioDisc
            : isMp3DiscProject
                ? WorkflowProjectKind.Mp3Disc
                : WorkflowProjectKind.Folder;
        var preset = ExportPreset.Parse(plan.SelectedPreset);
        var totalTracks = Math.Max(0, tracks.Count);
        var totalChapters = ExportPreviewMetricsService.CountPlannedChapters(tracks);
        var reusableConvertedTracks = Math.Clamp(
            tracks.Count(track => track.HasReusableConvertedFile),
            0,
            totalTracks);
        var conversionPercent = totalTracks == 0
            ? 100
            : (int)Math.Round(reusableConvertedTracks * 100d / totalTracks, MidpointRounding.AwayFromZero);
        var totalDiscs = GetLoadedProjectDiscCount(plan, tracks, projectKind);

        return new WorkflowStatusSnapshot
        {
            ProjectId = plan.ProjectFolder,
            ProjectKind = projectKind,
            ProjectState = pipelineState,
            SourceProgress = CreateCompletedSourceProgress(projectKind, totalTracks, totalDiscs),
            ConversionProgress = new ConversionActivityProgress(
                reusableConvertedTracks,
                totalTracks,
                Math.Clamp(conversionPercent, 0, 100),
                Array.Empty<int>(),
                preset.BitrateKbps,
                preset.Channels == 1,
                IsLive: true),
            IsLoadedProject = true,
            IsReadyToMerge = pipelineState == ProjectPipelineState.ReviewBeforeMerge && reusableConvertedTracks >= totalTracks,
            TotalSourceItems = projectKind is WorkflowProjectKind.AudioDisc or WorkflowProjectKind.Mp3Disc
                ? totalDiscs
                : totalTracks,
            TotalChapters = totalChapters
        };
    }

    private static SourceAcquisitionProgress CreateCompletedSourceProgress(
        WorkflowProjectKind projectKind,
        int totalTracks,
        int totalDiscs)
    {
        var kind = projectKind == WorkflowProjectKind.AudioDisc
            ? SourceAcquisitionKind.Ripping
            : SourceAcquisitionKind.Copying;

        return new SourceAcquisitionProgress(
            kind,
            totalTracks,
            totalTracks,
            totalTracks,
            totalTracks,
            CurrentDisc: projectKind is WorkflowProjectKind.AudioDisc or WorkflowProjectKind.Mp3Disc ? totalDiscs : 0,
            TotalDiscs: projectKind is WorkflowProjectKind.AudioDisc or WorkflowProjectKind.Mp3Disc ? totalDiscs : 0,
            Percent: 100,
            WorkingFormat: projectKind == WorkflowProjectKind.AudioDisc ? "FLAC" : null,
            CurrentSourceFinished: true,
            AllSourcesFinished: true);
    }

    private static int GetLoadedProjectDiscCount(
        ProjectResumePlan plan,
        IReadOnlyList<TrackInfo> tracks,
        WorkflowProjectKind projectKind)
    {
        if (projectKind is not WorkflowProjectKind.AudioDisc and not WorkflowProjectKind.Mp3Disc)
            return 0;

        var maxTrackDisc = tracks
            .Select(track => track.DiscNumber ?? 0)
            .DefaultIfEmpty(0)
            .Max();

        var discCount = projectKind == WorkflowProjectKind.Mp3Disc && plan.HasSuccessfulExport && !plan.CanContinueDiscImport
            ? plan.ImportedDiscCount : plan.TotalDiscs;
        return Math.Max(1, Math.Max(discCount, maxTrackDisc));
    }

    private static (string StatusText, string ProgressText) BuildLoadStatus(
        ProjectResumePlan plan,
        bool awaitingAudioDiscRip,
        bool waitingForManualMergeReview,
        bool completedProject)
    {
        if (awaitingAudioDiscRip && plan.NextMissingDiscNumber is int nextAudioDisc)
        {
            return (
                LocalizationText.Format("Resume.Load.AudioContinue", nextAudioDisc, plan.TotalDiscs),
                LocalizationText.Get("Resume.Load.AudioInstruction"));
        }

        if (plan.CanContinueDiscImport && plan.NextMissingDiscNumber is int nextDisc)
        {
            return (
                LocalizationText.Format("Resume.Load.MissingDisc", nextDisc, plan.TotalDiscs),
                LocalizationText.Format("Resume.Load.ImportLater", nextDisc));
        }

        if (completedProject)
        {
            return (
                LocalizationText.Get("Resume.Load.Completed"),
                LocalizationText.Get("Resume.Load.CompletedInstruction"));
        }

        if (waitingForManualMergeReview)
        {
            return (
                LocalizationText.Get("Resume.Load.Ready"),
                LocalizationText.Get("Resume.Load.ReviewInstruction"));
        }

        return (
            LocalizationText.Get("Resume.Load.Loaded"),
            string.Empty);
    }

    private static TrackInfo BuildTrackInfo(
        ProjectResumePlan plan,
        ProjectResumeTrackItem resumeTrack,
        SourceMetadataSnapshot? sourceMetadata)
    {
        var sourcePath = resumeTrack.SourcePath ?? string.Empty;
        var fileName = !string.IsNullOrWhiteSpace(resumeTrack.SourceFileName)
            ? resumeTrack.SourceFileName
            : Path.GetFileName(sourcePath);

        var relativeFolder = resumeTrack.RelativeFolder ?? string.Empty;
        if (string.IsNullOrWhiteSpace(relativeFolder) && !string.IsNullOrWhiteSpace(sourcePath))
        {
            relativeFolder = string.Equals(plan.ProjectType, ProjectManifestTypes.Mp3DiscProject, StringComparison.OrdinalIgnoreCase)
                ? GetRelativeFolderSafe(plan.ProjectFolder, sourcePath)
                : GetRelativeFolderSafe(plan.SourceFolder, sourcePath);
        }

        var result = new TrackInfo
        {
            SourceId = resumeTrack.SourceId,
            Index = resumeTrack.TrackIndex,
            DiscNumber = resumeTrack.DiscNumber,
            TrackNumber = resumeTrack.TrackNumber,
            FilePath = sourcePath,
            FileName = fileName,
            RelativeFolder = relativeFolder,
            ProjectRelativeFolder = BuildProjectRelativeFolder(plan, relativeFolder),
            HideFolderDisplay = IsDiscProject(plan.ProjectType),
            Extension = Path.GetExtension(fileName).TrimStart('.').ToUpperInvariant(),
            TagTitle = sourceMetadata?.CanonicalFields.Title ?? string.Empty,
            Artist = sourceMetadata?.CanonicalFields.Authors.FirstOrDefault()
                ?? sourceMetadata?.CanonicalFields.Performers.FirstOrDefault()
                ?? string.Empty,
            SourceMetadataFields = sourceMetadata?.CanonicalFields,
            SourceTrackNumber = sourceMetadata?.CanonicalFields.SourceTrackNumber,
            SourceTrackTotal = sourceMetadata?.CanonicalFields.SourceTrackTotal,
            SourceDiscNumber = sourceMetadata?.CanonicalFields.SourceDiscNumber,
            SourceDiscTotal = sourceMetadata?.CanonicalFields.SourceDiscTotal,
            ChapterTitle = string.IsNullOrWhiteSpace(resumeTrack.ChapterTitle)
                ? Path.GetFileNameWithoutExtension(fileName)
                : resumeTrack.ChapterTitle,
            Duration = resumeTrack.Duration ?? string.Empty,
            DurationTicks = resumeTrack.DurationTicks,
            ProcessingAction = ProcessingActionIds.NormalizeOrEmpty(resumeTrack.Action),
            PreparedConvertedPath = resumeTrack.ConvertedPath ?? string.Empty,
            PreparedConvertedPreset = resumeTrack.Preset ?? string.Empty
        };


        try
        {
            if (!string.IsNullOrWhiteSpace(sourcePath) && File.Exists(sourcePath))
            {
                var fileInfo = new FileInfo(sourcePath);
                result.SizeMb = Math.Round(fileInfo.Length / 1024d / 1024d, 2);
            }
            else
            {
                if (!(string.Equals(plan.ProjectType, ProjectManifestTypes.AudioCdProject, StringComparison.OrdinalIgnoreCase) &&
                      ProcessingActionIds.Normalize(resumeTrack.Action) == ProcessingActionIds.RipFlac))
                {
                    result.SetWarning(TrackWarningPresentation.Format(TrackWarningCodes.MissingSource),
                        TrackWarningCodes.MissingSource);
                }
            }
        }
        catch
        {
            result.Warning = LocalizationText.Get("Resume.Load.SourceCheckFailed");
        }

        return result;
    }

    private static bool IsDiscProject(string? projectType) =>
        string.Equals(projectType, ProjectManifestTypes.Mp3DiscProject, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(projectType, ProjectManifestTypes.AudioCdProject, StringComparison.OrdinalIgnoreCase);

    private static string GetRelativeFolderSafe(string? sourceRoot, string? sourcePath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(sourceRoot) || string.IsNullOrWhiteSpace(sourcePath))
                return string.Empty;

            var relativePath = Path.GetRelativePath(sourceRoot, sourcePath);
            return Path.GetDirectoryName(relativePath) ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string BuildProjectRelativeFolder(ProjectResumePlan plan, string relativeFolder)
    {
        if (!string.Equals(plan.ProjectType, ProjectManifestTypes.FolderProject, StringComparison.OrdinalIgnoreCase))
            return string.Empty;

        if (string.IsNullOrWhiteSpace(plan.SourceFolder))
            return relativeFolder;

        var rootName = Path.GetFileName(
            Path.GetFullPath(plan.SourceFolder)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(rootName))
            return relativeFolder;

        return string.IsNullOrWhiteSpace(relativeFolder) || relativeFolder == "."
            ? rootName
            : Path.Combine(rootName, relativeFolder);
    }
}

public sealed class ProjectResumeLoadResult
{
    public bool Success { get; init; }
    public string ErrorMessage { get; init; } = string.Empty;
    public bool IsMp3DiscProject { get; init; }
    public bool IsAudioDiscProject { get; init; }
    public bool NeedsMp3DiscImport { get; init; }
    public AudioDiscProjectManifest? AudioDiscManifest { get; init; }
    public bool IsAudioDiscProjectAwaitingRip { get; init; }
    public bool IsWaitingForManualMergeReview { get; init; }
    public bool IsCompletedProject { get; init; }
    public ProjectPipelineState PipelineState { get; init; } = ProjectPipelineState.Preparing;
    public string CurrentFolderPath { get; init; } = string.Empty;
    public string SelectedFolder { get; init; } = string.Empty;
    public ProjectMetadata? CanonicalMetadata { get; init; }
    public IReadOnlyList<TrackInfo> Tracks { get; init; } = [];
    public string StatusText { get; init; } = string.Empty;
    public string ProgressText { get; init; } = string.Empty;
    public WorkflowStatusSnapshot? LoadStatusSnapshot { get; init; }

    public static ProjectResumeLoadResult Failed(string errorMessage) => new()
    {
        Success = false,
        ErrorMessage = errorMessage
    };
}
