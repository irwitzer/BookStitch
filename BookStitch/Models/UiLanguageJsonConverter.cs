using System.Text.Json;
using System.Text.Json.Serialization;

namespace BookStitch.Models;

public sealed class UiLanguageJsonConverter : JsonConverter<string>
{
    public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
            return reader.GetString() ?? UiLanguageIds.System;

        using var ignored = JsonDocument.ParseValue(ref reader);
        return UiLanguageIds.System;
    }

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value);
}
