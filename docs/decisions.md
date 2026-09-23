# Bookworm — Voice Librarian for Vision Australia Library

## Context

The user is a Vision Australia Library member who wants a voice-driven "librarian/front desk" assistant for their personal library account (my.visionaustralia.org). Vision Australia exposes no public API — access is via VA Connect (mobile), the My VA/i-Access web portal, or i-Access Kiosk (for dedicated DAISY hardware). This app automates the user's own account against the web portal.

The core purpose is **not** just executing commands (add/remove/search) — it's replicating what a good human librarian does: helping a vision-impaired member **find, categorize, and manage books and authors they know about, and discover ones they don't**, entirely through succinct spoken conversation. Because the user can't visually scan a list of search results, the assistant can never just "read out 19 results" — it has to cluster, summarize, and progressively narrow through dialogue, the way a librarian would talk through options with someone at a desk.

Confirmed via live reconnaissance (logged into the real portal as the user): the site is Drupal-based with a custom `library_features_dodp` module (DODP = DAISY Online Delivery Protocol). No public API, but the frontend calls clean session-cookie-authenticated JSON endpoints — so a lightweight HTTP+JSON client works, no headless browser automation needed.

No `.NET SDK` is currently installed on this machine — Phase 0 includes installing it.

## Confirmed VA portal endpoints (session-cookie auth)

- `POST /dodp-auth/api/authenticate` — login, establishes session cookie
- `POST /library/quick-search` — keyword search → JSON (title, author, format, size, bookshareId, activeTitleId, status)
- `/library/search` — advanced search (format, language, title, author, categories filters)
- `/library/detail/{id}` — item detail
- `GET /library/my-library/my-bookshelf?tabChild=tab-book&limit=20&currentPage=1&sortOrder=dateAdded&direction=desc` — bookshelf JSON (books[], musics[], periodicals[], loan count vs. 20 cap)
- `GET /library/my-bookshelf/add/{id}?format=DAISY_Audio_Human` — add to bookshelf (consumes a loan slot)
- `GET /library/my-bookshelf/remove/{id}` — remove from bookshelf
- `GET /library/request-list?sortOrder=dateAdded&direction=asc&limit=10&currentPage=1` — request list JSON
- `/library/request-list/add/{id}` — add to request list (no loan cap; can auto-promote to bookshelf)
- `GET /library/my-library/subscription?limit=10&currentPage=1` — periodical subscriptions JSON
- `/library/my-library/subscription/add/{id}` — subscribe to periodical
- `/library/my-history` — loan history (server-rendered HTML — needs HTML-parse fallback, or further recon)
- `/library-user/my-preferences/...` — preferences (format/narrator/language defaults, auto-send)
- **Not yet found**: the actual zip-download URL for a bookshelf item. Pattern is very likely consistent with add/remove (e.g. `/library/my-bookshelf/download/{id}`) — confirm with one more recon pass early in Phase 1, not a blocker for the plan.

**Important catalog limitation**: VA's own search only matches keywords against **title, author, or series title** — never subject/synopsis. This directly shapes the Brain design below: thematic/discovery requests ("something like the Mary Beard book I read") cannot be answered by the VA search alone — the assistant must use its own general book knowledge to propose candidate titles/authors, then check availability via `search_library`.

## Decisions already made

- Full account management scope: search, bookshelf, request list, subscriptions, history.
- Windows first; Android is a later phase (Phase 6), not simultaneous.
- Cloud-based LLM (Claude API) is fine — no offline/on-device requirement.
- Half-duplex speech: push-to-talk, turn-based. Not full-duplex/interruptible.
- **Primary interaction mode is conversational discovery, not just commands.** Direct commands ("add the new Bernard Cornwell to my bookshelf") must work, but the app's reason for existing is the reader's-advisory conversation: finding, categorizing, and managing both known and unknown books/authors — entirely through succinct spoken summaries, never visual lists.
- Anthropic API account/billing now set up (separate from Claude Code Pro). `ClaudeBrain` can be implemented and tested for real once Phase 3 is reached — no longer a blocker. The key will be stored via `ICredentialStore` (Windows Credential Manager), never in plaintext config or chat.
- **The app must remember library and user preferences across sessions**, not just within one conversation. This is data VA's own account doesn't capture (see Cross-session memory section below) — a small cloud-hosted store is needed so it also works from the future Android app. User can provide hosting; recommendation below (Azure Blob Storage) can be swapped if they'd rather use something else.
- No public API exists; this automates the user's own account. The client must behave like a polite single human user (custom User-Agent, rate-limited), not a scraper.

