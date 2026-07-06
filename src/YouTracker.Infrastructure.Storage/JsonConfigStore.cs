using System.Text.Json;
using YouTracker.Core.Abstractions;
using YouTracker.Core.Config;

namespace YouTracker.Infrastructure.Storage;

/// <summary>
/// Loads <see cref="AppConfig"/> from <c>config.json</c> (camelCase JSON). Secrets can be
/// supplied/overridden via the <c>YOUTRACK_TOKEN</c> and <c>ANTHROPIC_API_KEY</c> environment
/// variables. Missing optional fields fall back to sensible defaults.
/// </summary>
public sealed class JsonConfigStore : IConfigStore
{
    private const string DefaultModel = "claude-opus-4-8";
    private const double DefaultTargetHours = 8.0;
    private const string DefaultTimezone = "Europe/Zurich";

    // The cmiag instance mixes localized state names: REST returns e.g. "In progress"
    // for some projects and German names for others.
    private static readonly string[] DefaultInProgressStates =
    [
        "In Bearbeitung",
        "In Arbeit",
        "In progress",
    ];

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public JsonConfigStore(string? directory = null) =>
        ConfigPath = Path.Combine(directory ?? StoragePaths.AppDataDir, "config.json");

    public string ConfigPath { get; }

    public bool Exists => File.Exists(ConfigPath);

    public AppConfig Load()
    {
        if (!Exists)
            throw new InvalidOperationException(
                $"Config file not found at '{ConfigPath}'. Create it from the template (see Template)."
            );

        ConfigDto dto;
        try
        {
            dto =
                JsonSerializer.Deserialize<ConfigDto>(File.ReadAllText(ConfigPath), Options)
                ?? new ConfigDto();
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"Config file at '{ConfigPath}' is not valid JSON: {ex.Message}",
                ex
            );
        }

        var token = FirstNonEmpty(
            Environment.GetEnvironmentVariable("YOUTRACK_TOKEN"),
            dto.YouTrack?.Token
        );
        var apiKey = FirstNonEmpty(
            Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY"),
            dto.Anthropic?.ApiKey
        );

        return new AppConfig(
            new YouTrackConfig(
                Require(dto.YouTrack?.BaseUrl, "youTrack.baseUrl"),
                Require(dto.YouTrack?.WebBaseUrl, "youTrack.webBaseUrl"),
                Require(token, "youTrack.token (or the YOUTRACK_TOKEN environment variable)"),
                FirstNonEmpty(dto.YouTrack?.IssueQuery, null),
                FirstNonEmpty(dto.YouTrack?.SprintPoolQuery, null)
            ),
            // apiKey is optional: without one, the host uses the Claude Code CLI provider.
            new AnthropicConfig(
                apiKey ?? string.Empty,
                FirstNonEmpty(dto.Anthropic?.Model, null) ?? DefaultModel,
                FirstNonEmpty(dto.Anthropic?.CliCommand, null) ?? "claude"
            ),
            new WorkdayConfig(
                dto.Workday?.TargetHours ?? DefaultTargetHours,
                FirstNonEmpty(dto.Workday?.Timezone, null) ?? DefaultTimezone,
                dto.Workday?.InProgressStates is { Count: > 0 } states
                    ? states
                    : DefaultInProgressStates
            )
        );
    }

    public string Template =>
        """
            {
              "youTrack": { "baseUrl": "https://cmiag.myjetbrains.com/youtrack", "webBaseUrl": "https://cmiag.youtrack.cloud", "token": "perm:...", "issueQuery": "", "sprintPoolQuery": "" },
              "anthropic": { "apiKey": "", "model": "claude-opus-4-8" },
              "workday": { "targetHours": 8.0, "timezone": "Europe/Zurich", "inProgressStates": ["In Bearbeitung", "In Arbeit", "In progress"] }
            }
            """;

    private string Require(string? value, string field) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException(
                $"Missing required config value '{field}' in '{ConfigPath}'."
            )
            : value;

    private static string? FirstNonEmpty(string? first, string? second) =>
        !string.IsNullOrWhiteSpace(first) ? first
        : !string.IsNullOrWhiteSpace(second) ? second
        : null;

    private sealed class ConfigDto
    {
        public YouTrackDto? YouTrack { get; set; }
        public AnthropicDto? Anthropic { get; set; }
        public WorkdayDto? Workday { get; set; }
    }

    private sealed class YouTrackDto
    {
        public string? BaseUrl { get; set; }
        public string? WebBaseUrl { get; set; }
        public string? Token { get; set; }

        /// <summary>Optional query template with $dev placeholder driving the ticket list.</summary>
        public string? IssueQuery { get; set; }

        /// <summary>Optional query template with $dev placeholder for triage sprint suggestions.</summary>
        public string? SprintPoolQuery { get; set; }
    }

    private sealed class AnthropicDto
    {
        public string? ApiKey { get; set; }
        public string? Model { get; set; }
        public string? CliCommand { get; set; }
    }

    private sealed class WorkdayDto
    {
        public double? TargetHours { get; set; }
        public string? Timezone { get; set; }
        public List<string>? InProgressStates { get; set; }
    }
}
