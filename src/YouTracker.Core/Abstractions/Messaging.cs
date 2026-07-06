namespace YouTracker.Core.Abstractions;

/// <summary>Marker for a read-only use case returning <typeparamref name="TResult"/>. Queries never change state.</summary>
public interface IQuery<TResult>;

/// <summary>Marker for a state-changing use case returning <typeparamref name="TResult"/>.</summary>
public interface ICommand<TResult>;

public interface IQueryHandler<in TQuery, TResult>
    where TQuery : IQuery<TResult>
{
    Task<TResult> HandleAsync(TQuery query, CancellationToken ct = default);
}

public interface ICommandHandler<in TCommand, TResult>
    where TCommand : ICommand<TResult>
{
    Task<TResult> HandleAsync(TCommand command, CancellationToken ct = default);
}

/// <summary>Single entry point for frontends: routes queries/commands to their registered handler.</summary>
public interface IDispatcher
{
    Task<TResult> QueryAsync<TResult>(IQuery<TResult> query, CancellationToken ct = default);
    Task<TResult> SendAsync<TResult>(ICommand<TResult> command, CancellationToken ct = default);
}

/// <summary>Opt-in caching for queries; honored by the caching dispatcher decorator.</summary>
public interface ICacheableQuery
{
    string CacheKey { get; }
    TimeSpan Ttl { get; }
    bool BypassCache { get; }
}
