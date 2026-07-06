using System.Collections.Concurrent;

namespace YouTracker.Core.Abstractions;

public interface IAppEvent;

public sealed record WorkItemCreated(string IssueId, DateOnly Date, int Minutes) : IAppEvent;

public sealed record TimerStarted(string IssueId, DateTimeOffset StartedUtc) : IAppEvent;

public sealed record TimerStopped(string IssueId, string IssueSummary, int ElapsedMinutes)
    : IAppEvent;

/// <summary>Minimal in-process pub/sub used for cache invalidation and UI refresh.</summary>
public interface IEventBus
{
    void Publish<TEvent>(TEvent evt)
        where TEvent : IAppEvent;
    IDisposable Subscribe<TEvent>(Action<TEvent> handler)
        where TEvent : IAppEvent;
}

public sealed class EventBus : IEventBus
{
    private readonly ConcurrentDictionary<Type, List<Delegate>> _subscribers = new();

    public void Publish<TEvent>(TEvent evt)
        where TEvent : IAppEvent
    {
        if (!_subscribers.TryGetValue(typeof(TEvent), out var handlers))
            return;
        Delegate[] snapshot;
        lock (handlers)
            snapshot = [.. handlers];
        foreach (var handler in snapshot)
            ((Action<TEvent>)handler).Invoke(evt);
    }

    public IDisposable Subscribe<TEvent>(Action<TEvent> handler)
        where TEvent : IAppEvent
    {
        var handlers = _subscribers.GetOrAdd(typeof(TEvent), _ => []);
        lock (handlers)
            handlers.Add(handler);
        return new Subscription(() =>
        {
            lock (handlers)
                handlers.Remove(handler);
        });
    }

    private sealed class Subscription(Action dispose) : IDisposable
    {
        public void Dispose() => dispose();
    }
}