## Tech stack — recommendation

**.NET 10, WPF for Windows now, .NET MAUI for Android later, sharing a plain-C# core library.**

WPF over WinUI 3 specifically because screen-reader compatibility (JAWS/NVDA) is the dominant constraint here, and WPF's UI Automation support is far more battle-tested than WinUI 3's. The UI surface is intentionally tiny (push-to-talk button, transcript/status log, settings pane), so WinUI 3's newer visuals buy nothing while adding accessibility risk.

Rejected: Python+Kivy (no native accessibility tree — disqualifying for a screen-reader-dependent user), Electron+Node (heavier path to correct ARIA accessibility, no benefit over native .NET Credential Manager / speech API access).

WPF and MAUI don't share XAML — the Android port (Phase 6) is a real UI rewrite, but the **Core** logic (VA client, Claude orchestration, conversational/discovery logic) is fully shared.

## Solution structure

```
D:\Users\Phil\GitHub\Bookworm\
  Bookworm.sln
  src\
    Bookworm.Core\                      (net10.0, no platform deps)
      Library\
        IVaLibraryClient.cs / VaLibraryClient.cs / VaLibraryClientOptions.cs
        Models\ (LibraryItem, BookshelfSnapshot, SearchQuery/Result, RequestListItem,
                  Subscription, HistoryEntry, LibraryPreferences)
        Exceptions\ (VaAuthenticationException, VaSessionExpiredException,
                      VaLoanCapExceededException, VaRequestFailedException)
      Auth\
        ICredentialStore.cs / LibraryCredentials.cs / VaSessionManager.cs
        AzureSpeechCredentials.cs
      Brain\
        IBrain.cs / ClaudeBrain.cs / StubBrain.cs
        ConversationContext.cs / BrainTurnResult.cs
        Tools\ ToolDefinitions.cs / ToolCallExecutor.cs
        Discovery\
          ReaderProfile.cs           (derived taste summary from history+bookshelf)
          ResultSummarizer.cs        (clusters raw search hits into spoken-friendly summaries)
      Memory\
        IMemoryStore.cs / AzureBlobMemoryStore.cs
        BookwormMemory.cs           (explicit preferences, conversation notes, last-session summary)
      Orchestration\
        LibrarianOrchestrator.cs
      Speech\
        ISpeechRecognizer.cs / ISpeechSynthesizer.cs
        AzureSpeechRecognizer.cs    (the real STT backend — portable, so it lives in Core, not Platform)

    Bookworm.Windows.Platform\          (net10.0-windows10.0.19041.0 — versioned for WinRT projections)
      Credentials\WindowsCredentialManagerStore.cs
      Speech\SapiSpeechSynthesizer.cs           (the real TTS backend)
      Speech\SystemSpeechRecognizer.cs          (SAPI STT — rejected for accuracy, kept for reference)
      Speech\WinRtSpeechRecognizer.cs           (WinRT STT — also rejected for accuracy, kept for reference)

    Bookworm.Windows\                   (net10.0-windows, WPF app)
      App.xaml(.cs) / MainWindow.xaml(.cs)
      ViewModels\MainViewModel.cs
      Services\PushToTalkController.cs
      appsettings.json

    Bookworm.Console\                   (net10.0 console — test harness, Phases 1 & 3)
      Program.cs

  tests\Bookworm.Core.Tests\            (xUnit + Moq, no live network calls)
  docs\ architecture.md / va-endpoints.md / decisions.md
```

