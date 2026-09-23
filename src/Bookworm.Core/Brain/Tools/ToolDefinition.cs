using System.Text.Json.Serialization;

namespace Bookworm.Core.Brain.Tools;

public sealed class ToolDefinition
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("description")]
    public required string Description { get; init; }

    [JsonPropertyName("input_schema")]
    public required object InputSchema { get; init; }
}

/// <summary>
/// One tool per VA operation the Brain can invoke, plus tools for cross-session memory: free-text
/// preferences/notes, a persisted reading history with ratings (independent of VA's own bookshelf/
/// history — see ReadingHistoryEntry), and explicitly user-confirmed preferred authors/genres. Kept 1:1
/// with <see cref="Bookworm.Core.Library.IVaLibraryClient"/>'s surface (plus the memory extensions) so
/// <see cref="ToolCallExecutor"/> can dispatch by name without a separate mapping layer to keep in sync.
/// </summary>
public static class ToolDefinitions
{
    private static object StringProp(string description) => new { type = "string", description };

    public static readonly IReadOnlyList<ToolDefinition> All =
    [
        new ToolDefinition
        {
            Name = "search_library",
            Description = "Search the Vision Australia Library catalogue by keyword. Only matches title, author, or series title — never subject or synopsis. For thematic/discovery requests, propose specific candidate titles or authors yourself first (using your own book knowledge), then call this to check real availability.",
            InputSchema = new
            {
                type = "object",
                properties = new
                {
                    keyword = StringProp("Search keywords (title, author, or series)."),
                    type = new { type = "string", @enum = new[] { "Book", "Picture Book", "Magazine", "Newspaper", "Podcast", "Music" }, description = "Item type to search. Defaults to Book." },
                },
                required = new[] { "keyword" },
            },
        },
        new ToolDefinition
        {
            Name = "get_bookshelf",
            Description = "Get the current bookshelf: everything on loan (books, music, periodicals), including how many of the 20 book/music loan slots are used.",
            InputSchema = new { type = "object", properties = new { } },
        },
        new ToolDefinition
        {
            Name = "add_to_bookshelf",
            Description = "Add a title to the bookshelf (borrows it — consumes one of 20 loan slots for books/music, no limit for periodicals). Do this immediately when asked; no confirmation needed for adding. The result tells you whether the author is new to the preferred-authors list and what genres are already saved — see the persona rules on what to ask afterward.",
            InputSchema = new
            {
                type = "object",
                properties = new
                {
                    bookshareId = StringProp("The catalog id from a search result."),
                    format = StringProp("A formatId from the search result's formats list, e.g. DAISY_Audio_Human."),
                    title = StringProp("The title, from the search result — used to log this to reading history."),
                    author = StringProp("The author, from the search result, if known — used for reading history and the preferred-authors check."),
                    type = new { type = "string", @enum = new[] { "book", "music", "periodical" }, description = "Defaults to book." },
                },
                required = new[] { "bookshareId", "format", "title" },
            },
        },
        new ToolDefinition
        {
            Name = "remove_from_bookshelf",
            Description = "Remove a title from the bookshelf, freeing a loan slot. This is destructive — always describe what will be removed and get an explicit yes from the user in a prior turn before calling this.",
            InputSchema = new
            {
                type = "object",
                properties = new
                {
                    activeTitleId = StringProp("The loan-instance id from get_bookshelf's results (not the bookshareId)."),
                    bookshareId = StringProp("The catalog id from get_bookshelf's results, if shown — used to mark the matching reading-history entry as removed. Omit if not available."),
                    type = new { type = "string", @enum = new[] { "book", "music", "periodical" }, description = "Defaults to book." },
                },
                required = new[] { "activeTitleId" },
            },
        },
        new ToolDefinition
        {
            Name = "get_request_list",
            Description = "Get the request list — titles saved to read later, which can auto-promote to the bookshelf when a loan slot frees up.",
            InputSchema = new { type = "object", properties = new { } },
        },
        new ToolDefinition
        {
            Name = "add_to_request_list",
            Description = "Add a title to the request list (no loan cap). Do this immediately when asked; no confirmation needed.",
            InputSchema = new
            {
                type = "object",
                properties = new { bookshareId = StringProp("The catalog id from a search result.") },
                required = new[] { "bookshareId" },
            },
        },
        new ToolDefinition
        {
            Name = "get_subscriptions",
            Description = "Get the list of periodicals (newspapers/magazines/podcasts) currently subscribed to.",
            InputSchema = new { type = "object", properties = new { } },
        },
        new ToolDefinition
        {
            Name = "subscribe_to_periodical",
            Description = "Subscribe to a periodical so new issues automatically appear on the bookshelf. Do this immediately when asked; no confirmation needed.",
            InputSchema = new
            {
                type = "object",
                properties = new { bookshareId = StringProp("The catalog id from a search result.") },
                required = new[] { "bookshareId" },
            },
        },
        new ToolDefinition
        {
            Name = "get_reading_profile",
            Description = "Get a summary of frequently-seen authors and titles from the CURRENT bookshelf and VA's own loan history — use this to ground recommendations and to recognize what's already been read or is already on loan, so it isn't re-suggested. For titles no longer on the bookshelf, or to check/search everything Bookworm has ever tracked (including ratings), use search_reading_history instead.",
            InputSchema = new { type = "object", properties = new { } },
        },
        new ToolDefinition
        {
            Name = "remember_preference",
            Description = "Save a short note about something the user has told you they like, dislike, or are looking for, so it's remembered in future sessions. Use this whenever the user states a preference outside a normal VA setting.",
            InputSchema = new
            {
                type = "object",
                properties = new { note = StringProp("A short, self-contained note, e.g. 'prefers DAISY Audio (Human) over synthetic' or 'not keen on graphic violence'.") },
                required = new[] { "note" },
            },
        },
        new ToolDefinition
        {
            Name = "recall_preferences",
            Description = "Get everything remembered about the user's stated preferences and ongoing interests from past sessions.",
            InputSchema = new { type = "object", properties = new { } },
        },
        new ToolDefinition
        {
            Name = "search_reading_history",
            Description = "Search Bookworm's own persisted record of every title ever added to the bookshelf (independent of what's currently on loan — includes titles since removed), with when it was added/removed and any rating. Use this to check whether a specific book has been read before, or to look up past reads.",
            InputSchema = new
            {
                type = "object",
                properties = new { query = StringProp("Title or author text to search for (partial match).") },
                required = new[] { "query" },
            },
        },
        new ToolDefinition
        {
            Name = "rate_book",
            Description = "Set a 1-5 star rating on a title in the reading history. Can be done at any time, not just right after finishing — search_reading_history first if you need to find the exact title.",
            InputSchema = new
            {
                type = "object",
                properties = new
                {
                    title = StringProp("The title to rate — matched against reading history (partial match ok if unambiguous)."),
                    rating = new { type = "integer", minimum = 1, maximum = 5, description = "1 to 5 stars." },
                },
                required = new[] { "title", "rating" },
            },
        },
        new ToolDefinition
        {
            Name = "get_preferred_authors",
            Description = "Get the list of authors the user has previously been asked about, including which ones they said are favorites.",
            InputSchema = new { type = "object", properties = new { } },
        },
        new ToolDefinition
        {
            Name = "add_preferred_author",
            Description = "Record an author as known/preferred, with whether the user said they're a favorite. Only call this after actually asking the user — see the persona rule about asking once per new author encountered via add_to_bookshelf.",
            InputSchema = new
            {
                type = "object",
                properties = new
                {
                    authorName = StringProp("The author's name, matching how it appears in search results."),
                    isFavorite = new { type = "boolean", description = "Whether the user said this is a favorite author." },
                },
                required = new[] { "authorName", "isFavorite" },
            },
        },
        new ToolDefinition
        {
            Name = "get_preferred_genres",
            Description = "Get the list of genres the user has explicitly agreed to add as a preference (never auto-populated from bookshelf contents alone).",
            InputSchema = new { type = "object", properties = new { } },
        },
        new ToolDefinition
        {
            Name = "add_preferred_genre",
            Description = "Record a genre as preferred. Only call this after actually asking the user — see the persona rule about proposing a genre when a book is added.",
            InputSchema = new
            {
                type = "object",
                properties = new { genre = StringProp("A short genre label, e.g. 'Historical Fiction' or 'Ancient History'.") },
                required = new[] { "genre" },
            },
        },
    ];
}
