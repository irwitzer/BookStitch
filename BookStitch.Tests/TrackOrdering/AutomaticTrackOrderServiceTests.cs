using BookStitch.Models.TrackOrdering;
using BookStitch.Services.TrackOrdering;
using Xunit;

namespace BookStitch.Tests.TrackOrdering;

public sealed class AutomaticTrackOrderServiceTests
{
    private readonly AutomaticTrackOrderService _service = new();

    [Fact]
    public void Analyze_00101_UsesFirstTokenSequence()
    {
        var result = AnalyzeLocal(
            Input("A", "001_01_Buch.flac"),
            Input("B", "002_01_Buch.flac"),
            Input("C", "003_01_Buch.flac"),
            Input("D", "004_01_Buch.flac"));

        AssertOrder(result, "A", "B", "C", "D");
        Assert.True(result.OverallConfidence >= TrackOrderConfidence.High);
    }

    [Fact]
    public void Analyze_OrderEquivalentOffsetMetadataDoesNotConflict()
    {
        var result = AnalyzeLocal(
            Input("A", "001.mp3", metadataTrack: 17),
            Input("B", "002.mp3", metadataTrack: 18),
            Input("C", "003.mp3", metadataTrack: 19),
            Input("D", "004.mp3", metadataTrack: 20));

        AssertOrder(result, "A", "B", "C", "D");
        Assert.DoesNotContain(result.Diagnostics, item => item.Code == TrackOrderDiagnosticCode.ConflictingTrackOrder);
        Assert.Equal(TrackOrderConfidence.VeryHigh, result.OverallConfidence);
    }

    [Fact]
    public void Analyze_MetadataInversionDoesNotWinJustBecauseMetadataExists()
    {
        var result = AnalyzeLocal(
            Input("A", "001.mp3", metadataTrack: 1),
            Input("B", "002.mp3", metadataTrack: 3),
            Input("C", "003.mp3", metadataTrack: 2),
            Input("D", "004.mp3", metadataTrack: 4));

        AssertOrder(result, "A", "B", "C", "D");
        Assert.Contains(result.Diagnostics, item => item.Code == TrackOrderDiagnosticCode.ConflictingTrackOrder);
        Assert.True(result.OverallConfidence <= TrackOrderConfidence.Medium);
    }

    [Fact]
    public void Analyze_FilenameAndFolderConsensusBeatsSingleWrongMetadata()
    {
        var result = AnalyzeLocal(
            Input("A", "Track01.mp3", folder: "CD1", metadataTrack: 1, metadataDisc: 1),
            Input("B", "Track02.mp3", folder: "CD1", metadataTrack: 3, metadataDisc: 1),
            Input("C", "Track03.mp3", folder: "CD1", metadataTrack: 2, metadataDisc: 1),
            Input("D", "Track04.mp3", folder: "CD1", metadataTrack: 4, metadataDisc: 1));

        AssertOrder(result, "A", "B", "C", "D");
    }

    [Fact]
    public void Analyze_GlobalFilenameAndDiscRelativeTracksAreCompatible()
    {
        var result = AnalyzeLocal(
            Input("A", "001 D1T1.mp3", folder: "CD1", metadataTrack: 1, metadataDisc: 1),
            Input("B", "002 D1T2.mp3", folder: "CD1", metadataTrack: 2, metadataDisc: 1),
            Input("C", "003 D2T1.mp3", folder: "CD2", metadataTrack: 1, metadataDisc: 2),
            Input("D", "004 D2T2.mp3", folder: "CD2", metadataTrack: 2, metadataDisc: 2));

        AssertOrder(result, "A", "B", "C", "D");
        Assert.DoesNotContain(result.Diagnostics, item => item.Code == TrackOrderDiagnosticCode.ConflictingTrackOrder);
    }

    [Fact]
    public void Analyze_AudioDiscConfirmedContextBeatsHeuristics()
    {
        var result = _service.Analyze(new AutomaticTrackOrderRequest(
            TrackOrderProjectType.AudioDisc,
            [
                Input("A", "999.mp3", metadataTrack: 99, confirmed: new ConfirmedOrderContext(1, 1, 1)),
                Input("B", "001.mp3", metadataTrack: 1, confirmed: new ConfirmedOrderContext(1, 2, 2)),
                Input("C", "002.mp3", metadataTrack: 2, confirmed: new ConfirmedOrderContext(2, 1, 3))
            ]));

        AssertOrder(result, "A", "B", "C");
        Assert.Equal(TrackOrderConfidence.VeryHigh, result.OverallConfidence);
    }

    [Fact]
    public void Analyze_DiscSequenceGapKeepsOrderConfidenceAndAddsDiagnostic()
    {
        var result = _service.Analyze(new AutomaticTrackOrderRequest(
            TrackOrderProjectType.Mp3Disc,
            [
                Input("A", "Track01.mp3", folder: "CD1", metadataTrack: 1, confirmed: new ConfirmedOrderContext(1, null, null)),
                Input("B", "Track01.mp3", folder: "CD2", metadataTrack: 1, confirmed: new ConfirmedOrderContext(2, null, null)),
                Input("C", "Track01.mp3", folder: "CD4", metadataTrack: 1, confirmed: new ConfirmedOrderContext(4, null, null))
            ]));

        AssertOrder(result, "A", "B", "C");
        Assert.Equal(TrackOrderConfidence.High, result.OverallConfidence);
        Assert.Contains(result.Diagnostics, item => item.Code == TrackOrderDiagnosticCode.DiscSequenceGap);
    }

