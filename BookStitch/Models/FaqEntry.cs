using System.Text.Json.Serialization;

namespace BookStitch.Models;

public sealed record FaqEntry(string Id, string Question, IReadOnlyList<FaqSection> Sections, FaqChatContent? Chat = null, FaqSidebarImage? SidebarImage = null, FaqNarrationContent? Narration = null);

public sealed record FaqSidebarImage(string Source, double FocusX = 0.5, double FocusY = 0.5, double Zoom = 1);

// Add a subtype and its WPF DataTemplate when a new content kind is needed.
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(FaqParagraph), "paragraph")]
[JsonDerivedType(typeof(FaqHeading), "heading")]
[JsonDerivedType(typeof(FaqImage), "image")]
public abstract record FaqSection(string Text);
public sealed record FaqParagraph(string Text) : FaqSection(Text);
public sealed record FaqHeading(string Text) : FaqSection(Text);

public sealed record FaqImage(string Text, string Source, double MaxWidth = double.PositiveInfinity, bool MatchQuestionWidth = false, double CornerRadius = 0) : FaqSection(Text);

public sealed record FaqChatContent(IReadOnlyList<FaqChatTurn> Turns);
public sealed record FaqChatTurn(string Human, string Assistant, string? Image = null);

public abstract class FaqChatItem { }
public sealed class FaqChatText(string text, bool isHuman) : FaqChatItem, System.ComponentModel.INotifyPropertyChanged
{
    public string Text { get; } = text;
    public bool IsHuman { get; } = isHuman;
    private string _displayText = text;
    public string DisplayText
    {
        get => _displayText;
        internal set
        {
            if (_displayText == value) return;
            _displayText = value;
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(DisplayText)));
        }
    }
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
}
public sealed class FaqChatImage(string source, string description) : FaqChatItem
{
    public string Source { get; } = source;
    public string Description { get; } = description;
}
public sealed class FaqChatGenerating : FaqChatItem { }

public sealed record FaqNarrationContent(string Audio, string TimingResource);
