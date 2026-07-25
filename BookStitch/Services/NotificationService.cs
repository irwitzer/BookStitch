using BookStitch.Models;

namespace BookStitch.Services;

public sealed class NotificationService : IDisposable
{
    private readonly SoundNotificationService _soundService;
    private readonly WindowAttentionService? _attentionService;

    public NotificationService(
        SoundNotificationService soundService,
        WindowAttentionService? attentionService = null)
    {
        _soundService = soundService ?? throw new ArgumentNullException(nameof(soundService));
        _attentionService = attentionService;
    }

    public void Notify(NotificationEvent notificationEvent)
    {
        _soundService.Play(notificationEvent);
        _attentionService?.RequestAttention(notificationEvent);
    }

    public void PlaySoundPreview()
    {
        _soundService.PlayPreview();
    }

    public void Dispose()
    {
        _soundService.Dispose();
    }
}
