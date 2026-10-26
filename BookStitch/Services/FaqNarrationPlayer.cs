using System.Windows.Media;

namespace BookStitch.Services;

// Small device boundary, allowing lifecycle tests without speakers or media codecs.
public interface IFaqNarrationPlayer
{
    event EventHandler? Opened;
    event EventHandler? Ended;
    event EventHandler? Failed;
    TimeSpan Position { get; }
    void Open(Uri source);
    void Play();
    void Pause();
    void Close();
}

public sealed class FaqNarrationPlayer : IFaqNarrationPlayer
{
    private MediaPlayer? _player;
    public event EventHandler? Opened;
    public event EventHandler? Ended;
    public event EventHandler? Failed;
    public TimeSpan Position => _player?.Position ?? TimeSpan.Zero;

    public void Open(Uri source)
    {
        Close();
        var player = new MediaPlayer();
        _player = player;
        // Ignore queued notifications from a closed/replaced playback instance.
        player.MediaOpened += (_, _) => { if (ReferenceEquals(_player, player)) Opened?.Invoke(this, EventArgs.Empty); };
        player.MediaEnded += (_, _) => { if (ReferenceEquals(_player, player)) Ended?.Invoke(this, EventArgs.Empty); };
        player.MediaFailed += (_, _) => { if (ReferenceEquals(_player, player)) Failed?.Invoke(this, EventArgs.Empty); };
        player.Open(source);
    }
    public void Play() => _player?.Play();
    public void Pause() => _player?.Pause();
    public void Close()
    {
        var player = _player;
        _player = null;
        player?.Close();
    }
}