## VA Library API client

`IVaLibraryClient` (implemented by `VaLibraryClient`) exposes one method per confirmed endpoint: `AuthenticateAsync`, `SearchAsync`, `GetBookshelfAsync`, `AddToBookshelfAsync`, `RemoveFromBookshelfAsync`, `GetRequestListAsync`, `AddToRequestListAsync`, `GetSubscriptionsAsync`, `SubscribeAsync`, `GetHistoryAsync` (HTML-parse fallback), `GetPreferencesAsync`, `DownloadItemAsync` (pending endpoint confirmation).

Single `HttpClient` + `CookieContainer`, custom User-Agent. Any response resembling the Drupal login page (redirect to `/user/login`, or HTML where JSON was expected) is treated as session expiry → `VaSessionManager` re-authenticates once and retries. `VaSessionManager` also rate-limits (~300ms min between calls) since an LLM turn can trigger several tool calls back-to-back.

Credentials stored via `ICredentialStore` → `WindowsCredentialManagerStore` (Windows Credential Manager, e.g. via the `CredentialManagement` NuGet package or direct P/Invoke). Never plaintext. Session cookie is not persisted across runs; app re-authenticates silently on start.

**Loan cap enforcement**: `BookshelfSnapshot` carries the current count vs. the 20-item cap; `ToolCallExecutor` checks this *before* calling `AddToBookshelfAsync` and steers the Brain toward the request list instead of letting the portal reject it silently.

## Brain / conversational design (the core of this app)

This is the part that makes Bookworm a librarian and not a command shell.

**System prompt persona**: a reader's-advisory librarian for a vision-impaired Vision Australia Library member, speaking responses that will be read aloud via TTS with no visual fallback. Hard rules baked into the prompt:

- **Never enumerate a raw result list.** When `search_library` or `get_bookshelf`/`get_history` returns many hits, the Brain must cluster and summarize (by author, series, or theme) in 1–3 sentences, then offer a small number of next steps ("mostly the core Harry Potter series in English and French, plus a stage-play script — want the first book, or are you after something specific?"). `Discovery/ResultSummarizer.cs` provides deterministic pre-clustering (group by author/series, count, dedupe) that the Brain's prompt is built around, so the LLM isn't doing raw list-formatting itself.
- **Progressive narrowing.** Treat ambiguous or broad requests as the start of a dialogue, not a single-shot answer — ask one clarifying question at a time rather than dumping options.
- **Use history/bookshelf as context for taste, not just record-keeping.** `Discovery/ReaderProfile.cs` derives a lightweight summary from `GetHistoryAsync` + `GetBookshelfAsync` (frequent authors, apparent genres/eras from titles) that's fed into the system context so "what should I read next" or "something like X" has real grounding, and so already-read/already-owned titles are recognized and not re-suggested.
- **Compensate for the catalog's title/author/series-only search.** For thematic or "something like X" discovery, the Brain must first reason using its own general book knowledge to propose specific candidate titles/authors, then call `search_library` to check real availability — the VA search itself cannot do thematic matching.
- **Act without confirmation for additive actions** (`add_to_bookshelf`, `add_to_request_list`, `subscribe_to_periodical`) — just do it and report the result. **Confirm only before removal** (`remove_from_bookshelf`, unsubscribe): describe what will be removed, ask "shall I go ahead?", only call the tool after an affirmative reply in the next turn. This is the safety net for half-duplex speech, where nothing can be interrupted mid-action, applied only where the action is actually destructive.
- **Support explicit categorization requests** ("what kinds of things have I been reading lately?") by summarizing the `ReaderProfile` conversationally rather than requiring the user to ask about individual titles.
- **No proactive narration.** The app never speaks unprompted — no auto-announced status on launch or after actions beyond what's needed to confirm the action itself. Status/summaries (loan count, what's new, reading profile) are only given when the user asks. This keeps the app predictable and avoids talking over/at someone who didn't ask for it.

