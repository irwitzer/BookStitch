namespace BookStitch.Services;

public enum TrackStateRefreshAction
{
    RefreshExportPreview,
    UpdateFinalStatus
}

public static class TrackStateRefreshDecisionService
{
    public static TrackStateRefreshAction Create(
        bool isBusy,
        bool isExporting,
        bool isDiscImporting) =>
        isBusy || isExporting || isDiscImporting
            ? TrackStateRefreshAction.RefreshExportPreview
            : TrackStateRefreshAction.UpdateFinalStatus;
}