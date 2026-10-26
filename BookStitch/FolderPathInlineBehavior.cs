using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace BookStitch;

public static class FolderPathInlineBehavior
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.RegisterAttached(
        "Text",
        typeof(string),
        typeof(FolderPathInlineBehavior),
        new PropertyMetadata(string.Empty, OnTextChanged));

    public static void SetText(DependencyObject element, string value) =>
        element.SetValue(TextProperty, value);

    public static string GetText(DependencyObject element) =>
        (string)element.GetValue(TextProperty);

    private static void OnTextChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
    {
        if (element is not TextBlock textBlock)
            return;

        var text = e.NewValue?.ToString() ?? string.Empty;
        textBlock.Inlines.Clear();
        if (text.Length == 0)
            return;

        var segmentStart = 0;
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] is not ('\\' or '/'))
                continue;

            AddTextRun(textBlock, text[segmentStart..index]);
            textBlock.Inlines.Add(new Run(text[index].ToString()));
            segmentStart = index + 1;
        }

        AddTextRun(textBlock, text[segmentStart..]);
    }

    private static void AddTextRun(TextBlock textBlock, string text)
    {
        if (text.Length > 0)
            textBlock.Inlines.Add(new Run(text));
    }
}
