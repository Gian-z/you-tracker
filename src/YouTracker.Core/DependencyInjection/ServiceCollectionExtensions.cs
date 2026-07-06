using Microsoft.Extensions.DependencyInjection;
using YouTracker.Core.Abstractions;
using YouTracker.Core.Application;
using YouTracker.Core.Application.Handlers;
using YouTracker.Core.Domain;
using YouTracker.Core.Metrics;
using YouTracker.Core.ReadModels;

namespace YouTracker.Core.DependencyInjection;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the CQRS pipeline and all handlers. Infrastructure modules must additionally
    /// register the ports (IIssueReader, IWorkItemReader, IWorkItemWriter, IAiProvider, ITimerStore)
    /// and an AppConfig instance in the host's composition root.
    /// </summary>
    public static IServiceCollection AddYouTrackerCore(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<TtlCache>();
        services.AddSingleton<IEventBus, EventBus>();
        services.AddSingleton<IDispatcher>(sp => new CachingDispatcher(
            new Dispatcher(sp),
            sp.GetRequiredService<TtlCache>(),
            sp.GetRequiredService<IEventBus>()
        ));

        // Query handlers
        services.AddTransient<
            IQueryHandler<GetMyOpenIssuesQuery, IReadOnlyList<TaskListItem>>,
            GetMyOpenIssuesQueryHandler
        >();
        services.AddTransient<
            IQueryHandler<GetTimeOverviewQuery, TimeOverview>,
            GetTimeOverviewQueryHandler
        >();
        services.AddTransient<
            IQueryHandler<GetWorkItemTypesQuery, IReadOnlyList<WorkItemType>>,
            GetWorkItemTypesQueryHandler
        >();
        services.AddTransient<
            IQueryHandler<GetTimerStateQuery, TimerState?>,
            GetTimerStateQueryHandler
        >();
        services.AddTransient<
            IQueryHandler<GetHygieneFindingsQuery, IReadOnlyList<HygieneFinding>>,
            GetHygieneFindingsQueryHandler
        >();
        services.AddTransient<
            IQueryHandler<DraftWorkLogQuery, WorkLogDraftResult>,
            DraftWorkLogQueryHandler
        >();
        services.AddTransient<
            IQueryHandler<SuggestGapFillsQuery, WorkLogDraftResult>,
            SuggestGapFillsQueryHandler
        >();
        services.AddTransient<
            IQueryHandler<SummarizePeriodQuery, string>,
            SummarizePeriodQueryHandler
        >();
        services.AddTransient<
            IQueryHandler<TriageIssuesQuery, TriageResult>,
            TriageIssuesQueryHandler
        >();

        // Command handlers
        services.AddTransient<
            ICommandHandler<CreateWorkItemCommand, WorkItem>,
            CreateWorkItemCommandHandler
        >();
        services.AddTransient<
            ICommandHandler<CommitWorkLogDraftsCommand, CommitResult>,
            CommitWorkLogDraftsCommandHandler
        >();
        services.AddTransient<
            ICommandHandler<StartTimerCommand, TimerState>,
            StartTimerCommandHandler
        >();
        services.AddTransient<
            ICommandHandler<StopTimerCommand, TimerStopResult?>,
            StopTimerCommandHandler
        >();
        services.AddTransient<
            ICommandHandler<SavePresetCommand, BookingPreset>,
            SavePresetCommandHandler
        >();
        services.AddTransient<
            ICommandHandler<DeletePresetCommand, bool>,
            DeletePresetCommandHandler
        >();

        services.AddTransient<
            IQueryHandler<GetSprintPoolQuery, IReadOnlyList<TaskListItem>>,
            GetSprintPoolQueryHandler
        >();

        // Directory + presets
        services.AddTransient<
            IQueryHandler<GetUsersQuery, IReadOnlyList<UserInfo>>,
            GetUsersQueryHandler
        >();
        services.AddTransient<
            IQueryHandler<GetCurrentUserQuery, UserInfo>,
            GetCurrentUserQueryHandler
        >();
        services.AddTransient<
            IQueryHandler<GetPresetsQuery, IReadOnlyList<BookingPreset>>,
            GetPresetsQueryHandler
        >();

        return services;
    }
}
