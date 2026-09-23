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
        4a. search_library results include moreResultsExist — when true, there is no way to fetch
           further pages, so never imply what's shown is the complete set. Mention there are more and
           offer to narrow (by series, era, format, etc.) rather than guessing which of the unseen ones
           the user meant.
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
        10. After a successful add_to_bookshelf, check its result for two things and ask about both
           together in one natural follow-up (don't interrupt with two separate questions):
           a. If authorAlreadyInPreferredAuthors is false, ask whether this author should be added as a
              known/preferred author, and whether they're a favorite — then call add_preferred_author
              with the answer. Only ask once per author; never ask again once they're already recorded.
           b. Using your own knowledge of the title/author, identify the likely genre. If it is not
              already in the result's currentPreferredGenres, ask if they'd like it added — then call
              add_preferred_genre only if they agree. Never add a genre without asking first.
        11. Use search_reading_history to check whether a specific title has been read/borrowed before
           (it covers everything ever added, not just what's currently on the bookshelf) and rate_book
           whenever the user wants to rate something — this works for titles no longer on the shelf too,
           at any time, not only right after finishing.
        """;

    private ConversationContext? _context;
    private BookwormMemory _memory = new();
    private string? _memoryEtag;
    private readonly SemaphoreSlim _turnGate = new(1, 1);

    internal int MessageCountForTests => _context?.Messages.Count ?? 0;

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
        // ReadingHistory deliberately excluded here — it can grow large over time and is searchable via
        // search_reading_history instead; PreferredAuthors/PreferredGenres stay small and are useful as
        // baseline context from turn one, though add_to_bookshelf's own result is still the authoritative,
        // live check within a session (this snapshot goes stale the moment either list changes).
        var memoryJson = JsonSerializer.Serialize(new
        {
            memory.ExplicitPreferences,
            memory.ConversationNotes,
            memory.LastSessionSummary,
            memory.PreferredAuthors,
            memory.PreferredGenres,
            readingHistoryEntryCount = memory.ReadingHistory.Count,
        });
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

        // Claude's API requires every tool_use to be followed immediately by its tool_result — if two
        // turns ran concurrently (e.g. the user spoke again before the previous reply finished) they'd
        // interleave messages and permanently corrupt that invariant for the rest of the session. This
        // gate serializes turns so a second call simply waits rather than racing the first.
        await _turnGate.WaitAsync(ct);
        try
        {
            var checkpoint = _context.Messages.Count;
            _context.Messages.Add(BrainMessage.User(transcript));

            try
            {
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
            catch
            {
                // Roll back this turn's partial history rather than leaving a dangling tool_use (or any
                // other malformed tail) that would break every subsequent turn for the rest of the session.
                _context.Messages.RemoveRange(checkpoint, _context.Messages.Count - checkpoint);
                throw;
            }
        }
        finally
        {
            _turnGate.Release();
        }
    }

    public async Task SaveMemoryAsync(CancellationToken ct = default)
    {
        // Every successful save changes the blob's ETag server-side — reusing the ETag from before this
        // save (or from the original Initialize load) on the *next* save always fails its precondition
        // (HTTP 412), even though nothing else touched the blob. This bug was real: it meant every
        // session after the first turn silently stopped persisting memory.
        _memoryEtag = await memoryStore.SaveAsync(_memory, _memoryEtag, ct);
    }
}
