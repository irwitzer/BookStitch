namespace BookStitch.Services;

public enum ApplicationActivity
{
    None,
    Mp3DiscImport,
    AudioDiscProcessing,
    Export,
    BackgroundWork
}

public sealed record ApplicationShutdownPrompt(
    string Heading,
    string Message,
    string ProgressText);

public enum ProjectCloseIntent { Save, DeleteNewProject, RollbackRun, DiscardMerge }

public sealed class ApplicationShutdownService
{
    internal static ProjectCloseIntent ResolveProjectCloseIntent(bool isNewPreparation, bool isExtension, bool isReconversion, bool isMerge = false) =>
        isMerge ? ProjectCloseIntent.DiscardMerge :
        isExtension || isReconversion ? ProjectCloseIntent.RollbackRun :
        isNewPreparation ? ProjectCloseIntent.DeleteNewProject : ProjectCloseIntent.Save;

    public ApplicationActivity GetActiveActivity(
        bool isDiscImporting,
        bool isAudioDiscProcessing,
        bool isExporting,
        bool isBusy,
        bool isPipelinePaused = false,
        bool hasPausedAudioDiscProject = false,
        bool hasPausedMp3DiscProject = false)
    {
        if (isDiscImporting)
        {
            return isAudioDiscProcessing
                ? ApplicationActivity.AudioDiscProcessing
                : ApplicationActivity.Mp3DiscImport;
        }

        if (isExporting)
            return ApplicationActivity.Export;

        if (isPipelinePaused)
        {
            if (hasPausedAudioDiscProject)
                return ApplicationActivity.AudioDiscProcessing;

            if (hasPausedMp3DiscProject)
                return ApplicationActivity.Mp3DiscImport;

            return ApplicationActivity.Export;
        }

        return isBusy
            ? ApplicationActivity.BackgroundWork
            : ApplicationActivity.None;
    }

    public ApplicationShutdownPrompt CreatePrompt(ApplicationActivity activity)
    {
        return activity switch
        {
            ApplicationActivity.Mp3DiscImport => new ApplicationShutdownPrompt(
                LocalizationText.Get("Shutdown.Mp3Import.Heading"),
                LocalizationText.Get("Shutdown.Mp3Import.Message"),
                LocalizationText.Get("Shutdown.Mp3Import.Progress")),

            ApplicationActivity.AudioDiscProcessing => new ApplicationShutdownPrompt(
                LocalizationText.Get("Shutdown.AudioDisc.Heading"),
                LocalizationText.Get("Shutdown.AudioDisc.Message"),
                LocalizationText.Get("Shutdown.AudioDisc.Progress")),

            ApplicationActivity.Export => new ApplicationShutdownPrompt(
                LocalizationText.Get("Shutdown.Export.Heading"),
                LocalizationText.Get("Shutdown.Export.Message"),
                LocalizationText.Get("Shutdown.Export.Progress")),

            ApplicationActivity.BackgroundWork => new ApplicationShutdownPrompt(
                LocalizationText.Get("Shutdown.Background.Heading"),
                LocalizationText.Get("Shutdown.Background.Message"),
                LocalizationText.Get("Shutdown.Background.Progress")),

            _ => new ApplicationShutdownPrompt(
                LocalizationText.Get("Shutdown.Normal.Heading"),
                LocalizationText.Get("Shutdown.Normal.Message"),
                LocalizationText.Get("Shutdown.Normal.Progress"))
        };
    }

    public async Task<bool> WaitForIdleAsync(
        Func<bool> isWorkActive,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(isWorkActive);

        if (!isWorkActive())
            return true;

        var startedUtc = DateTime.UtcNow;
        while (isWorkActive())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (DateTime.UtcNow - startedUtc >= timeout)
                return false;

            await Task.Delay(100, cancellationToken);
        }

        return true;
    }
}
