using System.Windows;

namespace BookStitch.Presentation;

public static class ChapterGridLayoutMetrics
{
    public const double ProposalDisclosureWidth = 24;
    public static GridLength ProposalDisclosureGridLength { get; } = new(ProposalDisclosureWidth);
    public const double HeaderSortReservation = 16;
    public const double SafetyPadding = 4;
    public static Thickness DataCellPadding { get; } = new(4, 2, 4, 2);
    public static Thickness HeaderPadding { get; } = new(6, 4, 6, 4);
    public static Thickness ProposalTextMargin { get; } = new(4, 0, 4, 0);
    public static Thickness EmbeddedProposalTextMargin { get; } = new(28, 0, 4, 0);

    public static double CalculateTextColumnWidth(
        string header,
        IEnumerable<string?> values,
        Func<string?, double> measureText,
        double minimum,
        double maximum)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(measureText);

        var width = measureText(header) + HeaderChromeWidth + SafetyPadding;
        foreach (var value in values)
            width = Math.Max(width, measureText(value) + Horizontal(DataCellPadding) + SafetyPadding);
        return Math.Clamp(width, minimum, maximum);
    }

    public static double CalculateProposalColumnWidth(
        string header,
        IEnumerable<string?> normalValues,
        IEnumerable<string?> embeddedValues,
        Func<string?, double> measureText,
        double minimum,
        double maximum)
    {
        ArgumentNullException.ThrowIfNull(normalValues);
        ArgumentNullException.ThrowIfNull(embeddedValues);
        ArgumentNullException.ThrowIfNull(measureText);

        var width = measureText(header) + HeaderChromeWidth + SafetyPadding;
        foreach (var value in normalValues)
        {
            width = Math.Max(
                width,
                measureText(value) + ProposalDisclosureWidth + Horizontal(ProposalTextMargin) + Horizontal(DataCellPadding) + SafetyPadding);
        }

        foreach (var value in embeddedValues)
        {
            width = Math.Max(
                width,
                measureText(value) + ProposalDisclosureWidth + Horizontal(EmbeddedProposalTextMargin) + Horizontal(DataCellPadding) + SafetyPadding);
        }

        return Math.Clamp(width, minimum, maximum);
    }

    private static double HeaderChromeWidth => Horizontal(HeaderPadding) + HeaderSortReservation;
    private static double Horizontal(Thickness thickness) => thickness.Left + thickness.Right;
}
