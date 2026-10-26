using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using BookStitch.Models;
using BookStitch.Services;

namespace BookStitch.Controls;

public partial class SettingsFaqControl : UserControl
{
    private bool _showingChat;
    private bool _showingNarration;
    private bool _followNarration = true;
    private bool _hasSidebarPreview;
    private bool _followChat = true;
    private bool _scrollingProgrammatically;
    private double _chatOffset;

    public SettingsFaqControl()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            WeakEventManager<DeveloperAppearancePreview, EventArgs>.AddHandler(
                DeveloperToolsCommands.AppearancePreview, nameof(DeveloperAppearancePreview.Changed), PreviewAppearanceChanged);
            RefreshSidebarPreview();
        };
        Unloaded += (_, _) => WeakEventManager<DeveloperAppearancePreview, EventArgs>.RemoveHandler(
            DeveloperToolsCommands.AppearancePreview, nameof(DeveloperAppearancePreview.Changed), PreviewAppearanceChanged);
        var entries = FaqCatalog.LoadEnglish();
        var chat = entries.FirstOrDefault(entry => entry.Chat is not null)?.Chat;
        if (chat is not null) IconChat.Initialize(chat);
        QuestionsComboBox.ItemsSource = entries;
        QuestionsComboBox.SelectedIndex = 0;
    }

    private void QuestionsComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, QuestionsComboBox)) return;
        var entry = QuestionsComboBox.SelectedItem as FaqEntry;
        AudibleNarration.Configure(entry);
        _showingNarration = entry?.Narration is not null;
        _followNarration = true;
        AnswerSections.Visibility = _showingNarration ? Visibility.Collapsed : Visibility.Visible;
        AudibleNarration.Visibility = _showingNarration ? Visibility.Visible : Visibility.Collapsed;
        RefreshSidebarPreview();
        var leavingChat = _showingChat && QuestionsComboBox.SelectedItem is not FaqEntry { Chat: not null };
        if (_showingChat) _chatOffset = AnswerScrollViewer.VerticalOffset;
        _showingChat = QuestionsComboBox.SelectedItem is FaqEntry { Chat: not null };
        IconChat.Visibility = _showingChat ? Visibility.Visible : Visibility.Collapsed;
        if (leavingChat)
        {
            IconChat.Reset();
            _chatOffset = 0;
        }
        ScrollTo(_showingChat ? _chatOffset : 0);
    }

    private void AnswerImage_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is not Image { DataContext: FaqImage content } image) return;
        image.Clip = content.CornerRadius > 0
            ? new RectangleGeometry(new Rect(0, 0, image.ActualWidth, image.ActualHeight), content.CornerRadius, content.CornerRadius)
            : null;
    }

    private void PreviewAppearanceChanged(object? sender, EventArgs e)
    {
        if (Dispatcher.CheckAccess() && IsLoaded) RefreshSidebarPreview();
    }

    private void RefreshSidebarPreview()
    {
        if (QuestionsComboBox is null) return;
        var preview = (QuestionsComboBox.SelectedItem as FaqEntry)?.SidebarImage;
        var overridden = DeveloperToolsCommands.AppearancePreview.GetImage(AppearanceAssetRole.FaqSquare);
        _hasSidebarPreview = preview is not null || overridden is not null;
        FaqSidebarImage.Source = overridden is null ? FaqSidebarImageService.GetImage(preview) :
            AppearanceSettingsService.LoadArtwork(AppearanceAssetRole.FaqSquare, overridden.RelativePath) ?? FaqSidebarImageService.GetImage(preview);
        FaqSidebarBorder.Padding = new Thickness(_hasSidebarPreview ? 0 : 16);
        FaqSidebarImage.Stretch = _hasSidebarPreview ? Stretch.UniformToFill : Stretch.Uniform;
        UpdateSidebarImageClip();
    }

    private void FaqSidebarImage_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateSidebarImageClip();

    private void UpdateSidebarImageClip()
    {
        if (!_hasSidebarPreview)
        {
            FaqSidebarImage.Clip = null;
            return;
        }
        var radius = Math.Max(0, FaqSidebarBorder.CornerRadius.TopLeft - FaqSidebarBorder.BorderThickness.Left);
        FaqSidebarImage.Clip = new RectangleGeometry(
            new Rect(0, 0, FaqSidebarImage.ActualWidth, FaqSidebarImage.ActualHeight), radius, radius);
    }

    private void ScrollTo(double offset)
    {
        _scrollingProgrammatically = true;
        AnswerScrollViewer.UpdateLayout();
        AnswerScrollViewer.ScrollToVerticalOffset(offset);
        Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() => _scrollingProgrammatically = false));
    }

    private void AudibleNarration_RevealRequested(object? sender, EventArgs e)
    {
        if (!_showingNarration || !IsVisible || !_followNarration) return;
        AnswerScrollViewer.UpdateLayout();
        if (AudibleNarration.IsImageRevealing && AnswerScrollViewer.Content is FrameworkElement content)
            ScrollTo(Math.Max(0, AudibleNarration.ImageAnchor.TranslatePoint(new Point(), content).Y - 8));
        else
            ScrollTo(AnswerScrollViewer.ScrollableHeight);
    }

    private void IconChat_SendRequested(object? sender, EventArgs e) => _followChat = true;

    private void IconChat_RevealRequested(object? sender, EventArgs e)
    {
        if (!_showingChat || !IsVisible || !_followChat) return;
        AnswerScrollViewer.UpdateLayout();
        // Once the next human prompt starts typing, follow it through to its send button.
        // Keep the opening of the assistant reply readable during its own reveal phase.
        if (IconChat.Session is { HasPendingHuman: true } or { IsComplete: true })
        {
            ScrollTo(AnswerScrollViewer.ScrollableHeight);
            return;
        }
        if (IconChat.GetCurrentReplyAnchor() is { } anchor && AnswerScrollViewer.Content is FrameworkElement content)
        {
            var top = anchor.TranslatePoint(new Point(), content).Y;
            ScrollTo(Math.Max(0, top - 8));
        }
    }

    private void AnswerScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (_showingNarration && ReferenceEquals(e.OriginalSource, AnswerScrollViewer) &&
            !_scrollingProgrammatically && e.ExtentHeightChange == 0 && e.VerticalChange < 0)
            _followNarration = false;
        if (!_showingChat || !ReferenceEquals(e.OriginalSource, AnswerScrollViewer)) return;
        if (!_scrollingProgrammatically && e.ExtentHeightChange == 0 && e.VerticalChange < 0)
            _followChat = false;
        if (!_scrollingProgrammatically && e.VerticalChange > 0 &&
            AnswerScrollViewer.ScrollableHeight - AnswerScrollViewer.VerticalOffset < 2)
            _followChat = true;
    }

    private void AnswerScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_showingNarration && e.Delta > 0) _followNarration = false;
        if (_showingChat && e.Delta > 0) _followChat = false;
    }

    private void AnswerScrollViewer_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_showingNarration && e.Key is Key.Up or Key.PageUp or Key.Home) _followNarration = false;
        if (_showingChat && e.Key is Key.Up or Key.PageUp or Key.Home) _followChat = false;
    }
}
