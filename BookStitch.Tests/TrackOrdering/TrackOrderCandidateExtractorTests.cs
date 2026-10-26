using BookStitch.Models.TrackOrdering;
using BookStitch.Services.TrackOrdering;
using Xunit;

namespace BookStitch.Tests.TrackOrdering;

public sealed class TrackOrderCandidateExtractorTests
{
    private readonly TrackOrderCandidateExtractor _extractor = new();

    [Theory]
    [InlineData("CD1", 1)]
    [InlineData("Disc 02", 2)]
    [InlineData("Disk-3", 3)]
    [InlineData("Der Hobbit [Disc 4]", 4)]
    [InlineData("Teil 05", 5)]
    [InlineData("Vol. 6", 6)]
    public void Extract_RecognizesExplicitDiscMarkers(string folder, int expected)
    {
        var set = _extractor.Extract(Input("track01.mp3", folder));

        Assert.Contains(set.Candidates, candidate =>
            candidate.Kind == TrackOrderCandidateKind.Disc &&
            candidate.Source == TrackOrderEvidenceSource.FolderName &&
            candidate.Value == expected);
    }

    [Theory]
    [InlineData("Track 07.mp3", 7)]
    [InlineData("Trk08.mp3", 8)]
    public void Extract_RecognizesExplicitTrackMarkers(string fileName, int expected)
    {
        var set = _extractor.Extract(Input(fileName));

        Assert.Contains(set.Candidates, candidate =>
            candidate.Kind == TrackOrderCandidateKind.Track &&
            candidate.Pattern == TrackOrderPattern.ExplicitTrackMarker &&
            candidate.Value == expected);
    }

    [Theory]
    [InlineData("Kapitola 3.mp3", 3)]
    [InlineData("Rozdział 3.mp3", 3)]
    [InlineData("Глава 3.mp3", 3)]
    [InlineData("Κεφάλαιο 3.mp3", 3)]
    [InlineData("第3章标题.mp3", 3)]
    [InlineData("第3話タイトル.mp3", 3)]
    public void Extract_RecognizesInternationalExplicitTrackMarkers(string fileName, int expected)
    {
        var set = _extractor.Extract(Input(fileName));

        Assert.Contains(set.Candidates, candidate =>
            candidate.Kind == TrackOrderCandidateKind.Track &&
            candidate.Pattern == TrackOrderPattern.ExplicitTrackMarker &&
            candidate.Value == expected);
    }

    [Theory]
    [InlineData("Część 2", 2)]
    [InlineData("Часть 3", 3)]
    [InlineData("Μέρος 4", 4)]
    [InlineData("Deel 5", 5)]
    public void Extract_RecognizesInternationalExplicitDiscMarkers(string folder, int expected)
    {
        var set = _extractor.Extract(Input("track01.mp3", folder));

        Assert.Contains(set.Candidates, candidate =>
            candidate.Kind == TrackOrderCandidateKind.Disc &&
            candidate.Pattern == TrackOrderPattern.ExplicitDiscMarker &&
            candidate.Value == expected);
    }

    [Theory]
    [InlineData("1984 Roman.mp3")]
    [InlineData("Version 2 Finale.mp3")]
    [InlineData("Room 101.mp3")]
    public void Extract_SemanticNumbersDoNotBecomeExplicitTrackMarkers(string fileName)
    {
        var set = _extractor.Extract(Input(fileName));

        Assert.DoesNotContain(set.Candidates, candidate =>
            candidate.Kind == TrackOrderCandidateKind.Track &&
            candidate.Pattern == TrackOrderPattern.ExplicitTrackMarker);
    }

    [Fact]
    public void Extract_SeparatesMultipleFilenameTokens()
    {
        var set = _extractor.Extract(Input("Harry Potter 5 - 2003 - CD2 - Track07.mp3"));

        Assert.Contains(set.Candidates, candidate => candidate.Kind == TrackOrderCandidateKind.Disc && candidate.Value == 2);
        Assert.Contains(set.Candidates, candidate => candidate.Kind == TrackOrderCandidateKind.Track && candidate.Value == 7);
        Assert.Contains(set.Candidates, candidate => candidate.Kind == TrackOrderCandidateKind.Sequence && candidate.TokenIndex == 0 && candidate.Value == 5);
        Assert.Contains(set.Candidates, candidate => candidate.Kind == TrackOrderCandidateKind.Sequence && candidate.TokenIndex == 1 && candidate.Value == 2003);
    }

    [Fact]
    public void Extract_Separates00101Tokens()
    {
        var set = _extractor.Extract(Input("001_01_Buch.flac"));

        Assert.Contains(set.Candidates, candidate => candidate.Kind == TrackOrderCandidateKind.Sequence && candidate.TokenIndex == 0 && candidate.Value == 1);
        Assert.Contains(set.Candidates, candidate => candidate.Kind == TrackOrderCandidateKind.Sequence && candidate.TokenIndex == 1 && candidate.Value == 1);
    }

    [Fact]
    public void Extract_UsesConfirmedAndMetadataWithoutDecision()
    {
        var input = Input(
            "001.mp3",
            metadata: new OriginalOrderMetadata(TrackNumber: 9, TrackTotal: 10, DiscNumber: 2, DiscTotal: 3),
            confirmed: new ConfirmedOrderContext(DiscNumber: 1, TrackNumber: 1, GlobalPosition: 1));

        var set = _extractor.Extract(input);

        Assert.Contains(set.Candidates, candidate => candidate.Source == TrackOrderEvidenceSource.ConfirmedImportContext && candidate.Value == 1);
        Assert.Contains(set.Candidates, candidate => candidate.Source == TrackOrderEvidenceSource.Metadata && candidate.Value == 9);
        Assert.Contains(set.Candidates, candidate => candidate.Source == TrackOrderEvidenceSource.Metadata && candidate.Value == 2);
    }

    private static TrackOrderInput Input(
        string fileName,
        string relativeFolder = "",
        OriginalOrderMetadata? metadata = null,
        ConfirmedOrderContext? confirmed = null)
    {
        return new TrackOrderInput(
            TrackKey: fileName + "|" + relativeFolder,
            DeterministicInputPosition: 0,
            FullPath: "C:/" + relativeFolder + "/" + fileName,
            FileName: fileName,
            RelativeFolder: relativeFolder,
            Metadata: metadata ?? new OriginalOrderMetadata(null, null, null, null),
            ConfirmedContext: confirmed);
    }
}
