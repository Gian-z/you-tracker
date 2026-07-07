namespace YouTracker.Core.Config;

/// <summary>
/// <paramref name="IssueQuery"/> / <paramref name="SprintPoolQuery"/> are optional YouTrack query
/// templates with a <c>$dev</c> placeholder (replaced by "me" or the selected login).
/// IssueQuery drives the ticket list and AI context; SprintPoolQuery defines the candidate pool
/// (other devs' sprint tasks) that triage may suggest from. Null → built-in involvement query / no pool.
/// </summary>
public sealed record YouTrackConfig(
    string BaseUrl,
    string WebBaseUrl,
    string Token,
    string? IssueQuery = null,
    string? SprintPoolQuery = null,
    IReadOnlyList<string>? FeatureTypes = null,
    IReadOnlyList<string>? TaskTypes = null
);

public sealed record AnthropicConfig(string ApiKey, string Model, string CliCommand = "claude")
{
    /// <summary>
    /// True when a real Anthropic API key is configured (not empty and not a template
    /// placeholder). When false, hosts fall back to the Claude Code CLI provider.
    /// </summary>
    public bool HasApiKey =>
        !string.IsNullOrWhiteSpace(ApiKey)
        && !ApiKey.Contains("PASTE", StringComparison.OrdinalIgnoreCase)
        && ApiKey != "sk-ant-...";
}

public sealed record WorkdayConfig(
    double TargetHours,
    string Timezone,
    IReadOnlyList<string> InProgressStates
);

/// <summary>
/// Local git activity: every repo found under <paramref name="ScanRoots"/> is scanned for the
/// user's commits (author defaults to `git config user.email`). Empty roots disable the feature.
/// </summary>
public sealed record GitConfig(IReadOnlyList<string> ScanRoots, string? Author = null);

public sealed record AppConfig(
    YouTrackConfig YouTrack,
    AnthropicConfig Anthropic,
    WorkdayConfig Workday,
    GitConfig? Git = null
)
{
    public int TargetMinutesPerWorkday => (int)Math.Round(Workday.TargetHours * 60);

    public TimeZoneInfo TimeZone => TimeZoneInfo.FindSystemTimeZoneById(Workday.Timezone);

    public DateOnly Today(TimeProvider time) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(time.GetUtcNow(), TimeZone).DateTime);

    public string WebUrlFor(string issueId) =>
        $"{YouTrack.WebBaseUrl.TrimEnd('/')}/issue/{issueId}";

    private static readonly string[] DefaultFeatureTypes = ["Feature"];
    private static readonly string[] DefaultTaskTypes = ["Task", "Aufgabe"];

    /// <summary>Issue types whose bookings are redirected to a task subtask.</summary>
    public IReadOnlyList<string> EffectiveFeatureTypes =>
        YouTrack.FeatureTypes is { Count: > 0 } configured ? configured : DefaultFeatureTypes;

    /// <summary>Issue types that count as bookable task subtasks (German instance default included).</summary>
    public IReadOnlyList<string> EffectiveTaskTypes =>
        YouTrack.TaskTypes is { Count: > 0 } configured ? configured : DefaultTaskTypes;
}
