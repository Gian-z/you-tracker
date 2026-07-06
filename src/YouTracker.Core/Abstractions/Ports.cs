using YouTracker.Core.Domain;

namespace YouTracker.Core.Abstractions;

/// <summary>Read port for issues (implemented by the YouTrack module; replaceable).</summary>
public interface IIssueReader
{
    /// <summary>
    /// Open issues the dev is involved in (assignee OR work author).
    /// <paramref name="devLogin"/> null means the current user.
    /// </summary>
    Task<IReadOnlyList<Issue>> GetOpenIssuesAsync(string? devLogin, CancellationToken ct = default);

    /// <summary>Issues the dev touched (updated) in the given period — gap-fill candidates.</summary>
    Task<IReadOnlyList<Issue>> GetRecentlyActiveIssuesAsync(
        string? devLogin,
        DateOnly from,
        DateOnly to,
        CancellationToken ct = default
    );

    /// <summary>
    /// Sprint tasks NOT belonging to the dev (candidate pool for triage suggestions).
    /// Empty when no pool query is configured.
    /// </summary>
    Task<IReadOnlyList<Issue>> GetSprintPoolIssuesAsync(
        string? devLogin,
        CancellationToken ct = default
    );
}

/// <summary>Read port for work items (time bookings) and their types.</summary>
public interface IWorkItemReader
{
    /// <summary>Work items authored by the dev (<paramref name="devLogin"/> null = current user).</summary>
    Task<IReadOnlyList<WorkItem>> GetWorkItemsAsync(
        string? devLogin,
        DateOnly from,
        DateOnly to,
        CancellationToken ct = default
    );

    /// <summary>Available work item types; empty when the instance does not expose them to this user.</summary>
    Task<IReadOnlyList<WorkItemType>> GetWorkItemTypesAsync(CancellationToken ct = default);
}

/// <summary>Read port for the tracker's user directory.</summary>
public interface IUserDirectory
{
    Task<UserInfo> GetCurrentUserAsync(CancellationToken ct = default);

    /// <summary>All selectable users; empty when the token lacks permission to list users.</summary>
    Task<IReadOnlyList<UserInfo>> GetUsersAsync(CancellationToken ct = default);
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

/// <summary>A saved standard booking (recurring meetings etc.) for one-click logging.</summary>
public sealed record BookingPreset(
    string Id,
    string Name,
    string IssueId,
    string IssueSummary,
    int Minutes,
    string? TypeId,
    string? TypeName,
    string? Comment
);

/// <summary>Persistence port for booking presets (local file; replaceable).</summary>
public interface IPresetStore
{
    IReadOnlyList<BookingPreset> Load();
    void Save(IReadOnlyList<BookingPreset> presets);
}

/// <summary>Port for loading app configuration.</summary>
public interface IConfigStore
{
    string ConfigPath { get; }
    bool Exists { get; }
    Config.AppConfig Load();
    string Template { get; }
}
