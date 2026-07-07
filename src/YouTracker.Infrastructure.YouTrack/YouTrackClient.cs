using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using YouTracker.Core.Abstractions;
using YouTracker.Core.Config;
using YouTracker.Core.Domain;

namespace YouTracker.Infrastructure.YouTrack;

/// <summary>YouTrack REST adapter implementing the Core issue/work-item/user/sprint ports.</summary>
public sealed class YouTrackClient
    : IIssueReader,
        IWorkItemReader,
        IWorkItemWriter,
        IUserDirectory,
        ISprintReader
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

    public async Task<IReadOnlyList<Issue>> GetOpenIssuesAsync(
        string? devLogin,
        CancellationToken ct = default
    )
    {
        // Configured template wins (personal board/sprint queries with a $dev placeholder);
        // otherwise "involved" = assignee OR has booked time. No parentheses in the default:
        // the live instance rejects `(...) #Unresolved`; implicit AND binds tighter than `or`.
        var dev = DevQueryValue(devLogin);
        var raw = string.IsNullOrWhiteSpace(_config.YouTrack.IssueQuery)
            ? $"for: {dev} #Unresolved or work author: {dev} #Unresolved sort by: updated desc"
            : SubstituteDev(_config.YouTrack.IssueQuery, dev);
        var issues = await GetPagedAsync<IssueDto>(
            $"issues?query={Uri.EscapeDataString(raw)}&fields={IssueFields}",
            pageSize: 100,
            ct
        );
        return issues.Select(MapIssue).ToList();
    }

    public async Task<IReadOnlyList<Issue>> GetSprintPoolIssuesAsync(
        string? devLogin,
        CancellationToken ct = default
    )
    {
        if (string.IsNullOrWhiteSpace(_config.YouTrack.SprintPoolQuery))
            return Array.Empty<Issue>();
        var raw = SubstituteDev(_config.YouTrack.SprintPoolQuery, DevQueryValue(devLogin));
        var issues = await GetPagedAsync<IssueDto>(
            $"issues?query={Uri.EscapeDataString(raw)}&fields={IssueFields}",
            pageSize: 50,
            ct
        );
        return issues.Select(MapIssue).ToList();
    }

    private static string SubstituteDev(string template, string dev) =>
        template.Replace("$dev", dev, StringComparison.Ordinal);

    public async Task<IReadOnlyList<Issue>> GetRecentlyActiveIssuesAsync(
        string? devLogin,
        DateOnly from,
        DateOnly to,
        CancellationToken ct = default
    )
    {
        var dev = DevQueryValue(devLogin);
        var query = Uri.EscapeDataString(
            $"updated by: {dev} updated: {FormatDate(from)} .. {FormatDate(to)}"
        );
        var issues = await GetPagedAsync<IssueDto>(
            $"issues?query={query}&fields={IssueFields}",
            pageSize: 50,
            ct
        );
        return issues.Select(MapIssue).ToList();
    }

    public async Task<IReadOnlyList<WorkItem>> GetWorkItemsAsync(
        string? devLogin,
        DateOnly from,
        DateOnly to,
        CancellationToken ct = default
    )
    {
        var login = devLogin ?? await GetCurrentLoginAsync(ct);

        if (!_useIssueScopedWorkItems)
        {
            try
            {
                var items = await GetPagedAsync<WorkItemDto>(
                    $"workItems?author={Uri.EscapeDataString(login)}"
                        + $"&startDate={FormatDate(from)}&endDate={FormatDate(to)}"
                        + $"&fields={WorkItemFields}",
                    pageSize: 500,
                    ct
                );
                return items.Select(MapWorkItem).ToList();
            }
            catch (YouTrackApiException ex) when (ex.StatusCode is 400 or 403 or 404)
            {
                _useIssueScopedWorkItems = true;
            }
        }

        // Fallback: find issues with the dev's work in the period, then read their work items.
        var query = Uri.EscapeDataString(
            $"work author: {DevQueryValue(devLogin)} work date: {FormatDate(from)} .. {FormatDate(to)}"
        );
        var issues = await GetPagedAsync<IssueDto>(
            $"issues?query={query}&fields=idReadable,summary",
            pageSize: 100,
            ct
        );

        var result = new List<WorkItem>();
        foreach (var issue in issues)
        {
            var items = await GetPagedAsync<WorkItemDto>(
                $"issues/{issue.IdReadable}/timeTracking/workItems?fields={WorkItemFields}",
                pageSize: 200,
                ct
            );
            result.AddRange(
                items
                    .Where(i =>
                        string.Equals(i.Author?.Login, login, StringComparison.OrdinalIgnoreCase)
                    )
                    .Select(MapWorkItem)
                    .Where(w => w.Date >= from && w.Date <= to)
            );
        }
        return result;
    }

    public async Task<IReadOnlyList<SprintTaskCategory>> GetSprintTaskCategoriesAsync(
        string taskQuery,
        CancellationToken ct = default
    )
    {
        // Roadmapvorhaben lives on the parent feature — traverse `Subtask INWARD` links.
        var fields =
            "idReadable,links(direction,linkType(name),issues(idReadable,customFields(name,value(name))))";
        var issues = await GetPagedAsync<IssueDto>(
            $"issues?query={Uri.EscapeDataString(taskQuery)}&fields={fields}",
            pageSize: 500,
            ct
        );
        return issues
            .Where(i => i.IdReadable is not null)
            .Select(i => new SprintTaskCategory(i.IdReadable!, ParentRoadmapvorhaben(i)))
            .ToList();
    }

    public async Task<IReadOnlyList<SprintFeature>> GetSprintFeaturesAsync(
        string featureQuery,
        CancellationToken ct = default
    )
    {
        var fields = "idReadable,summary,customFields(name,value(name,minutes,login))";
        var issues = await GetPagedAsync<IssueDto>(
            $"issues?query={Uri.EscapeDataString(featureQuery)}&fields={fields}",
            pageSize: 200,
            ct
        );

        return issues
            .Where(i => i.IdReadable is not null)
            .Select(i =>
            {
                JsonElement? Field(string name) =>
                    i
                        .CustomFields?.FirstOrDefault(f =>
                            string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase)
                        )
                        ?.Value;
                return new SprintFeature(
                    i.IdReadable!,
                    i.Summary ?? "",
                    ValueName(Field("Roadmapvorhaben")),
                    ValueLogin(Field("Assignee")),
                    ValueMinutes(Field("Estimation")),
                    ValueMinutes(Field("Spent time"))
                );
            })
            .ToList();
    }

    public async Task<IssueWithChildren?> GetIssueWithChildrenAsync(
        string issueId,
        CancellationToken ct = default
    )
    {
        // Children of a feature are `Subtask` links with direction OUTWARD ("parent for" —
        // the mirror of the INWARD traversal in ParentRoadmapvorhaben below).
        var fields =
            "idReadable,summary,resolved,customFields(name,value(name)),"
            + "links(direction,linkType(name),issues(idReadable,summary,resolved,customFields(name,value(name))))";
        IssueDto issue;
        try
        {
            issue = await GetAsync<IssueDto>(
                $"issues/{Uri.EscapeDataString(issueId)}?fields={fields}",
                ct
            );
        }
        catch (YouTrackApiException ex) when (ex.StatusCode is 404)
        {
            return null;
        }

        var children = (issue.Links ?? [])
            .Where(l =>
                string.Equals(l.LinkType?.Name, "Subtask", StringComparison.OrdinalIgnoreCase)
                && string.Equals(l.Direction, "OUTWARD", StringComparison.OrdinalIgnoreCase)
            )
            .SelectMany(l => l.Issues ?? [])
            .Where(c => c.IdReadable is not null)
            .Select(c => new IssueChild(
                c.IdReadable!,
                c.Summary ?? "",
                TypeOf(c),
                Resolved: c.Resolved is not null
            ))
            .ToList();

        return new IssueWithChildren(
            issue.IdReadable ?? issueId,
            issue.Summary ?? "",
            TypeOf(issue),
            children
        );

        static string? TypeOf(IssueDto dto) =>
            ValueName(
                dto.CustomFields?.FirstOrDefault(f =>
                    string.Equals(f.Name, "Type", StringComparison.OrdinalIgnoreCase)
                )?.Value
            );
    }

    private static string? ParentRoadmapvorhaben(IssueDto task)
    {
        foreach (var link in task.Links ?? [])
        {
            if (
                !string.Equals(link.LinkType?.Name, "Subtask", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(link.Direction, "INWARD", StringComparison.OrdinalIgnoreCase)
            )
                continue;
            foreach (var parent in link.Issues ?? [])
            {
                var rmv = parent
                    .CustomFields?.FirstOrDefault(f =>
                        string.Equals(f.Name, "Roadmapvorhaben", StringComparison.OrdinalIgnoreCase)
                    )
                    ?.Value;
                if (ValueName(rmv) is { } name)
                    return name;
            }
        }
        return null;
    }

    private static string? ValueLogin(JsonElement? value) =>
        value is { ValueKind: JsonValueKind.Object } v
        && v.TryGetProperty("login", out var login)
        && login.ValueKind == JsonValueKind.String
            ? login.GetString()
        : value is { ValueKind: JsonValueKind.Array } arr && arr.GetArrayLength() > 0
            ? ValueLogin(arr[0])
        : null;

    public async Task<UserInfo> GetCurrentUserAsync(CancellationToken ct = default)
    {
        var me = await GetAsync<UserDto>("users/me?fields=id,login,fullName", ct);
        _currentLogin ??= me.Login ?? "";
        return new UserInfo(me.Login ?? "", me.FullName ?? me.Login ?? "");
    }

    public async Task<IReadOnlyList<UserInfo>> GetUsersAsync(CancellationToken ct = default)
    {
        try
        {
            var users = await GetPagedAsync<UserDto>(
                "users?fields=login,fullName,banned",
                pageSize: 200,
                ct
            );
            return users
                .Where(u => !u.Banned && !string.IsNullOrWhiteSpace(u.Login))
                .Select(u => new UserInfo(u.Login!, u.FullName ?? u.Login!))
                .OrderBy(u => u.FullName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (YouTrackApiException)
        {
            // Listing users is a permission the token may not have — dropdown degrades to text input.
            return Array.Empty<UserInfo>();
        }
    }

    /// <summary>Login as it appears in a YouTrack query: null → "me"; brace-wrap multi-word values.</summary>
    private static string DevQueryValue(string? devLogin) =>
        string.IsNullOrWhiteSpace(devLogin) ? "me"
        : devLogin.Contains(' ') ? "{" + devLogin + "}"
        : devLogin;

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

    /// <summary>Hard ceiling across all pages — protects against a runaway query, not a real limit.</summary>
    private const int MaxPagedItems = 2000;

    /// <summary>
    /// Follows $skip/$top until a short page: YouTrack silently truncates at $top, which
    /// understated totals for anyone with more issues/work items than one page.
    /// <paramref name="pathAndQuery"/> must not already contain $skip/$top.
    /// </summary>
    private async Task<List<T>> GetPagedAsync<T>(
        string pathAndQuery,
        int pageSize,
        CancellationToken ct
    )
    {
        var separator = pathAndQuery.Contains('?') ? '&' : '?';
        var all = new List<T>();
        for (var skip = 0; ; skip += pageSize)
        {
            var page = await GetAsync<List<T>>(
                $"{pathAndQuery}{separator}$skip={skip}&$top={pageSize}",
                ct
            );
            all.AddRange(page);
            if (page.Count < pageSize || all.Count >= MaxPagedItems)
                return all;
        }
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
