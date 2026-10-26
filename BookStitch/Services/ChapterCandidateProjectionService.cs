using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using BookStitch.Models;

namespace BookStitch.Services;

public sealed record ChapterCandidateProjectionContext(
    ChapterProjectSourceKind ProjectKind,
    string ProjectIdentity,
    IReadOnlyDictionary<Guid, string> SourceFilenames,
    IReadOnlyDictionary<Guid, string> ScopeProvenanceBySourceId)
{
    public static ChapterCandidateProjectionContext ForLocalFolder(
        string projectIdentity,
        IReadOnlyCollection<SourceFileCopyIdentity> sourceIdentities)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectIdentity);
        ArgumentNullException.ThrowIfNull(sourceIdentities);

        return new(
            ChapterProjectSourceKind.LocalFolder,
            projectIdentity.Trim(),
            BuildUniqueMap(sourceIdentities, item => item.SourceId, item => item.SourceFileName),
            new Dictionary<Guid, string>());
    }

    public static ChapterCandidateProjectionContext ForMp3Cd(
        string projectIdentity,
        Mp3DiscProjectManifest? manifest,
        IReadOnlyCollection<Mp3DiscPendingChapterDisc>? pendingDiscs = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectIdentity);

        var filenames = new Dictionary<Guid, string>();
        var scopeIdentities = new Dictionary<Guid, string>();
        var completedDiscNumbers = new HashSet<int>();

        foreach (var disc in (manifest?.ImportedDiscs ?? Enumerable.Empty<Mp3DiscProjectManifestDisc>()).OrderBy(item => item.DiscNumber))
        {
            completedDiscNumbers.Add(disc.DiscNumber);

            AddMp3DiscSources(
                filenames,
                scopeIdentities,
                disc.Signature,
                disc.SourceFiles.Select(source => new Mp3DiscPlannedSourceIdentity(
                    source.SourceId,
                    source.RelativePath,
                    source.SourceFileName,
                    source.SourceSizeBytes,
                    source.SourceLastWriteUtcTicks)));
        }

        foreach (var pending in (pendingDiscs ?? []).OrderBy(item => item.DiscNumber))
        {
            if (completedDiscNumbers.Contains(pending.DiscNumber))
            {
                continue;
            }

            AddMp3DiscSources(
                filenames,
                scopeIdentities,
                pending.Signature,
                pending.Sources);
        }

        return new(
            ChapterProjectSourceKind.Mp3Cd,
            projectIdentity.Trim(),
            filenames,
            scopeIdentities);
    }

    private static void AddMp3DiscSources(
        IDictionary<Guid, string> filenames,
        IDictionary<Guid, string> scopeIdentities,
        string? signature,
        IEnumerable<Mp3DiscPlannedSourceIdentity> sources)
    {
        var materialized = sources.ToArray();
        var discIdentity = BuildDiscIdentity(
            "mp3",
            signature,
            materialized.Select(source => source.SourceId));

        foreach (var source in materialized.OrderBy(item => item.RelativePath, StringComparer.Ordinal))
        {
            if (source.SourceId == Guid.Empty)
                continue;

            filenames.TryAdd(source.SourceId, source.SourceFileName);
            scopeIdentities.TryAdd(source.SourceId, discIdentity);
        }
    }

    public static ChapterCandidateProjectionContext ForAudioCd(
        string projectIdentity,
        AudioDiscProjectManifest manifest,
        AudioDiscProjectManifestDisc? pendingDisc = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectIdentity);
        ArgumentNullException.ThrowIfNull(manifest);

        var scopeIdentities = new Dictionary<Guid, string>();
        foreach (var disc in manifest.Discs
            .Concat(pendingDisc is null || manifest.Discs.Any(disc => disc.DiscNumber == pendingDisc.DiscNumber)
                ? [] : new[] { pendingDisc }).OrderBy(item => item.DiscNumber))
        {
            var discIdentity = BuildDiscIdentity(
                "audio",
                disc.DiscIdentity,
                disc.Tracks.Select(track => track.SourceId));
            foreach (var track in disc.Tracks.OrderBy(item => item.GlobalIndex))
            {
                if (track.SourceId != Guid.Empty)
                    scopeIdentities.TryAdd(track.SourceId, discIdentity);
            }
        }

        return new(
            ChapterProjectSourceKind.AudioCd,
            projectIdentity.Trim(),
            new Dictionary<Guid, string>(),
            scopeIdentities);
    }

    public static ChapterCandidateProjectionContext ForAudioCdPreview(
        string projectIdentity,
        string discIdentity,
        IEnumerable<Guid> sourceIds)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectIdentity);
        ArgumentException.ThrowIfNullOrWhiteSpace(discIdentity);
        ArgumentNullException.ThrowIfNull(sourceIds);

        var stableSourceIds = sourceIds
            .Where(sourceId => sourceId != Guid.Empty)
            .Distinct()
            .OrderBy(sourceId => sourceId)
            .ToArray();
        if (stableSourceIds.Length == 0)
        {
            throw new InvalidOperationException(
                "An audio-CD preview requires at least one stable SourceId.");
        }

        var stableDiscIdentity = BuildDiscIdentity("audio", discIdentity, stableSourceIds);
        return new(
            ChapterProjectSourceKind.AudioCd,
            projectIdentity.Trim(),
            new Dictionary<Guid, string>(),
            stableSourceIds.ToDictionary(sourceId => sourceId, _ => stableDiscIdentity));
    }

    private static IReadOnlyDictionary<Guid, string> BuildUniqueMap<T>(
        IEnumerable<T> values,
        Func<T, Guid> idSelector,
        Func<T, string> valueSelector)
    {
        var result = new Dictionary<Guid, string>();
        foreach (var value in values)
        {
            var id = idSelector(value);
            if (id != Guid.Empty)
                result.TryAdd(id, valueSelector(value) ?? string.Empty);
        }

        return result;
    }

    private static string BuildDiscIdentity(
        string prefix,
        string? identity,
        IEnumerable<Guid> sourceIds)
    {
        var stableIdentity = identity?.Trim();
        if (!string.IsNullOrWhiteSpace(stableIdentity))
            return prefix + ":" + stableIdentity;

        var stableSources = sourceIds
            .Where(sourceId => sourceId != Guid.Empty)
            .Distinct()
            .OrderBy(sourceId => sourceId)
            .Select(sourceId => sourceId.ToString("N", CultureInfo.InvariantCulture))
            .ToArray();
        if (stableSources.Length == 0)
        {
            throw new InvalidOperationException(
                $"A {prefix} disc without a persisted identity requires at least one stable SourceId.");
        }

        var canonical = string.Join('\n', stableSources);
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
        return prefix + ":sources:" + hash;
    }
}

