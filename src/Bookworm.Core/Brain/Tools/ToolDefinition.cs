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
/// One tool per VA operation the Brain can invoke, plus two for cross-session memory. Kept 1:1 with
/// <see cref="Bookworm.Core.Library.IVaLibraryClient"/>'s surface so <see cref="ToolCallExecutor"/> can
/// dispatch by name without a separate mapping layer to keep in sync.
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
            Description = "Add a title to the bookshelf (borrows it — consumes one of 20 loan slots for books/music, no limit for periodicals). Do this immediately when asked; no confirmation needed for adding.",
            InputSchema = new
            {
                type = "object",
                properties = new
                {
                    bookshareId = StringProp("The catalog id from a search result."),
                    format = StringProp("A formatId from the search result's formats list, e.g. DAISY_Audio_Human."),
                    type = new { type = "string", @enum = new[] { "book", "music", "periodical" }, description = "Defaults to book." },
                },
                required = new[] { "bookshareId", "format" },
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
            Description = "Get a summary of frequently-seen authors and titles from the current bookshelf and loan history — use this to ground recommendations and to recognize what's already been read or is already on loan, so it isn't re-suggested.",
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
    ];
}
