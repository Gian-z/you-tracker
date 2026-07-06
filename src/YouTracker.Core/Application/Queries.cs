using YouTracker.Core.Abstractions;
using YouTracker.Core.Domain;
using YouTracker.Core.Metrics;
using YouTracker.Core.ReadModels;

namespace YouTracker.Core.Application;

public sealed record GetMyOpenIssuesQuery(bool BypassCache = false)
    : IQuery<IReadOnlyList<TaskListItem>>,
        ICacheableQuery
{
    public string CacheKey => "issues:my-open";
    public TimeSpan Ttl => TimeSpan.FromMinutes(2);
}

public sealed record GetTimeOverviewQuery(DateOnly From, DateOnly To, bool BypassCache = false)
    : IQuery<TimeOverview>,
        ICacheableQuery
{
    public string CacheKey => $"workitems:overview:{From:yyyyMMdd}:{To:yyyyMMdd}";
    public TimeSpan Ttl => TimeSpan.FromMinutes(2);
}

public sealed record GetWorkItemTypesQuery(bool BypassCache = false)
    : IQuery<IReadOnlyList<WorkItemType>>,
        ICacheableQuery
{
    public string CacheKey => "workitems:types";
    public TimeSpan Ttl => TimeSpan.FromHours(1);
}

public sealed record GetTimerStateQuery : IQuery<TimerState?>;

public sealed record GetHygieneFindingsQuery : IQuery<IReadOnlyList<HygieneFinding>>;

// --- AI queries: read-only proposals; their handlers have no access to IWorkItemWriter ---

public sealed record DraftWorkLogQuery(string FreeText, DateOnly Date) : IQuery<WorkLogDraftResult>;

public sealed record SuggestGapFillsQuery(DateOnly From, DateOnly To) : IQuery<WorkLogDraftResult>;

public sealed record SummarizePeriodQuery(DateOnly From, DateOnly To) : IQuery<string>;

public sealed record TriageIssuesQuery : IQuery<TriageResult>;
