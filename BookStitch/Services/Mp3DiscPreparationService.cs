using BookStitch.Models;
using System.IO;

namespace BookStitch.Services;

public sealed record Mp3DiscPresetPreparationItem(
    int Index,
    TrackInfo Track,
    string SourcePath,
    string ConvertedPath);

public sealed class Mp3DiscPreparationService
{
    public List<Mp3DiscPresetPreparationItem> BuildMissingPresetPreparationItems(
        IEnumerable<TrackInfo> tracks,
        string projectFolder,
        ExportPreset preset,
        string convertedFolder,
        Func<string, string, bool> canReusePreparedTrack)
    {
        ArgumentNullException.ThrowIfNull(tracks);
        ArgumentNullException.ThrowIfNull(preset);
        ArgumentNullException.ThrowIfNull(canReusePreparedTrack);

        var trackSnapshot = tracks.ToList();
        var result = new List<Mp3DiscPresetPreparationItem>();

        for (var index = 0; index < trackSnapshot.Count; index++)
        {
            var track = trackSnapshot[index];
            var sourcePath = TrackPathService.GetTrackPath(projectFolder, track);
            if (!File.Exists(sourcePath))
                continue;

            var convertedPath = ConvertedTrackPathService.GetConvertedTrackPath(convertedFolder, sourcePath, track);
            if (canReusePreparedTrack(sourcePath, convertedPath))
                continue;

            result.Add(new Mp3DiscPresetPreparationItem(index, track, sourcePath, convertedPath));
        }

        return result;
    }

    public TrackInfo BuildLiveConversionTrack(DiscCopiedFile copiedFile)
    {
        var importedFileName = Path.GetFileName(copiedFile.ImportedFile);
        var extension = Path.GetExtension(copiedFile.ImportedFile).TrimStart('.');

        return new TrackInfo
        {
            Index = copiedFile.CopiedFiles,
            DiscNumber = copiedFile.DiscNumber,
            TrackNumber = copiedFile.CopiedFiles,
            FilePath = copiedFile.ImportedFile,
            FileName = importedFileName,
            Extension = extension,
            Codec = extension.Equals("mp3", StringComparison.OrdinalIgnoreCase) ? "MP3" : "",
            ProcessingAction = "Konvertieren"
        };
    }
}
