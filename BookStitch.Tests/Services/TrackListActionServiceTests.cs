using BookStitch.Models;
using BookStitch.Services;
using System.ComponentModel;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class TrackListActionServiceTests
{
    private readonly TrackListActionService _service = new();

    [Fact]
    public void Original_channel_sort_uses_source_channels_without_changing_prepared_channels()
    {
        var tracks = CreateTracks("A", "B", "C");
        tracks[0].Channels = 6;
        tracks[1].Channels = 2;
        tracks[2].Channels = 1;
        foreach (var track in tracks)
        {
            track.PreparedConvertedPreset = "AAC Mono 64 kbps";
            track.HasReusableConvertedFile = true;
        }
        var sorted = _service.Sort(tracks, "Channels", ListSortDirection.Ascending);
        Assert.Equal(new[] { "C", "B", "A" }, sorted.Select(track => track.FileName));
        Assert.All(tracks, track => Assert.Equal("Mono", track.DisplayPreparedChannelLayout));
    }

    [Fact]
    public void Sample_rate_sort_uses_numeric_source_values_without_changing_original_indexes()
    {
        var tracks = CreateTracks("A", "B", "C");
        tracks[0].SampleRateHz = 96000;
        tracks[1].SampleRateHz = 11025;
        tracks[2].SampleRateHz = 44100;
        var indexes = tracks.Select(track => track.Index).ToArray();
        var sorted = _service.Sort(tracks, "SampleRateHz", ListSortDirection.Ascending);
        Assert.Equal(new[] { "B", "C", "A" }, sorted.Select(track => track.FileName));
        Assert.Equal(indexes, tracks.Select(track => track.Index));
    }

    [Fact]
    public void MoveSelectedUp_KeepsSelectedBlockOrder()
    {
        var tracks = CreateTracks("A", "B", "C", "D");
        var selected = new[] { tracks[2], tracks[3] };

        var result = _service.MoveSelectedUp(tracks, selected);

        Assert.Equal(["A", "C", "D", "B"], result.Select(track => track.FileName));
    }

    [Fact]
    public void MoveSelectedDown_KeepsSelectedBlockOrder()
    {
        var tracks = CreateTracks("A", "B", "C", "D");
        var selected = new[] { tracks[0], tracks[1] };

        var result = _service.MoveSelectedDown(tracks, selected);

        Assert.Equal(["C", "A", "B", "D"], result.Select(track => track.FileName));
    }

    [Fact]
    public void MoveSelectedUp_PreservesNonContiguousSelectionOrderAndStopsAtTop()
    {
        var tracks = CreateTracks("A", "B", "C", "D", "E");
        var selected = new[] { tracks[0], tracks[2], tracks[4] };

        var result = _service.MoveSelectedUp(tracks, selected);

        Assert.Equal(["A", "C", "B", "E", "D"], result.Select(track => track.FileName));
        Assert.Equal(["A", "C", "E"], result.Where(selected.Contains).Select(track => track.FileName));
    }

    [Fact]
    public void MoveSelectedDown_PreservesNonContiguousSelectionOrderAndStopsAtBottom()
    {
        var tracks = CreateTracks("A", "B", "C", "D", "E");
        var selected = new[] { tracks[0], tracks[2], tracks[4] };

        var result = _service.MoveSelectedDown(tracks, selected);

        Assert.Equal(["B", "A", "D", "C", "E"], result.Select(track => track.FileName));
        Assert.Equal(["A", "C", "E"], result.Where(selected.Contains).Select(track => track.FileName));
    }

    [Fact]
    public void MoveSelectedToTop_PreservesVisibleSelectionOrder()
    {
        var tracks = CreateTracks("A", "B", "C", "D");
        var selected = new[] { tracks[2], tracks[0] };

        var result = _service.MoveSelectedToTop(tracks, selected);

        Assert.Equal(["C", "A", "B", "D"], result.Select(track => track.FileName));
    }

    [Fact]
    public void MoveSelectedToBottom_PreservesVisibleSelectionOrder()
    {
        var tracks = CreateTracks("A", "B", "C", "D");
        var selected = new[] { tracks[1], tracks[3] };

        var result = _service.MoveSelectedToBottom(tracks, selected);

        Assert.Equal(["A", "C", "B", "D"], result.Select(track => track.FileName));
    }

    [Fact]
    public void MoveSelectedToTop_UsesCurrentOrderAndKeepsAutomaticOrder()
    {
        var tracks = new List<TrackInfo>
        {
            new() { FileName = "A", AutomaticOrderPosition = 1 },
            new() { FileName = "B", AutomaticOrderPosition = 2 },
            new() { FileName = "C", AutomaticOrderPosition = 3 }
        };

        var moved = _service.MoveSelectedToTop(tracks, [tracks[2]]);

        Assert.Equal(["C", "A", "B"], moved.Select(track => track.FileName));
        Assert.Equal([3, 1, 2], moved.Select(track => track.AutomaticOrderPosition));
    }

    [Fact]
    public void ExcludeSelected_MarksOnlySelectedTracksAndStoresRestoreTitle()
    {
        var tracks = CreateTracks("A", "B", "C", "D");
        var selected = new[] { tracks[1], tracks[3] };

        var changed = _service.ExcludeSelected(selected);

        Assert.Equal(2, changed);
        Assert.False(tracks[0].IsExcluded);
        Assert.True(tracks[1].IsExcluded);
        Assert.Equal("B", tracks[1].ExcludedChapterTitle);
        Assert.Equal(string.Empty, tracks[1].ChapterTitle);
        Assert.False(tracks[2].IsExcluded);
        Assert.True(tracks[3].IsExcluded);
    }

    [Fact]
    public void RestoreSelected_RestoresChapterTitle()
    {
        var tracks = CreateTracks("A", "B");
        _service.ExcludeSelected([tracks[1]]);

        var changed = _service.RestoreSelected([tracks[1]]);

        Assert.Equal(1, changed);
        Assert.False(tracks[1].IsExcluded);
        Assert.Equal("B", tracks[1].ChapterTitle);
        Assert.Equal(string.Empty, tracks[1].ExcludedChapterTitle);
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

        Assert.Equal(["Track 1.mp3", "Track 2.mp3", "Track 10.mp3"], result.Select(track => track.FileName));
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

        Assert.Equal(["B", "C", "A"], result.Select(track => track.FileName));
    }

    [Fact]
    public void Sort_UsesAutomaticOrderPosition()
    {
        var tracks = CreateTracks("A", "B", "C");
        tracks[0].AutomaticOrderPosition = 3;
        tracks[1].AutomaticOrderPosition = 1;
        tracks[2].AutomaticOrderPosition = 2;

        var result = _service.Sort(tracks, "AutomaticOrderPosition", ListSortDirection.Ascending);

        Assert.Equal(["B", "C", "A"], result.Select(track => track.FileName));
    }

    [Fact]
    public void Sort_UsesTheVisibleProjectFolderProjection()
    {
        var tracks = CreateTracks("A", "B", "C");
        tracks[0].ProjectRelativeFolder = "Volume 2";
        tracks[1].ProjectRelativeFolder = "Volume 10";
        tracks[2].ProjectRelativeFolder = "Volume 1";

        var result = _service.Sort(tracks, "DisplayFolder", ListSortDirection.Ascending);

        Assert.Equal(["C", "A", "B"], result.Select(track => track.FileName));
    }

    [Fact]
    public void Sort_UsesFileWarningText()
    {
        var tracks = CreateTracks("A", "B", "C");
        tracks[0].FileWarningText = "Zeta";
        tracks[1].FileWarningText = "Alpha";
        tracks[2].FileWarningText = "";

        var result = _service.Sort(tracks, "FileWarningText", ListSortDirection.Ascending);

        Assert.Equal(["C", "B", "A"], result.Select(track => track.FileName));
    }

    [Fact]
    public void RestoreAutomaticOrder_DoesNotChangeAutomaticOrderPositions()
    {
        var tracks = CreateTracks("A", "B", "C");
        tracks[0].AutomaticOrderPosition = 3;
        tracks[1].AutomaticOrderPosition = 1;
        tracks[2].AutomaticOrderPosition = 2;

        var result = _service.RestoreAutomaticOrder(tracks);

        Assert.Equal(["B", "C", "A"], result.Select(track => track.FileName));
        Assert.Equal(new int?[] { 3, 1, 2 }, tracks.Select(track => track.AutomaticOrderPosition).ToArray());
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
}
