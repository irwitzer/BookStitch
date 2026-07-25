using BookStitch.Models;
using BookStitch.Services;
using System.ComponentModel;
using Xunit;

namespace BookStitch.Tests.Services;

public class TrackListActionServiceTests
{
    private readonly TrackListActionService _service = new();

    [Fact]
    public void MoveSelectedUp_KeepsSelectedBlockOrder()
    {
        var tracks = CreateTracks("A", "B", "C", "D");
        var selected = new[] { tracks[2], tracks[3] };

        var result = _service.MoveSelectedUp(tracks, selected);

        Assert.Equal(new[] { "A", "C", "D", "B" }, result.Select(track => track.FileName));
    }

    [Fact]
    public void MoveSelectedDown_KeepsSelectedBlockOrder()
    {
        var tracks = CreateTracks("A", "B", "C", "D");
        var selected = new[] { tracks[0], tracks[1] };

        var result = _service.MoveSelectedDown(tracks, selected);

        Assert.Equal(new[] { "C", "A", "B", "D" }, result.Select(track => track.FileName));
    }

    [Fact]
    public void MoveSelectedToTop_PreservesVisibleSelectionOrder()
    {
        var tracks = CreateTracks("A", "B", "C", "D");
        var selected = new[] { tracks[2], tracks[0] };

        var result = _service.MoveSelectedToTop(tracks, selected);

        Assert.Equal(new[] { "C", "A", "B", "D" }, result.Select(track => track.FileName));
    }

    [Fact]
    public void MoveSelectedToBottom_PreservesVisibleSelectionOrder()
    {
        var tracks = CreateTracks("A", "B", "C", "D");
        var selected = new[] { tracks[1], tracks[3] };

        var result = _service.MoveSelectedToBottom(tracks, selected);

        Assert.Equal(new[] { "A", "C", "B", "D" }, result.Select(track => track.FileName));
    }

    [Fact]
    public void ExcludeSelected_MarksOnlySelectedTracks()
    {
        var tracks = CreateTracks("A", "B", "C", "D");
        var selected = new[] { tracks[1], tracks[3] };

        var changed = _service.ExcludeSelected(selected);

        Assert.Equal(2, changed);
        Assert.False(tracks[0].IsExcluded);
        Assert.True(tracks[1].IsExcluded);
        Assert.False(tracks[2].IsExcluded);
        Assert.True(tracks[3].IsExcluded);
    }


    [Fact]
    public void ToggleSelectedForDelete_ExcludesWhenAllSelectedTracksAreActive()
    {
        var tracks = CreateTracks("A", "B");

        var result = _service.ToggleSelectedForDelete(tracks);

        Assert.Equal(TrackExclusionToggleAction.Excluded, result.Action);
        Assert.Equal(2, result.ChangedCount);
        Assert.All(tracks, track => Assert.True(track.IsExcluded));
    }

    [Fact]
    public void ToggleSelectedForDelete_RestoresWhenSelectionContainsExcludedTrack()
    {
        var tracks = CreateTracks("A", "B", "C");
        tracks[1].IsExcluded = true;
        tracks[1].ExcludedChapterTitle = tracks[1].ChapterTitle;
        tracks[1].ChapterTitle = string.Empty;

        var result = _service.ToggleSelectedForDelete(tracks);

        Assert.Equal(TrackExclusionToggleAction.Restored, result.Action);
        Assert.Equal(1, result.ChangedCount);
        Assert.False(tracks[1].IsExcluded);
        Assert.False(tracks[0].IsExcluded);
        Assert.False(tracks[2].IsExcluded);
    }

    [Fact]
    public void Sort_UsesNaturalFileNameOrder()
    {
        var tracks = CreateTracks("Track 10.mp3", "Track 2.mp3", "Track 1.mp3");

        var result = _service.Sort(tracks, "FileName", ListSortDirection.Ascending);

        Assert.Equal(new[] { "Track 1.mp3", "Track 2.mp3", "Track 10.mp3" }, result.Select(track => track.FileName));
    }

    [Fact]
    public void Sort_DurationUsesTimeValue()
    {
        var tracks = new[]
        {
            new TrackInfo { FileName = "A", Duration = "01:10" },
            new TrackInfo { FileName = "B", Duration = "00:45" },
            new TrackInfo { FileName = "C", Duration = "01:05" }
        };

        var result = _service.Sort(tracks, "Duration", ListSortDirection.Ascending);

        Assert.Equal(new[] { "B", "C", "A" }, result.Select(track => track.FileName));
    }

    [Fact]
    public void Renumber_RewritesIndexSequentially()
    {
        var tracks = CreateTracks("A", "B", "C");
        tracks[0].Index = 10;
        tracks[1].Index = 20;
        tracks[2].Index = 30;

        _service.Renumber(tracks);

        Assert.Equal(new[] { 1, 2, 3 }, tracks.Select(track => track.Index));
    }


