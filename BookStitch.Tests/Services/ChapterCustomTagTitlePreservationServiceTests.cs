using BookStitch.Models;
using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class ChapterCustomTagTitlePreservationServiceTests
{
    [Theory]
    [InlineData("Kapitel 34", "Kapitel 35")]
    [InlineData("Chapter thirty four", "Chapter thirty five")]
    [InlineData("Chapitre XIV", "Chapitre XV")]
    [InlineData("Capítulo três", "Capítulo quatro")]
    [InlineData("Глава 34", "Глава 35")]
    [InlineData("第十四章", "第十五章")]
    [InlineData("Kapittel 34", "Kapittel 35")]
    [InlineData("Luku 34", "Luku 35")]
    [InlineData("Bölüm 34", "Bölüm 35")]
    [InlineData("Chương 34", "Chương 35")]
    [InlineData("অধ্যায় 34", "অধ্যায় 35")]
    public void IndependentRepeatedLabelsArePreservedAcrossLanguagesAndManualOrder(string first, string second)
    {
        var tracks = new[] { Track(125, "Zweiter Teil " + first), Track(126, first), Track(127, second) };
        var original = tracks.Select(track => track.TagTitle).ToArray();
        var service = new ChapterCustomTagTitlePreservationService();
        var result = service.Build(tracks);
        Assert.Equal("Zweiter Teil " + first, result[tracks[0]]);
        Assert.Equal(first, result[tracks[1]]);
        Assert.Equal(second, result[tracks[2]]);
        var reordered = service.Build(tracks.Reverse());
        Assert.All(tracks, track => Assert.Equal(result[track], reordered[track]));
        Assert.Equal(original, tracks.Select(track => track.TagTitle));
    }

    [Theory]
    [InlineData("Chapter 125", "Chapter 126", "Chapter 127")]
    [InlineData("Chapter 34", "Chapter 34", "Chapter 34")]
    [InlineData("Track 34", "Track 34", "Track 35")]
    [InlineData("CD 34", "CD 34", "CD 35")]
    [InlineData("Chapter 34 Disc 1", "Chapter 34 Disc 1", "Chapter 35 Disc 1")]
    public void TrackCountingUniformLabelsAndAmbiguousMarkersDoNotActivatePreservation(string a, string b, string c)
    {
        Assert.Empty(new ChapterCustomTagTitlePreservationService().Build(
            [Track(125, a), Track(126, b), Track(127, c)]));
    }

    [Fact]
    public void IncompleteProofAndDifferentDiscsDoNotCombineIntoEvidence()
    {
        var tracks = new[] { Track(125, "Chapter 34"), Track(126, "Chapter 34"), Track(127, "Chapter 35") };
        var service = new ChapterCustomTagTitlePreservationService();
        Assert.Empty(service.Build(tracks.Take(2)));
        tracks[2].DiscNumber = 2;
        Assert.Empty(service.Build(tracks));
        tracks[2].DiscNumber = 1;
        tracks[2].SourceTrackNumber = 1; // local tag number contradicts the prefix
        Assert.Empty(service.Build(tracks));
    }

    [Theory]
    [InlineData("CleanTagTitle", "Zweiter Teil Kapitel 34", "Kapitel 34", "Kapitel 35")]
    [InlineData("OriginalTagTitle", "125 Zweiter Teil Kapitel 34", "126 Kapitel 34", "127 Kapitel 35")]
    [InlineData("CleanFileName", "Quelle", "Quelle", "Quelle")]
    public void CustomEngineAndPreviewUsePreservationOnlyForCleanTagSlot(string slot, string a, string b, string c)
    {
        var tracks = new[] { Track(125, "Zweiter Teil Kapitel 34"), Track(126, "Kapitel 34"), Track(127, "Kapitel 35") };
        var settings = new ChapterSettings
        {
            Mode = "Custom", Slot1 = "Empty", Slot2 = "Empty", Slot3 = slot,
            NumberingStyle = "Plain", PreviewDataSource = "CurrentProject"
        };
        var request = new ChapterAutoEngineRequest(tracks, ProjectManifestTypes.Mp3DiscProject,
            "test", "", new ChapterCanonicalMetadataSnapshot(1, "Buch", MetadataOrigin.User, null, MetadataOrigin.None),
            settings, LegacyUseLeadingZeros: false);
        var result = new ChapterCustomEngineService().Apply(request);
        Assert.Equal(new[] { a, b, c }, result.RenderedTimeline.Select(chapter => chapter.Title));
        var preview = new ChapterSettingsPreviewService().CreatePreview(settings, false,
            new ChapterPreviewData("Buch", "", tracks), maxItems: 3);
        Assert.Equal(string.Join(Environment.NewLine, a, b, c), preview);
        Assert.Contains("title=" + a, new ExportChapterService().BuildFfmpegChapterMetadataFromTimeline(result.RenderedTimeline));
    }

    private static TrackInfo Track(int ordinal, string content) => new()
    {
        SourceTrackNumber = ordinal, DiscNumber = 1, TrackNumber = ordinal - 124,
        TagTitle = $"{ordinal:D3} {content}", FileName = $"{ordinal:D3}_Quelle.mp3",
        DurationTicks = TimeSpan.FromSeconds(1).Ticks
    };
}
