using YouTracker.Core.Abstractions;
using YouTracker.Core.Domain;
using YouTracker.Core.Metrics;
using YouTracker.Core.ReadModels;

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

public sealed record GetTimerStateQuery : IQuery<TimerState?>;

public sealed record GetPresetsQuery : IQuery<IReadOnlyList<BookingPreset>>;

public sealed record GetHygieneFindingsQuery(string? Dev = null)
    : IQuery<IReadOnlyList<HygieneFinding>>;

// --- AI queries: read-only proposals; their handlers have no access to IWorkItemWriter ---

public sealed record DraftWorkLogQuery(string FreeText, DateOnly Date, string? Dev = null)
    : IQuery<WorkLogDraftResult>;

public sealed record SuggestGapFillsQuery(DateOnly From, DateOnly To, string? Dev = null)
    : IQuery<WorkLogDraftResult>;

public sealed record SummarizePeriodQuery(DateOnly From, DateOnly To, string? Dev = null)
    : IQuery<string>;

public sealed record TriageIssuesQuery(string? Dev = null) : IQuery<TriageResult>;
