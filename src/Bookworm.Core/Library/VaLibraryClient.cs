using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Bookworm.Core.Auth;
using Bookworm.Core.Library.Exceptions;
using Bookworm.Core.Library.Models;

namespace Bookworm.Core.Library;

/// <summary>
/// Raw HTTP client for the VA portal. Talks session-cookie auth and JSON, exactly mirroring the
/// portal's own frontend JS (see docs/va-endpoints.md). Throws <see cref="VaSessionExpiredException"/>
/// when the session looks dead — callers should use <see cref="Bookworm.Core.Auth.VaSessionManager"/>,
/// which catches that and re-authenticates, rather than calling this directly.
/// </summary>
public sealed partial class VaLibraryClient : IVaLibraryClient, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly CookieContainer _cookies = new();

    public VaLibraryClient(VaLibraryClientOptions? options = null)
    {
        options ??= new VaLibraryClientOptions();
        var handler = new HttpClientHandler
        {
            CookieContainer = _cookies,
            UseCookies = true,
            AllowAutoRedirect = true,
        };
        _http = new HttpClient(handler)
        {
            BaseAddress = new Uri(options.BaseUrl),
            Timeout = options.RequestTimeout,
        };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(options.UserAgent);
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/json, text/html;q=0.8, */*;q=0.5");
    }

    public void Dispose() => _http.Dispose();

    /// <summary>Debug escape hatch for exploring/verifying endpoints against the live account — not part
    /// of <see cref="IVaLibraryClient"/>. Assumes the session is already authenticated.</summary>
    public Task<string> GetRawAsync(string relativeUrl, CancellationToken ct = default) =>
        _http.GetStringAsync(relativeUrl, ct);

    [GeneratedRegex("""name=["']csrf-token["']\s+value=["']([^"']+)["']""")]
    private static partial Regex CsrfTokenRegex();

    private static string ToBase64(string s) => Convert.ToBase64String(Encoding.UTF8.GetBytes(s));

    private static string TypeSegment(LibraryItemType type) => type switch
    {
        LibraryItemType.Book => "book",
        LibraryItemType.Music => "music",
        LibraryItemType.Periodical => "periodical",
        _ => "book",
    };

    // ---- Authentication ----
    // Mirrors modules/custom/dodp_auth/js/login.js exactly: fetch a CSRF token from the login page,
    // POST base64-"obfuscated" (not encrypted) credentials as JSON with that token as a header, then
    // complete the handshake with a GET to /dodp-auth/api/authorize — only after that is the portal
    // session actually established.
    public async Task AuthenticateAsync(LibraryCredentials credentials, CancellationToken ct = default)
    {
        var loginPageHtml = await _http.GetStringAsync("/library/login", ct);
        var match = CsrfTokenRegex().Match(loginPageHtml);
        if (!match.Success)
        {
            throw new VaAuthenticationException(
                "Could not find the CSRF token on the VA login page — its markup may have changed.");
        }
        var csrfToken = match.Groups[1].Value;

        var payload = new Dictionary<string, string>
        {
            ["email"] = ToBase64(credentials.EmailOrVaid),
            ["password"] = ToBase64(credentials.Password),
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, "/dodp-auth/api/authenticate")
        {
            Content = JsonContent.Create(payload),
        };
        request.Headers.Add("X-CSRF-Token", csrfToken);

        using var response = await _http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<AuthenticateResponse>(JsonOptions, ct);

        if (result is { ErrorMessage: "PasswordMustBeSet" })
        {
            throw new VaAuthenticationException(
                "This VA account needs a password set via the website before it can log in here.");
        }
        if (!string.IsNullOrEmpty(result?.ErrorCode) || !string.IsNullOrEmpty(result?.ErrorMessage))
        {
            throw new VaAuthenticationException(result?.ErrorMessage ?? "VA login failed for an unspecified reason.");
        }

        using var authorizeResponse = await _http.GetAsync("/dodp-auth/api/authorize", ct);
        authorizeResponse.EnsureSuccessStatusCode();
    }

    private sealed class AuthenticateResponse
    {
        [JsonPropertyName("errorCode")] public string? ErrorCode { get; set; }
        [JsonPropertyName("errorMessage")] public string? ErrorMessage { get; set; }
        [JsonPropertyName("redirectUrl")] public string? RedirectUrl { get; set; }
    }

    // ---- Shared response handling ----

    private static async Task<T> ReadJsonOrThrowAsync<T>(HttpResponseMessage response, CancellationToken ct)
    {
        var contentType = response.Content.Headers.ContentType?.MediaType;
        if (contentType is null || !contentType.Contains("json"))
        {
            // The portal redirects an expired session to the HTML login page instead of returning JSON.
            throw new VaSessionExpiredException();
        }
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new VaRequestFailedException($"VA request failed with HTTP {(int)response.StatusCode}.", (int)response.StatusCode, body);
        }
        var result = await response.Content.ReadFromJsonAsync<T>(JsonOptions, ct);
        return result ?? throw new VaRequestFailedException("VA returned an empty response where JSON was expected.");
    }

    // ---- Search ----
    // POST /library/quick-search, form-urlencoded {keyword, type, limit, format} — mirrors
    // library-search.js's `search.quickSearch`.
    public async Task<SearchResults> SearchAsync(SearchQuery query, CancellationToken ct = default)
    {
        var form = new Dictionary<string, string>
        {
            ["keyword"] = query.Keyword,
            ["type"] = query.Type,
            // The portal's own UI only ever asks for 10 (its quick-search box has no page control), but
            // the endpoint honors a much higher limit fine (confirmed live up to 100, no server-side cap
            // observed) — 50 is a deliberate choice, not the server's ceiling: high enough that the vast
            // majority of real searches come back complete in one call, while keeping the JSON handed to
            // Claude bounded rather than uncapped. True pagination (a "next page" tool) isn't implemented
            // — for anything larger, the Brain sees the real `total` (via ResultSummarizer) and is
            // expected to narrow the conversation rather than try to enumerate everything.
            ["limit"] = "50",
            ["format"] = query.Format ?? "",
        };
        using var content = new FormUrlEncodedContent(form);
        using var response = await _http.PostAsync("/library/quick-search", content, ct);
        var envelope = await ReadJsonOrThrowAsync<QuickSearchEnvelope>(response, ct);

        return new SearchResults
        {
            Books = SearchTabResult.From(envelope.Data?.BookTab),
            Periodicals = SearchTabResult.From(envelope.Data?.PeriodicalTab),
            Music = SearchTabResult.From(envelope.Data?.MusicTab),
        };
    }

    // ---- Bookshelf ----

    public async Task<BookshelfSnapshot> GetBookshelfAsync(CancellationToken ct = default)
    {
        const string url = "/library/my-library/my-bookshelf?tabChild=tab-book&limit=20&currentPage=1&sortOrder=dateAdded&direction=desc";
        using var response = await _http.GetAsync(url, ct);
        var envelope = await ReadJsonOrThrowAsync<BookshelfEnvelope>(response, ct);
        var data = envelope.Data ?? new BookshelfData();

        return new BookshelfSnapshot
        {
            TotalBookAndMusicBraille = data.TotalBookAndMusicBraille,
            Books = data.Books,
            Music = data.Musics,
            TotalPeriodical = data.TotalPeriodical,
            Periodicals = data.Periodicals,
        };
    }

    // GET /library/my-bookshelf/add/{bookshareId}/{format}?type={type} — mirrors
    // library-renderer.js's `renderAddToBookshelfUrl`.
    public async Task AddToBookshelfAsync(string bookshareId, string format, LibraryItemType type = LibraryItemType.Book, CancellationToken ct = default)
    {
        var url = $"/library/my-bookshelf/add/{Uri.EscapeDataString(bookshareId)}/{Uri.EscapeDataString(format)}?type={TypeSegment(type)}";
        using var response = await _http.GetAsync(url, ct);
        await ReadJsonOrThrowAsync<JsonDocument>(response, ct);
    }

    // GET /library/my-bookshelf/remove/{type}/{activeTitleId} — mirrors library-renderer.js's
    // `renderRemoveBookshelfUrl`. Not yet click-verified against the live portal (see docs/va-endpoints.md).
    public async Task RemoveFromBookshelfAsync(string activeTitleId, LibraryItemType type = LibraryItemType.Book, CancellationToken ct = default)
    {
        var url = $"/library/my-bookshelf/remove/{TypeSegment(type)}/{Uri.EscapeDataString(activeTitleId)}";
        using var response = await _http.GetAsync(url, ct);
        await ReadJsonOrThrowAsync<JsonDocument>(response, ct);
    }

    // ---- Request list ----

    public async Task<IReadOnlyList<RequestListItem>> GetRequestListAsync(CancellationToken ct = default)
    {
        const string url = "/library/request-list?sortOrder=dateAdded&direction=asc&limit=10&currentPage=1";
        using var response = await _http.GetAsync(url, ct);
        var envelope = await ReadJsonOrThrowAsync<RequestListEnvelope>(response, ct);
        return envelope.Data?.RequestList ?? [];
    }

    // POST /library/request-list/add/{bookshareId} — mirrors add-to-request-list.js exactly.
    public async Task AddToRequestListAsync(string bookshareId, CancellationToken ct = default)
    {
        using var response = await _http.PostAsync($"/library/request-list/add/{Uri.EscapeDataString(bookshareId)}", content: null, ct);
        await ReadJsonOrThrowAsync<JsonDocument>(response, ct);
    }

    // ---- Subscriptions ----

    public async Task<IReadOnlyList<Subscription>> GetSubscriptionsAsync(CancellationToken ct = default)
    {
        const string url = "/library/my-library/subscription?limit=10&currentPage=1";
        using var response = await _http.GetAsync(url, ct);
        var envelope = await ReadJsonOrThrowAsync<SubscriptionEnvelope>(response, ct);
        return envelope.Data?.Subscriptions ?? [];
    }

    // Best-effort — the exact call for subscribing was not fully traced through the frontend JS.
    // Confirm and adjust against the live account during Phase 1 testing (see docs/va-endpoints.md).
    public async Task SubscribeAsync(string bookshareId, CancellationToken ct = default)
    {
        using var response = await _http.PostAsync($"/library/my-library/subscription/add/{Uri.EscapeDataString(bookshareId)}", content: null, ct);
        await ReadJsonOrThrowAsync<JsonDocument>(response, ct);
    }

    // ---- History ----
    // /library/my-history renders server-side HTML with no observed JSON XHR — best-effort empty
    // implementation for now; needs real HTML-parsing once the actual markup is inspected (Phase 1).
    public Task<IReadOnlyList<HistoryEntry>> GetHistoryAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<HistoryEntry>>([]);

    // ---- Preferences ----
    // Not yet implemented against a confirmed endpoint/shape — placeholder defaults.
    public Task<LibraryPreferences> GetPreferencesAsync(CancellationToken ct = default) =>
        Task.FromResult(new LibraryPreferences());

    // ---- Download ----
    // Endpoint not yet found — see docs/va-endpoints.md open follow-ups.
    public Task<Stream> DownloadItemAsync(string activeTitleId, CancellationToken ct = default) =>
        throw new NotSupportedException(
            "The bookshelf item download endpoint hasn't been confirmed yet — see docs/va-endpoints.md.");
}
