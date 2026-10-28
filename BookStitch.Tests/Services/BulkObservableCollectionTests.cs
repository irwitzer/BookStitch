using BookStitch.Models;
using System.Collections.Specialized;
using System.ComponentModel;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class BulkObservableCollectionTests
{
    [Fact]
    public void In_place_reorder_emits_moves_without_reset_and_rejects_invalid_inventory()
    {
        var a=new TrackInfo();var b=new TrackInfo();var c=new TrackInfo();
        var collection=new BulkObservableCollection<TrackInfo>();collection.ReplaceAll([a,b,c]);
        var actions=new List<NotifyCollectionChangedAction>();collection.CollectionChanged+=(_,e)=>actions.Add(e.Action);
        Assert.True(collection.Reorder([c,a,b]));
        Assert.Equal(new[]{c,a,b},collection);
        Assert.All(actions,x=>Assert.Equal(NotifyCollectionChangedAction.Move,x));
        Assert.False(collection.Reorder([c,a,b]));
        Assert.Throws<ArgumentException>(()=>collection.Reorder([a,a,b]));
        Assert.Equal(new[]{c,a,b},collection);
    }

    [Fact]
    public void Sending_first_item_to_bottom_emits_only_one_move()
    {
        var rows=Enumerable.Range(1,1000).ToArray();
        var collection=new BulkObservableCollection<int>();collection.ReplaceAll(rows);
        var actions=new List<NotifyCollectionChangedAction>();collection.CollectionChanged+=(_,e)=>actions.Add(e.Action);
        collection.Reorder(rows.Skip(1).Append(rows[0]).ToArray());
        Assert.Equal(NotifyCollectionChangedAction.Move,Assert.Single(actions));
        Assert.Equal(rows.Skip(1).Append(rows[0]),collection);
    }

    [Theory]
    [InlineData(500)]
    [InlineData(1000)]
    public void Replacement_publishes_one_complete_reset_and_property_notifications(int count)
    {
        var collection = new BulkObservableCollection<TrackInfo>();
        var tracks = Enumerable.Range(1, count).Select(i => new TrackInfo { Index = i }).ToArray();
        var actions = new List<NotifyCollectionChangedAction>();
        var properties = new List<string?>();
        collection.CollectionChanged += (_, e) =>
        {
            actions.Add(e.Action);
            Assert.Equal(tracks, collection);
        };
        ((INotifyPropertyChanged)collection).PropertyChanged += (_, e) => properties.Add(e.PropertyName);
        collection.ReplaceAll(tracks);
        Assert.Equal(new[] { NotifyCollectionChangedAction.Reset }, actions);
        Assert.Equal(new[] { "Count", "Item[]" }, properties);
    }

    [Fact]
    public void Reordering_retains_track_identity_exclusions_and_chapter_state_and_supports_selection_restore()
    {
        var first = new TrackInfo { SourceId = Guid.NewGuid(), Index = 1, ChapterTitle = "One" };
        var second = new TrackInfo { SourceId = Guid.NewGuid(), Index = 2, IsExcluded = true,
            ChapterTitle = "Two", ExcludedChapterTitle = "Saved", PreparedConvertedPreset = "preset" };
        var tracks = new BulkObservableCollection<TrackInfo>();
        tracks.ReplaceAll(new[] { first, second });
        var selected = new[] { second };
        tracks.ReplaceAll(tracks.Reverse());
        Assert.Same(second, tracks[0]);
        Assert.Same(first, tracks[1]);
        Assert.True(second.IsExcluded);
        Assert.Equal("Two", second.ChapterTitle);
        Assert.Equal("Saved", second.ExcludedChapterTitle);
        Assert.Equal("preset", second.PreparedConvertedPreset);
        Assert.Equal(2, second.Index); // Collection replacement never changes business order/index.
        Assert.Same(second, Assert.Single(selected, tracks.Contains));
        tracks.ReplaceAll(tracks);
        Assert.Same(second, tracks[0]);
    }
}