    [Fact]
    public void Analyze_DoesNotAddDiscSequenceGapWhenPartialDiscSetStartsAfterOne()
    {
        var result = _service.Analyze(new AutomaticTrackOrderRequest(
            TrackOrderProjectType.Mp3Disc,
            [
                Input("A", "Track01.mp3", folder: "CD3", metadataTrack: 1, confirmed: new ConfirmedOrderContext(3, null, null)),
                Input("B", "Track01.mp3", folder: "CD4", metadataTrack: 1, confirmed: new ConfirmedOrderContext(4, null, null)),
                Input("C", "Track01.mp3", folder: "CD5", metadataTrack: 1, confirmed: new ConfirmedOrderContext(5, null, null))
            ]));

        AssertOrder(result, "A", "B", "C");
        Assert.DoesNotContain(result.Diagnostics, item => item.Code == TrackOrderDiagnosticCode.DiscSequenceGap);
    }

    [Fact]
    public void Analyze_WithoutEvidenceUsesDeterministicFallback()
    {
        var result = AnalyzeLocal(
            Input("B", "Beta.mp3", inputPosition: 2),
            Input("A", "Alpha.mp3", inputPosition: 1),
            Input("C", "Gamma.mp3", inputPosition: 3));

        AssertOrder(result, "A", "B", "C");
        Assert.Equal(TrackOrderConfidence.Unresolved, result.OverallConfidence);
        Assert.Contains(result.Diagnostics, item => item.Code == TrackOrderDiagnosticCode.FallbackOrderUsed);
    }

    [Fact]
    public void Analyze_IsInputPermutationInvariant()
    {
        var forward = AnalyzeLocal(
            Input("A", "001.mp3"),
            Input("B", "002.mp3"),
            Input("C", "010.mp3"));
        var shuffled = AnalyzeLocal(
            Input("C", "010.mp3"),
            Input("A", "001.mp3"),
            Input("B", "002.mp3"));

        Assert.Equal(
            forward.Tracks.OrderBy(item => item.TrackKey).Select(item => item.AutomaticOrderPosition),
            shuffled.Tracks.OrderBy(item => item.TrackKey).Select(item => item.AutomaticOrderPosition));
    }

    [Fact]
    public void Analyze_MetadataOffsetAndMonotoneTransformKeepOrder()
    {
        var offset = AnalyzeLocal(
            Input("A", "001.mp3", metadataTrack: 101),
            Input("B", "002.mp3", metadataTrack: 102),
            Input("C", "003.mp3", metadataTrack: 103));
        var transformed = AnalyzeLocal(
            Input("A", "001.mp3", metadataTrack: 10),
            Input("B", "002.mp3", metadataTrack: 20),
            Input("C", "003.mp3", metadataTrack: 30));

        AssertOrder(offset, "A", "B", "C");
        AssertOrder(transformed, "A", "B", "C");
    }

    [Fact]
    public void Analyze_LeadingZeroVariantsKeepOrder()
    {
        var result = AnalyzeLocal(
            Input("A", "001.mp3"),
            Input("B", "02.mp3"),
            Input("C", "10.mp3"));

        AssertOrder(result, "A", "B", "C");
    }

    [Fact]
    public void Analyze_FallsBackWhenEveryCandidateModelHasAmbiguousTies()
    {
        var result = AnalyzeLocal(
            Input("A", "001_001_Copy.mp3"),
            Input("B", "001_002_Copy.mp3"),
            Input("C", "002_001_Copy.mp3"),
            Input("D", "002_002_Copy.mp3"));

        AssertOrder(result, "A", "B", "C", "D");
        Assert.Equal(TrackOrderConfidence.Unresolved, result.OverallConfidence);
        Assert.Contains(result.Diagnostics, item => item.Code == TrackOrderDiagnosticCode.FallbackOrderUsed);
    }

    private AutomaticTrackOrderResult AnalyzeLocal(params TrackOrderInput[] inputs) =>
        _service.Analyze(new AutomaticTrackOrderRequest(TrackOrderProjectType.LocalFolder, inputs));

    private static TrackOrderInput Input(
        string key,
        string fileName,
        string folder = "",
        int? metadataTrack = null,
        int? metadataDisc = null,
        int inputPosition = 0,
        ConfirmedOrderContext? confirmed = null)
    {
        return new TrackOrderInput(
            key,
            inputPosition,
            "C:/" + folder + "/" + fileName,
            fileName,
            folder,
            new OriginalOrderMetadata(metadataTrack, null, metadataDisc, null),
            confirmed);
    }

    private static void AssertOrder(AutomaticTrackOrderResult result, params string[] expectedKeys)
    {
        Assert.Equal(
            expectedKeys,
            result.Tracks
                .OrderBy(item => item.AutomaticOrderPosition)
                .Select(item => item.TrackKey)
                .ToArray());
        Assert.Equal(Enumerable.Range(1, expectedKeys.Length), result.Tracks.Select(item => item.AutomaticOrderPosition).Order());
    }
}
