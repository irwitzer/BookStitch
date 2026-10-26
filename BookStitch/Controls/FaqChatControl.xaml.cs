using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using BookStitch.Models;
using BookStitch.Services;

namespace BookStitch.Controls;

public partial class FaqChatControl : UserControl
{
    private FaqChatContent? _content;
    private Window? _owner;
    private readonly HashSet<FrameworkElement> _animated = [];
    public FaqChatSession? Session { get; private set; }
    public event EventHandler? RevealRequested;
    public event EventHandler? SendRequested;

    public FaqChatControl()
    {
        InitializeComponent();
        Loaded += (_, _) => AttachOwner();
        IsVisibleChanged += (_, _) =>
        {
            if (!IsVisible) StopAnimations();
            else _ = StartChatAsync();
        };
    }

    public void Initialize(FaqChatContent content)
    {
        if (Session is not null) return;
        _content = content;
        CreateSession();
    }

    private void CreateSession()
    {
        Session = new FaqChatSession(_content!);
        Session.ContentAdded += Session_ContentAdded;
        DataContext = Session;
    }

    public Task StartChatAsync() => Dispatcher.InvokeAsync(async () =>
    {
        if (!IsVisible || Session is null) return;
        await Session.StartAsync();
        if (Session.CanSend) StartPulse(SendButton);
    }).Task.Unwrap();

    public void Reset()
    {
        if (Session is not null)
        {
            Session.ContentAdded -= Session_ContentAdded;
            Session.Dispose();
        }
        StopAnimations();
        CreateSession();
    }

    public FrameworkElement? GetCurrentReplyAnchor() => Session?.CurrentReply is { } reply
        ? HistoryItems.ItemContainerGenerator.ContainerFromItem(reply) as FrameworkElement
        : null;

    private void Session_ContentAdded(object? sender, EventArgs e) => RevealRequested?.Invoke(this, EventArgs.Empty);

    private void AttachOwner()
    {
        var owner = Window.GetWindow(this);
        if (owner is null || _owner is not null) return;
        _owner = owner;
        owner.Closed += Owner_Closed;
    }

    private void Owner_Closed(object? sender, EventArgs e)
    {
        if (_owner is not null) _owner.Closed -= Owner_Closed;
        _owner = null;
        StopAnimations();
        if (Session is not null)
        {
            Session.ContentAdded -= Session_ContentAdded;
            Session.Dispose();
        }
    }

    private async void Send_Click(object sender, RoutedEventArgs e) => await SendPreparedMessageAsync();

    public Task SendPreparedMessageAsync() => Dispatcher.InvokeAsync(async () =>
    {
        if (Session?.CanSend != true) return;
        SendRequested?.Invoke(this, EventArgs.Empty);
        await Session.SendAsync();
        // Restore keyboard continuity only if the user has not focused another control.
        if (IsVisible && Session.CanSend && System.Windows.Input.Keyboard.FocusedElement is not FrameworkElement { IsVisible: true })
            SendButton.Focus();
    }).Task.Unwrap();

    private void Restart_Click(object sender, RoutedEventArgs e)
    {
        if (Session?.IsComplete != true) return;
        Reset();
        _ = StartChatAsync();
        SendRequested?.Invoke(this, EventArgs.Empty);
        RevealRequested?.Invoke(this, EventArgs.Empty);
        SendButton.Focus();
    }

    private void FadeIn_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || !SystemParameters.ClientAreaAnimation) return;
        _animated.Add(element);
        element.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, element is Image ? FaqChatTiming.ImageFade : FaqChatTiming.Fade) { FillBehavior = FillBehavior.Stop });
    }

    private void Pulse_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element) StartPulse(element);
    }

    private void SendButton_StateChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (SendButton.IsVisible && SendButton.IsEnabled) StartPulse(SendButton);
        else StopAnimation(SendButton);
    }

    private void StartPulse(FrameworkElement element)
    {
        if (!IsVisible || !element.IsVisible || !SystemParameters.ClientAreaAnimation) return;
        _animated.Add(element);
        element.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0.45, FaqChatTiming.Pulse)
        {
            AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, FillBehavior = FillBehavior.Stop
        });
    }

    private void Animation_Unloaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element) StopAnimation(element);
    }

    private void StopAnimation(FrameworkElement element)
    {
        element.BeginAnimation(OpacityProperty, null);
        _animated.Remove(element);
    }

    private void StopAnimations()
    {
        foreach (var element in _animated.ToArray()) StopAnimation(element);
    }
}

// Deliberately English for this FAQ, independent of the general UI language.
// Kept together so a later FAQ language selection has one presentation-text owner.
public static class FaqChatLabels
{
    public const string Generating = "Generating image...";
    public const string Restart = "Restart chat";
    public const string ClickHere = "Please click here";
    public const string Send = "Send prepared message";
    public const string PreparedHuman = "Prepared human message";
    public const string Human = "Human message";
    public const string Assistant = "ChatGPT message";
}

public static class FaqChatPresentation
{
    public const double IconWidth = 60;
}
