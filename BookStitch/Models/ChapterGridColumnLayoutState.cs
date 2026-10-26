namespace BookStitch.Models;

public sealed class ChapterGridColumnLayoutState
{
    public double? ProposalWidth { get; set; }
    public double? DurationWidth { get; set; }
    public double? WarningWidth { get; set; }
    public bool AutoFitPending { get; private set; } = true;

    public void MarkAutoFitPending() => AutoFitPending = true;
    public void CompleteAutoFit() => AutoFitPending = false;

    public static bool ShouldUpdateAutoFitWidth(double currentWidth, double requiredWidth, bool force, double hysteresis) =>
        force || Math.Abs(requiredWidth - currentWidth) >= hysteresis;
}
