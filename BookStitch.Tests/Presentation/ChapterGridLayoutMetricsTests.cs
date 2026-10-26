using BookStitch.Presentation;
using Xunit;

namespace BookStitch.Tests.Presentation;

public sealed class ChapterGridLayoutMetricsTests
{
    [Fact]
    public void Proposal_width_accounts_for_disclosure_embedded_indent_cell_chrome_and_safety()
    {
        static double Measure(string? value) => value?.Length ?? 0;

        var width = ChapterGridLayoutMetrics.CalculateProposalColumnWidth(
            "Hdr",
            ["normal"],
            ["embedded"],
            Measure,
            minimum: 0,
            maximum: 1000);

        var embeddedChrome = ChapterGridLayoutMetrics.ProposalDisclosureWidth
                            + ChapterGridLayoutMetrics.EmbeddedProposalTextMargin.Left
                            + ChapterGridLayoutMetrics.EmbeddedProposalTextMargin.Right
                            + ChapterGridLayoutMetrics.DataCellPadding.Left
                            + ChapterGridLayoutMetrics.DataCellPadding.Right
                            + ChapterGridLayoutMetrics.SafetyPadding;
        Assert.Equal("embedded".Length + embeddedChrome, width);
    }

    [Fact]
    public void Text_column_width_includes_header_sort_chrome_and_four_dip_safety()
    {
        static double Measure(string? value) => value?.Length ?? 0;

        var width = ChapterGridLayoutMetrics.CalculateTextColumnWidth(
            "LongHeader",
            ["x"],
            Measure,
            minimum: 0,
            maximum: 1000);

        var headerChrome = ChapterGridLayoutMetrics.HeaderPadding.Left
                         + ChapterGridLayoutMetrics.HeaderPadding.Right
                         + ChapterGridLayoutMetrics.HeaderSortReservation
                         + ChapterGridLayoutMetrics.SafetyPadding;
        Assert.Equal("LongHeader".Length + headerChrome, width);
        Assert.Equal(4, ChapterGridLayoutMetrics.SafetyPadding);
    }
}
