using Bookworm.Core.Brain.Tools;

namespace Bookworm.Core.Brain;

/// <summary>
/// The seam that lets the rest of the app be built and tested before real Claude API access exists —
/// see <see cref="StubBrain"/> for a canned implementation and <see cref="ClaudeBrain"/> for the real one.
/// </summary>
public interface IBrain
{
    Task<BrainTurnResult> RespondAsync(ConversationContext context, IReadOnlyList<ToolDefinition> tools, CancellationToken ct = default);
}
