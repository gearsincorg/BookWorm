namespace Bookworm.Core.Memory;

/// <summary>
/// One book's life in Bookworm's own tracking — added when add_to_bookshelf succeeds, DateRemoved
/// filled in when remove_from_bookshelf succeeds for the same title. This is Bookworm's own persisted
/// record, independent of VA's live bookshelf/history (which only shows what's currently on loan, and
/// whose "My History" page isn't machine-readable — see docs/va-endpoints.md) — it survives the book
/// being removed from the actual bookshelf, which is the point: rating and searching past reads.
/// </summary>
public sealed class ReadingHistoryEntry
{
    public required string Title { get; set; }
    public string? Author { get; set; }
    public string? BookshareId { get; set; }
    public DateTimeOffset DateAdded { get; set; }
    public DateTimeOffset? DateRemoved { get; set; }

    /// <summary>1-5, or null if not yet rated.</summary>
    public int? Rating { get; set; }
}

/// <summary>An author the user has been asked about (see the persona's rule to ask once per new author
/// encountered via add_to_bookshelf) — IsFavorite records their answer, not an inference.</summary>
public sealed class AuthorPreference
{
    public required string AuthorName { get; set; }
    public bool IsFavorite { get; set; }
    public DateTimeOffset DateAdded { get; set; }
}

/// <summary>
/// The qualitative context a conversation builds up that VA's own account doesn't capture — see
/// docs/decisions.md's Cross-session memory section. ExplicitPreferences/ConversationNotes stay
/// free-text since the Brain decides what's worth remembering there; ReadingHistory/PreferredAuthors/
/// PreferredGenres are structured because they're populated by specific, repeatable app behaviors
/// (auto-logged on add/remove, or asked about explicitly) rather than free-form model judgment.
/// </summary>
public sealed class BookwormMemory
{
    public List<string> ExplicitPreferences { get; set; } = [];
    public List<string> ConversationNotes { get; set; } = [];
    public string? LastSessionSummary { get; set; }

    public List<ReadingHistoryEntry> ReadingHistory { get; set; } = [];
    public List<AuthorPreference> PreferredAuthors { get; set; } = [];

    /// <summary>Genres the user has explicitly agreed to add — never auto-populated from bookshelf
    /// contents alone; see the persona's rule to ask before adding one.</summary>
    public List<string> PreferredGenres { get; set; } = [];
}
