using System.IO;

using BookStitch.Models;

namespace BookStitch.Services;

public sealed record AudioDiscLiveConversionPreparation(
    TrackInfo Track,
    string SourcePath,
    string ConvertedPath);

public sealed class AudioDiscLiveConversionService
{
    public AudioDiscLiveConversionPreparation CreatePreparation(
        AudioDiscRippedTrack rippedTrack,
        string projectFolder,
        ExportPreset preset)
    {
        ArgumentNullException.ThrowIfNull(rippedTrack);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectFolder);
        ArgumentNullException.ThrowIfNull(preset);

        var track = new TrackInfo
        {
            Index = Math.Max(0, rippedTrack.GlobalIndex - 1),
            DiscNumber = rippedTrack.DiscNumber,
            TrackNumber = rippedTrack.TrackNumber,
            FilePath = rippedTrack.FilePath,
            FileName = Path.GetFileName(rippedTrack.FilePath),
            Extension = Path.GetExtension(rippedTrack.FilePath).TrimStart('.'),
            Codec = "FLAC",
            Channels = 2,
            ChannelLayout = "Stereo",
            DurationTicks = rippedTrack.Duration.Ticks,
            Duration = rippedTrack.Duration.ToString(@"hh\:mm\:ss", System.Globalization.CultureInfo.InvariantCulture),
            ProcessingAction = "Konvertieren"
        };

        var convertedFolder = ProjectFolderLayout.GetConvertedPresetFolder(
            projectFolder,
            preset.GetFolderName());
        var convertedPath = ConvertedTrackPathService.GetConvertedTrackPath(
            convertedFolder,
            rippedTrack.FilePath,
            track);

        return new AudioDiscLiveConversionPreparation(
            track,
            rippedTrack.FilePath,
            convertedPath);
    }
}
