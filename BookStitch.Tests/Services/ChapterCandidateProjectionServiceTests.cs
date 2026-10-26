using BookStitch.Models;
using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class ChapterCandidateProjectionServiceTests
{
    private readonly ChapterCandidateProjectionService _service = new();

    [Fact]
    public void Project_LocalFolderUsesImmutableSourceEvidenceAndExternalFilename()
    {
        var sourceId = Guid.NewGuid();
        var track = CreateTrack(sourceId, "working-convert-9f4a.m4a", "Working title");
        track.ChapterTitle = "001 Alter Auto-Fallback";
        var chapter = CreateEffectiveChapter(track, title: "Timeline fallback");
        var snapshot = CreateSnapshot(sourceId, "Source Track Title");
        var context = ChapterCandidateProjectionContext.ForLocalFolder(
            "project-a",
            [new SourceFileCopyIdentity("originals\\working.m4a", "003_External_Source.mp3", 10, 20, sourceId)]);

        var result = _service.Project(context, [chapter], [snapshot], new ProjectMetadata());

        Assert.Equal("Source Track Title", Candidate(result, ChapterCandidateSource.TrackTitle).Normalization.RawText);
        Assert.Equal("External Source", Candidate(result, ChapterCandidateSource.Filename).Normalization.NormalizedText);
        Assert.DoesNotContain(result.Candidates, candidate =>
            candidate.Normalization.RawText is "working-convert-9f4a.m4a" or "Timeline fallback" or "001 Alter Auto-Fallback");
    }

    [Fact]
    public void Project_EmbeddedTitleComesFromOriginalSnapshotInsteadOfTimelineTitle()
    {
        var sourceId = Guid.NewGuid();
        var embedded = new TrackEmbeddedChapterInfo { Title = "Working Embedded", StartTicks = 10, EndTicks = 20 };
        var track = CreateTrack(sourceId, "working.m4a", "Working title");
        track.EmbeddedChapters = [embedded];
        var chapter = CreateEffectiveChapter(track, embedded, "Resolved old title");
        var snapshot = CreateSnapshot(
            sourceId,
            "Source Track",
            [new TrackEmbeddedChapterInfo { Title = "Original Embedded", StartTicks = 10, EndTicks = 20 }]);
        var context = ChapterCandidateProjectionContext.ForLocalFolder("project-a", []);

        var result = _service.Project(context, [chapter], [snapshot], new ProjectMetadata());

        Assert.Equal(
            "Original Embedded",
            Candidate(result, ChapterCandidateSource.EmbeddedChapterTitle).Normalization.RawText);
    }

    [Fact]
    public void Project_DuplicateEmbeddedGeometryGetsDistinctStableEffectiveIdentities()
    {
        var sourceId = Guid.NewGuid();
        var first = new TrackEmbeddedChapterInfo { Title = "Erste Fassung", StartTicks = 14, EndTicks = 200 };
        var second = new TrackEmbeddedChapterInfo { Title = "Zweite Fassung", StartTicks = 14, EndTicks = 200 };
        var track = CreateTrack(sourceId, "duplicate-chapters.m4b", "Container");
        track.EmbeddedChapters = [first, second];
        var snapshot = CreateSnapshot(
            sourceId,
            "Container",
            [
                new TrackEmbeddedChapterInfo { Title = "Original Eins", StartTicks = 14, EndTicks = 200 },
                new TrackEmbeddedChapterInfo { Title = "Original Zwei", StartTicks = 14, EndTicks = 200 }
            ]);

        var result = _service.Project(
            ChapterCandidateProjectionContext.ForLocalFolder("project-a", []),
            [
                CreateEffectiveChapter(track, first, outputIndex: 1),
                CreateEffectiveChapter(track, second, outputIndex: 2)
            ],
            [snapshot],
            new ProjectMetadata());

        Assert.Equal(2, result.EffectiveChapters.Select(chapter => chapter.EffectiveChapterId).Distinct().Count());
        Assert.Equal([1, 2], result.EffectiveChapters.Select(chapter => chapter.EmbeddedOrdinal));
        Assert.Equal(
            ["Original Eins", "Original Zwei"],
            result.Candidates
                .Where(candidate => candidate.Source == ChapterCandidateSource.EmbeddedChapterTitle)
                .OrderBy(candidate => candidate.Provenance.EmbeddedOrdinal)
                .Select(candidate => candidate.Normalization.RawText));
    }

    [Fact]
    public void Project_Mp3CdUsesManifestSourceFilenameAndDiscProvenance()
    {
        var sourceId = Guid.NewGuid();
        var manifest = new Mp3DiscProjectManifest
        {
            ImportedDiscs =
            [
                new Mp3DiscProjectManifestDisc
                {
                    DiscNumber = 2,
                    Signature = "disc-signature-2",
                    SourceFiles =
                    [
                        new Mp3DiscProjectManifestSourceFile
                        {
                            SourceId = sourceId,
                            SourceFileName = "07_Reise.mp3",
                            RelativePath = "Audio\\07_Reise.mp3"
                        }
                    ]
                }
            ]
        };
        var context = ChapterCandidateProjectionContext.ForMp3Cd("project-mp3", manifest);

        var result = _service.Project(
            context,
            [CreateEffectiveChapter(CreateTrack(sourceId, "copied.m4a", ""))],
            [CreateSnapshot(sourceId, "")],
            new ProjectMetadata());

        var filename = Candidate(result, ChapterCandidateSource.Filename);
        Assert.Equal("Reise", filename.Normalization.NormalizedText);
        Assert.Equal("mp3:disc-signature-2", filename.Provenance.ScopeProvenanceIdentity);
    }

    [Fact]
    public void Mp3_context_includes_accepted_repeated_media_as_a_new_logical_disc()
    {
        var firstId = Guid.NewGuid();
        var repeatedId = Guid.NewGuid();
        var manifest = new Mp3DiscProjectManifest
        {
            ImportedDiscs = [new Mp3DiscProjectManifestDisc
            {
                DiscNumber = 1, Signature = "same-disc",
                SourceFiles = [new Mp3DiscProjectManifestSourceFile { RelativePath = "CD 01/001.mp3", SourceFileName = "001.mp3", SourceId = firstId }]
            }]
        };
        var repeated = new Mp3DiscPendingChapterDisc(2, "same-disc",
            [new Mp3DiscPlannedSourceIdentity(repeatedId, "001.mp3", "001.mp3", 10, 20)]);
        var context = ChapterCandidateProjectionContext.ForMp3Cd("project", manifest, [repeated]);
        Assert.Equal(2, context.SourceFilenames.Count);
        Assert.Contains(firstId, context.ScopeProvenanceBySourceId.Keys);
        Assert.Contains(repeatedId, context.ScopeProvenanceBySourceId.Keys);
        manifest.ImportedDiscs.Add(new Mp3DiscProjectManifestDisc
        {
            DiscNumber = 2, Signature = "same-disc",
            SourceFiles = [new Mp3DiscProjectManifestSourceFile { RelativePath = "CD 02/001.mp3", SourceFileName = "001.mp3", SourceId = repeatedId }]
        });
        var completed = ChapterCandidateProjectionContext.ForMp3Cd("project", manifest, [repeated]);
        Assert.Equal(context.SourceFilenames.OrderBy(item => item.Key), completed.SourceFilenames.OrderBy(item => item.Key));
    }

    [Fact]
    public void Project_Mp3CdUsesPendingAcceptedDiscBeforeManifestCompletion()
    {
        var sourceId = Guid.NewGuid();
        var pending = new Mp3DiscPendingChapterDisc(
            2,
            "pending-disc-signature",
            [
                new Mp3DiscPlannedSourceIdentity(
                    sourceId,
                    "Audio\\07_Reise.mp3",
                    "07_Reise.mp3",
                    123,
                    456)
            ]);
        var context = ChapterCandidateProjectionContext.ForMp3Cd(
            "project-mp3",
            new Mp3DiscProjectManifest { ImportedDiscs = [] },
            [pending]);

        var result = _service.Project(
            context,
            [CreateEffectiveChapter(CreateTrack(sourceId, "preview.mp3", ""))],
            [CreateSnapshot(sourceId, "")],
            new ProjectMetadata());

        var filename = Candidate(result, ChapterCandidateSource.Filename);
        Assert.Equal("Reise", filename.Normalization.NormalizedText);
        Assert.Equal("mp3:pending-disc-signature", filename.Provenance.ScopeProvenanceIdentity);
    }

    [Fact]
    public void Project_Mp3CdPreviewUsesPendingSourceWithoutPersistedManifest()
    {
        var sourceId = Guid.NewGuid();
        var pending = new Mp3DiscPendingChapterDisc(
            1,
            "preview-disc-signature",
            [
                new Mp3DiscPlannedSourceIdentity(
                    sourceId,
                    "9783837165128_001_225.mp3",
                    "9783837165128_001_225.mp3",
                    123,
                    456)
            ]);
        var context = ChapterCandidateProjectionContext.ForMp3Cd(
            "mp3-preview:preview-disc-signature",
            manifest: null,
            pendingDiscs: [pending]);

        var result = _service.Project(
            context,
            [CreateEffectiveChapter(CreateTrack(sourceId, "preview.mp3", "Vorspann"))],
            [CreateSnapshot(sourceId, "Vorspann")],
            new ProjectMetadata());

        var provenance = Assert.Single(result.EffectiveChapters);
        Assert.Equal(ChapterProjectSourceKind.Mp3Cd, provenance.ProjectKind);
        Assert.Equal("mp3:preview-disc-signature", provenance.ScopeProvenanceIdentity);
        Assert.Equal(
            "9783837165128 001 225",
            Candidate(result, ChapterCandidateSource.Filename).Normalization.NormalizedText);
    }

    [Fact]
    public void Project_AudioCdNeverCreatesFilenameCandidate()
    {
        var sourceId = Guid.NewGuid();
        var manifest = new AudioDiscProjectManifest
        {
            Discs =
            [
                new AudioDiscProjectManifestDisc
                {
                    DiscNumber = 1,
                    DiscIdentity = "audio-disc-1",
                    Tracks = [new AudioDiscProjectManifestTrack { SourceId = sourceId, GlobalIndex = 1 }]
                }
            ]
        };
        var context = ChapterCandidateProjectionContext.ForAudioCd("project-audio", manifest);

        var result = _service.Project(
            context,
            [CreateEffectiveChapter(CreateTrack(sourceId, "Track01.flac", ""))],
            [CreateSnapshot(sourceId, "Track 01")],
            new ProjectMetadata());

        Assert.DoesNotContain(result.Candidates, candidate => candidate.Source == ChapterCandidateSource.Filename);
        Assert.Equal("audio:audio-disc-1", Assert.Single(result.EffectiveChapters).ScopeProvenanceIdentity);
    }

    [Fact]
    public void Project_AudioCdPreviewUsesDiscScopeAndNeverCreatesFilenameCandidate()
    {
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var context = ChapterCandidateProjectionContext.ForAudioCdPreview(
            "audio-preview:disc-identity",
            "disc-identity",
            [firstId, secondId]);
        var firstTrack = CreateTrack(firstId, "Track01.flac", "Track 01");
        var secondTrack = CreateTrack(secondId, "Track02.flac", "Track 02");

        var result = _service.Project(
            context,
            [CreateEffectiveChapter(firstTrack), CreateEffectiveChapter(secondTrack)],
            [CreateSnapshot(firstId, "Track 01"), CreateSnapshot(secondId, "Track 02")],
            new ProjectMetadata());

        Assert.DoesNotContain(result.Candidates, candidate => candidate.Source == ChapterCandidateSource.Filename);
        Assert.All(result.EffectiveChapters, provenance =>
        {
            Assert.Equal(ChapterProjectSourceKind.AudioCd, provenance.ProjectKind);
            Assert.Equal("audio:disc-identity", provenance.ScopeProvenanceIdentity);
        });
    }

    [Fact]
    public void ProjectionContext_MissingMp3IdentityUsesStableSourceIdsOnly()
    {
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var first = Mp3ManifestWithoutIdentity(
            1,
            (firstId, "working-a.mp3"),
            (secondId, "working-b.mp3"));
        var reordered = Mp3ManifestWithoutIdentity(
            9,
            (secondId, "different-working-b.mp3"),
            (firstId, "different-working-a.mp3"));

        var firstContext = ChapterCandidateProjectionContext.ForMp3Cd("project", first);
        var reorderedContext = ChapterCandidateProjectionContext.ForMp3Cd("project", reordered);

        var identity = firstContext.ScopeProvenanceBySourceId[firstId];
        Assert.StartsWith("mp3:sources:", identity, StringComparison.Ordinal);
        Assert.Equal(identity, firstContext.ScopeProvenanceBySourceId[secondId]);
        Assert.Equal(identity, reorderedContext.ScopeProvenanceBySourceId[firstId]);
        Assert.Equal(identity, reorderedContext.ScopeProvenanceBySourceId[secondId]);
    }

    [Fact]
    public void ProjectionContext_DifferentSourceIdsProduceDifferentDiscFallbackIdentity()
    {
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var first = ChapterCandidateProjectionContext.ForAudioCd(
            "project",
            AudioManifestWithoutIdentity(1, firstId));
        var second = ChapterCandidateProjectionContext.ForAudioCd(
            "project",
            AudioManifestWithoutIdentity(1, secondId));

        Assert.NotEqual(
            first.ScopeProvenanceBySourceId[firstId],
            second.ScopeProvenanceBySourceId[secondId]);
    }

    [Fact]
    public void ProjectionContext_DiscWithoutIdentityOrSourceIdFailsClearly()
    {
        var manifest = Mp3ManifestWithoutIdentity(1, (Guid.Empty, "working.mp3"));

        Assert.Throws<InvalidOperationException>(() =>
            ChapterCandidateProjectionContext.ForMp3Cd("project", manifest));
    }

    [Fact]
    public void Project_UsesMetadataOriginToChooseCanonicalNormalization()
    {
        var sourceId = Guid.NewGuid();
        var metadata = new ProjectMetadata();
        metadata.SetValue(ProjectMetadataField.Title, "Chapter 11.mp3", MetadataOrigin.User);
        metadata.SetValue(ProjectMetadataField.Album, "Chapter 11.mp3", MetadataOrigin.SourceAggregation);

        var result = _service.Project(
            ChapterCandidateProjectionContext.ForLocalFolder("project-a", []),
            [CreateEffectiveChapter(CreateTrack(sourceId, "source.mp3", ""))],
            [CreateSnapshot(sourceId, "")],
            metadata);

        Assert.Equal("Chapter 11.mp3", Candidate(result, ChapterCandidateSource.CanonicalTitle).Normalization.NormalizedText);
        Assert.Equal("Chapter", Candidate(result, ChapterCandidateSource.CanonicalAlbum).Normalization.NormalizedText);
    }

    [Fact]
    public void Project_ExcludedTrackCreatesNeitherMemberNorCandidate()
    {
        var sourceId = Guid.NewGuid();
        var track = CreateTrack(sourceId, "source.mp3", "");
        track.IsExcluded = true;

        var result = _service.Project(
            ChapterCandidateProjectionContext.ForLocalFolder("project-a", []),
            [CreateEffectiveChapter(track)],
            [CreateSnapshot(sourceId, "Source")],
            new ProjectMetadata());

        Assert.Empty(result.EffectiveChapters);
        Assert.Empty(result.Candidates);
    }

    private static ProjectedChapterCandidate Candidate(
        ChapterCandidateProjectionResult result,
        ChapterCandidateSource source) =>
        Assert.Single(result.Candidates, candidate => candidate.Source == source);

    internal static TrackInfo CreateTrack(Guid sourceId, string fileName, string tagTitle) => new()
    {
        SourceId = sourceId,
        FileName = fileName,
        TagTitle = tagTitle,
        DurationTicks = 100
    };

    internal static EffectiveChapterInfo CreateEffectiveChapter(
        TrackInfo track,
        TrackEmbeddedChapterInfo? embedded = null,
        string title = "Derived title",
        int outputIndex = 1) => new(
            outputIndex,
            track,
            embedded,
            title,
            StartTicks: 0,
            EndTicks: 100,
            SourceStartTicks: embedded?.StartTicks ?? 0,
            SourceEndTicks: embedded?.EndTicks ?? 100,
            IsSynthetic: embedded is null);

    internal static SourceMetadataSnapshot CreateSnapshot(
        Guid sourceId,
        string title,
        IReadOnlyList<TrackEmbeddedChapterInfo>? chapters = null,
        int? trackNumber = null,
        int? discNumber = null) => new()
        {
            SourceId = sourceId,
            CanonicalFields = new SourceMetadataCanonicalFields
            {
                Title = title,
                SourceTrackNumber = trackNumber,
                SourceDiscNumber = discNumber
            },
            OriginalChapters = chapters ?? []
        };

    private static Mp3DiscProjectManifest Mp3ManifestWithoutIdentity(
        int discNumber,
        params (Guid SourceId, string WorkingPath)[] sources) => new()
        {
            ImportedDiscs =
            [
                new Mp3DiscProjectManifestDisc
                {
                    DiscNumber = discNumber,
                    Signature = string.Empty,
                    SourceFiles = sources.Select(source => new Mp3DiscProjectManifestSourceFile
                    {
                        SourceId = source.SourceId,
                        RelativePath = source.WorkingPath,
                        SourceFileName = source.WorkingPath
                    }).ToList()
                }
            ]
        };

    private static AudioDiscProjectManifest AudioManifestWithoutIdentity(
        int discNumber,
        Guid sourceId) => new()
        {
            Discs =
            [
                new AudioDiscProjectManifestDisc
                {
                    DiscNumber = discNumber,
                    DiscIdentity = string.Empty,
                    Tracks =
                    [
                        new AudioDiscProjectManifestTrack
                        {
                            SourceId = sourceId,
                            GlobalIndex = 1,
                            FileName = "working.flac",
                            RelativePath = "derived/working.flac"
                        }
                    ]
                }
            ]
        };
}
