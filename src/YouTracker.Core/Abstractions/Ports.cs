using YouTracker.Core.Domain;

namespace YouTracker.Core.Abstractions;

/// <summary>Read port for issues (implemented by the YouTrack module; replaceable).</summary>
public interface IIssueReader
{
    Task<IReadOnlyList<Issue>> GetMyOpenIssuesAsync(CancellationToken ct = default);

    /// <summary>Issues the current user touched (updated/commented) in the given period — gap-fill candidates.</summary>
    Task<IReadOnlyList<Issue>> GetMyRecentlyActiveIssuesAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken ct = default
    );
}

/// <summary>Read port for work items (time bookings) and their types.</summary>
public interface IWorkItemReader
{
    Task<IReadOnlyList<WorkItem>> GetMyWorkItemsAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken ct = default
    );

    /// <summary>Available work item types; empty when the instance does not expose them to this user.</summary>
    Task<IReadOnlyList<WorkItemType>> GetWorkItemTypesAsync(CancellationToken ct = default);
}

/// <summary>The only port that writes to the tracker. AI query handlers never receive this.</summary>
public interface IWorkItemWriter
{
    Task<WorkItem> CreateWorkItemAsync(NewWorkItem item, CancellationToken ct = default);
}

public sealed record NewWorkItem(
    string IssueId,
    DateOnly Date,
    int Minutes,
    string? TypeId,
    string? Text
);

/// <summary>LLM port. Providers return raw text/JSON; parsing and validation stay in Core.</summary>
public interface IAiProvider
{
    Task<string> CompleteTextAsync(
        string systemPrompt,
        string userPrompt,
        CancellationToken ct = default
    );

    /// <summary>Returns a raw JSON string conforming to <paramref name="jsonSchema"/>.</summary>
    Task<string> CompleteJsonAsync(
        string systemPrompt,
        string userPrompt,
        string jsonSchema,
        CancellationToken ct = default
    );
}

public sealed record TimerState(string IssueId, string IssueSummary, DateTimeOffset StartedUtc);

/// <summary>Persistence port for the single running timer (survives app restarts).</summary>
public interface ITimerStore
{
    TimerState? Load();
    void Save(TimerState state);
    void Clear();
}

/// <summary>Port for loading app configuration.</summary>
public interface IConfigStore
{
    string ConfigPath { get; }
    bool Exists { get; }
    Config.AppConfig Load();
    string Template { get; }
}
