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
                FirstNonEmpty(dto.YouTrack?.SprintPoolQuery, null),
                dto.YouTrack?.FeatureTypes,
                dto.YouTrack?.TaskTypes,
                FirstNonEmpty(dto.YouTrack?.SprintQuery, null)
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
            ),
            new GitConfig(dto.Git?.ScanRoots ?? [], FirstNonEmpty(dto.Git?.Author, null)),
            dto.Calendar is null
                ? null
                : new CalendarConfig(
                    FirstNonEmpty(dto.Calendar.IcsUrl, null),
                    dto.Calendar.Rules?.Where(r =>
                            !string.IsNullOrWhiteSpace(r.Pattern)
                            && !string.IsNullOrWhiteSpace(r.IssueId)
                        )
                        .Select(r => new MeetingRule(
                            r.Pattern!,
                            r.IssueId!,
                            FirstNonEmpty(r.WorkTypeName, null),
                            FirstNonEmpty(r.Comment, null)
                        ))
                        .ToList()
                )
        );
    }

    /// <summary>
    /// Persists the config in the same camelCase shape <see cref="Load"/> reads. Written via a
    /// temp file + move so a crash can't truncate the file (it holds the YouTrack token).
    /// Note: values supplied via YOUTRACK_TOKEN / ANTHROPIC_API_KEY env vars arrive here as the
    /// effective values and get baked into the file — acceptable for this local settings flow.
    /// </summary>
    public void Save(AppConfig config)
    {
        var dto = new ConfigDto
        {
            YouTrack = new YouTrackDto
            {
                BaseUrl = config.YouTrack.BaseUrl,
                WebBaseUrl = config.YouTrack.WebBaseUrl,
                Token = config.YouTrack.Token,
                IssueQuery = config.YouTrack.IssueQuery,
                SprintPoolQuery = config.YouTrack.SprintPoolQuery,
                FeatureTypes = config.YouTrack.FeatureTypes?.ToList(),
                TaskTypes = config.YouTrack.TaskTypes?.ToList(),
                SprintQuery = config.YouTrack.SprintQuery,
            },
            Anthropic = new AnthropicDto
            {
                ApiKey = config.Anthropic.ApiKey,
                Model = config.Anthropic.Model,
                CliCommand = config.Anthropic.CliCommand,
            },
            Workday = new WorkdayDto
            {
                TargetHours = config.Workday.TargetHours,
                Timezone = config.Workday.Timezone,
                InProgressStates = config.Workday.InProgressStates.ToList(),
            },
            Git = config.Git is null
                ? null
                : new GitDto
                {
                    ScanRoots = config.Git.ScanRoots.ToList(),
                    Author = config.Git.Author,
                },
            Calendar = config.Calendar is null
                ? null
                : new CalendarDto
                {
                    IcsUrl = config.Calendar.IcsUrl,
                    Rules = config
                        .Calendar.Rules?.Select(r => new MeetingRuleDto
                        {
                            Pattern = r.Pattern,
                            IssueId = r.IssueId,
                            WorkTypeName = r.WorkTypeName,
                            Comment = r.Comment,
                        })
                        .ToList(),
                },
        };

        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
        var tempPath = ConfigPath + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(dto, WriteOptions));
        File.Move(tempPath, ConfigPath, overwrite: true);
    }

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    public string Template =>
        """
            {
              "youTrack": { "baseUrl": "https://cmiag.myjetbrains.com/youtrack", "webBaseUrl": "https://cmiag.youtrack.cloud", "token": "perm:...", "issueQuery": "", "sprintPoolQuery": "", "sprintQuery": "", "featureTypes": ["Feature"], "taskTypes": ["Task", "Aufgabe"] },
              "anthropic": { "apiKey": "", "model": "claude-opus-4-8" },
              "workday": { "targetHours": 8.0, "timezone": "Europe/Zurich", "inProgressStates": ["In Bearbeitung", "In Arbeit", "In progress"] },
              "git": { "scanRoots": ["C:/cmi-github"], "author": "" },
              "calendar": { "icsUrl": "https://outlook.office365.com/owa/calendar/.../calendar.ics", "rules": [ { "pattern": "Daily*", "issueId": "AD-4711", "workTypeName": "Meeting", "comment": "Daily" } ] }
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
        public GitDto? Git { get; set; }
        public CalendarDto? Calendar { get; set; }
    }

    private sealed class CalendarDto
    {
        /// <summary>Published Outlook ICS feed URL ("Kalender veröffentlichen").</summary>
        public string? IcsUrl { get; set; }
        public List<MeetingRuleDto>? Rules { get; set; }
    }

    private sealed class MeetingRuleDto
    {
        public string? Pattern { get; set; }
        public string? IssueId { get; set; }
        public string? WorkTypeName { get; set; }
        public string? Comment { get; set; }
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

        /// <summary>Issue types treated as Features (bookings get redirected to a task subtask).</summary>
        public List<string>? FeatureTypes { get; set; }

        /// <summary>Issue types that count as bookable task subtasks.</summary>
        public List<string>? TaskTypes { get; set; }

        /// <summary>Optional query for ALL current-sprint tickets (colleagues' included).</summary>
        public string? SprintQuery { get; set; }
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

    private sealed class GitDto
    {
        public List<string>? ScanRoots { get; set; }
        public string? Author { get; set; }
    }
}
