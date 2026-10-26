using BookStitch.Models;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class ChapterGridColumnLayoutStateTests
{
    [Fact]
    public void Runtime_layout_keeps_widths_until_next_project_marks_autofit_pending()
    {
        var state = new ChapterGridColumnLayoutState
        {
            ProposalWidth = 412,
            DurationWidth = 96,
            WarningWidth = 138
        };

        Assert.True(state.AutoFitPending);
        state.CompleteAutoFit();
        Assert.False(state.AutoFitPending);
        Assert.Equal(412, state.ProposalWidth);
        Assert.Equal(96, state.DurationWidth);
        Assert.Equal(138, state.WarningWidth);

        state.MarkAutoFitPending();
        Assert.True(state.AutoFitPending);
        Assert.Equal(412, state.ProposalWidth);
        Assert.Equal(96, state.DurationWidth);
        Assert.Equal(138, state.WarningWidth);

        Assert.False(ChapterGridColumnLayoutState.ShouldUpdateAutoFitWidth(200, 209, force: false, hysteresis: 10));
        Assert.True(ChapterGridColumnLayoutState.ShouldUpdateAutoFitWidth(200, 210, force: false, hysteresis: 10));
        Assert.True(ChapterGridColumnLayoutState.ShouldUpdateAutoFitWidth(200, 175, force: false, hysteresis: 10));
        Assert.True(ChapterGridColumnLayoutState.ShouldUpdateAutoFitWidth(200, 201, force: true, hysteresis: 10));
    }
}
