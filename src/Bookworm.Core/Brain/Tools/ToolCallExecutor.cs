using System.Text.Json;
using Bookworm.Core.Brain.Discovery;
using Bookworm.Core.Library;
using Bookworm.Core.Library.Exceptions;
using Bookworm.Core.Library.Models;
using Bookworm.Core.Memory;

namespace Bookworm.Core.Brain.Tools;

/// <summary>
/// Dispatches a tool_use block to the matching <see cref="IVaLibraryClient"/> operation (or a memory
/// operation), returning a tool_result content block. Enforces the 20-item loan cap client-side (so the
/// Brain finds out from a normal tool result, not a failed VA call) and, in dry-run mode, logs intended
/// loan-consuming/state-changing calls instead of executing them — see docs/decisions.md Phase 3.
///
/// add_to_bookshelf/remove_from_bookshelf also maintain BookwormMemory's own persisted reading history
/// (independent of VA's bookshelf/history) and surface fresh authorAlreadyInPreferredAuthors/
/// currentPreferredGenres signals in their results, rather than relying on the model to remember to
/// check a possibly-stale system-prompt snapshot from session start — see docs/decisions.md.
/// </summary>
public sealed class ToolCallExecutor(IVaLibraryClient client, bool dryRun = false)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<ContentBlock> ExecuteAsync(ContentBlock toolUse, BookwormMemory memory, CancellationToken ct = default)
    {
        try
        {
            var input = toolUse.Input ?? default;
            string resultJson = toolUse.Name switch
            {
                "search_library" => await SearchAsync(input, ct),
                "get_bookshelf" => await GetBookshelfAsync(ct),
                "add_to_bookshelf" => await AddToBookshelfAsync(input, memory, ct),
                "remove_from_bookshelf" => await RemoveFromBookshelfAsync(input, memory, ct),
                "get_request_list" => await GetRequestListAsync(ct),
                "add_to_request_list" => await AddToRequestListAsync(input, ct),
                "get_subscriptions" => await GetSubscriptionsAsync(ct),
                "subscribe_to_periodical" => await SubscribeAsync(input, ct),
                "get_reading_profile" => await GetReadingProfileAsync(ct),
                "remember_preference" => RememberPreference(input, memory),
                "recall_preferences" => RecallPreferences(memory),
                "search_reading_history" => SearchReadingHistory(input, memory),
                "rate_book" => RateBook(input, memory),
                "get_preferred_authors" => GetPreferredAuthors(memory),
                "add_preferred_author" => AddPreferredAuthor(input, memory),
                "get_preferred_genres" => GetPreferredGenres(memory),
                "add_preferred_genre" => AddPreferredGenre(input, memory),
                _ => throw new InvalidOperationException($"Unknown tool: {toolUse.Name}"),
            };
            return ContentBlock.OfToolResult(toolUse.Id!, resultJson);
        }
        catch (Exception ex)
        {
            return ContentBlock.OfToolResult(toolUse.Id!, ex.Message, isError: true);
        }
    }

    private static string Json(object value) => JsonSerializer.Serialize(value, JsonOptions);

    private static string GetString(JsonElement input, string name) =>
        input.TryGetProperty(name, out var v) ? v.GetString() ?? "" : throw new ArgumentException($"Missing required argument: {name}");

    private static string? GetOptionalString(JsonElement input, string name) =>
        input.TryGetProperty(name, out var v) ? v.GetString() : null;

    private static int GetInt(JsonElement input, string name) =>
        input.TryGetProperty(name, out var v) ? v.GetInt32() : throw new ArgumentException($"Missing required argument: {name}");

    private static LibraryItemType ParseType(string? value) => value?.ToLowerInvariant() switch
    {
        "music" => LibraryItemType.Music,
        "periodical" => LibraryItemType.Periodical,
        _ => LibraryItemType.Book,
    };

    private async Task<string> SearchAsync(JsonElement input, CancellationToken ct)
    {
        var query = new SearchQuery
        {
            Keyword = GetString(input, "keyword"),
            Type = GetOptionalString(input, "type") ?? VaSearchType.Book,
            Format = GetOptionalString(input, "format"),
        };
        var results = await client.SearchAsync(query, ct);
        return Json(ResultSummarizer.Summarize(results));
    }

    private async Task<string> GetBookshelfAsync(CancellationToken ct)
    {
        var shelf = await client.GetBookshelfAsync(ct);
        return Json(shelf);
    }

    private async Task<string> AddToBookshelfAsync(JsonElement input, BookwormMemory memory, CancellationToken ct)
    {
        var bookshareId = GetString(input, "bookshareId");
        var format = GetString(input, "format");
        var title = GetString(input, "title");
        var author = GetOptionalString(input, "author");
        var type = ParseType(GetOptionalString(input, "type"));

        var shelf = await client.GetBookshelfAsync(ct);
        if (type != LibraryItemType.Periodical && !shelf.HasLoanSlotAvailable)
        {
            throw new VaLoanCapExceededException(shelf.TotalBookAndMusicBraille, BookshelfSnapshot.LoanCap);
        }

        // Read-only, safe to compute regardless of dry-run — without this, dry-run couldn't be used to
        // test the ask-about-author/genre conversational flow at all, only the real add path could.
        var authorKnown = author is not null
            && memory.PreferredAuthors.Any(a => string.Equals(a.AuthorName, author, StringComparison.OrdinalIgnoreCase));

        if (dryRun)
        {
            return Json(new
            {
                dryRun = true,
                action = "add_to_bookshelf",
                bookshareId,
                format,
                title,
                author,
                authorAlreadyInPreferredAuthors = authorKnown,
                currentPreferredGenres = memory.PreferredGenres,
            });
        }

        await client.AddToBookshelfAsync(bookshareId, format, type, ct);

        memory.ReadingHistory.Add(new ReadingHistoryEntry
        {
            Title = title,
            Author = author,
            BookshareId = bookshareId,
            DateAdded = DateTimeOffset.UtcNow,
        });

        return Json(new
        {
            success = true,
            bookshareId,
            title,
            author,
            loggedToReadingHistory = true,
            authorAlreadyInPreferredAuthors = authorKnown,
            currentPreferredGenres = memory.PreferredGenres,
        });
    }

    private async Task<string> RemoveFromBookshelfAsync(JsonElement input, BookwormMemory memory, CancellationToken ct)
    {
        var activeTitleId = GetString(input, "activeTitleId");
        var bookshareId = GetOptionalString(input, "bookshareId");
        var type = ParseType(GetOptionalString(input, "type"));

        if (dryRun)
        {
            return Json(new { dryRun = true, action = "remove_from_bookshelf", activeTitleId, bookshareId, type = type.ToString() });
        }

        await client.RemoveFromBookshelfAsync(activeTitleId, type, ct);

        var markedRemoved = false;
        if (bookshareId is not null)
        {
            var entry = memory.ReadingHistory.LastOrDefault(e => e.BookshareId == bookshareId && e.DateRemoved is null);
            if (entry is not null)
            {
                entry.DateRemoved = DateTimeOffset.UtcNow;
                markedRemoved = true;
            }
        }

        return Json(new { success = true, activeTitleId, readingHistoryUpdated = markedRemoved });
    }

    private async Task<string> GetRequestListAsync(CancellationToken ct)
    {
        var items = await client.GetRequestListAsync(ct);
        return Json(items);
    }

    private async Task<string> AddToRequestListAsync(JsonElement input, CancellationToken ct)
    {
        var bookshareId = GetString(input, "bookshareId");
        if (dryRun)
        {
            return Json(new { dryRun = true, action = "add_to_request_list", bookshareId });
        }
        await client.AddToRequestListAsync(bookshareId, ct);
        return Json(new { success = true, bookshareId });
    }

    private async Task<string> GetSubscriptionsAsync(CancellationToken ct)
    {
        var subs = await client.GetSubscriptionsAsync(ct);
        return Json(subs);
    }

    private async Task<string> SubscribeAsync(JsonElement input, CancellationToken ct)
    {
        var bookshareId = GetString(input, "bookshareId");
        if (dryRun)
        {
            return Json(new { dryRun = true, action = "subscribe_to_periodical", bookshareId });
        }
        await client.SubscribeAsync(bookshareId, ct);
        return Json(new { success = true, bookshareId });
    }

    private async Task<string> GetReadingProfileAsync(CancellationToken ct)
    {
        var shelf = await client.GetBookshelfAsync(ct);
        var history = await client.GetHistoryAsync(ct);
        return Json(ReaderProfile.Summarize(shelf, history));
    }

    private static string RememberPreference(JsonElement input, BookwormMemory memory)
    {
        var note = GetString(input, "note");
        memory.ExplicitPreferences.Add(note);
        return Json(new { remembered = note });
    }

    private static string RecallPreferences(BookwormMemory memory) => Json(new
    {
        preferences = memory.ExplicitPreferences,
        notes = memory.ConversationNotes,
        lastSession = memory.LastSessionSummary,
    });

    private static string SearchReadingHistory(JsonElement input, BookwormMemory memory)
    {
        var query = GetString(input, "query");
        var matches = memory.ReadingHistory
            .Where(e =>
                (e.Title?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (e.Author?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false))
            .OrderByDescending(e => e.DateAdded)
            .ToList();
        return Json(new { query, totalEntriesTracked = memory.ReadingHistory.Count, matches });
    }

    private static string RateBook(JsonElement input, BookwormMemory memory)
    {
        var title = GetString(input, "title");
        var rating = GetInt(input, "rating");
        if (rating is < 1 or > 5)
        {
            throw new ArgumentException("rating must be between 1 and 5.");
        }

        var candidates = memory.ReadingHistory
            .Where(e => e.Title.Contains(title, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(e => e.DateAdded)
            .ToList();

        if (candidates.Count == 0)
        {
            throw new InvalidOperationException($"No reading history entry matching '{title}' — try search_reading_history first to find the exact title.");
        }
        if (candidates.Count > 1 && candidates.Select(c => c.Title).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
        {
            throw new InvalidOperationException($"'{title}' matches more than one title in reading history — ask which one, or use the exact title.");
        }

        candidates[0].Rating = rating;
        return Json(new { rated = candidates[0].Title, rating });
    }

    private static string GetPreferredAuthors(BookwormMemory memory) => Json(new { authors = memory.PreferredAuthors });

    private static string AddPreferredAuthor(JsonElement input, BookwormMemory memory)
    {
        var authorName = GetString(input, "authorName");
        var isFavorite = input.TryGetProperty("isFavorite", out var v) && v.GetBoolean();

        var existing = memory.PreferredAuthors.FirstOrDefault(a => string.Equals(a.AuthorName, authorName, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            existing.IsFavorite = isFavorite;
        }
        else
        {
            memory.PreferredAuthors.Add(new AuthorPreference { AuthorName = authorName, IsFavorite = isFavorite, DateAdded = DateTimeOffset.UtcNow });
        }
        return Json(new { authorName, isFavorite });
    }

    private static string GetPreferredGenres(BookwormMemory memory) => Json(new { genres = memory.PreferredGenres });

    private static string AddPreferredGenre(JsonElement input, BookwormMemory memory)
    {
        var genre = GetString(input, "genre");
        if (!memory.PreferredGenres.Any(g => string.Equals(g, genre, StringComparison.OrdinalIgnoreCase)))
        {
            memory.PreferredGenres.Add(genre);
        }
        return Json(new { genre, preferredGenres = memory.PreferredGenres });
    }
}
