using System.Text.Json;
using System.Text.Json.Serialization;

namespace Bookworm.Core.Brain;

/// <summary>
/// One content block within a message, matching Claude's Messages API wire format directly (field names
/// and all) rather than modeling a clean polymorphic hierarchy — this is an internal wire DTO, not a
/// public library API, and the flat shape makes ClaudeBrain's (de)serialization trivial to get right.
/// </summary>
public sealed class ContentBlock
{
    [JsonPropertyName("type")]
    public required string Type { get; init; } // "text" | "tool_use" | "tool_result" | "thinking"

    [JsonPropertyName("text")]
    public string? Text { get; init; }

    [JsonPropertyName("thinking")]
    public string? Thinking { get; init; } // thinking — must be echoed back verbatim, including signature

    [JsonPropertyName("signature")]
    public string? Signature { get; init; } // thinking

    [JsonPropertyName("id")]
    public string? Id { get; init; } // tool_use

    [JsonPropertyName("name")]
    public string? Name { get; init; } // tool_use

    [JsonPropertyName("input")]
    public JsonElement? Input { get; init; } // tool_use

    [JsonPropertyName("tool_use_id")]
    public string? ToolUseId { get; init; } // tool_result

    [JsonPropertyName("content")]
    public string? Content { get; init; } // tool_result

    [JsonPropertyName("is_error")]
    public bool? IsError { get; init; } // tool_result

    public static ContentBlock OfText(string text) => new() { Type = "text", Text = text };

    public static ContentBlock OfToolResult(string toolUseId, string content, bool isError = false) => new()
    {
        Type = "tool_result",
        ToolUseId = toolUseId,
        Content = content,
        IsError = isError,
    };
}
