using BookStitch.Models;
using System.IO;

namespace BookStitch.Services;

public static class AacMergeSafetyService
{
    public static async Task<bool> ValidateOrFallbackAsync(IAudioExportProcessingService audio,
        string projectFolder, IReadOnlyList<ConvertedTrackPreparationPlanItem> items, ExportPreset preset,
        int rate, string ffmpeg, string ffprobe, PreparedTrackLifecycleSession lifecycle, CancellationToken token)
    {
        var store = new FolderProjectManifestService();
        var plan = store.Read(projectFolder)?.AacPlan;
        if (plan is null) return false;
        var paths = items.Select(item => item.ConvertedPath).ToArray();
        try
        {
            await audio.ValidateMergeInputsAsync(paths, rate, ffprobe, token, preset);
            // Packet checks are a safety boundary, not permission to repair timing by encoding.
            foreach (var path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
                await audio.AnalyzeConvertedTimingAsync(path, ffprobe, null, token);
            return false;
        }
        catch (InvalidDataException)
        {
            if (plan.IsOriginal || preset.IsPassThrough)
                throw new InvalidDataException(LocalizationText.Get("Aac.Plan.InvalidOriginal"));
        }
        // One bounded fallback for this entire output. A failure escapes; there is no retry loop.
        // Persist the new actions before producing artifacts so a paused run resumes this plan.
        plan.Tracks = plan.Tracks.Select(item => item with { Reuse = false, Reason = AacConversionReason.Technical }).ToList();
        plan.Decision = "SafetyFallback";
        store.SavePlan(projectFolder, plan);
        foreach (var item in items)
        {
            token.ThrowIfCancellationRequested();
            item.Track.AacPlanAction = ProcessingActionIds.Convert;
            item.Track.AacPlanPreset = preset.DisplayName;
            item.Track.ProcessingAction = ProcessingActionIds.Convert;
            lifecycle.MarkStarted(item.Index, item.Track, item.SourcePath, item.ConvertedPath, preset, token);
            await audio.PrepareTrackForExportAsync(item.Track, item.SourcePath, item.ConvertedPath, preset,
                ffmpeg, token, _ => { }, ffprobePath: ffprobe);
            lifecycle.Complete(item.Index, item.Track, item.SourcePath, item.ConvertedPath, preset, token);
        }
        await lifecycle.DrainAsync();
        await audio.ValidateMergeInputsAsync(paths, rate, ffprobe, token, preset);
        foreach (var path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
            await audio.AnalyzeConvertedTimingAsync(path, ffprobe, null, token);
        return true;
    }
}
