using System.Windows.Controls;
namespace BookStitch.Services;

public static class TrackGridDefaultLayoutService
{
    public static readonly IReadOnlyList<string> Keys = new[]
    {
        "AutomaticOrderPosition", "DiscNumber", "StatusIndicator", "ChapterProposal", "Start", "Duration", "TagTitle", "TrackNumber", "FileName",
        "ChapterWarningText", "FileWarningText", "BitrateKbps", "DisplayPreparedBitrateKbps", "Codec", "SampleRateHz", "PreparedSampleRateHz",
        "Channels", "DisplayPreparedChannelLayout", "AudioProfile", "DisplayPreparedProfile", "Extension", "DisplayFolder"
    };

    public static void Apply(IEnumerable<DataGridColumn> columns, Func<DataGridColumn, string> key)
    {
        var list = columns.ToArray();
        var ordered = Keys.SelectMany(id => list.Where(column => key(column) == id))
            .Concat(list.Where(column => !Keys.Contains(key(column)))).ToArray();
        for (var index = 0; index < ordered.Length; index++) ordered[index].DisplayIndex = index;
    }
}
