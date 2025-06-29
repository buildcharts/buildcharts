using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BuildCharts.Tool.Docker.Json;

public sealed class RestRawDictConverter : JsonConverter<Dictionary<string, string>>
{
    public override Dictionary<string, string> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions opts)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (reader.TokenType != JsonTokenType.StartArray)
        {
            throw new JsonException("RestRaw must be an array");
        }

        reader.Read();

        while (reader.TokenType != JsonTokenType.EndArray)
        {
            using JsonDocument doc = JsonDocument.ParseValue(ref reader);
            var obj = doc.RootElement;

            var name = obj.GetProperty("Name").GetString() ?? "";
            var value = obj.GetProperty("Value").GetString() ?? "";

            dict[name] = value;
            reader.Read();
        }

        return dict;
    }

    public override void Write(Utf8JsonWriter writer, Dictionary<string, string> value, JsonSerializerOptions opts)
    {
        writer.WriteStartArray();

        foreach (var kv in value)
        {
            writer.WriteStartObject();
            writer.WriteString("Name", kv.Key);
            writer.WriteString("Value", kv.Value);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }
}