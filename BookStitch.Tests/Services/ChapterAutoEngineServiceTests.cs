using BookStitch.Models;
using BookStitch.Services;
using BookStitch.Tests.TestHelpers;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class ChapterAutoEngineServiceTests
{
    [Theory]
    [InlineData(130, 3, "Plain", "100 Armageddon")]
    [InlineData(130, 3, "SpacePadded", "100 Armageddon")]
    [InlineData(130, 3, "ZeroPadded", "100 Armageddon")]
    [InlineData(130, 3, "None", "Armageddon")]
    [InlineData(1030, 4, "Plain", "1000 Armageddon")]
    [InlineData(1030, 4, "SpacePadded", "1000 Armageddon")]
    [InlineData(1030, 4, "ZeroPadded", "1000 Armageddon")]
    [InlineData(1030, 4, "None", "Armageddon")]
    public void Apply_SpacedDashSourceOrdinalsDoNotDuplicateAtDigitBoundary(
        int count, int width, string numberingStyle, string expectedBoundaryTitle)
    {
        var tracks = Enumerable.Range(1, count).Select(number =>
        {
            var padded = number.ToString("D" + width, System.Globalization.CultureInfo.InvariantCulture);
            var track = Track($"{padded}_Armageddon.mp3", $"{padded} - Armageddon", Guid.NewGuid());
            track.SourceMetadataFields = track.SourceMetadataFields! with { SourceTrackNumber = number };
            track.SourceTrackNumber = number;
            return track;
        }).ToArray();
        var originalTags = tracks.Select(track => track.TagTitle).ToArray();
        var files = tracks.Select(track => track.FileName).ToArray();
        var split = count - 70; // Like the reported project: disc 2 spans 061..130.
        var manifest = new Mp3DiscProjectManifest
        {
            ImportedDiscs = tracks.Select((track, index) => (track, disc: index < split ? 1 : 2))
                .GroupBy(item => item.disc)
                .Select(group => new Mp3DiscProjectManifestDisc
                {
                    DiscNumber = group.Key,
                    Signature = "disc-" + group.Key,
                    SourceFiles = group.Select(item => new Mp3DiscProjectManifestSourceFile
                    {
                        SourceId = item.track.SourceId,
                        SourceFileName = item.track.FileName,
                        RelativePath = item.track.FileName
                    }).ToList()
                }).ToList()
        };
        var result = _service.Apply(Request(tracks, Canonical(1, "Armageddon"),
            new ChapterSettings { Mode = "Auto", NumberingStyle = numberingStyle },
            projectType: ProjectManifestTypes.Mp3DiscProject, mp3Manifest: manifest));
        Assert.All(result.ResolveResult.Chapters, chapter => Assert.Equal("Armageddon", chapter.Text));
        var boundary = width == 3 ? 100 : 1000;
        Assert.Equal(expectedBoundaryTitle, tracks[boundary - 1].ChapterTitle);
        var export = new ExportChapterService().BuildFfmpegChapterMetadataFromTimeline(result.RenderedTimeline);
        Assert.Contains("title=" + expectedBoundaryTitle, export);
        Assert.Equal(originalTags, tracks.Select(track => track.TagTitle));
        Assert.Equal(files, tracks.Select(track => track.FileName));
        Assert.Equal(Enumerable.Range(1, count), tracks.Select(track => track.Index));
    }

    private readonly ChapterAutoEngineService _service = new();

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Apply_SingleFileOmitsAddedNumberOnlyForSingleEffectiveChapter(int embeddedCount)
    {
        var track = Track("book.m4b", "Arrival", Guid.NewGuid());
        track.DurationTicks = TimeSpan.FromSeconds(10).Ticks;
        track.EmbeddedChapters = Enumerable.Range(0, embeddedCount).Select(index => new TrackEmbeddedChapterInfo
        {
            Title = "Arrival", StartTicks = TimeSpan.FromSeconds(index * 5).Ticks,
            EndTicks = TimeSpan.FromSeconds((index + 1) * 5).Ticks
        }).ToList();
        var result = _service.Apply(Request([track]));
        Assert.Equal(Math.Max(1, embeddedCount), result.RenderedTimeline.Count);
        for (var index = 0; index < result.RenderedTimeline.Count; index++)
        {
            var text = result.ResolveResult.Chapters[index].Text;
            Assert.Equal(embeddedCount < 2 ? text : $"{index + 1} {text}", result.RenderedTimeline[index].Title);
        }
    }

    [Fact]
    public void Apply_PortugueseNumberWordsReachExportWithoutChangingSourcesOrOrder()
    {
        var tracks = new[]
        {
            Track("source-b.mp3", "Capítulo vinte – Chegada", Guid.NewGuid()),
            Track("source-a.mp3", "Capítulo dois – Partida", Guid.NewGuid())
        };
        var identities = tracks.Select(track => track.SourceId).ToArray();
        var originals = tracks.Select(track => track.TagTitle).ToArray();
        var result = _service.Apply(Request(tracks));
        Assert.Equal(new[] { "1 Chegada", "2 Partida" }, result.RenderedTimeline.Select(chapter => chapter.Title));
        Assert.Equal(identities, tracks.Select(track => track.SourceId));
        Assert.Equal(originals, tracks.Select(track => track.TagTitle));
        Assert.Equal(new[] { "source-b.mp3", "source-a.mp3" }, tracks.Select(track => track.FileName));
        var metadata = new ExportChapterService().BuildFfmpegChapterMetadataFromTimeline(result.RenderedTimeline);
        Assert.Contains("title=1 Chegada", metadata);
        Assert.Contains("title=2 Partida", metadata);
    }

    [Theory]
    [InlineData("Kapittel")]
    [InlineData("Luku")]
    [InlineData("Bölüm")]
    [InlineData("Chương")]
    [InlineData("অধ্যায়")]
    public void Apply_NumericOnlyMarkersPreserveSourceOrderAndProduceSemanticExportTitles(string marker)
    {
        var tracks = new[]
        {
            Track("source-b.mp3", marker + " 12 – Arrival", Guid.NewGuid()),
            Track("source-a.mp3", marker + " 3 – Departure", Guid.NewGuid())
        };
        var identities = tracks.Select(track => track.SourceId).ToArray();
        var originals = tracks.Select(track => track.TagTitle).ToArray();
        var result = _service.Apply(Request(tracks));
        Assert.Equal(new[] { "1 Arrival", "2 Departure" }, result.RenderedTimeline.Select(chapter => chapter.Title));
        Assert.Equal(identities, tracks.Select(track => track.SourceId));
        Assert.Equal(originals, tracks.Select(track => track.TagTitle));
        Assert.Equal(new[] { "source-b.mp3", "source-a.mp3" }, tracks.Select(track => track.FileName));
        var metadata = new ExportChapterService().BuildFfmpegChapterMetadataFromTimeline(result.RenderedTimeline);
        Assert.Contains("title=1 Arrival", metadata);
        Assert.Contains("title=2 Departure", metadata);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Repeated_source_has_separate_chapters_without_changing_source_identity(bool embedded)
    {
        var sourceId = Guid.NewGuid();
        var first = Track("same.mp3", "Wiederholung", sourceId, 2 * TimeSpan.TicksPerSecond);
        var second = Track("same.mp3", "Wiederholung", sourceId, 2 * TimeSpan.TicksPerSecond);
        if (embedded)
            foreach (var track in new[] { first, second })
                track.EmbeddedChapters = [new() { Title = "Anfang", StartTicks = 0, EndTicks = TimeSpan.TicksPerSecond },
                    new() { Title = "Ende", StartTicks = TimeSpan.TicksPerSecond, EndTicks = 2 * TimeSpan.TicksPerSecond }];
        var result = _service.Apply(Request([first, second]));
        Assert.Equal(embedded ? 4 : 2, result.RenderedTimeline.Count);
        Assert.Equal(result.RenderedTimeline.Count,
            result.AnalysisSnapshot.EffectiveChapters.Select(chapter => chapter.EffectiveChapterId).Distinct().Count());
        Assert.Equal(sourceId, first.SourceId);
        Assert.Equal(sourceId, second.SourceId);
        Assert.Equal(1, first.Index);
        Assert.Equal(2, second.Index);
        Assert.Equal(4 * TimeSpan.TicksPerSecond, result.RenderedTimeline.Last().EndTicks);
        if (embedded)
        {
            Assert.Equal(new[] { "1 Anfang", "2 Ende", "3 Anfang", "4 Ende" },
                result.RenderedTimeline.Select(chapter => chapter.Title));
            Assert.Equal(2, result.AnalysisSnapshot.ScopeResults.Count);
        }
    }

    [Fact]
    public void Apply_VisibleChapterTitleComesFromV4ResolveAndIgnoresOldResolvedTitle()
    {
        var track = Track("source-01.mp3", "Der Aufbruch");
        track.ChapterTitle = "999 Alter Auto-Vorschlag";

        var result = _service.Apply(Request([track]));

        Assert.Equal("Der Aufbruch", track.ChapterTitle);
        Assert.Equal(track.ChapterTitle, Assert.Single(result.RenderedTimeline).Title);
        Assert.Equal(
            ChapterCandidateSource.TrackTitle,
            Assert.Single(result.ResolveResult.Chapters).Source);
    }

    [Fact]
    public void Apply_CanonicalTitleChangeCreatesFreshSnapshotAndUpdatesGenericChapters()
    {
        var tracks = new[]
        {
            Track("track-01.mp3", "Track 01"),
            Track("track-02.mp3", "Track 02")
        };

        var first = _service.Apply(Request(tracks, Canonical(10, "Erstes Buch")));
        var second = _service.Apply(Request(tracks, Canonical(11, "Zweites Buch")));

        Assert.All(first.ResolveResult.Chapters, chapter => Assert.Equal("Erstes Buch", chapter.Text));
        Assert.All(second.ResolveResult.Chapters, chapter => Assert.Equal("Zweites Buch", chapter.Text));
        Assert.Equal(11, second.AnalysisSnapshot.ProjectMetadataRevision);
        Assert.Equal("1 Zweites Buch", tracks[0].ChapterTitle);
    }

    [Fact]
    public void Apply_GenericTrackTitleOrdinalFamilyCrossing100FallsBackToCanonicalTitle()
    {
        var tracks = Enumerable.Range(1, 112)
            .Select(index =>
            {
                var sourceOrdinal = index >= 107 ? index - 1 : index;
                var chapterNumber = Math.Min(36, ((index - 1) / 3) + 1);
                var track = Track(
                    $"track-{index:000}.mp3",
                    $"{sourceOrdinal:000} Kapitel {chapterNumber}");
                track.SourceTrackNumber = index;
                return track;
            })
            .ToArray();

        var result = _service.Apply(Request(
            tracks,
            Canonical(2, "Das Vermächtnis")));

        Assert.Equal(112, result.ResolveResult.Chapters.Count);
        Assert.All(result.ResolveResult.Chapters, chapter =>
        {
            Assert.Equal("Das Vermächtnis", chapter.Text);
            Assert.Equal(ChapterCandidateSource.CanonicalTitle, chapter.Source);
        });
        Assert.Equal("99 Das Vermächtnis", tracks[98].ChapterTitle);
        Assert.Equal("100 Das Vermächtnis", tracks[99].ChapterTitle);
        Assert.Equal("107 Das Vermächtnis", tracks[106].ChapterTitle);
        Assert.Equal("112 Das Vermächtnis", tracks[111].ChapterTitle);
    }

    [Fact]
    public void Apply_EmbeddedWrappedNumericFamilyAcross9To10IsOneTechnicalFamily()
    {
        const string canonicalTitle = "Haus der Hüterin - Das Erwachen";
        var track = Track(
            "02 Haus der Hüterin - Das Erwachen.m4a",
            "02 Haus der Hüterin - Das Erwachen",
            durationTicks: 860);
        track.EmbeddedChapters = Enumerable.Range(1, 86)
            .Select(index => new TrackEmbeddedChapterInfo
            {
                Title = $"({index:00})",
                StartTicks = (index - 1) * 10L,
                EndTicks = index * 10L
            })
            .ToList();

        var result = _service.Apply(Request(
            [track],
            Canonical(20, canonicalTitle)));

        Assert.Equal(86, result.ResolveResult.Chapters.Count);
        Assert.All(
            result.ResolveResult.Chapters,
            chapter => Assert.Equal(canonicalTitle, chapter.Text));
        Assert.DoesNotContain(
            result.ResolveResult.Chapters,
            chapter => chapter.Source == ChapterCandidateSource.EmbeddedChapterTitle);

        var embeddedCandidates = result.AnalysisSnapshot.CandidatesByEffectiveChapter
            .Values
            .SelectMany(candidates => candidates)
            .Where(candidate => candidate.Source == ChapterCandidateSource.EmbeddedChapterTitle)
            .ToArray();

        Assert.Equal(86, embeddedCandidates.Length);
        Assert.All(embeddedCandidates, candidate =>
        {
            Assert.False(candidate.IsUsable);
            Assert.True(candidate.TechnicalNumberingEvidence.FamilyDetected);
            Assert.Equal(
                ChapterTechnicalNumberingKind.PureNumber,
                candidate.TechnicalNumberingEvidence.Kind);
        });
    }
    [Fact]
    public void Apply_SourceAggregatedAndUserConfirmedCanonicalWinUniformTieImmediatelyWithoutReload()
    {
        var tracks = Enumerable.Range(1, 3)
            .Select(index =>
            {
                var track = Track("Uniform Source.mp3", "Uniform Source", Guid.NewGuid());
                track.FilePath = $"source-{index}.mp3";
                return track;
            })
            .ToArray();

        var automatic = _service.Apply(Request(
            tracks,
            new ChapterCanonicalMetadataSnapshot(
                10,
                "Canonical title",
                MetadataOrigin.SourceAggregation,
                null,
                MetadataOrigin.None)));
        var userConfirmed = _service.Apply(Request(
            tracks,
            new ChapterCanonicalMetadataSnapshot(
                11,
                "Canonical title",
                MetadataOrigin.User,
                null,
                MetadataOrigin.None)));

        Assert.All(automatic.ResolveResult.Chapters, chapter =>
        {
            Assert.Equal(ChapterCandidateSource.CanonicalTitle, chapter.Source);
            Assert.Equal(MetadataOrigin.SourceAggregation, chapter.MetadataOrigin);
            Assert.Equal(ChapterResolveDecisionReason.SourceAggregatedCanonicalPreference, chapter.DecisionReason);
        });
        Assert.All(userConfirmed.ResolveResult.Chapters, chapter =>
        {
            Assert.Equal(ChapterCandidateSource.CanonicalTitle, chapter.Source);
            Assert.Equal(MetadataOrigin.User, chapter.MetadataOrigin);
            Assert.Equal(ChapterResolveDecisionReason.UserConfirmedCanonicalPreference, chapter.DecisionReason);
        });
        Assert.Equal("1 Canonical title", tracks[0].ChapterTitle);
    }

    [Fact]
    public void Apply_CanonicalChangeKeepsIndividualTrackAndEmbeddedChapterEvidence()
    {
        var individual = Track("opening.mp3", "Die geheime Kammer");
        var embedded = Track("container.m4b", "Track 02", durationTicks: 20);
        embedded.EmbeddedChapters =
        [
            new TrackEmbeddedChapterInfo { Title = "Fund im Keller", StartTicks = 0, EndTicks = 10 },
            new TrackEmbeddedChapterInfo { Title = "Flucht über das Dach", StartTicks = 10, EndTicks = 20 }
        ];
        var secondIndividual = Track("tower.mp3", "Der Glockenturm");

        var result = _service.Apply(Request(
            [individual, embedded, secondIndividual],
            Canonical(3, "Neuer Kanontitel")));

        Assert.Equal("Die geheime Kammer", result.ResolveResult.Chapters[0].Text);
        Assert.Equal("Fund im Keller", result.ResolveResult.Chapters[1].Text);
        Assert.Equal("Flucht über das Dach", result.ResolveResult.Chapters[2].Text);
        Assert.Equal(ChapterCandidateQuality.IndividualMeaningful, result.ResolveResult.Chapters[0].Quality);
        Assert.Equal("1 Die geheime Kammer", individual.ChapterTitle);
        Assert.Equal("2 Fund im Keller", embedded.ChapterTitle);
    }

    [Fact]
    public void Apply_SortAndExclusionRebuildIndexesAndRenderedTimeline()
    {
        var opening = Track("opening.mp3", "Der Anfang");
        var ending = Track("ending.mp3", "Das Ende");
        var tracks = new List<TrackInfo> { ending, opening };

        var sorted = _service.Apply(Request(tracks));

        Assert.Equal(["1 Das Ende", "2 Der Anfang"], sorted.RenderedTimeline.Select(chapter => chapter.Title));
        ending.IsExcluded = true;
        var excluded = _service.Apply(Request(tracks));

        Assert.Equal(0, ending.Index);
        Assert.Equal(1, opening.Index);
        Assert.Equal("1 Der Anfang", Assert.Single(excluded.RenderedTimeline).Title);
    }

    [Fact]
    public void Apply_Mp3CdKeepsDiscScopesSeparate()
    {
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var tracks = new[]
        {
            Track("copied-a.m4a", "Track 01", firstId),
            Track("copied-b.m4a", "Track 01", secondId)
        };
        var manifest = new Mp3DiscProjectManifest
        {
            ImportedDiscs =
            [
                Disc(1, "disc-a", firstId, "01_Erster Teil.mp3"),
                Disc(2, "disc-b", secondId, "01_Zweiter Teil.mp3")
            ]
        };

        var result = _service.Apply(Request(
            tracks,
            projectType: ProjectManifestTypes.Mp3DiscProject,
            mp3Manifest: manifest));

        Assert.Equal(2, result.AnalysisSnapshot.ScopeResults.Count);
        Assert.Equal(2, result.AnalysisSnapshot.EffectiveChapters
            .Select(chapter => chapter.ScopeProvenanceIdentity)
            .Distinct(StringComparer.Ordinal)
            .Count());
    }

    [Fact]
    public void Apply_Mp3CdPreviewWithoutManifestUsesPendingDiscScope()
    {
        var sourceId = Guid.NewGuid();
        var track = Track("preview.mp3", "Vorspann", sourceId);
        var pending = new Mp3DiscPendingChapterDisc(
            1,
            "preview-signature",
            [
                new Mp3DiscPlannedSourceIdentity(
                    sourceId,
                    "9783837165128_001_225.mp3",
                    "9783837165128_001_225.mp3",
                    10,
                    20)
            ]);
        var request = Request(
            [track],
            projectType: ProjectManifestTypes.Mp3DiscProject) with
        {
            PendingMp3Discs = [pending]
        };

        var result = _service.Apply(request);

        Assert.Equal(ChapterScopeKind.Mp3DiscTrack, Assert.Single(result.AnalysisSnapshot.ScopeResults).ScopeKind);
        Assert.Equal("Vorspann", Assert.Single(result.ResolveResult.Chapters).Text);
    }

    [Fact]
    public void Apply_AudioCdPreviewWithoutManifestUsesAudioDiscScope()
    {
        var track = Track("Track01.flac", "Track 01", Guid.NewGuid());
        var request = Request(
            [track],
            projectType: ProjectManifestTypes.AudioCdProject) with
        {
            PendingAudioDiscIdentity = "audio-preview-disc"
        };

        var result = _service.Apply(request);

        Assert.Equal(ChapterScopeKind.AudioDisc, Assert.Single(result.AnalysisSnapshot.ScopeResults).ScopeKind);
        Assert.DoesNotContain(
            result.AnalysisSnapshot.CandidatesByEffectiveChapter.Values.SelectMany(candidates => candidates),
            candidate => candidate.Source == ChapterCandidateSource.Filename);
    }

    [Fact]
    public void Apply_MixedEmbeddedAndTrackChaptersUseOneCoherentTimeline()
    {
        var embedded = Track("container.m4b", "Container", durationTicks: 20);
        embedded.EmbeddedChapters =
        [
            new TrackEmbeddedChapterInfo { Title = "Innen Eins", StartTicks = 0, EndTicks = 10 },
            new TrackEmbeddedChapterInfo { Title = "Innen Zwei", StartTicks = 10, EndTicks = 20 }
        ];
        var synthetic = Track("after.mp3", "Danach", durationTicks: 10);

        var result = _service.Apply(Request([embedded, synthetic]));

        Assert.Equal(3, result.RenderedTimeline.Count);
        Assert.Equal(["1 Innen Eins", "2 Innen Zwei", "3 Danach"], result.RenderedTimeline.Select(chapter => chapter.Title));
        Assert.Equal(result.AnalysisSnapshot.SnapshotId, result.ResolveResult.SnapshotId);
    }

    [Fact]
    public void Apply_NoUsableEvidenceUsesLocalizedFallback()
    {
        var track = Track("", "");
        track.SourceMetadataFields = new SourceMetadataCanonicalFields();

        var result = _service.Apply(Request([track]));

        var resolved = Assert.Single(result.ResolveResult.Chapters);
        Assert.True(resolved.IsFallback);
        Assert.Equal("Kapitel", resolved.Text);
        Assert.Equal("Kapitel", track.ChapterTitle);
    }

    [Fact]
    public void Apply_UsesExistingNumberRendererWithoutChangingItsPaddingRules()
    {
        var tracks = Enumerable.Range(1, 10)
            .Select(index => Track($"{index}.mp3", $"Abschnitt {index}"))
            .ToList();
        var settings = new ChapterSettings { Mode = "Auto", NumberingStyle = "ZeroPadded" };

        var result = _service.Apply(Request(tracks, settings: settings));

        var renderer = new ChapterTitleRenderer();
        Assert.Equal(
            renderer.RenderContent(1, 10, result.ResolveResult.Chapters[0].Text, ChapterNumberingStyle.ZeroPadded),
            result.RenderedTimeline[0].Title);
        Assert.Equal("01 Abschnitt 1", result.RenderedTimeline[0].Title);
        Assert.Equal("10 Abschnitt 10", result.RenderedTimeline[9].Title);
    }

    [Fact]
    public void ExportMetadataUsesExactFinalRenderedTimeline()
    {
        var tracks = new[]
        {
            Track("a.mp3", "Anfang"),
            Track("b.mp3", "Ende")
        };
        var result = _service.Apply(Request(tracks));

        var metadata = new ExportChapterService()
            .BuildFfmpegChapterMetadataFromTimeline(result.RenderedTimeline);

        Assert.Contains("title=1 Anfang", metadata, StringComparison.Ordinal);
        Assert.Contains("title=2 Ende", metadata, StringComparison.Ordinal);
        Assert.Equal(
            result.RenderedTimeline.Count,
            metadata.Split("[CHAPTER]", StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public void Apply_ResumeUsesPersistedImmutableSourceSnapshotInsteadOfStoredChapterTitle()
    {
        using var folder = new TemporaryFolder();
        ProjectFolderLayout.EnsureProjectFolders(folder.Path);
        var sourceId = Guid.NewGuid();
        new SourceMetadataSnapshotStore().Save(folder.Path, new SourceMetadataSnapshot
        {
            SourceId = sourceId,
            CanonicalFields = new SourceMetadataCanonicalFields { Title = "Persistierter Originaltitel" }
        });
        var resumedTrack = Track("working-copy.m4a", "Geänderter Arbeitstitel", sourceId);
        resumedTrack.ChapterTitle = "77 Gespeicherter alter Vorschlag";
        resumedTrack.SourceMetadataFields = null;

        var result = _service.Apply(Request(
            [resumedTrack],
            projectFolder: folder.Path));

        Assert.Equal("Persistierter Originaltitel", Assert.Single(result.ResolveResult.Chapters).Text);
        Assert.Equal("Persistierter Originaltitel", resumedTrack.ChapterTitle);
    }

    private static ChapterAutoEngineRequest Request(
        IList<TrackInfo> tracks,
        ChapterCanonicalMetadataSnapshot? canonical = null,
        ChapterSettings? settings = null,
        string projectType = ProjectManifestTypes.FolderProject,
        Mp3DiscProjectManifest? mp3Manifest = null,
        string projectFolder = "") => new(
            tracks,
            projectType,
            "test-project",
            ProjectFolder: projectFolder,
            canonical ?? Canonical(1, null),
            settings ?? new ChapterSettings { Mode = "Auto", NumberingStyle = "Plain" },
            LegacyUseLeadingZeros: false,
            Mp3DiscManifest: mp3Manifest,
            AllowTransientLocalFilenameEvidence: false);

    private static ChapterCanonicalMetadataSnapshot Canonical(long revision, string? title) => new(
        revision,
        title,
        string.IsNullOrWhiteSpace(title) ? MetadataOrigin.None : MetadataOrigin.User,
        AlbumValue: null,
        MetadataOrigin.None);

    private static TrackInfo Track(
        string fileName,
        string sourceTitle,
        Guid sourceId = default,
        long durationTicks = 10) => new()
    {
        SourceId = sourceId,
        FileName = fileName,
        FilePath = fileName,
        TagTitle = sourceTitle,
        SourceMetadataFields = new SourceMetadataCanonicalFields { Title = sourceTitle },
        DurationTicks = durationTicks
    };

    private static Mp3DiscProjectManifestDisc Disc(
        int discNumber,
        string signature,
        Guid sourceId,
        string sourceFileName) => new()
    {
        DiscNumber = discNumber,
        Signature = signature,
        SourceFiles =
        [
            new Mp3DiscProjectManifestSourceFile
            {
                SourceId = sourceId,
                SourceFileName = sourceFileName,
                RelativePath = sourceFileName
            }
        ]
    };
}