public sealed class ChapterCandidateProjectionService
{
    private readonly ChapterCandidateNormalizer _normalizer;

    public ChapterCandidateProjectionService()
        : this(new ChapterCandidateNormalizer())
    {
    }

    public ChapterCandidateProjectionService(ChapterCandidateNormalizer normalizer)
    {
        _normalizer = normalizer ?? throw new ArgumentNullException(nameof(normalizer));
    }

    public ChapterCandidateProjectionResult Project(
        ChapterCandidateProjectionContext context,
        IReadOnlyList<EffectiveChapterInfo> effectiveChapters,
        IReadOnlyList<SourceMetadataSnapshot> sourceSnapshots,
        ProjectMetadata projectMetadata)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(effectiveChapters);
        ArgumentNullException.ThrowIfNull(sourceSnapshots);
        ArgumentNullException.ThrowIfNull(projectMetadata);

        return Project(
            context,
            effectiveChapters,
            sourceSnapshots,
            ChapterCanonicalMetadataSnapshot.Capture(projectMetadata));
    }

    public ChapterCandidateProjectionResult Project(
        ChapterCandidateProjectionContext context,
        IReadOnlyList<EffectiveChapterInfo> effectiveChapters,
        IReadOnlyList<SourceMetadataSnapshot> sourceSnapshots,
        ChapterCanonicalMetadataSnapshot canonicalMetadata)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(effectiveChapters);
        ArgumentNullException.ThrowIfNull(sourceSnapshots);
        ArgumentNullException.ThrowIfNull(canonicalMetadata);

        var snapshotsById = sourceSnapshots
            .Where(snapshot => snapshot.SourceId != Guid.Empty)
            .GroupBy(snapshot => snapshot.SourceId)
            .ToDictionary(group => group.Key, group => group.First());

        var activeChapters = effectiveChapters
            .Where(chapter => !chapter.SourceTrack.IsExcluded)
            .ToArray();
        var embeddedCounts = activeChapters
            .Where(chapter => chapter.SourceEmbeddedChapter is not null)
            .GroupBy(chapter => chapter.SourceTrack, ReferenceEqualityComparer<TrackInfo>.Instance)
            .ToDictionary(group => group.Key, group => group.Count(), ReferenceEqualityComparer<TrackInfo>.Instance);

        var provenances = new List<ChapterCandidateProvenance>(activeChapters.Length);
        var candidates = new List<ProjectedChapterCandidate>();
        var embeddedSequenceBySource = new Dictionary<Guid, int>();
        var embeddedGeometryOccurrences = new Dictionary<EmbeddedGeometryKey, int>();

        var trackOccurrences = new Dictionary<TrackInfo, int>(ReferenceEqualityComparer<TrackInfo>.Instance);
        var sourceOccurrences = new Dictionary<Guid, int>();
        TrackInfo? previousTrack = null;
        for (var index = 0; index < activeChapters.Length; index++)
        {
            var chapter = activeChapters[index];
            var track = chapter.SourceTrack;
            if (track.SourceId == Guid.Empty)
                throw new ArgumentException("Every projected chapter requires a stable SourceId.", nameof(effectiveChapters));

            if (!ReferenceEquals(previousTrack, track))
            {
                embeddedSequenceBySource.Clear();
                embeddedGeometryOccurrences.Clear();
                previousTrack = track;
            }
            if (!trackOccurrences.TryGetValue(track, out var occurrence))
            {
                sourceOccurrences.TryGetValue(track.SourceId, out occurrence);
                sourceOccurrences[track.SourceId] = ++occurrence;
                trackOccurrences.Add(track, occurrence);
            }
            snapshotsById.TryGetValue(track.SourceId, out var snapshot);
            var embeddedOrdinal = ResolveEmbeddedOrdinal(
                track.SourceId,
                chapter.SourceEmbeddedChapter,
                snapshot,
                embeddedSequenceBySource,
                embeddedGeometryOccurrences);
            var hasMultipleEmbedded = embeddedCounts.GetValueOrDefault(track) > 1;
            var provenance = new ChapterCandidateProvenance(
                context.ProjectKind,
                context.ProjectIdentity,
                track.SourceId,
                BuildEffectiveChapterId(track.SourceId, chapter.SourceEmbeddedChapter, embeddedOrdinal) +
                    (occurrence > 1 ? $":occurrence:{occurrence}" : string.Empty),
                ResolveScopeProvenance(context, track.SourceId),
                index + 1,
                chapter.OutputChapterIndex,
                snapshot?.CanonicalFields.SourceTrackNumber ?? track.SourceTrackNumber,
                snapshot?.CanonicalFields.SourceDiscNumber ?? track.SourceDiscNumber,
                embeddedOrdinal,
                chapter.SourceEmbeddedChapter?.StartTicks,
                chapter.SourceEmbeddedChapter?.EndTicks,
                hasMultipleEmbedded,
                occurrence);

            provenances.Add(provenance);
            AddSourceCandidates(candidates, provenance, chapter, snapshot, context);
            AddCanonicalCandidate(
                candidates,
                provenance,
                canonicalMetadata.TitleValue,
                canonicalMetadata.TitleOrigin,
                ChapterCandidateSource.CanonicalTitle);
            AddCanonicalCandidate(
                candidates,
                provenance,
                canonicalMetadata.AlbumValue,
                canonicalMetadata.AlbumOrigin,
                ChapterCandidateSource.CanonicalAlbum);
        }

        return new ChapterCandidateProjectionResult(
            Array.AsReadOnly(provenances.ToArray()),
            Array.AsReadOnly(candidates.ToArray()));
    }

    private void AddSourceCandidates(
        ICollection<ProjectedChapterCandidate> candidates,
        ChapterCandidateProvenance provenance,
        EffectiveChapterInfo chapter,
        SourceMetadataSnapshot? snapshot,
        ChapterCandidateProjectionContext context)
    {
        if (chapter.SourceEmbeddedChapter is not null && snapshot is not null)
        {
            var originalChapter = ResolveOriginalChapter(chapter.SourceEmbeddedChapter, provenance.EmbeddedOrdinal, snapshot);
            AddCandidate(candidates, provenance, ChapterCandidateSource.EmbeddedChapterTitle, originalChapter?.Title);
        }

        var trackTitle = snapshot?.CanonicalFields.Title ?? chapter.SourceTrack.SourceMetadataFields?.Title;
        AddCandidate(candidates, provenance, ChapterCandidateSource.TrackTitle, trackTitle);

        if (context.ProjectKind != ChapterProjectSourceKind.AudioCd &&
            context.SourceFilenames.TryGetValue(provenance.SourceId, out var sourceFilename))
        {
            AddCandidate(candidates, provenance, ChapterCandidateSource.Filename, sourceFilename);
        }
    }

    private void AddCanonicalCandidate(
        ICollection<ProjectedChapterCandidate> candidates,
        ChapterCandidateProvenance provenance,
        string? value,
        MetadataOrigin origin,
        ChapterCandidateSource source)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        var normalization = origin == MetadataOrigin.User
            ? _normalizer.NormalizeUserDefinedCanonical(source, value)
            : _normalizer.Normalize(source, value);

        candidates.Add(new ProjectedChapterCandidate(source, provenance, normalization, origin));
    }

    private void AddCandidate(
        ICollection<ProjectedChapterCandidate> candidates,
        ChapterCandidateProvenance provenance,
        ChapterCandidateSource source,
        string? rawText)
    {
        if (string.IsNullOrWhiteSpace(rawText))
            return;

        candidates.Add(new ProjectedChapterCandidate(
            source,
            provenance,
            _normalizer.Normalize(source, rawText)));
    }

    private static int? ResolveEmbeddedOrdinal(
        Guid sourceId,
        TrackEmbeddedChapterInfo? embeddedChapter,
        SourceMetadataSnapshot? snapshot,
        IDictionary<Guid, int> sequenceBySource,
        IDictionary<EmbeddedGeometryKey, int> geometryOccurrences)
    {
        if (embeddedChapter is null)
            return null;

        sequenceBySource.TryGetValue(sourceId, out var previousSequence);
        var sequence = previousSequence + 1;
        sequenceBySource[sourceId] = sequence;
        var geometryKey = new EmbeddedGeometryKey(
            sourceId,
            embeddedChapter.StartTicks,
            embeddedChapter.EndTicks);
        geometryOccurrences.TryGetValue(geometryKey, out var previousGeometryOccurrence);
        var geometryOccurrence = previousGeometryOccurrence + 1;
        geometryOccurrences[geometryKey] = geometryOccurrence;

        var chapters = snapshot?.OriginalChapters
            .OrderBy(chapter => chapter.StartTicks)
            .ThenBy(chapter => chapter.EndTicks)
            .ToArray() ?? [];

        var matchingOrdinal = 0;
        for (var index = 0; index < chapters.Length; index++)
        {
            if (chapters[index].StartTicks == embeddedChapter.StartTicks &&
                chapters[index].EndTicks == embeddedChapter.EndTicks)
            {
                matchingOrdinal++;
                if (matchingOrdinal == geometryOccurrence)
                    return index + 1;
            }
        }

        return sequence;
    }

    private static TrackEmbeddedChapterInfo? ResolveOriginalChapter(
        TrackEmbeddedChapterInfo current,
        int? ordinal,
        SourceMetadataSnapshot snapshot)
    {
        var chapters = snapshot.OriginalChapters
            .OrderBy(chapter => chapter.StartTicks)
            .ThenBy(chapter => chapter.EndTicks)
            .ToArray();

        if (ordinal is > 0 && ordinal <= chapters.Length)
        {
            var ordinalChapter = chapters[ordinal.Value - 1];
            if (ordinalChapter.StartTicks == current.StartTicks &&
                ordinalChapter.EndTicks == current.EndTicks)
            {
                return ordinalChapter;
            }
        }

        var geometryMatch = chapters.FirstOrDefault(chapter =>
            chapter.StartTicks == current.StartTicks && chapter.EndTicks == current.EndTicks);
        if (geometryMatch is not null)
            return geometryMatch;

        return ordinal is > 0 && ordinal <= chapters.Length
            ? chapters[ordinal.Value - 1]
            : null;
    }

    private static string BuildEffectiveChapterId(
        Guid sourceId,
        TrackEmbeddedChapterInfo? embeddedChapter,
        int? embeddedOrdinal)
    {
        var source = sourceId.ToString("N", CultureInfo.InvariantCulture);
        if (embeddedChapter is null)
            return source + ":track";

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{source}:embedded:{embeddedOrdinal ?? 0}:{embeddedChapter.StartTicks}:{embeddedChapter.EndTicks}");
    }

    private static string ResolveScopeProvenance(ChapterCandidateProjectionContext context, Guid sourceId)
    {
        if (context.ScopeProvenanceBySourceId.TryGetValue(sourceId, out var identity) &&
            !string.IsNullOrWhiteSpace(identity))
        {
            return identity;
        }

        return context.ProjectKind == ChapterProjectSourceKind.LocalFolder
            ? context.ProjectIdentity
            : throw new InvalidOperationException(
                $"No stable disc provenance exists for source '{sourceId:N}' in {context.ProjectKind}.");
    }

    private readonly record struct EmbeddedGeometryKey(
        Guid SourceId,
        long StartTicks,
        long EndTicks);
}