`IBrain` is the seam for developing without an API key:
```csharp
public interface IBrain { Task<BrainTurnResult> RespondAsync(ConversationContext context, CancellationToken ct); }
```
- `StubBrain` — canned/scripted responses for Phases 0–2 and unit tests.
- `ClaudeBrain` — real implementation (Phase 3+) once billing exists. No official Anthropic .NET SDK exists; hand-roll a small `HttpClient` caller against `POST https://api.anthropic.com/v1/messages` rather than depend on an unofficial package — keeps `Bookworm.Core` dependency-free (helps the MAUI port too).

`LibrarianOrchestrator.ProcessUserUtteranceAsync(string transcript) -> Task<string>`: append transcript to `ConversationContext` → call `IBrain.RespondAsync` → execute any tool calls via `ToolCallExecutor` (which enforces the loan cap and turns VA exceptions into tool-result errors) → feed results back to the Brain → loop (capped at ~5 round-trips per turn) until a final spoken-ready text response.

## Cross-session memory

VA's own account already persists bookshelf, request list, subscriptions, history, and its own formal preferences (format/narrator/language/auto-send/categories) — Bookworm reads these live via `IVaLibraryClient` each session rather than duplicating them. What VA does *not* capture is the qualitative context a conversation builds up: things the user has said they like/dislike outside VA's fixed preference categories, what they're currently chasing ("still looking for a follow-up to that Mary Beard book"), and a short summary of the last session for continuity.

This is modeled as one small document, `BookwormMemory`, with `IMemoryStore` as the storage seam:

```csharp
public interface IMemoryStore
{
    Task<BookwormMemory> LoadAsync(CancellationToken ct);
    Task SaveAsync(BookwormMemory memory, string? etag, CancellationToken ct); // optimistic concurrency
}
```

`BookwormMemory` holds: `ExplicitPreferences` (free-text notes like "prefers DAISY Audio (Human)", "not keen on graphic violence"), `ConversationNotes` (short free-text threads carried across sessions), `LastSessionSummary`. It is loaded once at the start of a session and folded into the Brain's context; the Brain gets two tools, `remember_preference(note)` and `recall_preferences()`, so it can update memory explicitly during conversation (e.g., when the user states a preference or asks "what have I told you I like?"). Saved at session end and after explicit `remember_preference` calls.

