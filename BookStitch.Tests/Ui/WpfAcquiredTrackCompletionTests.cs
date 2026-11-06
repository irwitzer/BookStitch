using BookStitch.Models;
using BookStitch.Services;
using BookStitch.Tests.TestHelpers;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Xunit;
namespace BookStitch.Tests.Ui;
public sealed partial class WpfDialogRuntimeTests
{
    [Fact]
    public void Acquired_copy_checkpoint_updates_the_visible_external_source_row_before_the_other_track_finishes()
    {
        RunOnStaThread(() =>
        {
            using var folder = new TemporaryFolder();
            var project = Path.Combine(folder.Path, "project"); ProjectFolderLayout.EnsureProjectFolders(project);
            var source = Path.Combine(folder.Path, "external.mp3"); File.WriteAllText(source, "source");
            var copy = Path.Combine(ProjectFolderLayout.GetOriginalsFolder(project), "external.mp3"); File.Copy(source, copy);
            var output = Path.Combine(project, "ready.m4a"); File.WriteAllText(output, "prepared");
            var track = new TrackInfo { SourceId = Guid.NewGuid(), FilePath = source, TargetSampleRateHz = 44100 };
            var waiting = new TrackInfo { SourceId = Guid.NewGuid(), FilePath = Path.Combine(folder.Path, "still-copying.mp3") };
            var grid = new DataGrid { AutoGenerateColumns = false };
            foreach (var name in new[] { "DisplayPreparedBitrateKbps", "DisplayPreparedSampleRateHz", "DisplayPreparedChannelLayout", "DisplayPreparedProfile" })
                grid.Columns.Add(new DataGridTextColumn { Binding = new Binding(name) });
            grid.Items.Add(track); grid.Items.Add(waiting);
            var window = new Window { Content = grid, Width = 700, Height = 180, ShowInTaskbar = false };
            try
            {
                window.Show(); DrainDispatcher();
                var store = new WorkManifestService(); var path = ProjectFolderLayout.GetExportManifestPath(project);
                var manifest = store.LoadOrCreate(path, project, Path.GetDirectoryName(copy)!, "AAC Stereo 160 kbps");
                var entry = store.UpdateTrack(manifest, 0, track, copy, output, ExportPreset.Parse("AAC Stereo 160 kbps"));
                store.Save(path, manifest);
                Assert.True(TrackPreparedStateRefreshService.ApplyCompletedCheckpoint(track, entry, "AAC Stereo 160 kbps"));
                DrainDispatcher();
                string[] Cells(TrackInfo row) => grid.Columns.Select(c => Assert.IsType<TextBlock>(c.GetCellContent(row)).Text).ToArray();
                Assert.Equal(new[] { "160", "44.100", "Stereo", "AAC-LC" }, Cells(track));
                Assert.All(Cells(waiting), Assert.Empty); Assert.Equal(source, track.FilePath);
                // The periodic refresh must not erase the just-published values while source paths differ.
                TrackStateUpdateQueueService.ApplyUpdates(new([track, waiting], project, "AAC Stereo 160 kbps", true));
                DrainDispatcher(); Assert.Equal("160", Cells(track)[0]); Assert.Equal(source, track.FilePath);
                // Neither an unrelated occurrence nor an inactive/unbound source may borrow this checkpoint.
                var other = new TrackInfo { SourceId = track.SourceId, FilePath = source };
                new TrackPreparedStateRefreshService().RefreshForCurrentPreset([other], project, "AAC Stereo 160 kbps");
                Assert.False(other.HasReusableConvertedFile);
                TrackStateUpdateQueueService.ApplyUpdates(new([waiting, track], project, "AAC Stereo 160 kbps", true));
                Assert.False(track.HasReusableConvertedFile);
                TrackStateUpdateQueueService.ApplyUpdates(new([track, waiting], project, "AAC Stereo 160 kbps", true));
                Assert.True(track.HasReusableConvertedFile);
                File.Delete(output); TrackStateUpdateQueueService.ApplyUpdates(new([track, waiting], project, "AAC Stereo 160 kbps", true));
                DrainDispatcher(); Assert.All(Cells(track), Assert.Empty);
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void Default_column_order_swaps_partners_in_place_and_hides_start_in_all_automatic_phases()
    {
        RunOnStaThread(() =>
        {
            var grid = new DataGrid { AutoGenerateColumns = false };
            foreach (var key in TrackGridDefaultLayoutService.Keys.Reverse()) grid.Columns.Add(new DataGridTextColumn { SortMemberPath = key });
            TrackGridDefaultLayoutService.Apply(grid.Columns, c => c.SortMemberPath);
            string[] Visible() => grid.Columns.OrderBy(c => c.DisplayIndex).Where(c => c.Visibility == Visibility.Visible).Select(c => c.SortMemberPath).ToArray();
            AudioColumnVisibilityService.Apply(grid.Columns, c => c.SortMemberPath, AudioColumnMode.Input, false);
            var expected = new[] { "AutomaticOrderPosition", "DiscNumber", "StatusIndicator", "ChapterProposal", "Duration", "TagTitle", "TrackNumber", "FileName", "ChapterWarningText", "FileWarningText", "BitrateKbps", "Codec", "SampleRateHz", "Channels", "AudioProfile", "Extension", "DisplayFolder" };
            Assert.Equal(expected, Visible());
            AudioColumnVisibilityService.Apply(grid.Columns, c => c.SortMemberPath, AudioColumnMode.Output, false);
            var replacements = new Dictionary<string,string> { ["BitrateKbps"] = "DisplayPreparedBitrateKbps", ["SampleRateHz"] = "PreparedSampleRateHz", ["Channels"] = "DisplayPreparedChannelLayout", ["AudioProfile"] = "DisplayPreparedProfile" };
            Assert.Equal(expected.Select(k => replacements.GetValueOrDefault(k, k)), Visible());
            foreach (var mode in Enum.GetValues<AudioColumnMode>())
            {
                grid.Columns.Single(c => c.SortMemberPath == "Start").Visibility = Visibility.Visible;
                AudioColumnVisibilityService.Apply(grid.Columns, c => c.SortMemberPath, mode, false);
                Assert.DoesNotContain("Start", Visible());
            }
            grid.Columns.Single(c => c.SortMemberPath == "Start").Visibility = Visibility.Visible;
            AudioColumnVisibilityService.Apply(grid.Columns, c => c.SortMemberPath, AudioColumnMode.Output, true);
            Assert.Contains("Start", Visible()); // Explicit manual choices retain the agreed precedence.
        });
    }
}
