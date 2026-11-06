using BookStitch.Models;

namespace BookStitch.Services;

public sealed record AacDecisionSelection(bool NeedsDialog, AacDecisionPlan Plan);
public enum AacDecisionChoice { PassThrough, EncodeAll, Back }

/// <summary>Selects automatic decisions from a complete validated plan, never from UI/source heuristics.</summary>
public static class AacDecisionSelectionService
{
    public static AacDecisionPlan? Confirm(AacDecisionOptions options, AacDecisionChoice choice)
    {
        if (choice == AacDecisionChoice.Back) return null;
        var plan = choice == AacDecisionChoice.EncodeAll ? options.EncodeAll : options.Optimized;
        plan.ForceEncodeAll = choice == AacDecisionChoice.EncodeAll;
        plan.Decision = plan.ForceEncodeAll ? "EncodeAll" : "Optimized";
        return plan;
    }

    public static AacDecisionSelection Select(AacDecisionOptions options, bool review = false, bool forceEncodeAll = false, bool requireReuseConfirmation = false)
    {
        if (forceEncodeAll) return new(false, options.EncodeAll);
        if (review) return new(false, options.UserValues);
        var plan = options.Optimized;
        if (plan.ReusedCount == 0)
        {
            options.EncodeAll.ForceEncodeAll = false;
            options.EncodeAll.Decision = "AutomaticConvertAll";
            return new(false, options.EncodeAll);
        }
        if (requireReuseConfirmation) return new(true, plan);
        var user = ExportPreset.Parse(plan.UserPreset);
        if (plan.ReusedCount == plan.Tracks.Count && !plan.IsOriginal && plan.Channels == user.Channels &&
            plan.SampleRateHz == plan.UserSampleRateHz && plan.BitrateKbps == user.BitrateKbps)
        {
            plan.Decision = "AutomaticReuse";
            return new(false, plan);
        }
        return new(true, plan);
    }
}
