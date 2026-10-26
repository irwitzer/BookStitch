using System;
using System.Linq;
using BookStitch.Models;

namespace BookStitch.Services;

public sealed class WorkflowExportFailureStatusService
{
    public WorkflowErrorStatus Create(Exception? exception)
    {
        if (exception is ExportTrackException trackException)
        {
            var message = string.IsNullOrWhiteSpace(trackException.ErrorSummary)
                ? LocalizationText.Get("MainWindow.Export.Failed.Progress")
                : trackException.ErrorSummary.Trim();
            var failedTrackOrFileNumber = trackException.TrackIndex > 0
                ? trackException.TrackIndex
                : (int?)null;

            return new WorkflowErrorStatus(message, failedTrackOrFileNumber);
        }

        var firstLine = ExportFailureDetailsService
            .SplitMessageLines(exception?.Message)
            .FirstOrDefault();

        return new WorkflowErrorStatus(
            string.IsNullOrWhiteSpace(firstLine)
                ? LocalizationText.Get("MainWindow.Export.Failed.Progress")
                : firstLine.Trim());
    }
}
