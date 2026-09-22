namespace Bookworm.Core.Brain;

public sealed class BrainTurnResult
{
    public required List<ContentBlock> Content { get; init; }

    public IEnumerable<ContentBlock> ToolUses => Content.Where(c => c.Type == "tool_use");

    public string TextOnly => string.Concat(Content.Where(c => c.Type == "text").Select(c => c.Text));
}
