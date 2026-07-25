using BookStitch.Models;
using System.IO;

namespace BookStitch.Services;

public sealed class ProjectResumeLoadWorkflowService
{
    private readonly AudioDiscProjectService _audioDiscProjectService;
    private readonly TrackListStateService _trackListStateService;

    public ProjectResumeLoadWorkflowService(
        AudioDiscProjectService audioDiscProjectService,
        TrackListStateService trackListStateService)
    {
        _audioDiscProjectService = audioDiscProjectService;
        _trackListStateService = trackListStateService;
    }

    public ProjectResumeLoadResult Prepare(ProjectResumePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

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
                "Das Audio-CD-Projektmanifest fehlt oder ist beschädigt. Das Projekt wurde nicht als lokales Projekt geöffnet.");
        }

        var currentFolderPath = isMp3DiscProject
            ? plan.ProjectFolder
            : plan.SourceFolder;
        if (string.IsNullOrWhiteSpace(currentFolderPath) || !Directory.Exists(currentFolderPath))
            currentFolderPath = plan.ProjectFolder;

        var tracks = plan.Tracks
            .OrderBy(track => track.TrackIndex)
            .Select(track => BuildTrackInfo(plan, track))
            .ToList();
        tracks = _trackListStateService.Apply(plan.ProjectFolder, tracks);

        var awaitingAudioDiscRip = isAudioDiscProject &&
            audioDiscManifest is not null &&
            !_audioDiscProjectService.IsProjectRipCompleted(audioDiscManifest);
        var pipelineState = awaitingAudioDiscRip || (isMp3DiscProject && plan.CanContinueDiscImport)
            ? ProjectPipelineState.AcquiringSources
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
        var totalChapters = Math.Max(0, tracks.Count(track => !track.IsExcluded));
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

        return Math.Max(1, Math.Max(plan.TotalDiscs, maxTrackDisc));
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
                $"Audio-CD-Projekt geladen. Fortsetzung ab Disc {nextAudioDisc} von {plan.TotalDiscs}.",
                "Lege die benötigte Audio-CD ein und klicke auf „Start“; bereits gerippte Tracks bleiben erhalten.");
        }

        if (plan.CanContinueDiscImport && plan.NextMissingDiscNumber is int nextDisc)
        {
            return (
                $"Projekt geladen. Es fehlt noch CD {nextDisc} von {plan.TotalDiscs}.",
                $"Projekt geladen. Import kann später ab CD {nextDisc} fortgesetzt werden.");
        }

        if (completedProject)
        {
            return (
                "Projekt geladen. Mindestens ein Hörbuchexport wurde bereits erfolgreich erstellt.",
                "Trackliste und Preset prüfen. Vorhandenes Preset zusammenfügen oder ein fehlendes Preset neu konvertieren.");
        }

        if (waitingForManualMergeReview)
        {
            return (
                "Projekt geladen. Die Originalquellen sind vollständig und das Projekt ist bereit zur Prüfung.",
                "Trackliste und Preset prüfen. Danach zusammenfügen oder ein fehlendes Preset neu konvertieren.");
        }

        return (
            "Projekt geladen.",
            string.Empty);
    }

    private static TrackInfo BuildTrackInfo(ProjectResumePlan plan, ProjectResumeTrackItem resumeTrack)
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
            Index = resumeTrack.TrackIndex,
            DiscNumber = resumeTrack.DiscNumber,
            TrackNumber = resumeTrack.TrackNumber,
            FilePath = sourcePath,
            FileName = fileName,
            RelativeFolder = relativeFolder,
            Extension = Path.GetExtension(fileName).TrimStart('.').ToUpperInvariant(),
            TagTitle = string.Empty,
            Artist = string.Empty,
            ChapterTitle = string.IsNullOrWhiteSpace(resumeTrack.ChapterTitle)
                ? Path.GetFileNameWithoutExtension(fileName)
                : resumeTrack.ChapterTitle,
            Duration = resumeTrack.Duration ?? string.Empty,
            DurationTicks = resumeTrack.DurationTicks,
            ProcessingAction = resumeTrack.Action ?? string.Empty,
            PreparedConvertedPath = resumeTrack.ConvertedPath ?? string.Empty,
            PreparedConvertedPreset = resumeTrack.Preset ?? string.Empty
        };

        result.HasReusableConvertedFile =
            string.Equals(result.PreparedConvertedPreset, plan.SelectedPreset, StringComparison.OrdinalIgnoreCase) &&
            PreparedConvertedTrackReuseService.CanReuseForDiscProject(
                plan.ProjectType,
                result.FilePath,
                result.PreparedConvertedPath);

        try
        {
            if (!string.IsNullOrWhiteSpace(sourcePath) && File.Exists(sourcePath))
            {
                var fileInfo = new FileInfo(sourcePath);
                result.SizeMb = Math.Round(fileInfo.Length / 1024d / 1024d, 2);
            }
            else
            {
                result.Warning = string.Equals(plan.ProjectType, ProjectManifestTypes.AudioCdProject, StringComparison.OrdinalIgnoreCase) &&
                                 string.Equals(resumeTrack.Action, "FLAC rippen", StringComparison.OrdinalIgnoreCase)
                    ? string.Empty
                    : "Quelldatei fehlt";
            }
        }
        catch
        {
            result.Warning = "Quelldatei konnte nicht geprüft werden";
        }

        return result;
    }

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
