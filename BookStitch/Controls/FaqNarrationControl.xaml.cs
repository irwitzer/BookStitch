using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using BookStitch.Models;
using BookStitch.Services;

namespace BookStitch.Controls;

public partial class FaqNarrationControl : UserControl
{
    public static readonly TimeSpan ImageFadeDuration = TimeSpan.FromSeconds(1.2);
    public static readonly DependencyProperty ImageWidthProperty = DependencyProperty.Register(
        nameof(ImageWidth), typeof(double), typeof(FaqNarrationControl), new PropertyMetadata(double.NaN,
            (owner, args) => ((FaqNarrationControl)owner).FinalImage.Width = (double)args.NewValue));
    public double ImageWidth { get => (double)GetValue(ImageWidthProperty); set => SetValue(ImageWidthProperty, value); }
    private readonly IFaqNarrationPlayer _player;
    private readonly DispatcherTimer _timer;
    private readonly Stopwatch _fade = new();
    private FaqNarrationContent? _content;
    private FaqNarrationTimeline? _timeline;
    private FaqImage? _image;
    private bool _active, _opened, _paused, _finishing, _completed;
    public bool IsPlaying => _active && !_paused && !_completed;
    public bool IsImageRevealing => _finishing || _completed;
    public FrameworkElement ImageAnchor => FinalImage;
    public event EventHandler? RevealRequested;

    public FaqNarrationControl() : this(new FaqNarrationPlayer()) { }
    public FaqNarrationControl(IFaqNarrationPlayer player)
    {
        _player = player;
        InitializeComponent();
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(25) };
        _timer.Tick += (_, _) => RefreshPlayback();
        _player.Opened += Player_Opened;
        _player.Ended += Player_Ended;
        _player.Failed += Player_Failed;
        IsVisibleChanged += (_, _) => { if (IsVisible) Start(); else Stop(); };
        Unloaded += (_, _) => Stop();
        UpdateButton();
    }

    public void Configure(FaqEntry? entry)
    {
        Stop();
        _content = entry?.Narration;
        _timeline = null;
        if (_content is null) return;
        _timeline = FaqNarrationTimeline.Load(_content.TimingResource,
            string.Join("\n\n", entry!.Sections.OfType<FaqParagraph>().Select(section => section.Text)));
        _image = entry.Sections.OfType<FaqImage>().Single();
        FinalImage.Source = new BitmapImage(new Uri("pack://application:,,," + _image.Source));
        AutomationProperties.SetName(FinalImage, _image.Text);
        if (IsVisible) Start();
    }

    private void Start()
    {
        if (_content is null || _timeline is null || _active || !IsVisible) return;
        _active = true;
        _paused = _finishing = _completed = _opened = false;
        NarrationText.Text = string.Empty;
        FinalImage.Visibility = Visibility.Collapsed;
        FinalImage.Opacity = 0;
        UpdateButton();
        try { _player.Open(new Uri(Path.Combine(AppContext.BaseDirectory, _content.Audio), UriKind.Absolute)); }
        catch { ShowWithoutAudio(); }
    }

    public void Stop()
    {
        _active = false;
        _timer.Stop();
        _fade.Reset();
        _opened = _paused = _finishing = _completed = false;
        try { _player.Close(); } catch { /* Optional FAQ media must not prevent navigation/shutdown. */ }
        NarrationText.Text = string.Empty;
        FinalImage.Visibility = Visibility.Collapsed;
        FinalImage.Opacity = 0;
        UpdateButton();
    }

    private void Player_Opened(object? sender, EventArgs args)
    {
        if (!_active) return;
        _opened = true;
        if (!_paused) { try { _player.Play(); _timer.Start(); } catch { ShowWithoutAudio(); } }
    }

    private void Player_Ended(object? sender, EventArgs args)
    {
        if (!_active || _finishing || _completed) return;
        NarrationText.Text = _timeline!.FullText;
        _finishing = true;
        FinalImage.Visibility = Visibility.Visible;
        _fade.Restart();
        if (_paused) _fade.Stop();
        else _timer.Start();
        RevealRequested?.Invoke(this, EventArgs.Empty);
    }

    private void Player_Failed(object? sender, EventArgs args) { if (_active) ShowWithoutAudio(); }
    private void ShowWithoutAudio()
    {
        _timer.Stop();
        _fade.Reset();
        try { _player.Close(); } catch { }
        NarrationText.Text = _timeline!.FullText;
        FinalImage.Visibility = Visibility.Visible;
        FinalImage.Opacity = 1;
        _completed = true;
        UpdateButton();
        RevealRequested?.Invoke(this, EventArgs.Empty);
    }

    public void TogglePlayback()
    {
        if (!_active || _completed) { Stop(); Start(); return; }
        _paused = !_paused;
        try
        {
            if (_paused) { _player.Pause(); _timer.Stop(); _fade.Stop(); }
            else if (_finishing) { _fade.Start(); _timer.Start(); }
            else if (_opened) { _player.Play(); _timer.Start(); }
        }
        catch { ShowWithoutAudio(); }
        UpdateButton();
    }

    public void RefreshPlayback()
    {
        if (!_active || _paused || _completed) return;
        if (_finishing)
        {
            FinalImage.Opacity = Math.Clamp(_fade.Elapsed.TotalMilliseconds / ImageFadeDuration.TotalMilliseconds, 0, 1);
            if (FinalImage.Opacity >= 1) { _completed = true; _timer.Stop(); _fade.Stop(); UpdateButton(); }
            return;
        }
        if (!_opened) return;
        try
        {
            var text = _timeline!.FullText[.._timeline.VisibleLength(_player.Position)];
            if (NarrationText.Text == text) return;
            NarrationText.Text = text;
            RevealRequested?.Invoke(this, EventArgs.Empty);
        }
        catch { ShowWithoutAudio(); }
    }

    private void UpdateButton()
    {
        PlaybackGlyph.Data = Geometry.Parse(IsPlaying ? "M0,0 H4 V12 H0 Z M8,0 H12 V12 H8 Z" : "M1,0 L12,6 L1,12 Z");
        AutomationProperties.SetName(PlaybackButton, LocalizationText.Get(IsPlaying ? "MainWindow.Action.Pause" : "UiStart"));
    }
    private void PlaybackButton_Click(object sender, RoutedEventArgs args) => TogglePlayback();
    private void FinalImage_SizeChanged(object sender, SizeChangedEventArgs args) =>
        FinalImage.Clip = new RectangleGeometry(new Rect(0, 0, FinalImage.ActualWidth, FinalImage.ActualHeight), _image?.CornerRadius ?? 28, _image?.CornerRadius ?? 28);
}
