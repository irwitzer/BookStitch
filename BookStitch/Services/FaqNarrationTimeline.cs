using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BookStitch.Services;

// The audio position is the only typing clock; pausing never accumulates timer drift.
public sealed class FaqNarrationTimeline
{
    public sealed record CharacterEvent([property: JsonPropertyName("t_ms")] int Milliseconds, int Offset);
    public string FullText { get; }
    public IReadOnlyList<CharacterEvent> Events { get; }

    public FaqNarrationTimeline(string text, IReadOnlyList<CharacterEvent> events)
    {
        FullText = text;
        var copy = events.ToArray();
        int time = -1, offset = 0;
        foreach (var item in copy)
        {
            if (item.Milliseconds < 0 || item.Milliseconds < time || item.Offset < offset || item.Offset > text.Length)
                throw new InvalidDataException("Invalid FAQ narration timing.");
            time = item.Milliseconds;
            offset = item.Offset;
        }
        if (copy.Length == 0 || offset != text.Length) throw new InvalidDataException("Incomplete FAQ narration timing.");
        Events = Array.AsReadOnly(copy);
    }

    public int VisibleLength(TimeSpan position)
    {
        int low = 0, high = Events.Count;
        while (low < high)
        {
            int middle = low + (high - low) / 2;
            if (Events[middle].Milliseconds <= position.TotalMilliseconds) low = middle + 1;
            else high = middle;
        }
        return low == 0 ? 0 : Events[low - 1].Offset;
    }

    public static FaqNarrationTimeline Load(string resourceName, string approvedText)
    {
        using var stream = typeof(FaqNarrationTimeline).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidDataException("FAQ narration timing resource missing.");
        using var json = JsonDocument.Parse(stream);
        var root = json.RootElement;
        var text = root.GetProperty("full_text").GetString()!;
        if (text != approvedText) throw new InvalidDataException("Narration timing does not match approved FAQ copy.");
        var events = root.GetProperty("character_events").Deserialize<CharacterEvent[]>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        return new FaqNarrationTimeline(text, events);
    }
}
