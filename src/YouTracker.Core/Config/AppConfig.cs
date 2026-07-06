namespace YouTracker.Core.Config;

public sealed record YouTrackConfig(string BaseUrl, string WebBaseUrl, string Token);

public sealed record AnthropicConfig(string ApiKey, string Model);

public sealed record WorkdayConfig(
    double TargetHours,
    string Timezone,
    IReadOnlyList<string> InProgressStates
);

public sealed record AppConfig(
    YouTrackConfig YouTrack,
    AnthropicConfig Anthropic,
    WorkdayConfig Workday
)
{
    public int TargetMinutesPerWorkday => (int)Math.Round(Workday.TargetHours * 60);

    public TimeZoneInfo TimeZone => TimeZoneInfo.FindSystemTimeZoneById(Workday.Timezone);

    public DateOnly Today(TimeProvider time) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(time.GetUtcNow(), TimeZone).DateTime);

    public string WebUrlFor(string issueId) =>
        $"{YouTrack.WebBaseUrl.TrimEnd('/')}/issue/{issueId}";
}
