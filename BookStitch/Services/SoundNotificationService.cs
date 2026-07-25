using BookStitch.Models;
using System.IO;
using System.Windows.Media;

namespace BookStitch.Services;

public sealed class SoundNotificationService : IDisposable
{
    private readonly Func<AppSettings> _settingsProvider;
    private readonly MediaPlayer _player = new();
    private readonly string _soundFolder;
    private bool _disposed;

    public SoundNotificationService(Func<AppSettings> settingsProvider, string? soundFolder = null)
    {
        _settingsProvider = settingsProvider ?? throw new ArgumentNullException(nameof(settingsProvider));
        _soundFolder = soundFolder ?? Path.Combine(AppContext.BaseDirectory, "Assets", "Sounds");
        _player.MediaEnded += (_, _) => ClosePlayer();
        _player.MediaFailed += (_, _) => ClosePlayer();
    }

    public void Play(NotificationEvent notificationEvent)
    {
        var settings = _settingsProvider();
        var profile = SoundSettingsService.NormalizeProfile(settings.SoundProfile);

        if (!SoundSettingsService.IsEnabled(profile, notificationEvent))
            return;

        var library = SoundSettingsService.NormalizeLibrary(settings.SoundLibrary);
        PlayFile(library, NotificationSoundCatalog.GetFileName(notificationEvent), settings.SoundVolumePercent);
    }

    public void PlayPreview()
    {
        var settings = _settingsProvider();
        var library = SoundSettingsService.NormalizeLibrary(settings.SoundLibrary);
        PlayFile(library, NotificationSoundCatalog.PreviewSoundFileName, settings.SoundVolumePercent);
    }

    private void PlayFile(SoundLibrary library, string? fileName, int volumePercent)
    {
        if (_disposed || string.IsNullOrWhiteSpace(fileName))
            return;

        try
        {
            var libraryFolder = NotificationSoundCatalog.GetLibraryFolderName(library);
            var path = Path.Combine(_soundFolder, libraryFolder, fileName);
            if (!File.Exists(path))
                return;

            _player.Stop();
            _player.Close();
            _player.Volume = SoundSettingsService.NormalizeVolumePercent(volumePercent) / 100d;
            _player.Open(new Uri(path, UriKind.Absolute));
            _player.Play();
        }
        catch
        {
            // Sounds are optional. Playback failures must never interrupt a workflow.
            ClosePlayer();
        }
    }

    private void ClosePlayer()
    {
        try
        {
            _player.Stop();
            _player.Close();
        }
        catch
        {
            // Ignore cleanup failures for optional notification audio.
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        ClosePlayer();
    }
}
