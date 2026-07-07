using YouTracker.Core.Abstractions;
using YouTracker.Core.Domain;
using YouTracker.Core.Metrics;
using YouTracker.Core.ReadModels;
using SprintVerdict = YouTracker.Core.ReadModels.SprintVerdict;

namespace YouTracker.Core.Application;

// `Dev` is a YouTrack login; null means the current user.

public sealed record GetMyOpenIssuesQuery(bool BypassCache = false, string? Dev = null)
    : IQuery<IReadOnlyList<TaskListItem>>,
        ICacheableQuery
{
    public string CacheKey => $"issues:open:{Dev ?? "me"}";
    public TimeSpan Ttl => TimeSpan.FromMinutes(2);
}

public sealed record GetTimeOverviewQuery(
    DateOnly From,
    DateOnly To,
    bool BypassCache = false,
    string? Dev = null
) : IQuery<TimeOverview>, ICacheableQuery
{
    public string CacheKey => $"workitems:overview:{Dev ?? "me"}:{From:yyyyMMdd}:{To:yyyyMMdd}";
    public TimeSpan Ttl => TimeSpan.FromMinutes(2);
}

public sealed record GetWorkItemTypesQuery(bool BypassCache = false)
    : IQuery<IReadOnlyList<WorkItemType>>,
        ICacheableQuery
{
    public string CacheKey => "workitems:types";
    public TimeSpan Ttl => TimeSpan.FromHours(1);
}

public sealed record GetUsersQuery(bool BypassCache = false)
    : IQuery<IReadOnlyList<UserInfo>>,
        ICacheableQuery
{
    public string CacheKey => "users:all";
    public TimeSpan Ttl => TimeSpan.FromHours(1);
}

public sealed record GetCurrentUserQuery(bool BypassCache = false)
    : IQuery<UserInfo>,
        ICacheableQuery
{
    public string CacheKey => "users:me";
    public TimeSpan Ttl => TimeSpan.FromHours(1);
}

/// <summary>Unclaimed/other sprint tasks from the configured pool query (empty without one).</summary>
public sealed record GetSprintPoolQuery(bool BypassCache = false, string? Dev = null)
    : IQuery<IReadOnlyList<TaskListItem>>,
        ICacheableQuery
{
    public string CacheKey => $"issues:pool:{Dev ?? "me"}";
    public TimeSpan Ttl => TimeSpan.FromMinutes(2);
}

/// <summary>
/// ALL tickets of the current sprint (colleagues' included) from the configured sprintQuery —
/// for booking testing/review on someone else's ticket. Empty without a configured query.
/// </summary>
public sealed record GetCurrentSprintIssuesQuery(bool BypassCache = false)
    : IQuery<IReadOnlyList<TaskListItem>>,
        ICacheableQuery
{
    public string CacheKey => "issues:sprint-all";
    public TimeSpan Ttl => TimeSpan.FromMinutes(2);
}

public sealed record GetTimerStateQuery : IQuery<TimerState?>;

/// <summary>
/// Global ticket search (free text, YouTrack query syntax, or an issue id) — for booking
/// on tickets outside the configured scope. Deliberately not cached: interactive per-keystroke
/// queries would flood the cache with unbounded keys.
/// </summary>
public sealed record SearchIssuesQuery(string Text, int Top = 25)
    : IQuery<IReadOnlyList<TaskListItem>>;

/// <summary>
/// Pre-flight for the "bookings land on tasks" rule: where would a booking on this issue go?
/// The UI uses this to show the redirect hint / subtask picker before sending the command.
/// </summary>
public sealed record GetBookingTargetQuery(string IssueId, bool BypassCache = false)
    : IQuery<BookingTarget>,
        ICacheableQuery
{
    // `issues:` prefix — evicted on WorkItemCreated like the other issue caches.
    public string CacheKey => $"issues:bookingtarget:{IssueId}";
    public TimeSpan Ttl => TimeSpan.FromMinutes(5);
}

// --- Sprint dashboard (Scrum Master view) ---

public sealed record GetTeamConfigQuery : IQuery<TeamConfig?>;

public sealed record GetSprintDashboardQuery(string SprintName, bool BypassCache = false)
    : IQuery<SprintDashboard>,
        ICacheableQuery
{
    public string CacheKey => $"workitems:sprint:{SprintName}";
    public TimeSpan Ttl => TimeSpan.FromMinutes(2);
}

/// <summary>German two-paragraph Fazit per dev; Ampel + KPIs are computed facts, AI writes prose only.</summary>
public sealed record GenerateSprintVerdictsQuery(string SprintName)
    : IQuery<IReadOnlyList<SprintVerdict>>;

public sealed record GetPresetsQuery : IQuery<IReadOnlyList<BookingPreset>>;

public sealed record GetHygieneFindingsQuery(string? Dev = null)
    : IQuery<IReadOnlyList<HygieneFinding>>;

/// <summary>
/// Deterministic (non-AI): the day's calendar meetings mapped to booking drafts via the
/// configured title-pattern rules. Deliberately uncached — button-triggered, and the result
/// depends on already-booked work items (like SearchIssuesQuery).
/// </summary>
public sealed record GetMeetingDraftsQuery(DateOnly Date) : IQuery<WorkLogDraftResult>;

// --- AI queries: read-only proposals; their handlers have no access to IWorkItemWriter ---

public sealed record DraftWorkLogQuery(string FreeText, DateOnly Date, string? Dev = null)
    : IQuery<WorkLogDraftResult>;

public sealed record SuggestGapFillsQuery(DateOnly From, DateOnly To, string? Dev = null)
    : IQuery<WorkLogDraftResult>;

public sealed record SummarizePeriodQuery(DateOnly From, DateOnly To, string? Dev = null)
    : IQuery<string>;

public sealed record TriageIssuesQuery(string? Dev = null) : IQuery<TriageResult>;