    [Fact]
    public void Renumber_UpdatesLeadingChapterNumbersAfterReordering()
    {
        var tracks = new List<TrackInfo>
        {
            new() { Index = 2, FileName = "002.flac", ChapterTitle = "002 Zweites Kapitel" },
            new() { Index = 1, FileName = "001.flac", ChapterTitle = "001 Erstes Kapitel" }
        };

        _service.Renumber(tracks);

        Assert.Equal(new[] { 1, 2 }, tracks.Select(track => track.Index));
        Assert.Equal(new[] { "1 Zweites Kapitel", "2 Erstes Kapitel" }, tracks.Select(track => track.ChapterTitle));
    }

    [Fact]
    public void Renumber_KeepsCustomChapterTitlesWithoutLeadingNumber()
    {
        var tracks = new List<TrackInfo>
        {
            new() { Index = 9, FileName = "intro.flac", ChapterTitle = "Prolog" }
        };

        _service.Renumber(tracks);

        Assert.Equal(1, tracks[0].Index);
        Assert.Equal("Prolog", tracks[0].ChapterTitle);
    }

    private static List<TrackInfo> CreateTracks(params string[] names)
    {
        return names
            .Select((name, index) => new TrackInfo
            {
                Index = index + 1,
                FileName = name,
                ChapterTitle = name
            })
            .ToList();
    }


    [Fact]
    public void UpdateGeneratedChapterTitles_ReplacesProjectGeneratedTitlesOnly()
    {
        var service = new TrackListActionService();
        var tracks = new List<TrackInfo>
        {
            new() { Index = 1, ChapterTitle = "001 Alter Titel" },
            new() { Index = 2, ChapterTitle = "Prolog" },
            new() { Index = 3, ChapterTitle = "003 Kapitel" }
        };

        var changed = service.UpdateGeneratedChapterTitles(tracks, "Alter Titel", "Neuer Titel");

        Assert.Equal(2, changed);
        Assert.Equal("1 Neuer Titel", tracks[0].ChapterTitle);
        Assert.Equal("Prolog", tracks[1].ChapterTitle);
        Assert.Equal("3 Neuer Titel", tracks[2].ChapterTitle);
    }

    [Fact]
    public void UpdateGeneratedChapterTitles_LeavesExcludedTracksUntouched()
    {
        var service = new TrackListActionService();
        var tracks = new List<TrackInfo>
        {
            new() { Index = 1, ChapterTitle = "001 Alter Titel", IsExcluded = true }
        };

        var changed = service.UpdateGeneratedChapterTitles(tracks, "Alter Titel", "Neuer Titel");

        Assert.Equal(0, changed);
        Assert.Equal("001 Alter Titel", tracks[0].ChapterTitle);
    }

    [Theory]
    [InlineData(1, 9, true, "1")]
    [InlineData(1, 10, true, "01")]
    [InlineData(9, 99, true, "09")]
    [InlineData(1, 100, true, "001")]
    [InlineData(99, 999, true, "099")]
    [InlineData(1, 1000, true, "0001")]
    [InlineData(999, 1000, true, "0999")]
    [InlineData(1, 1000, false, "1")]
    [InlineData(999, 1000, false, "999")]
    public void ChapterNumberingService_FormatsNumberFromTotalChapterCount(
        int index,
        int chapterCount,
        bool useLeadingZeros,
        string expected)
    {
        var service = new ChapterNumberingService();

        var result = service.FormatNumber(index, chapterCount, useLeadingZeros);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void Renumber_UsesActiveTrackCountAndIgnoresExcludedTracks()
    {
        var tracks = Enumerable.Range(1, 11)
            .Select(index => new TrackInfo
            {
                Index = index,
                ChapterTitle = $"{index:000} Kapitel",
                IsExcluded = index == 11
            })
            .ToList();

        _service.Renumber(tracks, useLeadingZeros: true);

        Assert.Equal("01 Kapitel", tracks[0].ChapterTitle);
        Assert.Equal("10 Kapitel", tracks[9].ChapterTitle);
        Assert.Equal(0, tracks[10].Index);
        Assert.Equal("011 Kapitel", tracks[10].ChapterTitle);
    }

    [Fact]
    public void Renumber_CanRemoveAllLeadingZeros()
    {
        var tracks = Enumerable.Range(1, 12)
            .Select(index => new TrackInfo
            {
                Index = index,
                ChapterTitle = $"{index:000} Kapitel"
            })
            .ToList();

        _service.Renumber(tracks, useLeadingZeros: false);

        Assert.Equal("1 Kapitel", tracks[0].ChapterTitle);
        Assert.Equal("12 Kapitel", tracks[11].ChapterTitle);
    }

}
