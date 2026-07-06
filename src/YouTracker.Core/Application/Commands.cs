using YouTracker.Core.Abstractions;
using YouTracker.Core.Domain;
using YouTracker.Core.ReadModels;

namespace YouTracker.Core.Application;

public sealed record CreateWorkItemCommand(
    string IssueId,
    DateOnly Date,
    int Minutes,
    string? TypeId,
    string? Text
) : ICommand<WorkItem>;

/// <summary>Writes only the drafts the user explicitly confirmed in the UI.</summary>
public sealed record CommitWorkLogDraftsCommand(
    IReadOnlyList<ReadModels.WorkLogDraft> ConfirmedDrafts,
    string? DefaultTypeId
) : ICommand<CommitResult>;

public sealed record StartTimerCommand(string IssueId, string IssueSummary) : ICommand<TimerState>;

/// <summary>Stops the running timer and returns the elapsed prefill for the log dialog. Does not log time itself.</summary>
public sealed record StopTimerCommand : ICommand<TimerStopResult?>;
