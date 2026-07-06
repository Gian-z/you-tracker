using YouTracker.Core.ReadModels;

namespace YouTracker.Web;

// Request DTOs for the REST API. Responses reuse the Core read models directly.

public sealed record StartTimerRequest(string IssueId, string IssueSummary);

public sealed record CreateWorkLogRequest(
    string IssueId,
    DateOnly Date,
    int Minutes,
    string? TypeId = null,
    string? Text = null
);

public sealed record CommitWorkLogRequest(
    IReadOnlyList<WorkLogDraft> Drafts,
    string? DefaultTypeId = null
);

public sealed record AiDraftRequest(string FreeText, DateOnly Date);

public sealed record PeriodRequest(DateOnly From, DateOnly To);
