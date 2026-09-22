using System.Text.Json.Serialization;

namespace Bookworm.Core.Brain;

public sealed class BrainMessage
{
    [JsonPropertyName("role")]
    public required string Role { get; init; } // "user" | "assistant"

    [JsonPropertyName("content")]
    public required List<ContentBlock> Content { get; init; }

    public static BrainMessage User(string text) => new() { Role = "user", Content = [ContentBlock.OfText(text)] };
    public static BrainMessage User(List<ContentBlock> content) => new() { Role = "user", Content = content };
    public static BrainMessage Assistant(List<ContentBlock> content) => new() { Role = "assistant", Content = content };
}

/// <summary>The running conversation state for one session: system prompt (built once, includes the
/// reader profile and remembered preferences) plus the growing message history.</summary>
public sealed class ConversationContext
{
    public required string SystemPrompt { get; init; }
    public List<BrainMessage> Messages { get; } = [];
}
