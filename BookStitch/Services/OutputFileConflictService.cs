using BookStitch.Models;
using System.IO;

namespace BookStitch.Services;

public static class OutputFileConflictService
{
    public static string CreateRenamedOutputPath(string outputPath, ExportPreset preset)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        ArgumentNullException.ThrowIfNull(preset);

        var directory = Path.GetDirectoryName(outputPath);
        if (string.IsNullOrWhiteSpace(directory))
            throw new ArgumentException("Der Ausgabepfad benötigt einen Ordner.", nameof(outputPath));

        var extension = Path.GetExtension(outputPath);
        var originalName = Path.GetFileNameWithoutExtension(outputPath);
        var channelSuffix = preset.Channels == 1 ? " Mono" : string.Empty;
        var prefix = $"NEU {preset.BitrateKbps}{channelSuffix} ";
        var candidate = Path.Combine(directory, prefix + originalName + extension);

        if (!File.Exists(candidate))
            return candidate;

        for (var number = 2; number < int.MaxValue; number++)
        {
            candidate = Path.Combine(directory, $"{prefix}({number}) {originalName}{extension}");
            if (!File.Exists(candidate))
                return candidate;
        }

        throw new IOException("Es konnte kein freier Dateiname für die Ausgabedatei ermittelt werden.");
    }
}
