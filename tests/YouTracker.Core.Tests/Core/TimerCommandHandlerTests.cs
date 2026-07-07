using YouTracker.Core.Abstractions;
using YouTracker.Core.Application;
using YouTracker.Core.Application.Handlers;

namespace YouTracker.Core.Tests.Core;

public class TimerCommandHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 6, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Stop_returns_elapsed_but_keeps_the_timer_store()
    {
        var store = new InMemoryTimerStore();
        store.Save(new TimerState("ABC-1", "Summary", Now.AddMinutes(-45)));
        var handler = new StopTimerCommandHandler(
            store,
            new FakeTimeProvider(Now),
            TestData.Config()
        );

        var result = await handler.HandleAsync(new StopTimerCommand());

        Assert.NotNull(result);
        Assert.Equal("ABC-1", result.IssueId);
        Assert.Equal(45, result.ElapsedMinutes);
        // Cancelling the log dialog must not lose the elapsed time.
        Assert.NotNull(store.Load());
    }

    [Fact]
    public async Task Discard_clears_the_store_and_publishes_TimerStopped()
    {
        var store = new InMemoryTimerStore();
        store.Save(new TimerState("ABC-1", "Summary", Now.AddMinutes(-30)));
        var events = new EventBus();
        TimerStopped? published = null;
        using var sub = events.Subscribe<TimerStopped>(e => published = e);
        var handler = new DiscardTimerCommandHandler(store, new FakeTimeProvider(Now), events);

        var discarded = await handler.HandleAsync(new DiscardTimerCommand());

        Assert.True(discarded);
        Assert.Null(store.Load());
        Assert.NotNull(published);
        Assert.Equal("ABC-1", published.IssueId);
        Assert.Equal(30, published.ElapsedMinutes);
    }

    [Fact]
    public async Task Discard_without_running_timer_returns_false()
    {
        var handler = new DiscardTimerCommandHandler(
            new InMemoryTimerStore(),
            new FakeTimeProvider(Now),
            new EventBus()
        );

        Assert.False(await handler.HandleAsync(new DiscardTimerCommand()));
    }
}
