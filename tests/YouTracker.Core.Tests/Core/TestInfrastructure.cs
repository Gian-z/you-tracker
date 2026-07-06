using Microsoft.Extensions.DependencyInjection;
using YouTracker.Core.Abstractions;
using YouTracker.Core.Config;
using YouTracker.Core.Domain;

namespace YouTracker.Core.Tests.Core;

/// <summary>Fixed-clock TimeProvider for deterministic tests.</summary>
public sealed class FakeTimeProvider(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;
}

/// <summary>
/// Minimal service provider over an IServiceCollection (last registration wins,
/// singletons cached). The concrete Microsoft DI container package is not referenced
/// by the test project, so this stands in for it.
/// </summary>
public sealed class TestServiceProvider : IServiceProvider
{
    private readonly Dictionary<Type, ServiceDescriptor> _descriptors = [];
    private readonly Dictionary<ServiceDescriptor, object> _singletons = [];

    public TestServiceProvider(IServiceCollection services)
    {
        foreach (var descriptor in services)
            _descriptors[descriptor.ServiceType] = descriptor;
    }

    public object? GetService(Type serviceType)
    {
        if (serviceType == typeof(IServiceProvider))
            return this;
        if (!_descriptors.TryGetValue(serviceType, out var descriptor))
            return null;
        if (
            descriptor.Lifetime == ServiceLifetime.Singleton
            && _singletons.TryGetValue(descriptor, out var cached)
        )
            return cached;

        var instance =
            descriptor.ImplementationInstance
            ?? descriptor.ImplementationFactory?.Invoke(this)
            ?? ActivatorUtilities.CreateInstance(this, descriptor.ImplementationType!);
        if (descriptor.Lifetime == ServiceLifetime.Singleton)
            _singletons[descriptor] = instance;
        return instance;
    }
}

public sealed class FakeIssueReader(params Issue[] issues) : IIssueReader
{
    public int OpenIssuesCalls { get; private set; }
    public List<Issue> Issues { get; } = [.. issues];

    public Task<IReadOnlyList<Issue>> GetMyOpenIssuesAsync(CancellationToken ct = default)
    {
        OpenIssuesCalls++;
        return Task.FromResult<IReadOnlyList<Issue>>(Issues);
    }

    public Task<IReadOnlyList<Issue>> GetMyRecentlyActiveIssuesAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken ct = default
    ) => Task.FromResult<IReadOnlyList<Issue>>(Issues);
}

public sealed class FakeWorkItemReader : IWorkItemReader
{
    public List<WorkItem> WorkItems { get; } = [];
    public List<WorkItemType> Types { get; } = [];

    public Task<IReadOnlyList<WorkItem>> GetMyWorkItemsAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken ct = default
    ) =>
        Task.FromResult<IReadOnlyList<WorkItem>>(
            [.. WorkItems.Where(w => w.Date >= from && w.Date <= to)]
        );

    public Task<IReadOnlyList<WorkItemType>> GetWorkItemTypesAsync(
        CancellationToken ct = default
    ) => Task.FromResult<IReadOnlyList<WorkItemType>>(Types);
}

public sealed class FakeWorkItemWriter : IWorkItemWriter
{
    public List<NewWorkItem> Created { get; } = [];

    public Task<WorkItem> CreateWorkItemAsync(NewWorkItem item, CancellationToken ct = default)
    {
        Created.Add(item);
        return Task.FromResult(
            new WorkItem(
                $"wi-{Created.Count}",
                item.IssueId,
                "summary",
                item.Date,
                item.Minutes,
                item.TypeId,
                null,
                item.Text,
                "me"
            )
        );
    }
}

public sealed class InMemoryTimerStore : ITimerStore
{
    private TimerState? _state;

    public TimerState? Load() => _state;

    public void Save(TimerState state) => _state = state;

    public void Clear() => _state = null;
}

public sealed class FakeAiProvider(string response = "{}") : IAiProvider
{
    public Task<string> CompleteTextAsync(
        string systemPrompt,
        string userPrompt,
        CancellationToken ct = default
    ) => Task.FromResult(response);

    public Task<string> CompleteJsonAsync(
        string systemPrompt,
        string userPrompt,
        string jsonSchema,
        CancellationToken ct = default
    ) => Task.FromResult(response);
}

public static class TestData
{
    public static AppConfig Config() =>
        new(
            new YouTrackConfig(
                "https://yt.example.com/youtrack",
                "https://yt.example.com",
                "perm:test"
            ),
            new AnthropicConfig("sk-ant-test", "claude-opus-4-8"),
            new WorkdayConfig(8.0, "Europe/Zurich", ["In Bearbeitung", "In Arbeit"])
        );

    public static Issue Issue(
        string id,
        string? state = "Open",
        int? estimate = null,
        int? spent = null,
        DateTimeOffset? updated = null
    ) =>
        new(
            id,
            $"Summary of {id}",
            id.Split('-')[0],
            "Task",
            state,
            "Normal",
            estimate,
            spent,
            updated ?? new DateTimeOffset(2026, 7, 5, 12, 0, 0, TimeSpan.Zero)
        );

    public static WorkItem WorkItem(
        string issueId,
        DateOnly date,
        int minutes,
        string? id = null
    ) =>
        new(
            id ?? Guid.NewGuid().ToString("N"),
            issueId,
            $"Summary of {issueId}",
            date,
            minutes,
            null,
            null,
            null,
            "me"
        );
}
