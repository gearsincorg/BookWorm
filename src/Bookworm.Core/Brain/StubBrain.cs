using Bookworm.Core.Brain.Tools;

namespace Bookworm.Core.Brain;

/// <summary>
/// Lets everything except the real conversational reasoning be built and tested without an Anthropic
/// API key: enqueue scripted responses, or supply a responder function, to exercise the orchestrator's
/// tool-call loop deterministically (see docs/decisions.md Phase 3 and Bookworm.Core.Tests).
/// </summary>
public sealed class StubBrain : IBrain
{
    private readonly Queue<BrainTurnResult> _scripted = new();
    private readonly Func<ConversationContext, BrainTurnResult>? _responder;

    public StubBrain()
    {
    }

    public StubBrain(Func<ConversationContext, BrainTurnResult> responder)
    {
        _responder = responder;
    }

    public void Enqueue(BrainTurnResult result) => _scripted.Enqueue(result);

    public void EnqueueText(string text) => Enqueue(new BrainTurnResult { Content = [ContentBlock.OfText(text)] });

    public void EnqueueToolUse(string id, string name, System.Text.Json.JsonElement input) =>
        Enqueue(new BrainTurnResult { Content = [new ContentBlock { Type = "tool_use", Id = id, Name = name, Input = input }] });

    public Task<BrainTurnResult> RespondAsync(ConversationContext context, IReadOnlyList<ToolDefinition> tools, CancellationToken ct = default)
    {
        if (_scripted.Count > 0)
        {
            return Task.FromResult(_scripted.Dequeue());
        }
        if (_responder is not null)
        {
            return Task.FromResult(_responder(context));
        }
        return Task.FromResult(new BrainTurnResult { Content = [ContentBlock.OfText("(stub brain: no scripted response configured)")] });
    }
}
