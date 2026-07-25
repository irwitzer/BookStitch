using System;
using System.Collections.Generic;
using System.Linq;

namespace BookStitch.Services;

public sealed class ExportTrackException : Exception
{
    public ExportTrackException(int trackIndex, string fileName, string sourcePath, Exception innerException)
        : base(innerException.Message, innerException)
    {
        TrackIndex = trackIndex;
        FileName = fileName;
        SourcePath = sourcePath;

        var lines = ExportFailureDetailsService.SplitMessageLines(innerException.Message);
        ErrorSummary = lines.FirstOrDefault() ?? "Exportfehler";
        TechnicalLines = lines.Skip(1).ToList();
    }

    public int TrackIndex { get; }
    public string FileName { get; }
    public string SourcePath { get; }
    public string ErrorSummary { get; }
    public IReadOnlyList<string> TechnicalLines { get; }
}
