using System.Windows;
using System.Windows.Controls;
namespace BookStitch.Services;

public enum AudioColumnMode { Input, Output, Both }

public static class AudioColumnVisibilityService
{
    public static Visibility? GetVisibility(string key, AudioColumnMode mode)
    {
        if (key == "Start") return Visibility.Collapsed;
        var input = key is "BitrateKbps" or "SampleRateHz" or "Channels" or "AudioProfile";
        var output = key is "DisplayPreparedBitrateKbps" or "PreparedSampleRateHz" or "DisplayPreparedChannelLayout" or "DisplayPreparedProfile";
        if (!input && !output) return null;
        return mode == AudioColumnMode.Both || input == (mode == AudioColumnMode.Input) ? Visibility.Visible : Visibility.Collapsed;
    }

    public static void Apply(IEnumerable<DataGridColumn> columns, Func<DataGridColumn, string> key,
        AudioColumnMode mode, bool customVisibility)
    {
        if (customVisibility) return;
        foreach (var column in columns)
            if (GetVisibility(key(column), mode) is { } visibility) column.Visibility = visibility;
    }
}
