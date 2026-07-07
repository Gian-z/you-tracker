using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using YouTracker.Core.Abstractions;
using YouTracker.Core.Application;
using YouTracker.Core.DependencyInjection;

namespace YouTracker.Core.Tests.Core;

public class DispatchingTests
{
    private sealed record UnknownQuery : IQuery<int>;

    private sealed class Harness
    {
        public FakeIssueReader IssueReader { get; } = new(TestData.Issue("ABC-1"));
        public FakeWorkItemReader WorkItemReader { get; } = new();
        public FakeWorkItemWriter Writer { get; } = new();
        public InMemoryTimerStore TimerStore { get; } = new();
        public FakeTimeProvider Time { get; } =
            new(new DateTimeOffset(2026, 7, 6, 10, 0, 0, TimeSpan.Zero));
        public IServiceProvider Provider { get; }
        public IDispatcher Dispatcher { get; }
        public IEventBus Events { get; }

        public Harness()
        {
            var services = new ServiceCollection();
            services.AddYouTrackerCore();
            services.AddSingleton<TimeProvider>(Time); // last registration wins
            services.AddSingleton<IIssueReader>(IssueReader);
            services.AddSingleton<IWorkItemReader>(WorkItemReader);
            services.AddSingleton<IWorkItemWriter>(Writer);
            services.AddSingleton<ITimerStore>(TimerStore);
            services.AddSingleton<IAiProvider>(new FakeAiProvider());
            services.AddSingleton(TestData.Config());

            Provider = new TestServiceProvider(services);
            Dispatcher = Provider.GetRequiredService<IDispatcher>();
            Events = Provider.GetRequiredService<IEventBus>();
        }
    }

    /// <summary>The dispatcher invokes handlers via reflection, which wraps sync throws.</summary>
    private static async Task<Exception> DispatchExceptionAsync(Func<Task> action)
    {
        var ex = await Record.ExceptionAsync(action);
        Assert.NotNull(ex);
        return ex is TargetInvocationException { InnerException: { } inner } ? inner : ex;
    }

    [Fact]
    public async Task GetMyOpenIssues_returns_mapped_task_list_items()
    {
        var harness = new Harness();

        var items = await harness.Dispatcher.QueryAsync(new GetMyOpenIssuesQuery());

        var item = Assert.Single(items);
        Assert.Equal("ABC-1", item.IssueId);
        Assert.Equal("Summary of ABC-1", item.Summary);
        Assert.Equal("https://yt.example.com/issue/ABC-1", item.WebUrl);
    }

    [Fact]
    public async Task Second_identical_query_is_served_from_cache()
    {
        var harness = new Harness();

        await harness.Dispatcher.QueryAsync(new GetMyOpenIssuesQuery());
        await harness.Dispatcher.QueryAsync(new GetMyOpenIssuesQuery());

        Assert.Equal(1, harness.IssueReader.OpenIssuesCalls);
    }

    [Fact]
    public async Task BypassCache_forces_a_refetch()
    {
        var harness = new Harness();

        await harness.Dispatcher.QueryAsync(new GetMyOpenIssuesQuery());
        await harness.Dispatcher.QueryAsync(new GetMyOpenIssuesQuery(BypassCache: true));

        Assert.Equal(2, harness.IssueReader.OpenIssuesCalls);
    }

    [Fact]
    public async Task CreateWorkItem_publishes_event_and_evicts_cache()
    {
        var harness = new Harness();
        var published = new List<WorkItemCreated>();
        harness.Events.Subscribe<WorkItemCreated>(published.Add);

        await harness.Dispatcher.QueryAsync(new GetMyOpenIssuesQuery());
        await harness.Dispatcher.SendAsync(
            new CreateWorkItemCommand("ABC-1", new DateOnly(2026, 7, 6), 60, null, "work")
        );
        await harness.Dispatcher.QueryAsync(new GetMyOpenIssuesQuery());

        var evt = Assert.Single(published);
        Assert.Equal("ABC-1", evt.IssueId);
        Assert.Equal(60, evt.Minutes);
        Assert.Equal(2, harness.IssueReader.OpenIssuesCalls); // cache was evicted
        Assert.Single(harness.Writer.Created);
    }

    [Fact]
    public async Task WorkItemsChanged_evicts_cached_issue_queries()
    {
        var harness = new Harness();

        await harness.Dispatcher.QueryAsync(new GetMyOpenIssuesQuery());
        await harness.Dispatcher.SendAsync(new DeleteWorkItemCommand("ABC-1", "142-9"));
        await harness.Dispatcher.QueryAsync(new GetMyOpenIssuesQuery());

        Assert.Equal(2, harness.IssueReader.OpenIssuesCalls); // cache was evicted
        Assert.Single(harness.Writer.Deleted);
    }

    [Fact]
    public async Task Unknown_query_type_throws_invalid_operation()
    {
        var harness = new Harness();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            harness.Dispatcher.QueryAsync(new UnknownQuery())
        );
    }

    [Fact]
    public async Task Starting_a_timer_twice_throws_invalid_operation()
    {
        var harness = new Harness();

        await harness.Dispatcher.SendAsync(new StartTimerCommand("ABC-1", "Summary of ABC-1"));
        var ex = await DispatchExceptionAsync(() =>
            harness.Dispatcher.SendAsync(new StartTimerCommand("ABC-2", "Another"))
        );

        Assert.IsType<InvalidOperationException>(ex);
    }

    [Fact]
    public async Task Stopping_without_a_running_timer_returns_null()
    {
        var harness = new Harness();

        var result = await harness.Dispatcher.SendAsync(new StopTimerCommand());

        Assert.Null(result);
    }
}
