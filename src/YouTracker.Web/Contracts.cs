using YouTracker.Core.ReadModels;

namespace YouTracker.Web;

// Request DTOs for the REST API. Responses reuse the Core read models directly.

public sealed record StartTimerRequest(string IssueId, string IssueSummary);

public sealed record CreateWorkLogRequest(
    string IssueId,
    DateOnly Date,
    int Minutes,
    string? TypeId = null,
    string? Text = null,
    bool AllowFeature = false
);

public sealed record CommitWorkLogRequest(
    IReadOnlyList<WorkLogDraft> Drafts,
    string? DefaultTypeId = null
);

public sealed record AiDraftRequest(string FreeText, DateOnly Date, string? Dev = null);

public sealed record PeriodRequest(DateOnly From, DateOnly To, string? Dev = null);

public sealed record SaveAbsencesRequest(
    string SprintName,
    IReadOnlyList<YouTracker.Core.Abstractions.TeamAbsence> Absences
);

public sealed record SprintVerdictsRequest(string SprintName);

public sealed record AddSprintRequest(string Name, DateOnly From, DateOnly To);

public sealed record SavePresetRequest(
    string? Id,
    string Name,
    string IssueId,
    string IssueSummary,
    int Minutes,
    string? TypeId = null,
    string? TypeName = null,
    string? Comment = null
);
