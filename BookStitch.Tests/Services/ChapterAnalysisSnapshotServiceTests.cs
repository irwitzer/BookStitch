using BookStitch.Models;
using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class ChapterAnalysisSnapshotServiceTests
{
    private readonly ChapterAnalysisSnapshotService _service = new();

    [Fact]
    public void Create_BuildsOneCoherentSnapshotForAllEffectiveChapters()
    {
        var metadata = Metadata("Canonical Book", "Canonical Album");
        var fixture = Fixture(2);

        var result = _service.Create(
            fixture.Context,
            fixture.EffectiveChapters,
            fixture.SourceSnapshots,
            metadata);

        Assert.NotEqual(Guid.Empty, result.SnapshotId);
        Assert.Equal(ChapterAnalysisRules.CurrentRevision, result.AnalysisRulesRevision);
        Assert.Equal(metadata.Revision, result.ProjectMetadataRevision);
        Assert.Equal(2, result.EffectiveChapters.Count);
        Assert.Equal(2, result.CandidatesByEffectiveChapter.Count);
        Assert.All(result.EffectiveChapters, chapter =>
        {
            var candidates = result.CandidatesByEffectiveChapter[chapter.EffectiveChapterId];
            Assert.NotEmpty(candidates);
            Assert.All(candidates, candidate =>
            {
                Assert.Equal(chapter.EffectiveChapterId, candidate.Provenance.EffectiveChapterId);
                Assert.Equal(result.ScopeIdByEffectiveChapter[chapter.EffectiveChapterId], candidate.ScopeId);
                Assert.Contains(candidate, result.ScopeResults.SelectMany(scope => scope.Candidates));
            });
        });
    }

    [Fact]
    public void Create_NewCanonicalMetadataProducesNewSnapshotWithoutChangingSourceSignature()
    {
        var fixture = Fixture(2);
        var metadata = Metadata("First Canonical Title", "Stable Album");

        var first = _service.Create(
            fixture.Context,
            fixture.EffectiveChapters,
            fixture.SourceSnapshots,
            metadata);
        metadata.SetValue(ProjectMetadataField.Title, "Second Canonical Title", MetadataOrigin.User);
        var second = _service.Create(
            fixture.Context,
            fixture.EffectiveChapters,
            fixture.SourceSnapshots,
            metadata);

        Assert.NotEqual(first.SnapshotId, second.SnapshotId);
        Assert.True(second.ProjectMetadataRevision > first.ProjectMetadataRevision);
        Assert.Equal(
            first.ScopeResults.Select(scope => scope.ScopeInputSignature),
            second.ScopeResults.Select(scope => scope.ScopeInputSignature));
        var canonicalTitles = Candidates(second, ChapterCandidateSource.CanonicalTitle);
        Assert.Equal(2, canonicalTitles.Count);
        Assert.All(canonicalTitles, candidate =>
            Assert.Equal("Second Canonical Title", candidate.RawText));
        Assert.DoesNotContain(
            second.CandidatesByEffectiveChapter.Values.SelectMany(candidates => candidates),
            candidate => candidate.RawText == "First Canonical Title");
    }

    [Fact]
    public void Projection_UsesOnePreviouslyCapturedCanonicalMetadataState()
    {
        var fixture = Fixture(1);
        var metadata = Metadata("Captured Title", "Captured Album");
        var captured = ChapterCanonicalMetadataSnapshot.Capture(metadata);
        metadata.SetValue(ProjectMetadataField.Title, "Later Title", MetadataOrigin.User);
        metadata.SetValue(ProjectMetadataField.Album, "Later Album", MetadataOrigin.User);

        var projection = new ChapterCandidateProjectionService().Project(
            fixture.Context,
            fixture.EffectiveChapters,
            fixture.SourceSnapshots,
            captured);

        Assert.Equal(
            "Captured Title",
            Assert.Single(projection.Candidates, candidate =>
                candidate.Source == ChapterCandidateSource.CanonicalTitle).Normalization.RawText);
        Assert.Equal(
            "Captured Album",
            Assert.Single(projection.Candidates, candidate =>
                candidate.Source == ChapterCandidateSource.CanonicalAlbum).Normalization.RawText);
    }

    [Fact]
    public void Create_DuplicateEffectiveChapterIdentityFailsClearly()
    {
        var fixture = Fixture(1);
        var duplicate = fixture.EffectiveChapters[0] with { OutputChapterIndex = 2 };

        Assert.Throws<InvalidOperationException>(() => _service.Create(
            fixture.Context,
            [fixture.EffectiveChapters[0], duplicate],
            fixture.SourceSnapshots,
            new ProjectMetadata()));
    }

    [Fact]
    public void CreateAndResolve_HigherQualityFilenameWinsOverUniformTrackAndCanonicalTitles()
    {
        var fixture = Fixture(2);
        var snapshot = _service.Create(
            fixture.Context,
            fixture.EffectiveChapters,
            fixture.SourceSnapshots,
            Metadata("Uniform Canonical Title", "Uniform Canonical Album"));

        var resolved = new ChapterResolveService().Resolve(snapshot);

        Assert.Equal(snapshot.SnapshotId, resolved.SnapshotId);
        Assert.Equal(["Source 1", "Source 2"], resolved.Chapters.Select(chapter => chapter.Text));
        Assert.All(resolved.Chapters, chapter =>
        {
            Assert.Equal(ChapterCandidateSource.Filename, chapter.Source);
            Assert.Equal(ChapterCandidateQuality.IndividualMeaningful, chapter.Quality);
            Assert.False(chapter.IsFallback);
        });
    }

    private static IReadOnlyList<AnalyzedChapterCandidate> Candidates(
        ChapterAnalysisSnapshot snapshot,
        ChapterCandidateSource source) =>
        snapshot.CandidatesByEffectiveChapter.Values
            .SelectMany(candidates => candidates)
            .Where(candidate => candidate.Source == source)
            .ToArray();

    private static ProjectMetadata Metadata(string title, string album)
    {
        var metadata = new ProjectMetadata();
        metadata.SetValue(ProjectMetadataField.Title, title, MetadataOrigin.User);
        metadata.SetValue(ProjectMetadataField.Album, album, MetadataOrigin.User);
        return metadata;
    }

    private static SnapshotFixture Fixture(int count)
    {
        var identities = new List<SourceFileCopyIdentity>();
        var chapters = new List<EffectiveChapterInfo>();
        var snapshots = new List<SourceMetadataSnapshot>();

        for (var index = 1; index <= count; index++)
        {
            var sourceId = new Guid(index, 1, 0, new byte[8]);
            var filename = $"{index:00}_Source_{index}.mp3";
            var track = ChapterCandidateProjectionServiceTests.CreateTrack(sourceId, "derived.m4a", string.Empty);
            identities.Add(new SourceFileCopyIdentity(filename, filename, 100, index, sourceId));
            chapters.Add(ChapterCandidateProjectionServiceTests.CreateEffectiveChapter(track, outputIndex: index));
            snapshots.Add(ChapterCandidateProjectionServiceTests.CreateSnapshot(
                sourceId,
                $"Source Title {index}",
                trackNumber: index));
        }

        return new SnapshotFixture(
            ChapterCandidateProjectionContext.ForLocalFolder("snapshot-project", identities),
            chapters,
            snapshots);
    }

    private sealed record SnapshotFixture(
        ChapterCandidateProjectionContext Context,
        IReadOnlyList<EffectiveChapterInfo> EffectiveChapters,
        IReadOnlyList<SourceMetadataSnapshot> SourceSnapshots);
}
