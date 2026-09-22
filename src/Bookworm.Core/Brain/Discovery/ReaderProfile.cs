using Bookworm.Core.Library.Models;

namespace Bookworm.Core.Brain.Discovery;

/// <summary>
/// A lightweight, deterministic taste summary derived from the bookshelf and loan history — grounds
/// "what should I read next" and lets the Brain recognize already-read/already-owned titles rather than
/// re-suggesting them. Deliberately not genre/theme inference: that's left to the Brain's own book
/// knowledge (see docs/decisions.md — VA's catalog can't search by subject, so thematic reasoning has to
/// happen in the model, not here).
/// </summary>
public static class ReaderProfile
{
    public static object Summarize(BookshelfSnapshot bookshelf, IReadOnlyList<HistoryEntry> history)
    {
        var bookshelfTitles = bookshelf.Books.Select(b => new { b.Title, author = b.AuthorDisplay, source = "bookshelf" });
        var historyTitles = history.Select(h => new { h.Title, author = h.Author, source = "history" });
        var allTitles = bookshelfTitles.Concat(historyTitles).ToList();

        var topAuthors = allTitles
            .Select(t => t.author)
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .GroupBy(a => a)
            .OrderByDescending(g => g.Count())
            .Take(8)
            .Select(g => new { author = g.Key, count = g.Count() });

        return new
        {
            frequentAuthors = topAuthors,
            currentlyOnBookshelfOrHistory = allTitles,
            loanSlotsUsed = bookshelf.TotalBookAndMusicBraille,
            loanSlotsTotal = BookshelfSnapshot.LoanCap,
        };
    }
}
