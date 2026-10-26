using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;

namespace BookStitch.Controls;

public static class ComboBoxContentWidth
{
    private const double ChromeReserve = 42d;

    public static readonly DependencyProperty IncludeEditableTextProperty = DependencyProperty.RegisterAttached(
        "IncludeEditableText", typeof(bool), typeof(ComboBoxContentWidth), new PropertyMetadata(true));
    public static void SetIncludeEditableText(DependencyObject element, bool value) => element.SetValue(IncludeEditableTextProperty, value);
    public static bool GetIncludeEditableText(DependencyObject element) => (bool)element.GetValue(IncludeEditableTextProperty);

    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled",
        typeof(bool),
        typeof(ComboBoxContentWidth),
        new PropertyMetadata(false, OnIsEnabledChanged));

    private static readonly DependencyProperty StateProperty = DependencyProperty.RegisterAttached(
        "State",
        typeof(MeasurementState),
        typeof(ComboBoxContentWidth));

    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);

    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ComboBox comboBox)
            return;

        if ((bool)e.NewValue)
        {
            if (comboBox.GetValue(StateProperty) is MeasurementState)
                return;

            var state = new MeasurementState(comboBox);
            comboBox.SetValue(StateProperty, state);
            state.Attach();
        }
        else if (comboBox.GetValue(StateProperty) is MeasurementState state)
        {
            state.Detach();
            comboBox.ClearValue(StateProperty);
            comboBox.ClearValue(FrameworkElement.WidthProperty);
        }
    }

    private sealed class MeasurementState
    {
        private readonly ComboBox _comboBox;
        private INotifyCollectionChanged? _items;

        public MeasurementState(ComboBox comboBox) => _comboBox = comboBox;

        public void Attach()
        {
            _comboBox.Loaded += ComboBox_Loaded;
            _comboBox.DropDownOpened += ComboBox_DropDownOpened;
            _comboBox.AddHandler(TextBoxBase.TextChangedEvent, new TextChangedEventHandler(EditableTextBox_TextChanged));
            _items = _comboBox.Items as INotifyCollectionChanged;
            if (_items is not null)
                _items.CollectionChanged += Items_CollectionChanged;
            ScheduleMeasure();
        }

        public void Detach()
        {
            _comboBox.Loaded -= ComboBox_Loaded;
            _comboBox.DropDownOpened -= ComboBox_DropDownOpened;
            _comboBox.RemoveHandler(TextBoxBase.TextChangedEvent, new TextChangedEventHandler(EditableTextBox_TextChanged));
            if (_items is not null)
                _items.CollectionChanged -= Items_CollectionChanged;
            _items = null;
        }

        private void ComboBox_Loaded(object sender, RoutedEventArgs e) => ScheduleMeasure();

        private void ComboBox_DropDownOpened(object? sender, EventArgs e) => ScheduleMeasure();

        private void Items_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => ScheduleMeasure();

        private void EditableTextBox_TextChanged(object sender, TextChangedEventArgs e) => ScheduleMeasure();

        private void ScheduleMeasure()
        {
            _comboBox.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(Measure));
        }

        private void Measure()
        {
            if (!GetIsEnabled(_comboBox))
                return;

            var widest = _comboBox.IsEditable && GetIncludeEditableText(_comboBox) ? MeasureContent(_comboBox.Text) : 0d;
            foreach (var item in _comboBox.Items)
            {
                var content = item is ComboBoxItem comboBoxItem ? comboBoxItem.Content : item;
                widest = Math.Max(widest, MeasureContent(content));
            }

            if (widest <= 0)
                return;

            var desired = Math.Ceiling(widest + ChromeReserve);
            desired = Math.Max(desired, _comboBox.MinWidth);
            if (!double.IsInfinity(_comboBox.MaxWidth))
                desired = Math.Min(desired, _comboBox.MaxWidth);

            _comboBox.Width = desired;
        }

        private double MeasureContent(object? content)
        {
            if (content is FrameworkElement element)
            {
                element.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                return element.DesiredSize.Width;
            }

            var text = new TextBlock
            {
                Text = content?.ToString() ?? string.Empty,
                FontFamily = _comboBox.FontFamily,
                FontSize = _comboBox.FontSize,
                FontStyle = _comboBox.FontStyle,
                FontWeight = _comboBox.FontWeight,
                FontStretch = _comboBox.FontStretch
            };
            text.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            return text.DesiredSize.Width;
        }
    }
}
