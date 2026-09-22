using Bookworm.Core.Auth;
using Bookworm.Core.Library.Models;

namespace Bookworm.Core.Library;

/// <summary>
/// Client for the Vision Australia Library portal (my.visionaustralia.org). There is no official/public
/// API — this calls the same session-cookie-authenticated JSON endpoints the portal's own frontend uses.
/// See docs/va-endpoints.md for the confirmed endpoint map.
/// </summary>
public interface IVaLibraryClient
{
    Task AuthenticateAsync(LibraryCredentials credentials, CancellationToken ct = default);

    Task<SearchResults> SearchAsync(SearchQuery query, CancellationToken ct = default);

    Task<BookshelfSnapshot> GetBookshelfAsync(CancellationToken ct = default);

    /// <param name="bookshareId">The catalog id (from search results), not an activeTitleId.</param>
    Task AddToBookshelfAsync(string bookshareId, string format, LibraryItemType type = LibraryItemType.Book, CancellationToken ct = default);

    /// <param name="activeTitleId">The loan-instance id (from <see cref="BookshelfItem.ActiveTitleId"/>).</param>
    Task RemoveFromBookshelfAsync(string activeTitleId, LibraryItemType type = LibraryItemType.Book, CancellationToken ct = default);

    Task<IReadOnlyList<RequestListItem>> GetRequestListAsync(CancellationToken ct = default);

    Task AddToRequestListAsync(string bookshareId, CancellationToken ct = default);

    Task<IReadOnlyList<Subscription>> GetSubscriptionsAsync(CancellationToken ct = default);

    Task SubscribeAsync(string bookshareId, CancellationToken ct = default);

    Task<IReadOnlyList<HistoryEntry>> GetHistoryAsync(CancellationToken ct = default);

    Task<LibraryPreferences> GetPreferencesAsync(CancellationToken ct = default);

    /// <summary>Not yet confirmed against the live portal — see docs/va-endpoints.md open follow-ups.</summary>
    Task<Stream> DownloadItemAsync(string activeTitleId, CancellationToken ct = default);
}
