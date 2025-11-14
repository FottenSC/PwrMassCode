using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Community.PowerToys.Run.Plugin.PwrMassCode;

internal sealed class FlexibleBoolConverter : JsonConverter<bool>
{
    public override bool Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.True:
                return true;
            case JsonTokenType.False:
                return false;
            case JsonTokenType.Number:
                if (reader.TryGetInt64(out var n))
                {
                    return n != 0;
                }
                break;
            case JsonTokenType.String:
                var s = reader.GetString();
                if (string.Equals(s, "true", StringComparison.OrdinalIgnoreCase)) return true;
                if (string.Equals(s, "false", StringComparison.OrdinalIgnoreCase)) return false;
                if (long.TryParse(s, out var v)) return v != 0;
                break;
        }
        throw new JsonException($"Cannot convert token '{reader.TokenType}' to bool.");
    }

    public override void Write(Utf8JsonWriter writer, bool value, JsonSerializerOptions options)
    {
        writer.WriteBooleanValue(value);
    }
}
