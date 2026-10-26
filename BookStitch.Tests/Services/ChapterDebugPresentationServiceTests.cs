using BookStitch.Models;
using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class ChapterDebugPresentationServiceTests
{
    [Fact]
    public void Create_ProjectsExistingResolveAnalysisWithoutRunningAnotherEngine()
    {
        var track = new TrackInfo
        {
            SourceId = Guid.NewGuid(),
            FilePath = "source.mp3",
            FileName = "source.mp3",
            TagTitle = "Uniform source",
            SourceMetadataFields = new SourceMetadataCanonicalFields { Title = "Uniform source" },
            DurationTicks = TimeSpan.FromSeconds(1).Ticks
        };
        var request = new ChapterAutoEngineRequest(
            [track],
            ProjectManifestTypes.FolderProject,
            "debug-test",
            string.Empty,
            new ChapterCanonicalMetadataSnapshot(
                3,
                "User title",
                MetadataOrigin.User,
                null,
                MetadataOrigin.None),
            new ChapterSettings { Mode = ChapterSettingsService.ModeAuto },
            LegacyUseLeadingZeros: false);
        var engineResult = new ChapterAutoEngineService().Apply(request);
        var active = new ActiveChapterResult(
            ChapterSettingsService.ModeAuto,
            engineResult.RenderedTimeline,
            engineResult);

        var debug = new ChapterDebugPresentationService().Create(active, track);

        Assert.Same(engineResult.ResolveResult.Chapters[0], debug.Resolved);
        Assert.Equal(ChapterCandidateSource.CanonicalTitle, debug.Winner?.Source);
        Assert.Equal(MetadataOrigin.User, debug.Winner?.MetadataOrigin);
        Assert.NotNull(debug.RunnerUp);
        Assert.NotNull(debug.SourceSummary);
        Assert.False(string.IsNullOrWhiteSpace(debug.DominantGroupText));
    }

    [Fact]
    public void Create_NoChaptersHasNoArtificialResolveDiagnostics()
    {
        var debug = new ChapterDebugPresentationService().Create(
            new ActiveChapterResult(ChapterSettingsService.ModeNone, []),
            selectedTrack: null);

        Assert.Equal(ChapterSettingsService.ModeNone, debug.ActiveMode);
        Assert.Null(debug.Resolved);
        Assert.Null(debug.Winner);
    }
}
