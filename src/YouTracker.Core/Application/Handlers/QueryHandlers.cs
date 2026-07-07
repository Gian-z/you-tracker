using YouTracker.Core.Abstractions;
using YouTracker.Core.Config;
using YouTracker.Core.Domain;
using YouTracker.Core.Metrics;
using YouTracker.Core.ReadModels;

namespace YouTracker.Core.Application.Handlers;

/// <summary>Issue → TaskListItem mapping shared by every issue-list query handler.</summary>
internal static class TaskListMapper
{
    public static IReadOnlyList<TaskListItem> Map(IEnumerable<Issue> issues, AppConfig config) =>
        [
            .. issues.Select(i => new TaskListItem(
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

public sealed class GetMyOpenIssuesQueryHandler(IIssueReader issues, AppConfig config)
    : IQueryHandler<GetMyOpenIssuesQuery, IReadOnlyList<TaskListItem>>
{
    public async Task<IReadOnlyList<TaskListItem>> HandleAsync(
        GetMyOpenIssuesQuery query,
        CancellationToken ct = default
    )
    {
        var open = await issues.GetOpenIssuesAsync(query.Dev, ct).ConfigureAwait(false);
        return TaskListMapper.Map(open, config);
    }
}

public sealed class SearchIssuesQueryHandler(IIssueReader issues, AppConfig config)
    : IQueryHandler<SearchIssuesQuery, IReadOnlyList<TaskListItem>>
{
    public async Task<IReadOnlyList<TaskListItem>> HandleAsync(
        SearchIssuesQuery query,
        CancellationToken ct = default
    )
    {
        var text = query.Text?.Trim();
        if (text is null || text.Length < 2)
            return [];
        var top = Math.Clamp(query.Top, 1, 50);
        var found = await issues.SearchIssuesAsync(text, top, ct).ConfigureAwait(false);
        return TaskListMapper.Map(found, config);
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
            .GetWorkItemsAsync(query.Dev, query.From, query.To, ct)
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

public sealed class GetSprintPoolQueryHandler(IIssueReader issues, AppConfig config)
    : IQueryHandler<GetSprintPoolQuery, IReadOnlyList<TaskListItem>>
{
    public async Task<IReadOnlyList<TaskListItem>> HandleAsync(
        GetSprintPoolQuery query,
        CancellationToken ct = default
    )
    {
        var pool = await issues.GetSprintPoolIssuesAsync(query.Dev, ct).ConfigureAwait(false);
        return TaskListMapper.Map(pool, config);
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

public sealed class GetUsersQueryHandler(IUserDirectory users)
    : IQueryHandler<GetUsersQuery, IReadOnlyList<UserInfo>>
{
    public Task<IReadOnlyList<UserInfo>> HandleAsync(
        GetUsersQuery query,
        CancellationToken ct = default
    ) => users.GetUsersAsync(ct);
}

public sealed class GetCurrentUserQueryHandler(IUserDirectory users)
    : IQueryHandler<GetCurrentUserQuery, UserInfo>
{
    public Task<UserInfo> HandleAsync(GetCurrentUserQuery query, CancellationToken ct = default) =>
        users.GetCurrentUserAsync(ct);
}

public sealed class GetTimerStateQueryHandler(ITimerStore timerStore)
    : IQueryHandler<GetTimerStateQuery, TimerState?>
{
    public Task<TimerState?> HandleAsync(
        GetTimerStateQuery query,
        CancellationToken ct = default
    ) => Task.FromResult(timerStore.Load());
}

public sealed class GetPresetsQueryHandler(IPresetStore presets)
    : IQueryHandler<GetPresetsQuery, IReadOnlyList<BookingPreset>>
{
    public Task<IReadOnlyList<BookingPreset>> HandleAsync(
        GetPresetsQuery query,
        CancellationToken ct = default
    ) => Task.FromResult(presets.Load());
}

public sealed class GetBookingTargetQueryHandler(IIssueReader issues, AppConfig config)
    : IQueryHandler<GetBookingTargetQuery, BookingTarget>
{
    public Task<BookingTarget> HandleAsync(
        GetBookingTargetQuery query,
        CancellationToken ct = default
    ) => BookingTargetResolver.ResolveAsync(issues, config, query.IssueId, ct);
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
        var open = await issues.GetOpenIssuesAsync(query.Dev, ct).ConfigureAwait(false);
        var recent = await workItems
            .GetWorkItemsAsync(query.Dev, today.AddDays(-14), today, ct)
            .ConfigureAwait(false);
        return MetricsCalculator.Hygiene(
            open,
            recent,
            [.. config.Workday.InProgressStates],
            today,
            config.TimeZone
        );
    }
}
