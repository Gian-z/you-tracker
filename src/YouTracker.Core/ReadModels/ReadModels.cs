namespace YouTracker.Core.ReadModels;

/// <summary>Frontend-agnostic row for the task list.</summary>
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
    string WebUrl
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

public sealed record TriageResult(IReadOnlyList<TriagedIssue> Ranked, string FocusSuggestion);

public sealed record TimerStopResult(
    string IssueId,
    string IssueSummary,
    int ElapsedMinutes,
    DateOnly Date
);

public sealed record CommitResult(int Created, IReadOnlyList<string> Errors);
