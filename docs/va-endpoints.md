# Vision Australia Library — Portal Endpoints

Reference for `Bookworm.Core/Library/VaLibraryClient.cs`. Confirmed by live reconnaissance against the real portal (my.visionaustralia.org, a Drupal site with a custom `library_features_dodp` module — DODP = DAISY Online Delivery Protocol). No public/official API exists; these are the JSON endpoints the portal's own frontend JS calls, under session-cookie auth. Treat this client as a polite single human user: custom User-Agent, rate-limited, no aggressive polling.

Status key: **Confirmed live** = verified end-to-end against the real account. **Confirmed via JS source** = exact request shape traced through the frontend JS but not yet exercised live. **Best-effort** = inferred from naming/module conventions, not verified either way.

## Authentication — Confirmed live

Traced exactly through `modules/custom/dodp_auth/js/login.js`. Not a simple POST — a three-step handshake:

1. `GET /library/login` — establishes a session cookie and returns HTML containing a CSRF token: `<input type="hidden" name="csrf-token" value="...">`.
2. `POST /dodp-auth/api/authenticate` — JSON body `{"email": base64(emailOrVaid), "password": base64(password)}` (base64 is obfuscation over HTTPS, not real encryption — mirrors the client JS's `btoa()` calls), with header `X-CSRF-Token: <token from step 1>`. Response JSON has `errorCode`/`errorMessage` on failure (empty/absent on success). A `honeypot_time`/honeypot-field pair exists in the JS for Drupal's Honeypot anti-spam module, but the live login form doesn't render those inputs, so the payload omits them — if VA ever adds them to the form, watch for 403s here.
3. `GET /dodp-auth/api/authorize` — completes the handshake; the real portal session is only established after this succeeds. Skipping this step leaves you "authenticated" per step 2's response but without a working session.

There's also an empty, unused Google reCAPTCHA block on the login form (`#block-googlerecaptchablock`) — no token for it appears anywhere in the submitted payload, so it isn't currently gating this endpoint.

`VaSessionManager` treats any non-JSON response (i.e., a redirect back to the HTML login page) from any other endpoint as session expiry and re-runs this whole handshake once before failing.

## Search — Confirmed live

- `POST /library/quick-search`, **form-urlencoded** (not JSON) body: `keyword`, `type` (one of `Book`, `Picture Book`, `Magazine`, `Newspaper`, `Podcast`, `Music` — exact dropdown values), `limit` (10), `format` (a formatId like `DAISY_Audio_Human`, or empty for "All formats"). Returns JSON: `bookTab`/`periodicalTab`/`musicTab`, each with `total` and `listArticle[]` (title, authors[], formats[], size — string for books, **numeric 0 for periodicals**, bookshareId, activeTitleId, status.key e.g. `READY_FOR_DOWNLOAD`, description).
- `/library/search` — advanced search with filters (format, language, title, author, categories). Request shape not traced.
- `/library/detail/{id}` — single item detail page. Not traced.

**Important limitation**: keyword search only matches **title, author, or series title** — never subject/synopsis. Thematic discovery ("something like X") cannot be done via VA search alone; the Brain must propose candidates from its own knowledge and check availability here.

## Bookshelf (loan cap: 20 books/music braille combined; no cap on periodicals, max 3 issues/periodical)

- `GET /library/my-library/my-bookshelf?tabChild=tab-book&limit=20&currentPage=1&sortOrder=dateAdded&direction=desc` — **Confirmed live**. JSON: `books[]`, `musics[]`, `periodicals[]` (each item: `activeTitleId`, `title`, `size`, `format`, `book: {authorBy, title, bookshareId}` or `periodical: {seriesId, publicationDate, title}`, `dateAdded`, `status.key`), `totalBookAndMusicBraille`, `totalPeriodical`.
- `GET /library/my-bookshelf/add/{bookshareId}/{format}?type={book|music|periodical}` — **Confirmed via JS source** (`library-renderer.js` → `renderAddToBookshelfUrl`). Periodicals add `&isPeriodical=1&periodical_id={id}` before `&type=`.
- `GET /library/my-bookshelf/remove/{type}/{activeTitleId}` — **Confirmed via JS source only** (`renderRemoveBookshelfUrl`) — note the type segment comes *before* the id, unlike add. Not click-verified: the live "My Bookshelf" UI actually uses a checkbox + "Remove Selected" bulk action whose exact call wasn't traced, so this single-item URL may be dead code or used only in another view (e.g. periodical issue removal). **Verify with a real (low-stakes, e.g. periodical) removal before relying on this.**
- **Still not found**: the zip-download URL. The button is real (`library-renderer.js` renders `<button id="download_{seriesId}_..." class="item-download" data-series-id="...">`), so it's a JS click handler reading `data-series-id`, but that handler wasn't located in `library.js`, `library-tabs.js`, `library-renderer.js`, or `library-portal.js`. Next step: capture the actual network request by clicking a real Download button in the browser while watching Network — direct JS-source hunting hit diminishing returns here.

## Request list (no loan cap; can auto-promote to bookshelf when space frees up) — Confirmed live (container shape)

- `GET /library/request-list?sortOrder=dateAdded&direction=asc&limit=10&currentPage=1` — field is `requestList` (not `books`), plus `totalRequestList`. Confirmed live, but the account tested had 0 items, so per-item field names are still best-effort (same shape assumed as bookshelf items).
- `POST /library/request-list/add/{bookshareId}` — **Confirmed via JS source** (`add-to-request-list.js`), empty body.
- Remove-from-request-list endpoint not yet captured.

## Subscriptions (periodicals; max 3 issues/periodical kept on bookshelf) — Confirmed live (container shape)

- `GET /library/my-library/subscription?limit=10&currentPage=1` — fields `subscriptions` (⚠️ **empty string `""`, not `[]`, when there are none** — `VaLibraryClient` handles this via `FlexibleListConverter<T>`) and `totalSubscription`. Confirmed live; account tested had 0 subscriptions, so per-item shape is still best-effort.
- `POST /library/my-library/subscription/add/{bookshareId}` — **Best-effort**. The URL-building function (`renderAddSubscriptionUrl`) only returns the bare `/library/my-library/subscription/add/` endpoint in the JS source; whatever appends the id wasn't traced. Verify before relying on it.
- Unsubscribe endpoint not yet captured.

## History — Confirmed live (negative result: no JSON endpoint)

- `/library/my-history` returns full server-rendered HTML (Drupal page), and pagination (`my-history.js`) POSTs to a URL read from a hidden input and splices the returned **HTML fragment** into `#history_content` — there is no JSON data endpoint here at all, unlike every other tab. A real implementation needs HTML parsing (e.g. AngleSharp) once the actual row markup is inspected — the test account had 20/20 recently-added loans and possibly zero history, so the row markup itself hasn't been seen yet either. `VaLibraryClient.GetHistoryAsync` is currently a stub returning `[]`.

## Preferences — Not implemented

- `/library-user/my-preferences/...` — format/narrator/language defaults, auto-send settings, category/author include-exclude lists (Self-Managed vs. Automatic member modes). `GetPreferencesAsync` currently returns placeholder defaults, not real data.

## Quirks to remember when extending this client

- VA's JSON is inconsistently typed across otherwise-similar fields: `size` is a string for books but a bare number for periodicals; `subscriptions` is `""` instead of `[]` when empty. Don't assume a field's JSON type without checking — prefer the tolerant converters (`FlexibleStringConverter`, `FlexibleListConverter<T>`) over adding new strict-typed fields.
- Two different IDs matter per item: `bookshareId` (the catalog id, stable, used for search/add/request-list operations) and `activeTitleId` (the loan-instance id, only meaningful once something is actually on the bookshelf/request list, used for remove operations).
- Debug tip: `Bookworm.Console raw <relativeUrl>` prints the raw response body for a GET against an authenticated session — the fastest way to check a real shape without going back to the browser.

## Open follow-ups

1. Find the real download endpoint (see Bookshelf section above).
2. Confirm request-list-remove and subscription-unsubscribe endpoints.
3. Verify `remove` against a real (low-stakes) item — never click-tested.
4. Confirm whether `add`/`remove`/`subscribe` GET/POST endpoints need any CSRF token beyond the session cookie — they don't appear to (unlike login), but this is inferred from working results, not explicitly confirmed from source.
5. Confirm session cookie max-age/expiry so `VaSessionManager`'s re-auth trigger is tuned correctly (untested — re-auth-on-expiry logic exists but hasn't actually been triggered by a real expiry yet).
6. Implement real History HTML parsing once an account with actual history entries is available to inspect.
7. Verify per-item field shapes for request-list and subscriptions once the account has real items in either.
