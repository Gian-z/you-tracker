namespace YouTracker.Core.ReadModels;

/// <summary>Frontend-agnostic row for the task list. Developer = login of the team's user field.</summary>
public sealed record TaskListItem(
    string IssueId,
    string Summary,
    string ProjectKey,
    string? Type,
    string? State,
    string? Priority,
    string? Estimate,
    string? Spent,
    DateTimeOffset Updated,
    string WebUrl,
    string? Developer = null
);

public sealed record WorkLogDraft(
    string IssueId,
    string? IssueSummary,
    string Confidence,
    DateOnly Date,
    int Minutes,
    string? WorkTypeName,
    string? Comment,
    string? Reasoning
);

public sealed record UnmatchedActivity(string Text, string Reason);

/// <summary>AI proposal — plain data. Nothing here can write; committing goes through the command side.</summary>
public sealed record WorkLogDraftResult(
    IReadOnlyList<WorkLogDraft> Drafts,
    IReadOnlyList<UnmatchedActivity> Unmatched
);

public sealed record TriagedIssue(
    string IssueId,
    string Summary,
    int Rank,
    int Score,
    IReadOnlyList<string> Reasons
);

/// <summary>
/// <paramref name="SprintSuggestions"/>: tasks from the configured sprint pool (not currently
/// the dev's) that match their recent focus — proposals only, empty without a pool query.
/// </summary>
public sealed record TriageResult(
    IReadOnlyList<TriagedIssue> Ranked,
    string FocusSuggestion,
    IReadOnlyList<TriagedIssue> SprintSuggestions
);

/// <summary>AI-written German Fazit for one developer (facts computed deterministically upstream).</summary>
public sealed record SprintVerdict(string Login, string Text);

public sealed record TimerStopResult(
    string IssueId,
    string IssueSummary,
    int ElapsedMinutes,
    DateOnly Date
);

public sealed record CommitResult(int Created, IReadOnlyList<string> Errors)
{
    /// <summary>Informational messages (e.g. Feature→Task redirects) — not failures.</summary>
    public IReadOnlyList<string> Notes { get; init; } = [];
}

/// <summary>Where a booking on the requested issue would actually land.</summary>
public enum BookingTargetKind
{
    /// <summary>Book on the requested issue as-is (not a Feature, or unknown issue).</summary>
    Direct,

    /// <summary>Feature with an unambiguous task subtask — booking is redirected to it.</summary>
    Redirected,

    /// <summary>Feature with several task subtasks — the user must pick one.</summary>
    Ambiguous,

    /// <summary>Feature without any task subtask — book on the feature only after explicit confirmation.</summary>
    NoTask,
}

public sealed record SubtaskCandidate(string IssueId, string Summary, bool Resolved);

public sealed record BookingTarget(
    string RequestedIssueId,
    BookingTargetKind Kind,
    string TargetIssueId,
    string? TargetSummary,
    bool TargetResolved,
    IReadOnlyList<SubtaskCandidate> Candidates
);
