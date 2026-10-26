using System.Collections.ObjectModel;
using System.ComponentModel;
using BookStitch.Models;

namespace BookStitch.Services;

// All chat timing lives here; no settings or project state is persisted.
public static class FaqChatTiming
{
    public static readonly TimeSpan Thinking = TimeSpan.FromMilliseconds(500);
    public static readonly TimeSpan Generating = TimeSpan.FromMilliseconds(2400);
    public static readonly TimeSpan Fade = TimeSpan.FromMilliseconds(250);
    public static readonly TimeSpan ImageFade = TimeSpan.FromMilliseconds(1100);
    public static readonly TimeSpan ImagePause = TimeSpan.FromMilliseconds(450);
    public const int TextBatchCharacters = 6;
    public static readonly TimeSpan TextStep = TimeSpan.FromMilliseconds(45);
    public static readonly TimeSpan Pulse = TimeSpan.FromMilliseconds(850);
}

public sealed class FaqChatSession : INotifyPropertyChanged, IDisposable
{
    private readonly FaqChatContent _content;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly CancellationTokenSource _lifetime = new();
    private int _nextTurn;
    private bool _busy;
    private bool _started;
    private FaqChatText? _pendingHuman;
    private bool _disposed;

    public FaqChatSession(FaqChatContent content, Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _content = content;
        _delay = delay ?? Task.Delay;
    }

    public FaqChatText? CurrentReply { get; private set; }
    public ObservableCollection<FaqChatItem> History { get; } = [];
    public string? PendingHuman => _pendingHuman?.DisplayText;
    public bool HasPendingHuman => _pendingHuman is not null;
    public bool IsFirstPrompt => _nextTurn == 0 && CanSend;
    public bool CanSend => !_disposed && !_busy && !IsComplete && _pendingHuman is not null;
    public bool IsComplete => _nextTurn >= _content.Turns.Count;
    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? ContentAdded;

    public async Task StartAsync()
    {
        if (_started || _disposed) return;
        _started = true;
        _busy = true;
        try { await PrepareHumanAsync(); }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        finally { FinishOperation(); }
    }

    public async Task SendAsync()
    {
        if (!CanSend) return;
        var turn = _content.Turns[_nextTurn];
        _busy = true;
        CurrentReply = null;
        _pendingHuman = null;
        History.Add(new FaqChatText(turn.Human, true));
        Notify();
        ContentAdded?.Invoke(this, EventArgs.Empty);
        try
        {
            await _delay(FaqChatTiming.Thinking, _lifetime.Token);
            _lifetime.Token.ThrowIfCancellationRequested();
            var reply = new FaqChatText(turn.Assistant, false) { DisplayText = string.Empty };
            CurrentReply = reply;
            History.Add(reply);
            ContentAdded?.Invoke(this, EventArgs.Empty);
            await TypeAsync(reply);
            if (turn.Image is not null)
            {
                var generating = new FaqChatGenerating();
                History.Add(generating);
                ContentAdded?.Invoke(this, EventArgs.Empty);
                await _delay(FaqChatTiming.Generating, _lifetime.Token);
                _lifetime.Token.ThrowIfCancellationRequested();
                History.Remove(generating);
                History.Add(new FaqChatImage(turn.Image, $"Generated padlock icon {_nextTurn + 1}"));
                ContentAdded?.Invoke(this, EventArgs.Empty);
                await _delay(FaqChatTiming.ImageFade, _lifetime.Token);
                _lifetime.Token.ThrowIfCancellationRequested();
                await _delay(FaqChatTiming.ImagePause, _lifetime.Token);
                _lifetime.Token.ThrowIfCancellationRequested();
            }
            _nextTurn++;
            if (!IsComplete) await PrepareHumanAsync();
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        finally { FinishOperation(); }
    }

    private async Task PrepareHumanAsync()
    {
        _pendingHuman = new FaqChatText(_content.Turns[_nextTurn].Human, true) { DisplayText = string.Empty };
        Notify();
        await TypeAsync(_pendingHuman);
    }

    private async Task TypeAsync(FaqChatText message)
    {
        var characters = System.Globalization.StringInfo.ParseCombiningCharacters(message.Text);
        for (var count = FaqChatTiming.TextBatchCharacters; ; count += FaqChatTiming.TextBatchCharacters)
        {
            await _delay(FaqChatTiming.TextStep, _lifetime.Token);
            _lifetime.Token.ThrowIfCancellationRequested();
            message.DisplayText = count >= characters.Length ? message.Text : message.Text[..characters[count]];
            if (message.IsHuman) Notify();
            ContentAdded?.Invoke(this, EventArgs.Empty);
            if (count >= characters.Length) break;
        }
    }

    private void FinishOperation()
    {
        if (_disposed) { _lifetime.Dispose(); return; }
        _busy = false;
        Notify();
        ContentAdded?.Invoke(this, EventArgs.Empty);
    }

    private void Notify()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PendingHuman)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasPendingHuman)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsFirstPrompt)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanSend)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsComplete)));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime.Cancel();
        if (!_busy) _lifetime.Dispose();
    }
}
