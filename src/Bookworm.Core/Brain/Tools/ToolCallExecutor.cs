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
                "add_to_bookshelf" => await AddToBookshelfAsync(input, ct),
                "remove_from_bookshelf" => await RemoveFromBookshelfAsync(input, ct),
                "get_request_list" => await GetRequestListAsync(ct),
                "add_to_request_list" => await AddToRequestListAsync(input, ct),
                "get_subscriptions" => await GetSubscriptionsAsync(ct),
                "subscribe_to_periodical" => await SubscribeAsync(input, ct),
                "get_reading_profile" => await GetReadingProfileAsync(ct),
                "remember_preference" => RememberPreference(input, memory),
                "recall_preferences" => RecallPreferences(memory),
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

    private async Task<string> AddToBookshelfAsync(JsonElement input, CancellationToken ct)
    {
        var bookshareId = GetString(input, "bookshareId");
        var format = GetString(input, "format");
        var type = ParseType(GetOptionalString(input, "type"));

        var shelf = await client.GetBookshelfAsync(ct);
        if (type != LibraryItemType.Periodical && !shelf.HasLoanSlotAvailable)
        {
            throw new VaLoanCapExceededException(shelf.TotalBookAndMusicBraille, BookshelfSnapshot.LoanCap);
        }

        if (dryRun)
        {
            return Json(new { dryRun = true, action = "add_to_bookshelf", bookshareId, format, type = type.ToString() });
        }

        await client.AddToBookshelfAsync(bookshareId, format, type, ct);
        return Json(new { success = true, bookshareId, format });
    }

    private async Task<string> RemoveFromBookshelfAsync(JsonElement input, CancellationToken ct)
    {
        var activeTitleId = GetString(input, "activeTitleId");
        var type = ParseType(GetOptionalString(input, "type"));

        if (dryRun)
        {
            return Json(new { dryRun = true, action = "remove_from_bookshelf", activeTitleId, type = type.ToString() });
        }

        await client.RemoveFromBookshelfAsync(activeTitleId, type, ct);
        return Json(new { success = true, activeTitleId });
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
}
