using System.Text.Json;
using System.Text.Json.Serialization;

namespace BookStitch.Services;

public static class SampleRateSettingsService
{
    public static int Normalize(int value) => value == 48000 ? 48000 : 44100;
}

// A malformed new preference must not discard the user's otherwise valid settings.
public sealed class DefaultSampleRateJsonConverter : JsonConverter<int>
{
    public override int Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
    {
        using var value = JsonDocument.ParseValue(ref reader);
        var text = value.RootElement.ToString();
        return int.TryParse(text, out var rate) ? SampleRateSettingsService.Normalize(rate) : 44100;
    }

    public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options) =>
        writer.WriteNumberValue(SampleRateSettingsService.Normalize(value));
}
