using Bookworm.Core.Library;
using Bookworm.Core.Library.Exceptions;
using Bookworm.Core.Library.Models;

namespace Bookworm.Core.Auth;

/// <summary>
/// Wraps a raw <see cref="IVaLibraryClient"/> with the two concerns that don't belong in the HTTP layer
/// itself: transparent re-authentication when the session expires, and pacing requests so an LLM-driven
/// caller that fires several tool calls in one turn doesn't hammer the portal. This is the type
/// everything else in the app should depend on, not <see cref="VaLibraryClient"/> directly.
/// </summary>
public sealed class VaSessionManager(IVaLibraryClient inner, ICredentialStore credentialStore) : IVaLibraryClient
{
    private static readonly TimeSpan MinInterval = TimeSpan.FromMilliseconds(300);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private DateTimeOffset _lastCallAt = DateTimeOffset.MinValue;
    private bool _authenticated;

    private async Task PaceAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var wait = MinInterval - (DateTimeOffset.UtcNow - _lastCallAt);
            if (wait > TimeSpan.Zero)
            {
                await Task.Delay(wait, ct);
            }
            _lastCallAt = DateTimeOffset.UtcNow;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task EnsureAuthenticatedAsync(CancellationToken ct)
    {
        if (_authenticated)
        {
            return;
        }

        var credentials = await LibraryCredentials.LoadAsync(credentialStore, ct)
            ?? throw new VaAuthenticationException(
                "No stored VA credentials. Call AuthenticateAsync once with real credentials first — they'll be saved securely for future sessions.");
        await inner.AuthenticateAsync(credentials, ct);
        _authenticated = true;
    }

    private async Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken ct)
    {
        await PaceAsync(ct);
        await EnsureAuthenticatedAsync(ct);
        try
        {
            return await action(ct);
        }
        catch (VaSessionExpiredException)
        {
            _authenticated = false;
            await EnsureAuthenticatedAsync(ct);
            return await action(ct);
        }
    }

    private Task RunAsync(Func<CancellationToken, Task> action, CancellationToken ct) =>
        RunAsync<object?>(async c => { await action(c); return null; }, ct);

    public async Task AuthenticateAsync(LibraryCredentials credentials, CancellationToken ct = default)
    {
        await PaceAsync(ct);
        await inner.AuthenticateAsync(credentials, ct);
        await credentials.SaveAsync(credentialStore, ct);
        _authenticated = true;
    }

    public Task<SearchResults> SearchAsync(SearchQuery query, CancellationToken ct = default) =>
        RunAsync(c => inner.SearchAsync(query, c), ct);

    public Task<BookshelfSnapshot> GetBookshelfAsync(CancellationToken ct = default) =>
        RunAsync(c => inner.GetBookshelfAsync(c), ct);

    public Task AddToBookshelfAsync(string bookshareId, string format, LibraryItemType type = LibraryItemType.Book, CancellationToken ct = default) =>
        RunAsync(c => inner.AddToBookshelfAsync(bookshareId, format, type, c), ct);

    public Task RemoveFromBookshelfAsync(string activeTitleId, LibraryItemType type = LibraryItemType.Book, CancellationToken ct = default) =>
        RunAsync(c => inner.RemoveFromBookshelfAsync(activeTitleId, type, c), ct);

    public Task<IReadOnlyList<RequestListItem>> GetRequestListAsync(CancellationToken ct = default) =>
        RunAsync(c => inner.GetRequestListAsync(c), ct);

    public Task AddToRequestListAsync(string bookshareId, CancellationToken ct = default) =>
        RunAsync(c => inner.AddToRequestListAsync(bookshareId, c), ct);

    public Task<IReadOnlyList<Subscription>> GetSubscriptionsAsync(CancellationToken ct = default) =>
        RunAsync(c => inner.GetSubscriptionsAsync(c), ct);

    public Task SubscribeAsync(string bookshareId, CancellationToken ct = default) =>
        RunAsync(c => inner.SubscribeAsync(bookshareId, c), ct);

    public Task<IReadOnlyList<HistoryEntry>> GetHistoryAsync(CancellationToken ct = default) =>
        RunAsync(c => inner.GetHistoryAsync(c), ct);

    public Task<LibraryPreferences> GetPreferencesAsync(CancellationToken ct = default) =>
        RunAsync(c => inner.GetPreferencesAsync(c), ct);

    public Task<Stream> DownloadItemAsync(string activeTitleId, CancellationToken ct = default) =>
        RunAsync(c => inner.DownloadItemAsync(activeTitleId, c), ct);
}
