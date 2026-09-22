using System.Text.Json;
using System.Text.Json.Serialization;

namespace Bookworm.Core.Library.Models;

/// <summary>
/// VA's API is inconsistent about "size": books send it as a string ("298.50 MB"), periodicals send it
/// as a bare JSON number (0). This accepts either and normalizes to a string.
/// </summary>
public sealed class FlexibleStringConverter : JsonConverter<string?>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType switch
        {
            JsonTokenType.String => reader.GetString(),
            JsonTokenType.Number => reader.GetDouble().ToString(System.Globalization.CultureInfo.InvariantCulture),
            JsonTokenType.Null => null,
            _ => null,
        };

    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value);
}

/// <summary>
/// VA's API sends an empty string ("") instead of an empty array for some list fields (observed on
/// subscriptions) — this tolerates that and returns an empty list rather than throwing.
/// </summary>
public sealed class FlexibleListConverter<T> : JsonConverter<List<T>?>
{
    public override List<T>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.StartArray)
        {
            return JsonSerializer.Deserialize<List<T>>(ref reader, options);
        }
        reader.Skip();
        return [];
    }

    public override void Write(Utf8JsonWriter writer, List<T>? value, JsonSerializerOptions options) =>
        JsonSerializer.Serialize(writer, value, options);
}

public sealed class LibraryFormat
{
    [JsonPropertyName("formatId")]
    public string FormatId { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";
}

public sealed class LibraryAuthor
{
    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = "";
}

public sealed class ItemStatus
{
    [JsonPropertyName("key")]
    public string Key { get; set; } = "";

    public bool IsReadyForDownload => Key == "READY_FOR_DOWNLOAD";
}
