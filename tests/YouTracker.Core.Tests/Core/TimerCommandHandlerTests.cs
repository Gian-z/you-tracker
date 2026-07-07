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

    [Fact]
    public async Task Pause_accumulates_elapsed_and_persists_pause_marker()
    {
        var store = new InMemoryTimerStore();
        store.Save(new TimerState("ABC-1", "s", Now.AddMinutes(-45)));
        var handler = new PauseTimerCommandHandler(store, new FakeTimeProvider(Now));

        var paused = await handler.HandleAsync(new PauseTimerCommand());

        Assert.NotNull(paused);
        Assert.Equal(45 * 60, paused.AccumulatedSeconds);
        Assert.Equal(Now, paused.PausedAtUtc);
        Assert.Equal(paused, store.Load());
    }

    [Fact]
    public async Task Pause_when_already_paused_is_a_noop()
    {
        var store = new InMemoryTimerStore();
        var alreadyPaused = new TimerState(
            "ABC-1",
            "s",
            Now.AddHours(-2),
            600,
            Now.AddMinutes(-30)
        );
        store.Save(alreadyPaused);
        var handler = new PauseTimerCommandHandler(store, new FakeTimeProvider(Now));

        Assert.Equal(alreadyPaused, await handler.HandleAsync(new PauseTimerCommand()));
        Assert.Equal(alreadyPaused, store.Load());
    }

    [Fact]
    public async Task Pause_and_resume_without_timer_return_null()
    {
        var store = new InMemoryTimerStore();
        var time = new FakeTimeProvider(Now);

        Assert.Null(
            await new PauseTimerCommandHandler(store, time).HandleAsync(new PauseTimerCommand())
        );
        Assert.Null(
            await new ResumeTimerCommandHandler(store, time).HandleAsync(new ResumeTimerCommand())
        );
    }

    [Fact]
    public async Task Resume_rebases_started_and_keeps_accumulated()
    {
        var store = new InMemoryTimerStore();
        store.Save(new TimerState("ABC-1", "s", Now.AddHours(-3), 600, Now.AddMinutes(-30)));
        var handler = new ResumeTimerCommandHandler(store, new FakeTimeProvider(Now));

        var resumed = await handler.HandleAsync(new ResumeTimerCommand());

        Assert.NotNull(resumed);
        Assert.Equal(Now, resumed.StartedUtc);
        Assert.Null(resumed.PausedAtUtc);
        Assert.Equal(600, resumed.AccumulatedSeconds);
    }

    [Fact]
    public async Task Resume_when_running_is_a_noop()
    {
        var store = new InMemoryTimerStore();
        var running = new TimerState("ABC-1", "s", Now.AddMinutes(-10));
        store.Save(running);
        var handler = new ResumeTimerCommandHandler(store, new FakeTimeProvider(Now));

        Assert.Equal(running, await handler.HandleAsync(new ResumeTimerCommand()));
    }

    [Fact]
    public async Task Stop_on_paused_timer_counts_only_accumulated_time()
    {
        var store = new InMemoryTimerStore();
        // StartedUtc two hours ago must NOT count — the timer is paused.
        store.Save(new TimerState("ABC-1", "s", Now.AddHours(-2), 1500, Now.AddMinutes(-90)));
        var handler = new StopTimerCommandHandler(
            store,
            new FakeTimeProvider(Now),
            TestData.Config()
        );

        var result = await handler.HandleAsync(new StopTimerCommand());

        Assert.Equal(25, result?.ElapsedMinutes);
    }

    [Fact]
    public async Task Stop_sums_accumulated_and_running_segment()
    {
        var store = new InMemoryTimerStore();
        store.Save(new TimerState("ABC-1", "s", Now.AddMinutes(-20), 600));
        var handler = new StopTimerCommandHandler(
            store,
            new FakeTimeProvider(Now),
            TestData.Config()
        );

        var result = await handler.HandleAsync(new StopTimerCommand());

        Assert.Equal(30, result?.ElapsedMinutes);
    }

    [Fact]
    public async Task Discard_on_paused_timer_publishes_accumulated_minutes()
    {
        var store = new InMemoryTimerStore();
        store.Save(new TimerState("ABC-1", "s", Now.AddHours(-5), 1200, Now.AddMinutes(-10)));
        var events = new EventBus();
        TimerStopped? published = null;
        using var sub = events.Subscribe<TimerStopped>(e => published = e);
        var handler = new DiscardTimerCommandHandler(store, new FakeTimeProvider(Now), events);

        Assert.True(await handler.HandleAsync(new DiscardTimerCommand()));
        Assert.Equal(20, published?.ElapsedMinutes);
    }
}
