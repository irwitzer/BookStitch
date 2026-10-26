using BookStitch.Models;
using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class ChapterScopeBuilderTests
{
    private readonly ChapterCandidateProjectionService _projectionService = new();
    private readonly ChapterScopeBuilder _scopeBuilder = new();

    [Fact]
    public void Build_LocalFolderKeepsSingleChapterFilesInOneScopeRegardlessOfDiscTags()
    {
        var fixture = CreateLocalFixture(
            ("A", 1),
            ("B", 2),
            ("C", 3));

        var scopes = _scopeBuilder.Build(fixture);

        var scope = Assert.Single(scopes);
        Assert.Equal(ChapterScopeKind.FolderTrack, scope.Kind);
        Assert.Equal(3, scope.Members.Count);
        Assert.Equal([1, 2, 3], scope.Members.Select(member => member.ScopeOrder));
    }

    [Fact]
    public void Build_MultiChapterSourceCreatesOwnEmbeddedScope()
    {
        var sourceId = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        var first = new TrackEmbeddedChapterInfo { Title = "One", StartTicks = 0, EndTicks = 50 };
        var second = new TrackEmbeddedChapterInfo { Title = "Two", StartTicks = 50, EndTicks = 100 };
        var multiTrack = ChapterCandidateProjectionServiceTests.CreateTrack(sourceId, "multi.mp3", "");
        multiTrack.EmbeddedChapters = [first, second];
        var normalTrack = ChapterCandidateProjectionServiceTests.CreateTrack(otherId, "normal.mp3", "");
        var projection = _projectionService.Project(
            ChapterCandidateProjectionContext.ForLocalFolder("project-a", []),
            [
                ChapterCandidateProjectionServiceTests.CreateEffectiveChapter(multiTrack, first, outputIndex: 1),
                ChapterCandidateProjectionServiceTests.CreateEffectiveChapter(multiTrack, second, outputIndex: 2),
                ChapterCandidateProjectionServiceTests.CreateEffectiveChapter(normalTrack, outputIndex: 3)
            ],
            [
                ChapterCandidateProjectionServiceTests.CreateSnapshot(sourceId, "Track", [first, second]),
                ChapterCandidateProjectionServiceTests.CreateSnapshot(otherId, "Other")
            ],
            new ProjectMetadata());

        var scopes = _scopeBuilder.Build(projection);

        Assert.Equal(2, scopes.Count);
        Assert.Equal(2, Assert.Single(scopes, scope => scope.Kind == ChapterScopeKind.Embedded).Members.Count);
        Assert.Single(Assert.Single(scopes, scope => scope.Kind == ChapterScopeKind.FolderTrack).Members);
    }

    [Fact]
    public void Build_Mp3CdCreatesOneTrackScopePerDisc()
    {
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var manifest = new Mp3DiscProjectManifest
        {
            ImportedDiscs =
            [
                Disc(1, "disc-one", firstId),
                Disc(2, "disc-two", secondId)
            ]
        };
        var projection = _projectionService.Project(
            ChapterCandidateProjectionContext.ForMp3Cd("project-mp3", manifest),
            [
                ChapterCandidateProjectionServiceTests.CreateEffectiveChapter(
                    ChapterCandidateProjectionServiceTests.CreateTrack(firstId, "a.mp3", ""), outputIndex: 1),
                ChapterCandidateProjectionServiceTests.CreateEffectiveChapter(
                    ChapterCandidateProjectionServiceTests.CreateTrack(secondId, "b.mp3", ""), outputIndex: 2)
            ],
            [
                ChapterCandidateProjectionServiceTests.CreateSnapshot(firstId, "A"),
                ChapterCandidateProjectionServiceTests.CreateSnapshot(secondId, "B")
            ],
            new ProjectMetadata());

        var scopes = _scopeBuilder.Build(projection);

        Assert.Equal(2, scopes.Count);
        Assert.All(scopes, scope => Assert.Equal(ChapterScopeKind.Mp3DiscTrack, scope.Kind));
        Assert.Equal(2, scopes.Select(scope => scope.ScopeId).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Build_AudioCdCreatesOneScopePerDiscWithoutFilenameCandidates()
    {
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var manifest = new AudioDiscProjectManifest
        {
            Discs =
            [
                new AudioDiscProjectManifestDisc
                {
                    DiscNumber = 1,
                    DiscIdentity = "audio-one",
                    Tracks = [new AudioDiscProjectManifestTrack { SourceId = firstId, GlobalIndex = 1 }]
                },
                new AudioDiscProjectManifestDisc
                {
                    DiscNumber = 2,
                    DiscIdentity = "audio-two",
                    Tracks = [new AudioDiscProjectManifestTrack { SourceId = secondId, GlobalIndex = 2 }]
                }
            ]
        };
        var projection = _projectionService.Project(
            ChapterCandidateProjectionContext.ForAudioCd("project-audio", manifest),
            [
                ChapterCandidateProjectionServiceTests.CreateEffectiveChapter(
                    ChapterCandidateProjectionServiceTests.CreateTrack(firstId, "Track01.flac", ""), outputIndex: 1),
                ChapterCandidateProjectionServiceTests.CreateEffectiveChapter(
                    ChapterCandidateProjectionServiceTests.CreateTrack(secondId, "Track02.flac", ""), outputIndex: 2)
            ],
            [
                ChapterCandidateProjectionServiceTests.CreateSnapshot(firstId, "A"),
                ChapterCandidateProjectionServiceTests.CreateSnapshot(secondId, "B")
            ],
            new ProjectMetadata());

        var scopes = _scopeBuilder.Build(projection);

        Assert.Equal(2, scopes.Count);
        Assert.All(scopes, scope => Assert.Equal(ChapterScopeKind.AudioDisc, scope.Kind));
        Assert.DoesNotContain(scopes.SelectMany(scope => scope.Candidates), candidate =>
            candidate.Source == ChapterCandidateSource.Filename);
    }

    [Fact]
    public void Build_IsDeterministicForSameInput()
    {
        var projection = CreateLocalFixture(("A", null), ("B", null));

        var first = _scopeBuilder.Build(projection);
        var second = _scopeBuilder.Build(projection);

        Assert.Equal(first.Select(scope => scope.ScopeId), second.Select(scope => scope.ScopeId));
        Assert.Equal(
            first.SelectMany(scope => scope.Members).Select(member => member.EffectiveChapterId),
            second.SelectMany(scope => scope.Members).Select(member => member.EffectiveChapterId));
        Assert.Equal(
            first.SelectMany(scope => scope.Candidates).Select(candidate =>
                (candidate.Provenance.EffectiveChapterId, candidate.Source, candidate.Normalization.NormalizedText)),
            second.SelectMany(scope => scope.Candidates).Select(candidate =>
                (candidate.Provenance.EffectiveChapterId, candidate.Source, candidate.Normalization.NormalizedText)));
    }

    private ChapterCandidateProjectionResult CreateLocalFixture(params (string Title, int? Disc)[] items)
    {
        var chapters = new List<EffectiveChapterInfo>();
        var snapshots = new List<SourceMetadataSnapshot>();

        for (var index = 0; index < items.Length; index++)
        {
            var sourceId = Guid.NewGuid();
            var track = ChapterCandidateProjectionServiceTests.CreateTrack(sourceId, items[index].Title + ".mp3", "");
            chapters.Add(ChapterCandidateProjectionServiceTests.CreateEffectiveChapter(track, outputIndex: index + 1));
            snapshots.Add(ChapterCandidateProjectionServiceTests.CreateSnapshot(
                sourceId,
                items[index].Title,
                discNumber: items[index].Disc));
        }

        return _projectionService.Project(
            ChapterCandidateProjectionContext.ForLocalFolder("project-a", []),
            chapters,
            snapshots,
            new ProjectMetadata());
    }

    private static Mp3DiscProjectManifestDisc Disc(int number, string signature, Guid sourceId) => new()
    {
        DiscNumber = number,
        Signature = signature,
        SourceFiles =
        [
            new Mp3DiscProjectManifestSourceFile
            {
                SourceId = sourceId,
                SourceFileName = number + ".mp3",
                RelativePath = number + ".mp3"
            }
        ]
    };
}
