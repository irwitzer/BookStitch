using System;
using System.Collections.Generic;
using System.Linq;

namespace BookStitch.Services;

public sealed class ExportFailureDetailsService
{
    public IReadOnlyList<string> BuildExportFailureDetails(Exception exception, string convertedFolder)
    {
        var details = new List<string>();

        if (exception is ExportTrackException trackException)
        {
            details.Add($"• #{trackException.TrackIndex}  {trackException.FileName}");
            details.Add($"   ◦ {trackException.ErrorSummary}");

            if (!string.IsNullOrWhiteSpace(trackException.SourcePath))
                details.Add("   ◦ Pfad: " + trackException.SourcePath);

            foreach (var line in trackException.TechnicalLines.Take(14))
                details.Add("   ◦ FFmpeg: " + line);
        }
        else
        {
            details.Add("• Allgemeiner Exportfehler");

            foreach (var line in SplitMessageLines(exception.Message).Take(16))
                details.Add("   ◦ " + line);
        }

        if (!string.IsNullOrWhiteSpace(convertedFolder))
        {
            details.Add("");
            details.Add("Arbeitsordner:");
            details.Add(convertedFolder);
        }

        return details;
    }

    public static IReadOnlyList<string> SplitMessageLines(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return Array.Empty<string>();

        return message
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToList();
    }
}
