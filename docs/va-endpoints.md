# Vision Australia Library — Portal Endpoints

Reference for `Bookworm.Core/Library/VaLibraryClient.cs`. Confirmed by live reconnaissance against the real portal (my.visionaustralia.org, a Drupal site with a custom `library_features_dodp` module — DODP = DAISY Online Delivery Protocol). No public/official API exists; these are the JSON endpoints the portal's own frontend JS calls, under session-cookie auth. Treat this client as a polite single human user: custom User-Agent, rate-limited, no aggressive polling.

## Authentication

- `POST /dodp-auth/api/authenticate` — login with VA email/VAID + password. Establishes a session cookie. Exact cookie lifetime not yet confirmed — `VaSessionManager` should treat any response that looks like the Drupal login page (redirect to `/user/login`, or HTML where JSON was expected) as session expiry and re-authenticate once before failing.

## Search

- `POST /library/quick-search` — keyword search. Returns JSON: `bookTab`/`periodicalTab`/`musicTab`, each with `total` and `listArticle[]` (title, authors, formats, size, bookshareId, activeTitleId, status.key e.g. `READY_FOR_DOWNLOAD`, description).
- `/library/search` — advanced search with filters (format, language, title, author, categories).
- `/library/detail/{id}` — single item detail page.

**Important limitation**: keyword search only matches **title, author, or series title** — never subject/synopsis. Thematic discovery ("something like X") cannot be done via VA search alone.

## Bookshelf (loan cap: 20 books/music braille combined; no cap on periodicals, max 3 issues/periodical)

- `GET /library/my-library/my-bookshelf?tabChild=tab-book&limit=20&currentPage=1&sortOrder=dateAdded&direction=desc` — JSON: `books[]`, `musics[]`, `periodicals[]`, `totalBookAndMusicBraille` (current count vs. 20 cap).
- `GET /library/my-bookshelf/add/{id}?format=DAISY_Audio_Human` — add to bookshelf (consumes a loan slot).
- `GET /library/my-bookshelf/remove/{id}` — remove from bookshelf.
- **Not yet found**: the actual zip-download URL for a bookshelf item. Likely pattern-consistent with add/remove (e.g. `/library/my-bookshelf/download/{id}`) — confirm during Phase 1 implementation (open the real portal, click Download on a bookshelf item, capture the request).

## Request list (no loan cap; can auto-promote to bookshelf when space frees up)

- `GET /library/request-list?sortOrder=dateAdded&direction=asc&limit=10&currentPage=1` — JSON list.
- `/library/request-list/add/{id}` — add to request list.
- Remove-from-request-list endpoint not yet captured — confirm during Phase 1.

## Subscriptions (periodicals; max 3 issues/periodical kept on bookshelf)

- `GET /library/my-library/subscription?limit=10&currentPage=1` — JSON list.
- `/library/my-library/subscription/add/{id}` — subscribe.
- Unsubscribe endpoint not yet captured — confirm during Phase 1.

## History

- `/library/my-history` — loan history. Did not show a separate JSON XHR during recon (likely server-rendered HTML) — needs an HTML-parsing fallback, or further recon to find a JSON endpoint if one exists.

## Preferences

- `/library-user/my-preferences/...` — format/narrator/language defaults, auto-send settings, category/author include-exclude lists (Self-Managed vs. Automatic member modes).

## Open follow-ups for Phase 1

1. Confirm the bookshelf item download URL.
2. Confirm request-list-remove and subscription-unsubscribe endpoints.
3. Confirm whether `add`/`remove` GET endpoints require any CSRF token/header beyond the session cookie (unusual for Drupal to leave state-changing requests unprotected — verify rather than assume).
4. Confirm session cookie max-age/expiry so `VaSessionManager`'s re-auth trigger is tuned correctly.
5. Determine whether `/library/my-history` has a JSON endpoint or genuinely requires HTML parsing.
