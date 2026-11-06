using BookStitch.Services;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace BookStitch.Dialog;

public sealed class AacDecisionDialog : AppMessageDialog
{
    public TextBlock RecommendedProfile { get; }
    public TextBlock ReuseSummary { get; }
    public TextBlock ConvertSummary { get; }
    public Border RecommendationCard { get; }
    public Grid DecisionButtons { get; }

    public AacDecisionDialog(AacDecisionOptions options)
        : base("BookStitch", LocalizationText.Get("Aac.Plan.Title"), LocalizationText.Get("Aac.Compact.Explanation"),
            AppDialogKind.Information, null,
            [new(LocalizationText.Get("Aac.Compact.Accept"), AppDialogResult.Yes, IsPrimary: true, IsDefault: true),
             new(LocalizationText.Get("Aac.Compact.EncodeAll"), AppDialogResult.No),
             new(LocalizationText.Get("Aac.Plan.Back"), AppDialogResult.Cancel, IsCancel: true)])
    {
        Title = LocalizationText.Get("Aac.Plan.Title");
        Width = 580; MinWidth = 320; MinHeight = 0;
        Height = double.NaN; SizeToContent = SizeToContent.Height;
        MaxWidth = Math.Max(320, SystemParameters.WorkArea.Width - 24);
        MaxHeight = Math.Max(240, SystemParameters.WorkArea.Height - 24);
        IconCircle.Visibility = Visibility.Collapsed;

        var content = new StackPanel();
        var cardContent = new StackPanel();
        cardContent.Children.Add(Text(LocalizationText.Get("Aac.Compact.Recommendation"), 0, FontWeights.SemiBold));
        RecommendedProfile = Text(AacDecisionPresentationService.Profile(options.Optimized), 6, FontWeights.SemiBold);
        cardContent.Children.Add(RecommendedProfile);
        ReuseSummary = Text(AacDecisionPresentationService.ReuseSummary(options.Optimized), 10);
        ConvertSummary = Text(AacDecisionPresentationService.ConvertSummary(options.Optimized), 6);
        cardContent.Children.Add(ReuseSummary); cardContent.Children.Add(ConvertSummary);
        RecommendationCard = new Border { Child = cardContent, Padding = new Thickness(12), CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(1) };
        RecommendationCard.SetResourceReference(BackgroundProperty, "PanelBackgroundBrush");
        RecommendationCard.SetResourceReference(Border.BorderBrushProperty, "PanelBorderBrush");
        content.Children.Add(RecommendationCard);
        content.Children.Add(Text(LocalizationText.Get("Aac.Compact.Note"), 10));
        AdditionalContent.Margin = new Thickness(0, 10, 0, 0);
        AdditionalContent.Content = content; AdditionalContent.Visibility = Visibility.Visible;

        var buttons = ButtonsPanel.Children.OfType<Button>().ToArray();
        ButtonsPanel.Children.Clear();
        ButtonsPanel.HorizontalAlignment = HorizontalAlignment.Stretch;
        ButtonsPanel.Orientation = Orientation.Vertical;
        DecisionButtons = new Grid();
        DecisionButtons.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        DecisionButtons.ColumnDefinitions.Add(new() { Width = new GridLength(12) });
        DecisionButtons.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        DecisionButtons.RowDefinitions.Add(new() { Height = GridLength.Auto });
        DecisionButtons.RowDefinitions.Add(new() { Height = GridLength.Auto });
        for (var i = 0; i < buttons.Length; i++)
        {
            var button = buttons[i];
            var label = button.Content.ToString()!;
            button.Content = new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center };
            AutomationProperties.SetName(button, label);
            button.MinWidth = 0; button.MinHeight = 34; button.Height = double.NaN;
            button.Padding = new Thickness(10, 6, 10, 6);
            button.Margin = i == 2 ? new Thickness(0, 12, 0, 0) : new Thickness(0);
            if (i < 2) Grid.SetColumn(button, i * 2);
            else { Grid.SetRow(button, 1); Grid.SetColumnSpan(button, 3); button.HorizontalAlignment = HorizontalAlignment.Right; }
            DecisionButtons.Children.Add(button);
        }
        ButtonsPanel.Children.Add(DecisionButtons);

        // Scroll the complete body when DPI or work-area constraints cap the auto-sized window.
        var body = (Grid)((FrameworkElement)HeadingText.Parent).Parent;
        var parent = (Grid)body.Parent;
        var row = Grid.GetRow(body);
        var margin = new Thickness(20, 14, 20, 8);
        body.Margin = new Thickness(0);
        parent.Children.Remove(body);
        var scroll = new ScrollViewer { Content = body, Margin = margin, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, CanContentScroll = false, Focusable = false };
        Grid.SetRow(scroll, row); parent.Children.Add(scroll);
    }

    private static TextBlock Text(string? value, double top, FontWeight? weight = null) => new()
    {
        Text = value ?? "", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, top, 0, 0),
        FontWeight = weight ?? FontWeights.Normal, Visibility = value is null ? Visibility.Collapsed : Visibility.Visible
    };
}
