using BookStitch.Dialog;
using BookStitch.Models;
using Xunit;

namespace BookStitch.Tests.Dialog;

public sealed class ConceptSeriesEntryCandidateTests
{
    [Fact]
    public void Candidate_preserves_structured_entry_and_builds_display_value()
    {
        var candidate = new ConceptSeriesEntryCandidate(
            new SeriesEntry("Die Bäume", "1.5"),
            16,
            MetadataOrigin.SourceAggregation,
            isAutomaticWinner: true);

        Assert.Equal(new SeriesEntry("Die Bäume", "1.5"), candidate.Entry);
        Assert.Equal("Die Bäume 1.5", candidate.DisplayValue);
        Assert.Equal(16, candidate.SourceCount);
    }
}
