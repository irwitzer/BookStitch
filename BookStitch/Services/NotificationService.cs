using BookStitch.Models;

namespace BookStitch.Services;

public sealed class NotificationService : IDisposable
{
    private readonly SoundNotificationService _soundService;
    private readonly WindowAttentionService? _attentionService;
    private readonly Func<AppSettings>? _settingsProvider;
    private readonly NotificationDecisionService _decisionService;
    private readonly Action<NotificationEvent>? _playSound;
    private readonly Action<NotificationEvent>? _requestAttention;

    public NotificationService(
        SoundNotificationService soundService,
        WindowAttentionService? attentionService = null,
        Func<AppSettings>? settingsProvider = null,
        NotificationDecisionService? decisionService = null)
    {
        _soundService = soundService ?? throw new ArgumentNullException(nameof(soundService));
        _attentionService = attentionService;
        _settingsProvider = settingsProvider;
        _decisionService = decisionService ?? new NotificationDecisionService();
    }

    internal NotificationService(
        Func<AppSettings> settingsProvider,
        Action<NotificationEvent> playSound,
        Action<NotificationEvent> requestAttention,
        NotificationDecisionService? decisionService = null)
    {
        _settingsProvider = settingsProvider ?? throw new ArgumentNullException(nameof(settingsProvider));
        _playSound = playSound ?? throw new ArgumentNullException(nameof(playSound));
        _requestAttention = requestAttention ?? throw new ArgumentNullException(nameof(requestAttention));
        _soundService = null!;
        _decisionService = decisionService ?? new NotificationDecisionService();
    }

    public void Notify(NotificationEvent notificationEvent)
    {
        Notify(NotificationEventContext.FromEvent(notificationEvent));
    }

    public void Notify(NotificationEventContext context)
    {
        if (!_decisionService.ShouldNotify(context))
            return;

        var settings = _settingsProvider?.Invoke();
        if (settings is not null)
        {
            var eventProfile = EventProfileSettingsService.NormalizeProfile(settings.EventProfile);
            if (!EventProfileSettingsService.Includes(eventProfile, context.Type))
                return;
        }

        if (settings is null || SoundSettingsService.NormalizeLibrary(settings.SoundLibrary) != SoundLibrary.None)
        {
            if (_playSound is not null)
                _playSound(context.Type);
            else
                _soundService.Play(context.Type);
        }

        if (_requestAttention is not null)
            _requestAttention(context.Type);
        else
            _attentionService?.RequestAttention(context.Type);
    }

    public void PlaySoundPreview()
    {
        _soundService.PlayPreview();
    }

    public void Dispose()
    {
        _soundService?.Dispose();
    }
}
