using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;

namespace BookStitch.Controls;

public static class CandidateComboBoxInteraction
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled",
        typeof(bool),
        typeof(CandidateComboBoxInteraction),
        new PropertyMetadata(false, OnIsEnabledChanged));

    public static readonly DependencyProperty MaxInputLengthProperty = DependencyProperty.RegisterAttached(
        "MaxInputLength",
        typeof(int),
        typeof(CandidateComboBoxInteraction),
        new PropertyMetadata(0, OnMaxInputLengthChanged));

    private static readonly DependencyProperty IsAutomaticSelectionProperty = DependencyProperty.RegisterAttached(
        "IsAutomaticSelection",
        typeof(bool),
        typeof(CandidateComboBoxInteraction),
        new PropertyMetadata(false));

    private static readonly DependencyProperty SuppressionDepthProperty = DependencyProperty.RegisterAttached(
        "SuppressionDepth",
        typeof(int),
        typeof(CandidateComboBoxInteraction),
        new PropertyMetadata(0));

    public static void SetIsEnabled(DependencyObject element, bool value) =>
        element.SetValue(IsEnabledProperty, value);

    public static bool GetIsEnabled(DependencyObject element) =>
        (bool)element.GetValue(IsEnabledProperty);

    public static void SetMaxInputLength(DependencyObject element, int value) =>
        element.SetValue(MaxInputLengthProperty, Math.Max(0, value));

    public static int GetMaxInputLength(DependencyObject element) =>
        (int)element.GetValue(MaxInputLengthProperty);

    internal static bool IsAutomaticSelection(ComboBox comboBox) =>
        (bool)comboBox.GetValue(IsAutomaticSelectionProperty);

    internal static IDisposable SuppressSelectionFinalization(ComboBox comboBox) =>
        new SelectionFinalizationSuppression(comboBox);

    internal static bool ProcessTextInput(ComboBox comboBox, string input)
    {
        if (!GetIsEnabled(comboBox) || !comboBox.IsEditable || string.IsNullOrEmpty(input))
            return false;

        comboBox.ApplyTemplate();
        if (GetEditableTextBox(comboBox) is not { } textBox)
            return false;

        var start = textBox.SelectionStart;
        var current = textBox.Text ?? string.Empty;
        var selectedLength = textBox.SelectionLength;
        var acceptedInput = LimitInput(comboBox, current, selectedLength, input);
        if (acceptedInput.Length == 0)
            return true;

        var next = current.Remove(start, selectedLength).Insert(start, acceptedInput);
        var typedPrefixLength = start + acceptedInput.Length;
        var match = FindPrefixCandidate(comboBox, next);

        using (SuppressSelectionFinalization(comboBox))
        {
            comboBox.SetValue(IsAutomaticSelectionProperty, match is not null);
            comboBox.SelectedItem = match;
        }

        if (match is null)
        {
            SetEditorText(comboBox, textBox, next, typedPrefixLength, 0);
            return true;
        }

        var candidateText = GetCandidateText(comboBox, match);
        var completion = next + candidateText[next.Length..];
        SetEditorText(
            comboBox,
            textBox,
            completion,
            typedPrefixLength,
            completion.Length - typedPrefixLength);
        return true;
    }

    internal static bool ProcessDeleteKey(ComboBox comboBox, Key key)
    {
        if (!GetIsEnabled(comboBox) || !comboBox.IsEditable || key is not (Key.Back or Key.Delete))
            return false;

        comboBox.ApplyTemplate();
        if (GetEditableTextBox(comboBox) is not { } textBox)
            return false;

        var text = textBox.Text ?? string.Empty;
        var start = textBox.SelectionStart;
        var length = textBox.SelectionLength;
        var changed = false;

        if (length > 0)
        {
            text = text.Remove(start, length);
            changed = true;
        }
        else if (key == Key.Back && start > 0)
        {
            text = text.Remove(start - 1, 1);
            start--;
            changed = true;
        }
        else if (key == Key.Delete && start < text.Length)
        {
            text = text.Remove(start, 1);
            changed = true;
        }

        if (!changed)
            return false;

        using (SuppressSelectionFinalization(comboBox))
        {
            comboBox.SetValue(IsAutomaticSelectionProperty, false);
            comboBox.SelectedItem = null;
        }

        SetEditorText(comboBox, textBox, text, start, 0);
        return true;
    }

    internal static void RestoreEditorState(
        ComboBox comboBox,
        string text,
        int selectionStart,
        int selectionLength,
        int caretIndex,
        bool isAutomaticSelection)
    {
        comboBox.SetValue(IsAutomaticSelectionProperty, isAutomaticSelection);
        ApplyEditorState(comboBox, text, selectionStart, selectionLength, caretIndex);

        _ = comboBox.Dispatcher.BeginInvoke(
            DispatcherPriority.ContextIdle,
            () =>
            {
                if (!GetIsEnabled(comboBox) ||
                    !string.Equals(comboBox.Text, text, StringComparison.Ordinal))
                {
                    return;
                }

                ApplyEditorState(comboBox, text, selectionStart, selectionLength, caretIndex);
            });
    }

    private static void OnMaxInputLengthChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is ComboBox comboBox)
            ApplyMaxInputLength(comboBox);
    }

    private static void OnIsEnabledChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is not ComboBox comboBox)
            return;

        if ((bool)e.OldValue)
            Detach(comboBox);
        if ((bool)e.NewValue)
            Attach(comboBox);
    }

    private static void Attach(ComboBox comboBox)
    {
        comboBox.Loaded += ComboBox_Loaded;
        comboBox.Unloaded += ComboBox_Unloaded;
        comboBox.PreviewTextInput += ComboBox_PreviewTextInput;
        comboBox.PreviewKeyDown += ComboBox_PreviewKeyDown;
        comboBox.PreviewMouseLeftButtonUp += ComboBox_PreviewMouseLeftButtonUp;
        comboBox.SelectionChanged += ComboBox_SelectionChanged;
        comboBox.DropDownOpened += ComboBox_DropDownOpened;
        DisableNativeTextSearch(comboBox);
        ApplyMaxInputLength(comboBox);
    }

    private static void Detach(ComboBox comboBox)
    {
        comboBox.Loaded -= ComboBox_Loaded;
        comboBox.Unloaded -= ComboBox_Unloaded;
        comboBox.PreviewTextInput -= ComboBox_PreviewTextInput;
        comboBox.PreviewKeyDown -= ComboBox_PreviewKeyDown;
        comboBox.PreviewMouseLeftButtonUp -= ComboBox_PreviewMouseLeftButtonUp;
        comboBox.SelectionChanged -= ComboBox_SelectionChanged;
        comboBox.DropDownOpened -= ComboBox_DropDownOpened;
        comboBox.ClearValue(IsAutomaticSelectionProperty);
        comboBox.ClearValue(SuppressionDepthProperty);
    }

    private static void ComboBox_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is ComboBox comboBox)
        {
            DisableNativeTextSearch(comboBox);
            ApplyMaxInputLength(comboBox);
            ConfigureEditableEditor(comboBox);
            ConfigureDropDown(comboBox);
        }
    }

    private static void ComboBox_DropDownOpened(object? sender, EventArgs e)
    {
        if (sender is ComboBox comboBox)
            ConfigureDropDown(comboBox);
    }

    private static void ComboBox_Unloaded(object sender, RoutedEventArgs e)
    {
        if (sender is ComboBox comboBox)
            comboBox.SetValue(IsAutomaticSelectionProperty, false);
    }

    private static void ComboBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (sender is ComboBox comboBox && ProcessTextInput(comboBox, e.Text))
            e.Handled = true;
    }

    private static void ComboBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not ComboBox comboBox)
            return;

        if (ProcessDeleteKey(comboBox, e.Key))
        {
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter &&
            comboBox.SelectedItem is not null &&
            !IsAutomaticSelection(comboBox))
        {
            FinalizeExplicitSelection(comboBox);
        }
    }

    private static void ComboBox_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is ComboBox comboBox &&
            e.OriginalSource is DependencyObject source &&
            FindVisualAncestor<ComboBoxItem>(source) is not null &&
            comboBox.SelectedItem is not null)
        {
            FinalizeExplicitSelection(comboBox);
        }
    }

    private static void ComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox comboBox ||
            !comboBox.IsEditable ||
            comboBox.SelectedItem is null ||
            IsSuppressed(comboBox))
        {
            return;
        }

        FinalizeExplicitSelection(comboBox);
    }

    internal static void FinalizeExplicitSelection(ComboBox comboBox)
    {
        if (comboBox.SelectedItem is null || IsSuppressed(comboBox))
            return;

        comboBox.SetValue(IsAutomaticSelectionProperty, false);
        var candidateText = GetCandidateText(comboBox, comboBox.SelectedItem);
        if (candidateText.Length == 0)
            return;

        comboBox.ApplyTemplate();
        if (GetEditableTextBox(comboBox) is not { } textBox)
            return;

        SetEditorText(comboBox, textBox, candidateText, candidateText.Length, 0);
        _ = comboBox.Dispatcher.BeginInvoke(
            DispatcherPriority.ContextIdle,
            () => MoveCaretToEnd(comboBox));
    }

    private static object? FindPrefixCandidate(ComboBox comboBox, string query)
    {
        if (query.Length == 0)
            return null;

        var compareInfo = CultureInfo.CurrentCulture.CompareInfo;
        const CompareOptions options = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace;

        foreach (var candidate in comboBox.Items)
        {
            var text = GetCandidateText(comboBox, candidate);
            if (text.Length >= query.Length && compareInfo.IsPrefix(text, query, options))
                return candidate;
        }

        return null;
    }

    private static string GetCandidateText(ComboBox comboBox, object candidate)
    {
        var path = TextSearch.GetTextPath(comboBox);
        if (string.IsNullOrWhiteSpace(path))
            return candidate.ToString() ?? string.Empty;

        object? current = candidate;
        foreach (var segment in path.Split('.'))
        {
            if (current is null)
                return string.Empty;

            var descriptor = TypeDescriptor.GetProperties(current).Find(segment, ignoreCase: false);
            if (descriptor is null)
                return string.Empty;
            current = descriptor.GetValue(current);
        }

        return current?.ToString() ?? string.Empty;
    }

    private static TextBox? GetEditableTextBox(ComboBox comboBox) =>
        comboBox.Template?.FindName("PART_EditableTextBox", comboBox) as TextBox;

    private static void ApplyEditorState(
        ComboBox comboBox,
        string text,
        int selectionStart,
        int selectionLength,
        int caretIndex)
    {
        comboBox.ApplyTemplate();
        if (GetEditableTextBox(comboBox) is not { } textBox)
            return;

        comboBox.SetCurrentValue(ComboBox.TextProperty, text);
        textBox.Text = text;
        var safeStart = Math.Clamp(selectionStart, 0, text.Length);
        textBox.CaretIndex = Math.Clamp(caretIndex, 0, text.Length);
        textBox.SelectionStart = safeStart;
        textBox.SelectionLength = Math.Clamp(
            selectionLength,
            0,
            text.Length - safeStart);
    }

    private static void SetEditorText(
        ComboBox comboBox,
        TextBox textBox,
        string text,
        int selectionStart,
        int selectionLength)
    {
        comboBox.SetCurrentValue(ComboBox.TextProperty, text);
        textBox.Text = text;
        var safeStart = Math.Clamp(selectionStart, 0, text.Length);
        textBox.CaretIndex = safeStart;
        textBox.SelectionStart = safeStart;
        textBox.SelectionLength = Math.Clamp(
            selectionLength,
            0,
            text.Length - safeStart);
    }

    private static void MoveCaretToEnd(ComboBox comboBox)
    {
        if (IsSuppressed(comboBox))
            return;

        comboBox.ApplyTemplate();
        if (GetEditableTextBox(comboBox) is not { } textBox)
            return;

        textBox.SelectionLength = 0;
        textBox.CaretIndex = textBox.Text.Length;
    }

    private static string LimitInput(ComboBox comboBox, string current, int selectionLength, string input)
    {
        var maxLength = GetMaxInputLength(comboBox);
        if (maxLength <= 0)
            return input;

        var available = maxLength - (current.Length - selectionLength);
        if (available <= 0)
            return string.Empty;

        return input.Length <= available ? input : input[..available];
    }

    private static void ApplyMaxInputLength(ComboBox comboBox)
    {
        comboBox.ApplyTemplate();
        if (GetEditableTextBox(comboBox) is { } textBox)
            textBox.MaxLength = GetMaxInputLength(comboBox);
    }

    private static void ConfigureEditableEditor(ComboBox comboBox)
    {
        if (!comboBox.IsEditable)
            return;

        comboBox.ApplyTemplate();
        if (FindVisualChild<ToggleButton>(comboBox) is { } dropDownButton)
            dropDownButton.Cursor = Cursors.Hand;

        if (GetEditableTextBox(comboBox) is not { } editableTextBox)
            return;

        // Keep the 27-DIP concept editor readable without inheriting the generic TextBox padding.
        editableTextBox.Margin = new Thickness(6, 0, 28, 0);
        editableTextBox.Padding = new Thickness(0);
        editableTextBox.VerticalContentAlignment = VerticalAlignment.Center;
    }

    private static void ConfigureDropDown(ComboBox comboBox)
    {
        comboBox.ApplyTemplate();
        if (comboBox.Template.FindName("Popup", comboBox) is not Popup popup)
            return;

        popup.Placement = PlacementMode.Bottom;
        popup.PlacementTarget = comboBox;
        popup.HorizontalOffset = 0;

        if (popup.Child is FrameworkElement child)
        {
            BindingOperations.SetBinding(
                child,
                FrameworkElement.WidthProperty,
                new Binding(nameof(FrameworkElement.ActualWidth)) { Source = comboBox });
        }
    }

    private static void DisableNativeTextSearch(ComboBox comboBox)
    {
        if (comboBox.IsEditable)
            comboBox.SetCurrentValue(ComboBox.IsTextSearchEnabledProperty, false);
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, index);
            if (child is T match)
                return match;
            if (FindVisualChild<T>(child) is { } nested)
                return nested;
        }

        return null;
    }

    private static T? FindVisualAncestor<T>(DependencyObject source) where T : DependencyObject
    {
        var current = source;
        while (current is not null)
        {
            if (current is T match)
                return match;
            current = System.Windows.Media.VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    private static bool IsSuppressed(ComboBox comboBox) =>
        (int)comboBox.GetValue(SuppressionDepthProperty) > 0;

    private sealed class SelectionFinalizationSuppression : IDisposable
    {
        private ComboBox? _comboBox;

        public SelectionFinalizationSuppression(ComboBox comboBox)
        {
            _comboBox = comboBox;
            comboBox.SetValue(
                SuppressionDepthProperty,
                (int)comboBox.GetValue(SuppressionDepthProperty) + 1);
        }

        public void Dispose()
        {
            if (_comboBox is not { } comboBox)
                return;

            comboBox.SetValue(
                SuppressionDepthProperty,
                Math.Max(0, (int)comboBox.GetValue(SuppressionDepthProperty) - 1));
            _comboBox = null;
        }
    }
}
