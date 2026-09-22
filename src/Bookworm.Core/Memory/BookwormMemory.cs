namespace Bookworm.Core.Memory;

/// <summary>
/// The qualitative context a conversation builds up that VA's own account doesn't capture — see
/// docs/decisions.md's Cross-session memory section. Deliberately just a few free-text lists rather
/// than a rigid schema, since the Brain (not app code) decides what's worth remembering.
/// </summary>
public sealed class BookwormMemory
{
    public List<string> ExplicitPreferences { get; set; } = [];
    public List<string> ConversationNotes { get; set; } = [];
    public string? LastSessionSummary { get; set; }
}