**Storage**: `AzureBlobMemoryStore` — a single versioned JSON blob (`memory.json`) in the `memory` container of the Azure Storage account (`mrphilbookworm`), confirmed by enumerating the account via `BlobServiceClient` (Bookworm.Console's `listcontainers` command) rather than assumed, using the blob's ETag for optimistic concurrency on read-modify-write. Chosen over a full database because the data is a single small document with infrequent writes from effectively one active client at a time; a database would be unneeded overhead. The Azure Storage SDK is portable .NET (not Windows-only), so `AzureBlobMemoryStore` lives directly in `Bookworm.Core` and is reused unchanged by the future MAUI/Android app. The storage connection string/SAS token is stored via the same `ICredentialStore` mechanism as VA credentials — never in plaintext config. Cost is negligible (a few cents/month) for this data volume. If the user prefers a different hosting option they already have access to, this is a one-file swap behind `IMemoryStore` — no other code changes.

## Speech pipeline — outcome (Phase 2, tested against the real account holder's voice/headset)

- **TTS**: `System.Speech.Synthesis` (legacy SAPI) — tested and confirmed acceptable quality, free, offline. Implemented as `SapiSpeechSynthesizer` in `Bookworm.Windows.Platform`. (Azure AI Speech, added for STT below, also offers higher-quality neural voices under the same resource — an easy future upgrade if SAPI's quality ever feels lacking, but not needed now.)
- **STT**: escalated past both free Windows options, in order tested:
  1. `System.Speech.Recognition` (SAPI dictation) — **rejected**: garbled entire phrases in real testing ("add the new Bernard Cornwell book to my bookshelf" → "And the need to not clone will go to my books a"), not just proper nouns.
  2. `Windows.Media.SpeechRecognition` (WinRT) — **rejected**: despite being the "modern" API, accuracy was equally poor in real testing. Its API design is newer than SAPI's, but it isn't backed by a meaningfully better language model for freeform dictation — that turned out to require Windows' separate Voice Typing feature, which isn't exposed as an embeddable API this way.
  3. **Azure AI Speech (cloud)** — **adopted**. Perfect transcription of the same test phrase. Implemented as `AzureSpeechRecognizer` directly in `Bookworm.Core` (the SDK is portable, not Windows-only, so this is reusable unchanged by the future MAUI/Android app — unlike the two rejected Windows-only implementations, which remain in `Bookworm.Windows.Platform` for reference but aren't used by the real app). Free tier (5 hours/month) expected to cover personal use; the resource lives in the same Azure account as the memory-store blob storage. Credentials stored via `ICredentialStore` under `CredentialKeys.AzureSpeech`, same pattern as VA and memory-store secrets.
- **Push-to-talk model**: `ISpeechRecognizer.StartListening()` / `StopListeningAsync()` maps directly onto each backend's continuous-recognition start/stop, so button press/release timing is exact rather than relying on a backend's own auto-endpointing.
- **Push-to-talk UI** (Phase 4, not yet built): a standard WPF `Button` (not custom-drawn, for correct automation-peer behavior) with `AutomationProperties.Name="Talk"`, wired to both mouse and keyboard press/release via `PushToTalkController`.

## Phased build order

| Phase | Goal | Needs Claude API key? |
|---|---|---|
| **0 — Setup** | Install .NET 10 SDK, scaffold solution/projects above, git init, NuGet (CredentialManagement, xUnit, Moq, Azure.Storage.Blobs). Azure Storage account (`mrphilbookworm`) already created — enumerate its containers via the SDK at Phase 3 implementation time to confirm exact naming, then load the saved connection string into `ICredentialStore`. | No |
| **1 — VA client** | Implement `Bookworm.Core/Library/*` + `Bookworm.Console` CLI (login/search/bookshelf/add/remove/requestlist/subscriptions/history) against the real account. Confirm the download endpoint and whether add/remove GETs need CSRF headers; confirm session cookie lifetime. | No |
| **2 — Speech echo test** | ✅ Done. Push-to-talk → STT → print → TTS speaks it back, standalone, tested against the real account holder's voice. SAPI and WinRT STT both failed accuracy testing; escalated to Azure AI Speech, which nailed it. SAPI TTS confirmed acceptable. | No |
| **3 — Conversational Brain harness** | ✅ Done. `Bookworm.Console chat [--dry-run]` — `LibrarianOrchestrator`, `ToolCallExecutor`, `ResultSummarizer`, `ReaderProfile`, `IMemoryStore`/`AzureBlobMemoryStore` all implemented and unit-tested (17 tests) against `StubBrain`, then live-tested end-to-end against real Claude, the real VA account, and the real Azure memory blob. Confirmed live: discovery-style requests correctly cluster and use the Brain's own book knowledge before checking availability; loan-cap awareness steers toward the request list; remove correctly asks for confirmation and only acts on an explicit yes; `--dry-run` genuinely protects the real bookshelf (verified: a dry-run "remove" left the real item in place); preferences stated in one session are recalled correctly in a separate process run via the real Azure blob. Two real bugs found and fixed during live testing, not caught by unit tests since they only surface against the actual API: `ContentBlock`'s unused nullable fields were serialized as explicit JSON `null`s instead of omitted, which Claude's strict per-block-type validation rejects (fixed via `JsonIgnoreCondition.WhenWritingNull`); and the model's `thinking` content blocks weren't captured by `ContentBlock`, so echoing conversation history back on the next turn dropped required fields (fixed by adding `Thinking`/`Signature` properties, which must round-trip verbatim). | StubBrain: No. ClaudeBrain: Yes. |
| **4 — Full integration** | ✅ Done. WPF app (`PushToTalkController`, `MainViewModel`, `MainWindow`) wiring Azure Speech STT + SAPI TTS (Phase 2) + `LibrarianOrchestrator` (Phase 3) + the VA client (Phase 1) behind a real Talk button, with a live-region status line and a transcript log. Talk works both as mouse press-and-hold and as a Space/Enter press/press toggle (a true hold isn't practical for keyboard/screen-reader use). Composition root (`App.xaml.cs`) reads every credential from the same Windows Credential Manager store the console harness's setup commands wrote to — everything configured during Phases 1–3 testing worked immediately, no separate setup needed. Live-tested by the user: "worked well, good comprehension and replies." No first-run setup UI yet (Phase 5), and no real NVDA pass yet (also Phase 5). | Yes |
| **5 — Polish** | Mostly done. Retry/backoff tuning ✅, file logging ✅, global hotkey (Ctrl+Alt+B, debounced) ✅, barge-in/cancellation ✅, first-run setup (VA-only; developer's own Anthropic/Speech/Storage credentials seeded by the installer, never entered by the end user) ✅, Inno Setup installer (`installer/Bookworm.iss`, produces a single self-contained `Bookworm-Setup.exe`) ✅ — tested end-to-end via a real silent install on the dev machine. Real bugs found via live voice testing (not caught by unit tests): concurrent turns could corrupt shared conversation history if a new question was asked before the previous reply finished (fixed with a turn gate + rollback-on-failure, and then built into "barge in" as a feature rather than blocked); rapid stop-then-start on the same Azure `SpeechRecognizer` object crashed the app (fixed by creating a fresh recognizer per turn); a raw API error was once spoken aloud in full via TTS (fixed — technical detail now goes only to the log); and `AzureBlobMemoryStore` reused a stale ETag on every save after the first, so memory silently stopped persisting after one turn per session (fixed — the orchestrator now tracks the ETag returned by each save, not just the one from the initial load). Still open: a real NVDA pass (needs a screen reader available to test with) and confirmation-UX refinement beyond what's already in the persona prompt. | Yes |
| **6 — Android/MAUI port** | New `Bookworm.Maui` project reusing `Bookworm.Core` unchanged; `Bookworm.Android.Platform` implementing credential storage (Android Keystore) and speech (Android `SpeechRecognizer`/`TextToSpeech`). | Yes |

## Open risks

- **Missing download endpoint** — confirm early in Phase 1 (small, bounded).
- **CSRF on state-changing GETs** — unusual for Drupal; confirm add/remove work on cookie auth alone.
- **Rate-limiting/good-citizen risk** — an LLM can over-call tools in one reasoning turn; `VaSessionManager` paces calls, orchestrator caps round-trips per turn.
- **20-item loan cap** — enforced client-side in `ToolCallExecutor`, not just reported after a failed call.
- **WPF shell accessibility** — standard controls only, correct `AutomationProperties`, real NVDA testing in Phase 5, not assumed.
- **Claude API key/billing gap** — resolved; account now set up.
- **Catalog search is title/author/series only** — the Brain's discovery behavior must compensate with its own book knowledge (see Brain design above); this is a design requirement, not just a risk.
- **Memory store hosting is a one-time setup dependency** — needs an Azure Storage account (or the user's preferred alternative) created and its secret stored before Phase 3's memory persistence can be tested; low cost but not zero-setup.

## Verification

- Phase 1: `Bookworm.Console` commands run against the real VA account produce correct JSON-derived output (search results, bookshelf contents, add/remove reflected on next fetch).
- Phase 2: spoken input round-trips to recognized text and back to speech with acceptable accuracy on sample book/author names.
- Phase 3: text-mode conversations against `StubBrain` exercise clustering/narrowing logic without live Claude calls; once billing exists, the same harness validates real `ClaudeBrain` responses for discovery-style prompts ("something like the last Mary Beard book I read") and confirms that removal actions ask for confirmation while additive actions (add to bookshelf/request list, subscribe) proceed immediately.
- Phase 4: end-to-end push-to-talk session against the real account, including at least one discovery conversation and one direct command, run by the user themselves (screen-reader-off first, then a screen-reader pass in Phase 5).
