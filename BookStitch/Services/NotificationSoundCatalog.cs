using BookStitch.Models;

namespace BookStitch.Services;

public static class NotificationSoundCatalog
{
    public const string PreviewSoundFileName = "project-completed.wav";

    public static string GetLibraryFolderName(SoundLibrary library)
    {
        return library switch
        {
            SoundLibrary.Gentle => "Gentle",
            SoundLibrary.Bass => "Bass",
            SoundLibrary.HammondOrgan => "HammondOrgan",
            SoundLibrary.Warm => "Warm",
            SoundLibrary.Retro => "Retro",
            _ => "Gentle"
        };
    }

    public static string? GetFileName(NotificationEvent notificationEvent)
    {
        return notificationEvent switch
        {
            NotificationEvent.DiscChangeRequired => "disc-change.wav",
            NotificationEvent.UserActionRequired => "warning.wav",
            NotificationEvent.ProjectCompleted => "project-completed.wav",
            NotificationEvent.Warning => "warning.wav",
            NotificationEvent.Information => "information.wav",
            NotificationEvent.Error => "error.wav",
            _ => null
        };
    }
}
