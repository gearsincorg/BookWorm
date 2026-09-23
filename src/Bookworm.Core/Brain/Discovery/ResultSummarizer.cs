using Bookworm.Core.Library.Models;

namespace Bookworm.Core.Brain.Discovery;

/// <summary>
/// Pre-clusters raw search hits by author before they ever reach the Brain, so the system prompt's
/// "never enumerate a raw list" rule is easy to follow — the model is handed an already-grouped view,
/// not dozens of flat items to summarize itself. See docs/decisions.md's Brain design section.
/// </summary>
public static class ResultSummarizer
{
    public static object Summarize(SearchResults results) => new
    {
        books = SummarizeTab(results.Books),
        periodicals = SummarizeTab(results.Periodicals),
        music = SummarizeTab(results.Music),
    };

    private static object SummarizeTab(SearchTabResult tab) => new
    {
        total = tab.Total,
        shown = tab.Items.Count,
        // Explicit rather than left for the model to notice by comparing total vs shown — the catalogue
        // has no further-pages tool, so when this is true the right move is to narrow the conversation
        // (author/series/era), not to imply the results below are the complete set.
        moreResultsExist = tab.Items.Count < tab.Total,
        byAuthor = tab.Items
            .GroupBy(i => string.IsNullOrWhiteSpace(i.AuthorNames) ? "(unknown author)" : i.AuthorNames)
            .Select(g => new
            {
                author = g.Key,
                count = g.Count(),
                titles = g.Select(i => new
                {
                    i.Title,
                    i.BookshareId,
                    formats = i.Formats.Where(f => !string.IsNullOrEmpty(f.FormatId)).Select(f => f.FormatId),
                    status = i.Status.Key,
                    i.Size,
                }),
            }),
    };
}
