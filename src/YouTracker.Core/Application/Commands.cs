using YouTracker.Core.Abstractions;
using YouTracker.Core.Domain;
using YouTracker.Core.ReadModels;

namespace YouTracker.Core.Application;

/// <summary>
/// <paramref name="AllowFeature"/>: bookings on Feature issues are redirected to their task
/// subtask (or rejected when ambiguous/absent) unless the user explicitly confirmed booking
/// on the feature itself.
/// </summary>
public sealed record CreateWorkItemCommand(
    string IssueId,
    DateOnly Date,
    int Minutes,
    string? TypeId,
    string? Text,
    bool AllowFeature = false
) : ICommand<WorkItem>;

/// <summary>
/// Edits an existing work item (duration/date/type/comment). Deliberately no Feature→Task
/// redirect: the item already sits on its issue and YouTrack's update endpoint cannot move
/// it anyway — moving a booking = delete + re-book.
/// </summary>
public sealed record UpdateWorkItemCommand(
    string IssueId,
    string WorkItemId,
    DateOnly Date,
    int Minutes,
    string? TypeId,
    string? Text
) : ICommand<WorkItem>;

public sealed record DeleteWorkItemCommand(string IssueId, string WorkItemId) : ICommand<bool>;

/// <summary>Writes only the drafts the user explicitly confirmed in the UI.</summary>
public sealed record CommitWorkLogDraftsCommand(
    IReadOnlyList<ReadModels.WorkLogDraft> ConfirmedDrafts,
    string? DefaultTypeId
) : ICommand<CommitResult>;

public sealed record StartTimerCommand(string IssueId, string IssueSummary) : ICommand<TimerState>;

/// <summary>
/// Returns the elapsed prefill for the log dialog without clearing the timer — the elapsed
/// time must survive a cancelled/failed booking. Clearing happens via DiscardTimerCommand
/// once the booking is confirmed (or the user explicitly discards).
/// </summary>
public sealed record StopTimerCommand : ICommand<TimerStopResult?>;

/// <summary>Clears the persisted timer. Sent after a successful stop-and-log booking or an explicit discard.</summary>
public sealed record DiscardTimerCommand : ICommand<bool>;

/// <summary>Pauses the running timer. Null when no timer exists; already paused → no-op returning the state.</summary>
public sealed record PauseTimerCommand : ICommand<TimerState?>;

/// <summary>Resumes a paused timer. Null when no timer exists; already running → no-op returning the state.</summary>
public sealed record ResumeTimerCommand : ICommand<TimerState?>;

/// <summary>Creates or updates a booking preset (matched by Id; empty Id = create new).</summary>
public sealed record SavePresetCommand(BookingPreset Preset) : ICommand<BookingPreset>;

public sealed record DeletePresetCommand(string Id) : ICommand<bool>;

/// <summary>Replaces the absence list of one sprint in the team config (UI absence editor).</summary>
public sealed record SaveSprintAbsencesCommand(
    string SprintName,
    IReadOnlyList<TeamAbsence> Absences
) : ICommand<TeamSprint>;

/// <summary>Adds a sprint to the team config; workdays = Mo–Fr within From..To.</summary>
public sealed record AddSprintCommand(string Name, DateOnly From, DateOnly To)
    : ICommand<TeamSprint>;

/// <summary>Replaces the whole team config (settings dialog → Team tab).</summary>
public sealed record SaveTeamConfigCommand(TeamConfig Config) : ICommand<TeamConfig>;

/// <summary>
/// Persists the app config from the settings dialog. Returns the effective config
/// (re-loaded, so env-var overrides and defaults are applied consistently).
/// </summary>
public sealed record SaveAppConfigCommand(Config.AppConfig Config) : ICommand<Config.AppConfig>;

public sealed record SaveUserSettingsCommand(UserSettings Settings) : ICommand<UserSettings>;

/// <summary>Upserts one day's presence/absence state.</summary>
public sealed record SaveDayStateCommand(DateOnly Date, DayState State) : ICommand<DayState>;
