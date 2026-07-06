using YouTracker.Core.Abstractions;
using YouTracker.Core.Config;
using YouTracker.Core.Domain;
using YouTracker.Core.Metrics;
using YouTracker.Core.ReadModels;

namespace YouTracker.Core.Application.Handlers;

public sealed class GetMyOpenIssuesQueryHandler(IIssueReader issues, AppConfig config)
    : IQueryHandler<GetMyOpenIssuesQuery, IReadOnlyList<TaskListItem>>
{
    public async Task<IReadOnlyList<TaskListItem>> HandleAsync(
        GetMyOpenIssuesQuery query,
        CancellationToken ct = default
    )
    {
        var open = await issues.GetMyOpenIssuesAsync(ct).ConfigureAwait(false);
        return
        [
            .. open.Select(i => new TaskListItem(
                i.Id,
                i.Summary,
                i.ProjectKey,
                i.Type,
                i.State,
                i.Priority,
                i.EstimateMinutes is { } e ? DurationFormat.ToPresentation(e) : null,
                i.SpentMinutes is { } s ? DurationFormat.ToPresentation(s) : null,
                i.Updated,
                config.WebUrlFor(i.Id)
            )),
        ];
    }
}

public sealed class GetTimeOverviewQueryHandler(
    IWorkItemReader workItems,
    AppConfig config,
    TimeProvider time
) : IQueryHandler<GetTimeOverviewQuery, TimeOverview>
{
    public async Task<TimeOverview> HandleAsync(
        GetTimeOverviewQuery query,
        CancellationToken ct = default
    )
    {
        var items = await workItems
            .GetMyWorkItemsAsync(query.From, query.To, ct)
            .ConfigureAwait(false);
        return MetricsCalculator.BuildOverview(
            items,
            query.From,
            query.To,
            config.Today(time),
            config.TargetMinutesPerWorkday
        );
    }
}

public sealed class GetWorkItemTypesQueryHandler(IWorkItemReader workItems)
    : IQueryHandler<GetWorkItemTypesQuery, IReadOnlyList<WorkItemType>>
{
    public Task<IReadOnlyList<WorkItemType>> HandleAsync(
        GetWorkItemTypesQuery query,
        CancellationToken ct = default
    ) => workItems.GetWorkItemTypesAsync(ct);
}

public sealed class GetTimerStateQueryHandler(ITimerStore timerStore)
    : IQueryHandler<GetTimerStateQuery, TimerState?>
{
    public Task<TimerState?> HandleAsync(
        GetTimerStateQuery query,
        CancellationToken ct = default
    ) => Task.FromResult(timerStore.Load());
}

public sealed class GetHygieneFindingsQueryHandler(
    IIssueReader issues,
    IWorkItemReader workItems,
    AppConfig config,
    TimeProvider time
) : IQueryHandler<GetHygieneFindingsQuery, IReadOnlyList<HygieneFinding>>
{
    public async Task<IReadOnlyList<HygieneFinding>> HandleAsync(
        GetHygieneFindingsQuery query,
        CancellationToken ct = default
    )
    {
        var today = config.Today(time);
        var open = await issues.GetMyOpenIssuesAsync(ct).ConfigureAwait(false);
        var recent = await workItems
            .GetMyWorkItemsAsync(today.AddDays(-14), today, ct)
            .ConfigureAwait(false);
        return MetricsCalculator.Hygiene(open, recent, [.. config.Workday.InProgressStates], today);
    }
}
