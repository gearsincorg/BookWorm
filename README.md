# Bookworm

A voice-driven librarian/front-desk assistant for a Vision Australia Library member's personal account. Talk to it (push-to-talk), and it searches, manages your bookshelf/request list/subscriptions, and — its main purpose — helps you find and categorize books and authors, known and unknown, through conversation, without ever needing you to scan a visual list.

See [docs/decisions.md](docs/decisions.md) for the full implementation plan, architecture, and reasoning behind every major decision. See [docs/Bookworm_Implementation_Plan.docx](docs/Bookworm_Implementation_Plan.docx) for a formatted copy suitable for sharing.

## Status

Phase 1 mostly complete — the VA Library API client (`Bookworm.Core.Library`) is implemented and verified against a real account for login, search, bookshelf (list/add), request list, and subscriptions. Remaining gaps: the bookshelf item download endpoint hasn't been found yet, remove-from-bookshelf/request-list/subscription aren't click-verified, and history parsing isn't implemented (VA serves it as HTML, not JSON). See [docs/va-endpoints.md](docs/va-endpoints.md) for full details. `Bookworm.Console` is a working CLI test harness (`login`, `search`, `bookshelf`, `add`, `remove`, `requestlist`, `addrequest`, `subscriptions`, `subscribe`, `history`, `raw`).

## Solution layout

- `src/Bookworm.Core` — platform-independent library: the Vision Australia library API client, the Claude "Brain" and conversational/discovery logic, cross-session memory. Shared by both the Windows app and (later) the Android app.
- `src/Bookworm.Windows.Platform` — Windows-specific implementations (credential storage, speech recognition/synthesis).
- `src/Bookworm.Windows` — the WPF desktop app (push-to-talk UI).
- `src/Bookworm.Console` — a console test harness used during development (Phases 1 and 3) to exercise the library client and conversational Brain without needing the full UI or speech pipeline.
- `tests/Bookworm.Core.Tests` — unit tests for `Bookworm.Core`.

## Requirements

- .NET 10 SDK
- Windows 10/11 (for speech APIs and Credential Manager)
- An Anthropic API key (for the real conversational Brain — not required for Phases 0–2)
- Vision Australia Library membership credentials
