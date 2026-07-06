# you-tracker

Personal YouTrack time-tracking companion: a keyboard-first terminal UI with AI assistance for drafting work logs, filling booking gaps, standup summaries, and task triage.

## Quick start

```powershell
# 1. Create the config (printed automatically on first run if missing)
#    %APPDATA%\you-tracker\config.json — YouTrack permanent token + Anthropic API key
#    Env vars YOUTRACK_TOKEN / ANTHROPIC_API_KEY override the file values.

# 2. Smoke test (read-only: open issues + this week's bookings)
dotnet run --project src/YouTracker.Tui -- --check

# 3. Run the TUI
dotnet run --project src/YouTracker.Tui
```

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

## Architecture

CQRS-light with ports & adapters. `YouTracker.Core` holds all abstractions, application handlers (queries/commands via `IDispatcher` with a caching decorator), domain metrics (Fokus-Score, gaps, hygiene), and the AI prompt/parsing layer. Each external system is a replaceable module implementing Core ports:

```
src/
├── YouTracker.Core/                    # ports, CQRS handlers, metrics, AI prompts/parsing
├── YouTracker.Infrastructure.YouTrack/ # REST adapter → IIssueReader/IWorkItemReader/IWorkItemWriter
├── YouTracker.Infrastructure.Anthropic/# Claude API → IAiProvider (official Anthropic SDK)
├── YouTracker.Infrastructure.Storage/  # %APPDATA% JSON files → ITimerStore/IConfigStore
└── YouTracker.Tui/                     # Terminal.Gui v1 host; Program.cs is the only composition root
tests/YouTracker.Core.Tests/
```

Views talk to `IDispatcher` only; swapping YouTrack→Jira or Claude→another LLM is one new module plus one DI line in `Program.cs`. AI query handlers receive reader ports only — they structurally cannot write.

```powershell
dotnet build ; dotnet test tests/YouTracker.Core.Tests
```
