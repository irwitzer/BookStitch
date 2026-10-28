using System.IO;
using System.Security.Cryptography;
using System.Text;
using BookStitch.Models;

namespace BookStitch.Services;

public sealed record ChapterAutoEngineRequest(
    IList<TrackInfo> Tracks,
    string ProjectType,
    string ProjectIdentity,
    string ProjectFolder,
    ChapterCanonicalMetadataSnapshot CanonicalMetadata,
    ChapterSettings? ChapterSettings,
    bool LegacyUseLeadingZeros,
    int? EffectiveActiveChapterCount = null,
    Mp3DiscProjectManifest? Mp3DiscManifest = null,
    AudioDiscProjectManifest? AudioDiscManifest = null,
    bool AllowTransientLocalFilenameEvidence = false,
    IReadOnlyCollection<Mp3DiscPendingChapterDisc>? PendingMp3Discs = null,
    string? PendingAudioDiscIdentity = null,
    AudioDiscProjectManifestDisc? PendingAudioDisc = null);

public sealed record ChapterAutoEngineResult(
    ChapterAnalysisSnapshot AnalysisSnapshot,
    ChapterResolveResult ResolveResult,
    IReadOnlyList<EffectiveChapterInfo> RenderedTimeline);

/// <summary>
/// Productive entry point for the complete V4 auto-chapter pipeline.
/// </summary>
public sealed class ChapterAutoEngineService
{
    private readonly ChapterTimelineService _timelineService;
    private readonly ChapterAnalysisSnapshotService _snapshotService;
    private readonly ChapterResolveService _resolveService;
    private readonly ChapterTitleRenderer _titleRenderer;
    private readonly SourceMetadataSnapshotStore _snapshotStore;
    private readonly SourceCopyIdentityStore _sourceIdentityStore;
    private readonly Mp3DiscProjectService _mp3DiscProjectService;
    private readonly AudioDiscProjectService _audioDiscProjectService;

    public ChapterAutoEngineService()
        : this(
            new ChapterTimelineService(),
            new ChapterAnalysisSnapshotService(),
            new ChapterResolveService(),
            new ChapterTitleRenderer(),
            new SourceMetadataSnapshotStore(),
            new SourceCopyIdentityStore(),
            new Mp3DiscProjectService(),
            new AudioDiscProjectService())
    {
    }

    public ChapterAutoEngineService(
        ChapterTimelineService timelineService,
        ChapterAnalysisSnapshotService snapshotService,
        ChapterResolveService resolveService,
        ChapterTitleRenderer titleRenderer,
        SourceMetadataSnapshotStore snapshotStore,
        SourceCopyIdentityStore sourceIdentityStore,
        Mp3DiscProjectService mp3DiscProjectService,
        AudioDiscProjectService audioDiscProjectService)
    {
        _timelineService = timelineService ?? throw new ArgumentNullException(nameof(timelineService));
        _snapshotService = snapshotService ?? throw new ArgumentNullException(nameof(snapshotService));
        _resolveService = resolveService ?? throw new ArgumentNullException(nameof(resolveService));
        _titleRenderer = titleRenderer ?? throw new ArgumentNullException(nameof(titleRenderer));
        _snapshotStore = snapshotStore ?? throw new ArgumentNullException(nameof(snapshotStore));
        _sourceIdentityStore = sourceIdentityStore ?? throw new ArgumentNullException(nameof(sourceIdentityStore));
        _mp3DiscProjectService = mp3DiscProjectService ?? throw new ArgumentNullException(nameof(mp3DiscProjectService));
        _audioDiscProjectService = audioDiscProjectService ?? throw new ArgumentNullException(nameof(audioDiscProjectService));
    }

