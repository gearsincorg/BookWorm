using System.Text.Json;
using Bookworm.Core.Brain;
using Bookworm.Core.Brain.Discovery;
using Bookworm.Core.Brain.Tools;
using Bookworm.Core.Library;
using Bookworm.Core.Memory;

namespace Bookworm.Core.Orchestration;

/// <summary>
/// The turn loop: user transcript in -> Brain -> execute any tool calls -> feed results back -> repeat
/// until a final spoken-ready text response. See docs/decisions.md's Brain design section for the full
/// rationale behind the persona rules baked into <see cref="PersonaPrompt"/>.
/// </summary>
public sealed class LibrarianOrchestrator(IBrain brain, ToolCallExecutor toolExecutor, IVaLibraryClient client, IMemoryStore memoryStore)
{
    private const int MaxToolRoundTrips = 5;

    private const string PersonaPrompt = """
        You are Bookworm, a reader's-advisory librarian for a vision-impaired Vision Australia Library
        member. Every response you give is read aloud by text-to-speech with no visual fallback — the
        user cannot scan a list, so how you present things matters as much as what you say.

        Hard rules:
        1. Never enumerate a raw result list. When a search or bookshelf/history tool returns many hits,
           cluster and summarize by author, series, or theme in 1-3 sentences, then offer a small number
           of next steps. Never read out every title in a long list.
        2. Progressive narrowing. Treat ambiguous or broad requests as the start of a dialogue — ask one
           clarifying question at a time rather than dumping options.
        3. Use the reading profile (get_reading_profile) as context for taste, not just record-keeping.
           Ground "what should I read next" style requests in it, and recognize titles already on the
           bookshelf or in history so you don't re-suggest them.
        4. Compensate for the catalogue's narrow search. search_library only matches title, author, or
           series — never subject or theme. For "something like X" or genre-based discovery, first think
           of specific candidate titles or authors yourself using your own knowledge, then call
           search_library to check real availability.
        5. Act without confirmation for additive actions (add_to_bookshelf, add_to_request_list,
           subscribe_to_periodical) — just do it and report the result. Confirm only before removal
           (remove_from_bookshelf): describe what will be removed and wait for an explicit yes in the
           next turn before calling the tool.
        6. Support categorization requests (e.g. "what have I been reading lately?") by summarizing the
           reading profile conversationally, not by listing individual titles.
        7. No proactive narration. Never volunteer status, counts, or summaries unless the user asked for
           them in this turn.
        8. Keep responses short, concrete, and free of anything that only makes sense visually — no
           "click here", no bullet points, no "see below".
        9. Use remember_preference whenever the user states a preference outside a normal search request
           (favorite genres/authors, formats, things to avoid), and recall_preferences when it would help
           answer the current request.
        """;

    private ConversationContext? _context;
    private BookwormMemory _memory = new();
    private string? _memoryEtag;

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        var loaded = await memoryStore.LoadAsync(ct);
        _memory = loaded.Memory;
        _memoryEtag = loaded.ETag;

        var shelf = await client.GetBookshelfAsync(ct);
        var history = await client.GetHistoryAsync(ct);
        var profile = ReaderProfile.Summarize(shelf, history);

        _context = new ConversationContext { SystemPrompt = BuildSystemPrompt(profile, _memory) };
    }

    private static string BuildSystemPrompt(object profile, BookwormMemory memory)
    {
        var profileJson = JsonSerializer.Serialize(profile);
        var memoryJson = JsonSerializer.Serialize(new { memory.ExplicitPreferences, memory.ConversationNotes, memory.LastSessionSummary });
        return $"""
            {PersonaPrompt}

            Current reading profile (bookshelf + history snapshot at session start):
            {profileJson}

            Remembered from previous sessions:
            {memoryJson}
            """;
    }

    public async Task<string> ProcessUserUtteranceAsync(string transcript, CancellationToken ct = default)
    {
        if (_context is null)
        {
            throw new InvalidOperationException("Call InitializeAsync before processing an utterance.");
        }

        _context.Messages.Add(BrainMessage.User(transcript));

        for (var round = 0; round < MaxToolRoundTrips; round++)
        {
            var result = await brain.RespondAsync(_context, ToolDefinitions.All, ct);
            _context.Messages.Add(BrainMessage.Assistant(result.Content));

            var toolUses = result.ToolUses.ToList();
            if (toolUses.Count == 0)
            {
                return result.TextOnly;
            }

            var toolResults = new List<ContentBlock>();
            foreach (var call in toolUses)
            {
                toolResults.Add(await toolExecutor.ExecuteAsync(call, _memory, ct));
            }
            _context.Messages.Add(BrainMessage.User(toolResults));
        }

        return "Sorry, that turned into more steps than I can handle in one go — could you try asking again, maybe more specifically?";
    }

    public Task SaveMemoryAsync(CancellationToken ct = default) => memoryStore.SaveAsync(_memory, _memoryEtag, ct);
}
