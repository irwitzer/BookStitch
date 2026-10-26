using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class TrackStateRefreshDecisionServiceTests
{
    [Theory]
    [InlineData(false, false, false, TrackStateRefreshAction.UpdateFinalStatus)]
    [InlineData(true, false, false, TrackStateRefreshAction.RefreshExportPreview)]
    [InlineData(false, true, false, TrackStateRefreshAction.RefreshExportPreview)]
    [InlineData(false, false, true, TrackStateRefreshAction.RefreshExportPreview)]
    [InlineData(true, true, true, TrackStateRefreshAction.RefreshExportPreview)]
    public void Create_SelectsRefreshAction(
        bool isBusy,
        bool isExporting,
        bool isDiscImporting,
        TrackStateRefreshAction expected)
    {
        var result = TrackStateRefreshDecisionService.Create(
            isBusy,
            isExporting,
            isDiscImporting);

        Assert.Equal(expected, result);
    }
}