    public ChapterAutoEngineResult Apply(ChapterAutoEngineRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Tracks);
        ArgumentNullException.ThrowIfNull(request.CanonicalMetadata);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ProjectIdentity);

        var settings = ChapterSettingsService.Normalize(
            request.ChapterSettings,
            request.LegacyUseLeadingZeros);
        if (!string.Equals(settings.Mode, ChapterSettingsService.ModeAuto, StringComparison.Ordinal))
            throw new InvalidOperationException("The V4 auto-chapter engine can only process Auto chapter settings.");

        var sourceIdentities = LoadSourceIdentities(request.ProjectFolder);
        EnsureStableSourceIds(request.Tracks, request.ProjectIdentity, request.ProjectFolder, sourceIdentities);

        var activeTracks = request.Tracks.Where(track => !track.IsExcluded).ToList();
        ApplyTrackIndexes(request.Tracks);
        if (activeTracks.Count == 0)
        {
            var emptySnapshot = _snapshotService.Create(
                ChapterCandidateProjectionContext.ForLocalFolder(request.ProjectIdentity, []),
                [],
                [],
                request.CanonicalMetadata);
            return new ChapterAutoEngineResult(
                emptySnapshot,
                _resolveService.Resolve(emptySnapshot),
                []);
        }

        var context = BuildProjectionContext(request, sourceIdentities);
        var timeline = _timelineService.BuildTimeline(activeTracks);
        var sourceSnapshots = LoadSourceSnapshots(request.ProjectFolder, activeTracks);
        var analysisSnapshot = _snapshotService.Create(
            context,
            timeline,
            sourceSnapshots,
            request.CanonicalMetadata);
        var resolveResult = _resolveService.Resolve(analysisSnapshot);
        var resolvedById = resolveResult.Chapters.ToDictionary(
            chapter => chapter.EffectiveChapterId,
            StringComparer.Ordinal);
        var activeChapterCount = Math.Max(
            timeline.Count,
            request.EffectiveActiveChapterCount ?? 0);
        var numberingStyle = ChapterSettingsService.GetNumberingStyle(
            settings,
            request.LegacyUseLeadingZeros);
        if (request.Tracks.Count == 1 && timeline.Count == 1 && activeChapterCount == 1)
            numberingStyle = ChapterNumberingStyle.None;

        var renderedTimeline = new EffectiveChapterInfo[timeline.Count];
        for (var index = 0; index < timeline.Count; index++)
        {
            var sourceChapter = timeline[index];
            var provenance = analysisSnapshot.EffectiveChapters[index];
            EnsureCoherentMapping(sourceChapter, provenance);
            var resolved = resolvedById[provenance.EffectiveChapterId];
            renderedTimeline[index] = sourceChapter with
            {
                Title = _titleRenderer.RenderContent(
                    sourceChapter.OutputChapterIndex,
                    Math.Max(1, activeChapterCount),
                    resolved.Text,
                    numberingStyle)
            };
        }

        ApplyTrackChapterTitles(activeTracks, renderedTimeline);
        return new ChapterAutoEngineResult(
            analysisSnapshot,
            resolveResult,
            Array.AsReadOnly(renderedTimeline));
    }

    private ChapterCandidateProjectionContext BuildProjectionContext(
        ChapterAutoEngineRequest request,
        IReadOnlyList<SourceFileCopyIdentity> sourceIdentities)
    {
        if (string.Equals(request.ProjectType, ProjectManifestTypes.Mp3DiscProject, StringComparison.OrdinalIgnoreCase))
        {
            var manifest = request.Mp3DiscManifest ?? TryLoadMp3Manifest(request.ProjectFolder);
            var pendingDiscs = request.PendingMp3Discs ?? Array.Empty<Mp3DiscPendingChapterDisc>();
            if (manifest is null && pendingDiscs.Count == 0)
            {
                throw new InvalidOperationException(
                    "The MP3-CD chapter context requires either a project manifest or a transient/pending disc source.");
            }

            return ChapterCandidateProjectionContext.ForMp3Cd(
                request.ProjectIdentity,
                manifest,
                pendingDiscs);
        }

        if (string.Equals(request.ProjectType, ProjectManifestTypes.AudioCdProject, StringComparison.OrdinalIgnoreCase))
        {
            var manifest = request.AudioDiscManifest ?? TryLoadAudioManifest(request.ProjectFolder);
            if (manifest is not null)
                return ChapterCandidateProjectionContext.ForAudioCd(request.ProjectIdentity, manifest, request.PendingAudioDisc);

            if (!string.IsNullOrWhiteSpace(request.PendingAudioDiscIdentity))
            {
                return ChapterCandidateProjectionContext.ForAudioCdPreview(
                    request.ProjectIdentity,
                    request.PendingAudioDiscIdentity,
                    request.Tracks.Select(track => track.SourceId));
            }

            throw new InvalidOperationException(
                "The audio-CD chapter context requires either a project manifest or a transient disc identity.");
        }

        var identities = sourceIdentities.ToList();
        if (request.AllowTransientLocalFilenameEvidence)
        {
            identities.AddRange(request.Tracks
                .Where(track => track.SourceId != Guid.Empty && !string.IsNullOrWhiteSpace(track.FileName))
                .Select(track => new SourceFileCopyIdentity(
                    track.FilePath,
                    track.FileName,
                    0,
                    0,
                    track.SourceId)));
        }

        return ChapterCandidateProjectionContext.ForLocalFolder(request.ProjectIdentity, identities);
    }

    private IReadOnlyList<SourceMetadataSnapshot> LoadSourceSnapshots(
        string projectFolder,
        IReadOnlyList<TrackInfo> activeTracks)
    {
        var persisted = string.IsNullOrWhiteSpace(projectFolder)
            ? []
            : _snapshotStore.LoadAll(projectFolder);
        var bySourceId = persisted
            .GroupBy(snapshot => snapshot.SourceId)
            .ToDictionary(group => group.Key, group => group.First());

        foreach (var track in activeTracks)
        {
            if (!bySourceId.ContainsKey(track.SourceId))
                bySourceId.Add(track.SourceId, CreateTransientSourceSnapshot(track));
        }

        return bySourceId.Values.OrderBy(snapshot => snapshot.SourceId).ToArray();
    }

    private static SourceMetadataSnapshot CreateTransientSourceSnapshot(TrackInfo track)
    {
        var fields = track.SourceMetadataFields ?? new SourceMetadataCanonicalFields
        {
            SourceTrackNumber = track.SourceTrackNumber,
            SourceTrackTotal = track.SourceTrackTotal,
            SourceDiscNumber = track.SourceDiscNumber,
            SourceDiscTotal = track.SourceDiscTotal,
            Title = track.TagTitle
        };

        return new SourceMetadataSnapshot
        {
            SourceId = track.SourceId,
            CanonicalFields = fields,
            OriginalChapters = track.EmbeddedChapters
                .Select(chapter => new TrackEmbeddedChapterInfo
                {
                    Title = chapter.Title,
                    StartTicks = chapter.StartTicks,
                    EndTicks = chapter.EndTicks
                })
                .ToArray(),
            TechnicalInfo = new SourceTechnicalInfo { SourcePath = track.FilePath }
        }.RequireComplete();
    }

    private static void EnsureStableSourceIds(
        IList<TrackInfo> tracks,
        string projectIdentity,
        string projectFolder,
        IReadOnlyList<SourceFileCopyIdentity> identities)
    {
        var identitiesByPath = BuildIdentityPathMap(projectFolder, identities);
        foreach (var track in tracks)
        {
            if (track.SourceId != Guid.Empty)
                continue;

            if (TryResolvePersistedSourceId(track.FilePath, identitiesByPath, out var persistedSourceId))
            {
                track.SourceId = persistedSourceId;
                continue;
            }

            var sourceIdentity = BuildTransientSourceIdentity(projectIdentity, track);
            track.SourceId = CreateDeterministicGuid(sourceIdentity);
        }
    }

    private static IReadOnlyDictionary<string, Guid> BuildIdentityPathMap(
        string projectFolder,
        IReadOnlyList<SourceFileCopyIdentity> identities)
    {
        if (string.IsNullOrWhiteSpace(projectFolder))
            return new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);

        var result = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        foreach (var identity in identities.Where(identity => identity.SourceId != Guid.Empty))
        {
            var path = NormalizePath(Path.Combine(projectFolder, identity.TargetRelativePath));
            result.TryAdd(path, identity.SourceId);
        }

        return result;
    }

    private static bool TryResolvePersistedSourceId(
        string filePath,
        IReadOnlyDictionary<string, Guid> identitiesByPath,
        out Guid sourceId)
    {
        sourceId = Guid.Empty;
        if (string.IsNullOrWhiteSpace(filePath))
            return false;

        return identitiesByPath.TryGetValue(NormalizePath(filePath), out sourceId);
    }

    private static string BuildTransientSourceIdentity(string projectIdentity, TrackInfo track)
    {
        var path = string.IsNullOrWhiteSpace(track.FilePath)
            ? track.FileName
            : NormalizePath(track.FilePath);
        if (string.IsNullOrWhiteSpace(path))
        {
            path = string.Join(
                '|',
                track.SourceDiscNumber?.ToString() ?? string.Empty,
                track.SourceTrackNumber?.ToString() ?? string.Empty,
                track.DiscNumber?.ToString() ?? string.Empty,
                track.TrackNumber?.ToString() ?? string.Empty,
                track.TagTitle);
        }

        return projectIdentity.Trim() + "\n" + path;
    }

    private static Guid CreateDeterministicGuid(string identity)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(identity));
        return new Guid(hash.AsSpan(0, 16));
    }

    private static string NormalizePath(string path)
    {
        try
        {
            return Path.GetFullPath(path)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return path.Trim();
        }
    }

    private IReadOnlyList<SourceFileCopyIdentity> LoadSourceIdentities(string projectFolder)
    {
        return string.IsNullOrWhiteSpace(projectFolder)
            ? []
            : _sourceIdentityStore.Load(projectFolder);
    }

    private Mp3DiscProjectManifest? TryLoadMp3Manifest(string projectFolder)
    {
        return string.IsNullOrWhiteSpace(projectFolder)
            ? null
            : _mp3DiscProjectService.ReadForChapterProjection(projectFolder);
    }

    private AudioDiscProjectManifest? TryLoadAudioManifest(string projectFolder)
    {
        return string.IsNullOrWhiteSpace(projectFolder)
            ? null
            : _audioDiscProjectService.ReadForChapterProjection(projectFolder);
    }

    private static void EnsureCoherentMapping(
        EffectiveChapterInfo chapter,
        ChapterCandidateProvenance provenance)
    {
        if (chapter.SourceTrack.SourceId != provenance.SourceId ||
            chapter.OutputChapterIndex != provenance.CurrentOutputChapterIndex ||
            chapter.SourceEmbeddedChapter?.StartTicks != provenance.EmbeddedStartTicks ||
            chapter.SourceEmbeddedChapter?.EndTicks != provenance.EmbeddedEndTicks)
        {
            throw new InvalidOperationException(
                "The resolved chapter result no longer matches its effective chapter timeline.");
        }
    }

    private static void ApplyTrackIndexes(IList<TrackInfo> tracks)
    {
        var activeIndex = 0;
        foreach (var track in tracks)
            track.Index = track.IsExcluded ? 0 : ++activeIndex;
    }

    private static void ApplyTrackChapterTitles(
        IReadOnlyList<TrackInfo> activeTracks,
        IReadOnlyList<EffectiveChapterInfo> renderedTimeline)
    {
        var firstChapterByTrack = renderedTimeline
            .GroupBy(chapter => chapter.SourceTrack, ReferenceEqualityComparer<TrackInfo>.Instance)
            .ToDictionary(
                group => group.Key,
                group => group.First().Title,
                ReferenceEqualityComparer<TrackInfo>.Instance);

        foreach (var track in activeTracks)
            track.ChapterTitle = firstChapterByTrack.GetValueOrDefault(track) ?? string.Empty;
    }
}
