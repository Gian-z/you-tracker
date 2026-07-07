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
