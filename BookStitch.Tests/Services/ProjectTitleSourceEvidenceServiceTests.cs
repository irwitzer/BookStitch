using BookStitch.Models;
using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class ProjectTitleSourceEvidenceServiceTests
{
    private readonly ProjectTitleSourceEvidenceService _service = new();

    [Fact]
    public void EvaluateTitle_UsesSameSequenceGroupingAsCandidateGroups()
    {
        var snapshots = Enumerable.Range(1, 16)
            .Select(index => Snapshot($"{index:00} S Ánti Gundula"))
            .ToArray();

        var groups = _service.BuildTitleCandidateGroups(snapshots);
        var evaluation = _service.EvaluateTitle(snapshots);

        var group = Assert.Single(groups);
        Assert.Equal("S Ánti Gundula", group.Value);
        Assert.Equal(16, group.SourceCount);
        Assert.Equal(group.Value, evaluation.Selected?.Value);
        Assert.Equal(group.SourceCount, evaluation.Selected?.SourceCount);
    }

    [Fact]
    public void EvaluateFilenameTitle_CollapsesZeroPaddedOneToSixteenSequence()
    {
        var fileNames = Enumerable.Range(1, 16)
            .Select(index => $"{index:00} S Ánti Gundula.mp3")
            .ToArray();

        var evaluation = _service.EvaluateFilenameTitle(fileNames);

        Assert.Equal("S Ánti Gundula", evaluation.Selected?.Value);
        Assert.Equal(16, evaluation.Selected?.SourceCount);
        Assert.Equal(16, evaluation.CandidateCount);
    }

    [Fact]
    public void EvaluateFilenameTitle_CollapsesNonPaddedOneToSixteenSequence()
    {
        var fileNames = Enumerable.Range(1, 16)
            .Select(index => $"{index} S Ánti Gundula.mp3")
            .ToArray();

        var evaluation = _service.EvaluateFilenameTitle(fileNames);

        Assert.Equal("S Ánti Gundula", evaluation.Selected?.Value);
        Assert.Equal(16, evaluation.Selected?.SourceCount);
    }

    [Fact]
    public void EvaluateFilenameTitle_CollapsesExistingCompactZeroPaddedTechnicalPrefix()
    {
        var evaluation = _service.EvaluateFilenameTitle(
        [
            "0101John Grisham Bestechung.mp3",
            "0102John Grisham Bestechung.mp3",
            "0103John Grisham Bestechung.mp3"
        ]);

        Assert.Equal("John Grisham Bestechung", evaluation.Selected?.Value);
        Assert.Equal(3, evaluation.Selected?.SourceCount);
    }

    [Fact]
    public void BuildFilenameTitleCandidateGroups_PreservesMeaningfulNumericTitle()
    {
        var groups = _service.BuildFilenameTitleCandidateGroups(
        [
            "1984 Roman.mp3",
            "1984 Roman.mp3"
        ]);

        var group = Assert.Single(groups);
        Assert.Equal("1984 Roman", group.Value);
        Assert.Equal(2, group.SourceCount);
    }

    [Fact]
    public void BuildFilenameTitleCandidateGroups_DoesNotCollapseElevenSeptemberAsTrackSequence()
    {
        var groups = _service.BuildFilenameTitleCandidateGroups(
        [
            "11. September.mp3",
            "12. September.mp3"
        ]);

        Assert.Equal(2, groups.Count);
        Assert.Contains(groups, group => group.Value == "11. September");
        Assert.Contains(groups, group => group.Value == "12. September");
    }

    [Fact]
    public void BuildFilenameTitleCandidateGroups_DoesNotCollapseInsufficientlySequentialNumbers()
    {
        var groups = _service.BuildFilenameTitleCandidateGroups(
        [
            "1 S Ánti Gundula.mp3",
            "5 S Ánti Gundula.mp3",
            "12 S Ánti Gundula.mp3"
        ]);

        Assert.Equal(3, groups.Count);
        Assert.Contains(groups, group => group.Value == "1 S Ánti Gundula");
        Assert.Contains(groups, group => group.Value == "5 S Ánti Gundula");
        Assert.Contains(groups, group => group.Value == "12 S Ánti Gundula");
    }

    private static SourceMetadataSnapshot Snapshot(string title) =>
        new()
        {
            SourceId = Guid.NewGuid(),
            CanonicalFields = new SourceMetadataCanonicalFields
            {
                Title = title
            }
        };
}
