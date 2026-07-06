using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using YouTracker.Core.Abstractions;
using YouTracker.Core.Config;
using YouTracker.Core.Domain;

namespace YouTracker.Infrastructure.YouTrack;

/// <summary>YouTrack REST adapter implementing the Core issue/work-item ports.</summary>
public sealed class YouTrackClient : IIssueReader, IWorkItemReader, IWorkItemWriter
{
    private const string IssueFields =
        "idReadable,summary,updated,project(shortName),customFields(name,value(name,minutes,presentation))";

    private const string WorkItemFields =
        "id,date,duration(minutes,presentation),type(id,name),text,issue(idReadable,summary),author(login)";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient _http;
    private readonly AppConfig _config;

    private string? _currentLogin;

    /// <summary>Set after the top-level workItems endpoint failed once; avoids re-probing every call.</summary>
    private bool _useIssueScopedWorkItems;

    public YouTrackClient(HttpClient http, AppConfig config)
    {
        _http = http;
        _config = config;
        _http.BaseAddress = new Uri(config.YouTrack.BaseUrl.TrimEnd('/') + "/api/");
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            config.YouTrack.Token
        );
        _http.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json")
        );
    }

    public async Task<IReadOnlyList<Issue>> GetMyOpenIssuesAsync(CancellationToken ct = default)
    {
        var query = Uri.EscapeDataString("for: me #Unresolved sort by: updated desc");
        var issues = await GetAsync<List<IssueDto>>(
            $"issues?query={query}&$top=100&fields={IssueFields}",
            ct
        );
        return issues.Select(MapIssue).ToList();
    }

    public async Task<IReadOnlyList<Issue>> GetMyRecentlyActiveIssuesAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken ct = default
    )
    {
        var query = Uri.EscapeDataString(
            $"updated by: me updated: {FormatDate(from)} .. {FormatDate(to)}"
        );
        var issues = await GetAsync<List<IssueDto>>(
            $"issues?query={query}&$top=50&fields={IssueFields}",
            ct
        );
        return issues.Select(MapIssue).ToList();
    }

    public async Task<IReadOnlyList<WorkItem>> GetMyWorkItemsAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken ct = default
    )
    {
        var login = await GetCurrentLoginAsync(ct);

        if (!_useIssueScopedWorkItems)
        {
            try
            {
                var items = await GetAsync<List<WorkItemDto>>(
                    $"workItems?author={Uri.EscapeDataString(login)}"
                        + $"&startDate={FormatDate(from)}&endDate={FormatDate(to)}"
                        + $"&$top=500&fields={WorkItemFields}",
                    ct
                );
                return items.Select(MapWorkItem).ToList();
            }
            catch (YouTrackApiException ex) when (ex.StatusCode is 400 or 403 or 404)
            {
                _useIssueScopedWorkItems = true;
            }
        }

        // Fallback: find issues with my work in the period, then read their work items.
        var query = Uri.EscapeDataString(
            $"work author: me work date: {FormatDate(from)} .. {FormatDate(to)}"
        );
        var issues = await GetAsync<List<IssueDto>>(
            $"issues?query={query}&fields=idReadable,summary",
            ct
        );

        var result = new List<WorkItem>();
        foreach (var issue in issues)
        {
            var items = await GetAsync<List<WorkItemDto>>(
                $"issues/{issue.IdReadable}/timeTracking/workItems?fields={WorkItemFields}&$top=200",
                ct
            );
            result.AddRange(
                items
                    .Where(i => i.Author?.Login == login)
                    .Select(MapWorkItem)
                    .Where(w => w.Date >= from && w.Date <= to)
            );
        }
        return result;
    }

    public async Task<IReadOnlyList<WorkItemType>> GetWorkItemTypesAsync(
        CancellationToken ct = default
    )
    {
        try
        {
            var types = await GetAsync<List<WorkItemTypeDto>>(
                "admin/timeTrackingSettings/workItemTypes?fields=id,name",
                ct
            );
            return types.Select(t => new WorkItemType(t.Id ?? "", t.Name ?? "")).ToList();
        }
        catch (YouTrackApiException)
        {
            // Not exposed to this user (typically 403/404) — types are optional.
            return Array.Empty<WorkItemType>();
        }
    }

    public async Task<WorkItem> CreateWorkItemAsync(
        NewWorkItem item,
        CancellationToken ct = default
    )
    {
        // Noon local time in the configured timezone avoids DST/date-boundary shifts.
        var localNoon = item.Date.ToDateTime(new TimeOnly(12, 0));
        var epochMs = new DateTimeOffset(
            localNoon,
            _config.TimeZone.GetUtcOffset(localNoon)
        ).ToUnixTimeMilliseconds();

        var body = new Dictionary<string, object?>
        {
            ["date"] = epochMs,
            ["duration"] = new Dictionary<string, object?> { ["minutes"] = item.Minutes },
            ["text"] = item.Text,
        };
        if (item.TypeId is not null)
            body["type"] = new Dictionary<string, object?> { ["id"] = item.TypeId };

        var url = $"issues/{item.IssueId}/timeTracking/workItems?fields={WorkItemFields}";
        using var content = new StringContent(
            JsonSerializer.Serialize(body),
            Encoding.UTF8,
            "application/json"
        );
        using var response = await _http.PostAsync(url, content, ct);
        var responseBody = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new YouTrackApiException((int)response.StatusCode, responseBody, url);

        var created = JsonSerializer.Deserialize<WorkItemDto>(responseBody, JsonOptions)!;
        return MapWorkItem(created);
    }

    private async Task<string> GetCurrentLoginAsync(CancellationToken ct)
    {
        if (_currentLogin is null)
        {
            var me = await GetAsync<UserDto>("users/me?fields=id,login,fullName", ct);
            _currentLogin = me.Login ?? "";
        }
        return _currentLogin;
    }

    private async Task<T> GetAsync<T>(string url, CancellationToken ct)
    {
        using var response = await _http.GetAsync(url, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new YouTrackApiException((int)response.StatusCode, body, url);
        return JsonSerializer.Deserialize<T>(body, JsonOptions)!;
    }

    private static string FormatDate(DateOnly date) =>
        date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private Issue MapIssue(IssueDto dto)
    {
        var id = dto.IdReadable ?? "";
        var fields = dto.CustomFields ?? new List<CustomFieldDto>();

        JsonElement? Field(string name) =>
            fields
                .FirstOrDefault(f =>
                    string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase)
                )
                ?.Value;

        var dash = id.IndexOf('-');
        var projectKey = dto.Project?.ShortName ?? (dash > 0 ? id[..dash] : id);

        return new Issue(
            Id: id,
            Summary: dto.Summary ?? "",
            ProjectKey: projectKey,
            Type: ValueName(Field("Type")),
            State: ValueName(Field("State")),
            Priority: ValueName(Field("Priority")),
            EstimateMinutes: ValueMinutes(Field("Estimation")),
            SpentMinutes: ValueMinutes(Field("Spent time")),
            Updated: DateTimeOffset.FromUnixTimeMilliseconds(dto.Updated)
        );
    }

    private static string? ValueName(JsonElement? value) =>
        value is not { } v
            ? null
            : v.ValueKind switch
            {
                JsonValueKind.Object => v.TryGetProperty("name", out var name)
                && name.ValueKind == JsonValueKind.String
                    ? name.GetString()
                    : null,
                JsonValueKind.Array => v.GetArrayLength() > 0 ? ValueName(v[0]) : null,
                _ => null,
            };

    private static int? ValueMinutes(JsonElement? value) =>
        value is { ValueKind: JsonValueKind.Object } v
        && v.TryGetProperty("minutes", out var minutes)
        && minutes.ValueKind == JsonValueKind.Number
            ? minutes.GetInt32()
            : null;

    private WorkItem MapWorkItem(WorkItemDto dto)
    {
        var local = TimeZoneInfo.ConvertTime(
            DateTimeOffset.FromUnixTimeMilliseconds(dto.Date),
            _config.TimeZone
        );
        return new WorkItem(
            Id: dto.Id ?? "",
            IssueId: dto.Issue?.IdReadable ?? "",
            IssueSummary: dto.Issue?.Summary ?? "",
            Date: DateOnly.FromDateTime(local.DateTime),
            Minutes: dto.Duration?.Minutes ?? 0,
            TypeId: dto.Type?.Id,
            TypeName: dto.Type?.Name,
            Text: dto.Text,
            AuthorLogin: dto.Author?.Login
        );
    }
}
