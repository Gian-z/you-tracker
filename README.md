# you-tracker

Personal YouTrack time-tracking companion: a keyboard-first terminal UI with AI assistance for drafting work logs, filling booking gaps, standup summaries, and task triage.

## Quick start

```powershell
# 1. Create the config (printed automatically on first run if missing)
#    %APPDATA%\you-tracker\config.json — YouTrack permanent token + Anthropic API key
#    Env vars YOUTRACK_TOKEN / ANTHROPIC_API_KEY override the file values.

# 2. Smoke tests
dotnet run --project src/YouTracker.Tui -- --check     # read-only: open issues + this week's bookings
dotnet run --project src/YouTracker.Tui -- --check-ai  # one trivial completion through the active AI provider

# 3a. Run the TUI
dotnet run --project src/YouTracker.Tui

# 3b. Or run the web GUI (Angular SPA served at http://localhost:5210)
dotnet run --project src/YouTracker.Web
```

## Web GUI

`YouTracker.Web` hosts a REST API (`/api/*`) plus the Angular SPA. The **Dashboard** (default page) is a one-page overview replacing the YouTrack dashboard: KPI tiles (today/week booked vs target, Fokus-Score, ticket count), the ticket list from your query, a status distribution, unclaimed sprint tasks, today's bookings with one-click presets, and a compact AI panel (draft / gaps / summary / triage with sprint pickup suggestions). Dedicated pages remain for Tasks · Week · Assistant; the timer widget and the AI draft-review commit gate work everywhere — same confirm-before-write rule as the TUI.

- **Use it:** `dotnet run --project src/YouTracker.Web` → open http://localhost:5210
- **Frontend development:** `cd frontend; npm start` → http://localhost:4200 with `/api` proxied to :5210 (run the web host alongside)
- **Rebuild the SPA into wwwroot:** `cd frontend; npx ng build`

## Keys

| Key | Action |
|---|---|
| `F1` / `F2` / `F3` | Task list · Time overview · AI assistant |
| `Ctrl+T` | Start/stop timer (stop opens the log dialog prefilled) |
| `Enter` | Task list: log time · Time overview (gap day): AI gap suggestions |
| `t` / `o` / `r` / `/` | Start timer · open in browser · refresh · filter |
| `←` `→` `d` | Time overview: previous/next week, today's week |
| `Ctrl+Q` | Quit |

AI actions (F3) only ever **propose** work items. Nothing is written to YouTrack until you check the drafts and press Commit in the review dialog.

## Scope & involvement

The ticket list is driven by `youTrack.issueQuery` in the config — any YouTrack query with a `$dev` placeholder (e.g. a sprint-board query filtering on a custom `Entwickler` field). Without one, the built-in default covers issues where the dev is **involved**: assignee OR has booked time (`for: X or work author: X`, unresolved, newest 100). `youTrack.sprintPoolQuery` (optional, same `$dev` placeholder) defines a candidate pool — e.g. unclaimed sprint tasks — from which **AI triage** proposes up to five pickup tasks matching the dev's recent booking focus.

The web GUI's top-bar picker switches the viewed dev (dropdown from YouTrack's user directory, falling back to a text input without list permission). Viewing another dev is strictly read-only — timer, log and commit actions are disabled because bookings are always created as the token owner.

## Booking presets

Recurring bookings (daily standup, plannings, …) can be saved as presets: tick **"Save as preset"** in the Log time dialog, then book them with one click from the strip on the Week page (books the preset's duration for today). Presets live in `%APPDATA%\you-tracker\presets.json`.

## AI provider selection (hybrid)

The composition root picks the AI backend from `anthropic.apiKey` in the config:

- **Empty / placeholder key** → **Claude Code CLI** (`claude -p --output-format json`, headless): uses your existing Claude Code login, no API key needed. Override the executable with `anthropic.cliCommand` (default `"claude"`).
- **Real key** → the official Anthropic SDK (`anthropic.model`, default `claude-opus-4-8`).

Both implement the same `IAiProvider` port; switching is zero code changes.

## Architecture

CQRS-light with ports & adapters. `YouTracker.Core` holds all abstractions, application handlers (queries/commands via `IDispatcher` with a caching decorator), domain metrics (Fokus-Score, gaps, hygiene), and the AI prompt/parsing layer. Each external system is a replaceable module implementing Core ports:

```
src/
├── YouTracker.Core/                    # ports, CQRS handlers, metrics, AI prompts/parsing
├── YouTracker.Infrastructure.YouTrack/ # REST adapter → IIssueReader/IWorkItemReader/IWorkItemWriter
├── YouTracker.Infrastructure.Anthropic/# Claude API → IAiProvider (official Anthropic SDK)
├── YouTracker.Infrastructure.ClaudeCli/# Claude Code CLI (headless) → IAiProvider, no API key
├── YouTracker.Infrastructure.Storage/  # %APPDATA% JSON files → ITimerStore/IConfigStore
├── YouTracker.Tui/                     # Terminal.Gui v1 host (composition root #1)
└── YouTracker.Web/                     # ASP.NET Core API + Angular SPA host (composition root #2)
frontend/                               # Angular 21 app (signals, zoneless); builds into YouTracker.Web/wwwroot
tests/YouTracker.Core.Tests/
```

Views talk to `IDispatcher` only; swapping YouTrack→Jira or Claude→another LLM is one new module plus one DI line in `Program.cs`. AI query handlers receive reader ports only — they structurally cannot write.

```powershell
dotnet build ; dotnet test tests/YouTracker.Core.Tests
```
