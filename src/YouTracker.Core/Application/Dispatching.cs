using System.Collections.Concurrent;
using YouTracker.Core.Abstractions;

namespace YouTracker.Core.Application;

/// <summary>Resolves the single registered handler for a query/command and invokes it.</summary>
public sealed class Dispatcher(IServiceProvider services) : IDispatcher
{
    public Task<TResult> QueryAsync<TResult>(
        IQuery<TResult> query,
        CancellationToken ct = default
    ) => InvokeAsync<TResult>(typeof(IQueryHandler<,>), query, ct);

    public Task<TResult> SendAsync<TResult>(
        ICommand<TResult> command,
        CancellationToken ct = default
    ) => InvokeAsync<TResult>(typeof(ICommandHandler<,>), command, ct);

    private Task<TResult> InvokeAsync<TResult>(
        Type openHandlerType,
        object message,
        CancellationToken ct
    )
    {
        var handlerType = openHandlerType.MakeGenericType(message.GetType(), typeof(TResult));
        var handler =
            services.GetService(handlerType)
            ?? throw new InvalidOperationException(
                $"No handler registered for {message.GetType().Name}."
            );
        var handleMethod = handlerType.GetMethod("HandleAsync")!;
        try
        {
            return (Task<TResult>)handleMethod.Invoke(handler, [message, ct])!;
        }
        catch (System.Reflection.TargetInvocationException tie)
            when (tie.InnerException is not null)
        {
            // Surface the handler's own exception instead of the reflection wrapper.
            System
                .Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(tie.InnerException)
                .Throw();
            throw; // unreachable
        }
    }
}

/// <summary>
/// Decorator: serves <see cref="ICacheableQuery"/> results from a TTL cache and evicts
/// time-sensitive entries when a work item is created.
/// </summary>
public sealed class CachingDispatcher : IDispatcher
{
    private readonly IDispatcher _inner;
    private readonly TtlCache _cache;

    public CachingDispatcher(IDispatcher inner, TtlCache cache, IEventBus events)
    {
        _inner = inner;
        _cache = cache;
        events.Subscribe<WorkItemCreated>(_ =>
        {
            _cache.EvictByPrefix("workitems:");
            _cache.EvictByPrefix("issues:");
        });
    }

    public async Task<TResult> QueryAsync<TResult>(
        IQuery<TResult> query,
        CancellationToken ct = default
    )
    {
        if (query is not ICacheableQuery cacheable)
            return await _inner.QueryAsync(query, ct).ConfigureAwait(false);

        if (!cacheable.BypassCache && _cache.TryGet<TResult>(cacheable.CacheKey, out var cached))
            return cached;

        var result = await _inner.QueryAsync(query, ct).ConfigureAwait(false);
        _cache.Set(cacheable.CacheKey, result, cacheable.Ttl);
        return result;
    }

    public Task<TResult> SendAsync<TResult>(
        ICommand<TResult> command,
        CancellationToken ct = default
    ) => _inner.SendAsync(command, ct);
}

public sealed class TtlCache(TimeProvider time)
{
    private readonly ConcurrentDictionary<
        string,
        (object? Value, DateTimeOffset Expires)
    > _entries = new();

    public bool TryGet<T>(string key, out T value)
    {
        if (_entries.TryGetValue(key, out var entry))
        {
            if (entry.Expires > time.GetUtcNow() && entry.Value is T typed)
            {
                value = typed;
                return true;
            }
            _entries.TryRemove(key, out _);
        }
        value = default!;
        return false;
    }

    public void Set<T>(string key, T value, TimeSpan ttl) =>
        _entries[key] = (value, time.GetUtcNow() + ttl);

    public void EvictByPrefix(string prefix)
    {
        foreach (
            var key in _entries.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal))
        )
            _entries.TryRemove(key, out _);
    }
}
