using BookStitch.Models;

namespace BookStitch.Services;

public enum AudioDiscRipWorkflowOutcome
{
    Completed,
    Canceled,
    WaitingForDisc,
    Failed
}

public sealed record AudioDiscRipWorkflowResult(
    AudioDiscRipWorkflowOutcome Outcome,
    string Message,
    int CompletedTracks)
{
    public bool Succeeded => Outcome == AudioDiscRipWorkflowOutcome.Completed;
}

public sealed record AudioDiscRipWorkflowRequest(
    AudioDiscProjectManifest Manifest,
    CancellationToken CancellationToken,
    Func<int, CancellationToken, Task<AudioDiscProjectManifestDisc?>> WaitForNextDiscAsync,
    Func<AudioDiscProjectManifestDisc, CancellationToken, Task<AudioDiscPollingResult?>> WaitForRequiredDiscAsync,
    Func<AudioDiscProjectManifestDisc, CancellationToken, Task<bool>> ConfirmRequiredDiscAvailableAsync,
    Func<AudioDiscProjectManifestDisc, IProgress<AudioDiscRipProgress>, CancellationToken, Task<AudioDiscRipResult>> RipDiscAsync,
    Func<AudioDiscProjectManifestDisc, bool> TryEjectDisc,
    Func<Task> QueueExistingRippedTracksAsync,
    Func<Task> WaitForLiveConversionsAsync,
    Action SaveSnapshot,
    Action<string> MarkLiveConversionCanceled,
    Action<string> MarkLiveConversionFailed,
    Action<AudioDiscProjectManifestDisc, AudioDiscPollingResult> UpdateDiscSource,
    Action<AudioDiscProjectManifestDisc, AudioDiscRipProgress> ReportRipProgress,
    Action<AudioDiscProjectManifestDisc> RequiredDiscRemoved,
    Action<AudioDiscProjectManifestDisc, bool> DiscChangeRequired);

public sealed class AudioDiscRipWorkflowService
{
    private readonly AudioDiscProjectService _projectService;
    private readonly AudioDiscPipelineTimingService _timingService;

    public AudioDiscRipWorkflowService(
        AudioDiscProjectService projectService,
        AudioDiscPipelineTimingService timingService)
    {
        _projectService = projectService ?? throw new ArgumentNullException(nameof(projectService));
        _timingService = timingService ?? throw new ArgumentNullException(nameof(timingService));
    }

    public async Task<AudioDiscRipWorkflowResult> RunAsync(AudioDiscRipWorkflowRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Manifest);

        var manifest = request.Manifest;
        int? confirmedRequiredDiscNumber = null;
        await request.QueueExistingRippedTracksAsync();

        while (true)
        {
            request.CancellationToken.ThrowIfCancellationRequested();

            var nextDiscNumber = _projectService.GetNextRequiredDiscNumber(manifest);
            if (!nextDiscNumber.HasValue)
                break;

            var disc = manifest.Discs.FirstOrDefault(item => item.DiscNumber == nextDiscNumber.Value);
            if (disc is null)
            {
                disc = await request.WaitForNextDiscAsync(nextDiscNumber.Value, request.CancellationToken);
                if (disc is null)
                {
                    return WaitForDisc(
                        "Audio-CD-Projekt wartet auf die nächste Disc.");
                }
            }
            else if (confirmedRequiredDiscNumber == disc.DiscNumber)
            {
                confirmedRequiredDiscNumber = null;
            }
            else
            {
                var locatedDisc = await request.WaitForRequiredDiscAsync(disc, request.CancellationToken);
                if (locatedDisc?.Disc is not { })
                {
                    return WaitForDisc(
                        "Audio-CD-Projekt wartet auf die benötigte Disc.");
                }

                request.UpdateDiscSource(disc, locatedDisc);
                request.SaveSnapshot();
            }

            _timingService.Start();
            _projectService.MarkDiscRipping(manifest, disc.DiscNumber);
            request.SaveSnapshot();

            var progress = new Progress<AudioDiscRipProgress>(value => request.ReportRipProgress(disc, value));
            var ripResult = await request.RipDiscAsync(disc, progress, request.CancellationToken);

            var timingSnapshot = ripResult.Succeeded
                ? _timingService.Complete()
                : _timingService.GetSnapshot();
            var measuredRipDuration = timingSnapshot.RipDuration ?? timingSnapshot.TotalDuration;
            var projectRipCompletedAfterDisc = false;

            if (ripResult.Succeeded)
            {
                _projectService.MarkDiscCompleted(manifest, disc.DiscNumber, measuredRipDuration);
                request.SaveSnapshot();

                projectRipCompletedAfterDisc = _projectService.IsProjectRipCompleted(manifest);
                var ejected = request.TryEjectDisc(disc);
                if (!projectRipCompletedAfterDisc)
                    request.DiscChangeRequired(disc, ejected);
            }
            else if (ripResult.WasCanceled)
            {
                return Cancel(
                    request,
                    "Audio-CD-Ripping und AAC-Vorbereitung wurden abgebrochen.",
                    AudioDiscRipWorkflowOutcome.Canceled,
                    ripResult.CompletedTracks);
            }
            else
            {
                var discStillAvailable = await request.ConfirmRequiredDiscAvailableAsync(
                    disc,
                    request.CancellationToken);

                if (!discStillAvailable)
                {
                    _projectService.MarkProjectCanceled(manifest);
                    request.SaveSnapshot();
                    request.RequiredDiscRemoved(disc);

                    var resumedDisc = await request.WaitForRequiredDiscAsync(disc, request.CancellationToken);
                    if (resumedDisc?.Disc is not { })
                    {
                        return WaitForDisc(
                            "Die benötigte Disc fehlt.",
                            ripResult.CompletedTracks);
                    }

                    request.UpdateDiscSource(disc, resumedDisc);
                    request.SaveSnapshot();
                    confirmedRequiredDiscNumber = disc.DiscNumber;
                    continue;
                }

                _projectService.MarkDiscFailed(manifest, disc.DiscNumber, ripResult.ErrorMessage);
                request.SaveSnapshot();
                request.MarkLiveConversionFailed(ripResult.ErrorMessage);
                return new AudioDiscRipWorkflowResult(
                    AudioDiscRipWorkflowOutcome.Failed,
                    ripResult.ErrorMessage,
                    ripResult.CompletedTracks);
            }

            if (projectRipCompletedAfterDisc)
                break;
        }

        await request.WaitForLiveConversionsAsync();
        return new AudioDiscRipWorkflowResult(
            AudioDiscRipWorkflowOutcome.Completed,
            string.Empty,
            manifest.Discs.SelectMany(disc => disc.Tracks).Count(track =>
                string.Equals(track.Status, AudioDiscTrackStatus.Ripped, StringComparison.OrdinalIgnoreCase)));
    }

    private static AudioDiscRipWorkflowResult WaitForDisc(
        string reason,
        int completedTracks = 0) =>
        new(
            AudioDiscRipWorkflowOutcome.WaitingForDisc,
            reason,
            completedTracks);

    private AudioDiscRipWorkflowResult Cancel(
        AudioDiscRipWorkflowRequest request,
        string reason,
        AudioDiscRipWorkflowOutcome outcome,
        int completedTracks = 0)
    {
        _projectService.MarkProjectCanceled(request.Manifest);
        request.SaveSnapshot();
        request.MarkLiveConversionCanceled(reason);
        return new AudioDiscRipWorkflowResult(outcome, reason, completedTracks);
    }
}
