using System.ComponentModel;
using System.Globalization;

namespace BookStitch.Models;

public sealed class DiscCountInputState : INotifyPropertyChanged
{
    private string _text;
    public DiscCountInputState(int initialCount, int minimum, int maximum)
    {
        Minimum = Math.Clamp(minimum, 1, 99);
        Maximum = Math.Clamp(maximum, Minimum, 99);
        _text = Math.Clamp(initialCount, Minimum, Maximum).ToString(CultureInfo.InvariantCulture);
    }
    public int Minimum { get; }
    public int Maximum { get; }
    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action<int>? ValueChanged;
    public string Text
    {
        get => _text;
        set
        {
            var next = value ?? string.Empty;
            if (_text == next) return;
            var wasValid = TryGetValue(out var previous);
            _text = next;
            PropertyChanged?.Invoke(this, new(nameof(Text)));
            if (TryGetValue(out var current) && (!wasValid || current != previous))
                ValueChanged?.Invoke(current);
        }
    }
    public bool TryGetValue(out int value) =>
        int.TryParse(Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value) &&
        value >= Minimum && value <= Maximum;
    public bool RequiresStartConfirmation => TryGetValue(out var value) && (value == 1 || value >= 13);
    public bool AcceptsInput(string proposed) => proposed.Length <= 2 && proposed.All(char.IsDigit) &&
        (!int.TryParse(proposed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) || value <= Maximum);
    public void Step(int delta)
    {
        if (!int.TryParse(Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            value = Minimum;
        Text = Math.Clamp(value + delta, Minimum, Maximum).ToString(CultureInfo.InvariantCulture);
    }
}
