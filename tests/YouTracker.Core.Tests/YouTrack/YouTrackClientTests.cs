using System.Net;
using System.Text.Json;
using YouTracker.Core.Abstractions;
using YouTracker.Core.Config;
using YouTracker.Infrastructure.YouTrack;

namespace YouTracker.Core.Tests.YouTrack;

public sealed class YouTrackClientTests
{
    private static (YouTrackClient Client, StubHttpHandler Handler) CreateClient()
    {
        var handler = new StubHttpHandler();
        var config = new AppConfig(
            new YouTrackConfig("https://yt.example.com/", "https://yt.example.com", "TOKEN123"),
            new AnthropicConfig("key", "claude"),
            new WorkdayConfig(8.4, "Europe/Zurich", new[] { "In Progress" })
        );
        return (new YouTrackClient(new HttpClient(handler), config), handler);
    }

    private const string OpenIssuesJson = """
        [
          {
            "idReadable": "ALPHA-1238",
            "summary": "Klapp-Endpunkte: Version erst ab 26.0.3 verfügbar machen",
            "updated": 1751667321000,
            "project": { "shortName": "ALPHA" },
            "customFields": [
              { "name": "Type", "value": { "name": "Feature" } },
              { "name": "Priority", "value": { "name": "Normal" } },
              { "name": "State", "value": [ { "name": "In Bearbeitung" }, { "name": "Zweiter" } ] },
              { "name": "Estimation", "value": { "minutes": 240, "presentation": "4h" } },
              { "name": "Spent time", "value": null }
            ]
          },
          {
            "idReadable": "BETA-7",
            "summary": "No project field returned",
            "updated": 0,
            "customFields": []
          }
        ]
        """;

    [Fact]
    public async Task GetMyOpenIssues_SendsEscapedQueryAndAuthHeaders()
    {
        var (client, handler) = CreateClient();
        handler.Enqueue(HttpStatusCode.OK, OpenIssuesJson);

        await client.GetOpenIssuesAsync(null);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.StartsWith("https://yt.example.com/api/issues?", request.Uri.AbsoluteUri);
        Assert.Contains(
            "query=for: me #Unresolved or work author: me #Unresolved sort by: updated desc",
            Uri.UnescapeDataString(request.Uri.AbsoluteUri)
        );
        Assert.Contains("$top=100", request.Uri.AbsoluteUri);
        Assert.Contains(
            "fields=idReadable,summary,updated,project(shortName),customFields(name,value(name,minutes,presentation))",
            Uri.UnescapeDataString(request.Uri.AbsoluteUri)
        );
        Assert.Equal("Bearer TOKEN123", request.Authorization);
        Assert.Contains("application/json", request.Accept);
    }

    [Fact]
    public async Task GetMyOpenIssues_MapsPolymorphicCustomFieldsAndEpoch()
    {
        var (client, handler) = CreateClient();
        handler.Enqueue(HttpStatusCode.OK, OpenIssuesJson);

        var issues = await client.GetOpenIssuesAsync(null);

        Assert.Equal(2, issues.Count);

        var first = issues[0];
        Assert.Equal("ALPHA-1238", first.Id);
        Assert.Equal("Klapp-Endpunkte: Version erst ab 26.0.3 verfügbar machen", first.Summary);
        Assert.Equal("ALPHA", first.ProjectKey);
        Assert.Equal("Feature", first.Type); // object value
        Assert.Equal("Normal", first.Priority); // object value
        Assert.Equal("In Bearbeitung", first.State); // array value -> first element
        Assert.Equal(240, first.EstimateMinutes); // period value with minutes
        Assert.Null(first.SpentMinutes); // null value
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1751667321000), first.Updated);

        var second = issues[1];
        Assert.Equal("BETA", second.ProjectKey); // fallback: prefix of idReadable
        Assert.Null(second.Type);
        Assert.Null(second.EstimateMinutes);
    }

    [Fact]
    public async Task GetMyRecentlyActiveIssues_SendsDateRangeQuery()
    {
        var (client, handler) = CreateClient();
        handler.Enqueue(HttpStatusCode.OK, "[]");

        await client.GetRecentlyActiveIssuesAsync(
            null,
            new DateOnly(2026, 7, 1),
            new DateOnly(2026, 7, 5)
        );

        var request = Assert.Single(handler.Requests);
        Assert.Contains(
            "query=updated%20by%3A%20me%20updated%3A%202026-07-01%20..%202026-07-05",
            request.Uri.AbsoluteUri
        );
        Assert.Contains("$top=50", request.Uri.AbsoluteUri);
    }

    private const string CreatedWorkItemJson = """
        {
          "id": "142-999",
          "date": 1783332000000,
          "duration": { "minutes": 90, "presentation": "1h 30m" },
          "type": { "id": "77-1", "name": "Development" },
          "text": "did stuff",
          "issue": { "idReadable": "ALPHA-1", "summary": "Some issue" },
          "author": { "login": "gzw" }
        }
        """;

    [Fact]
    public async Task CreateWorkItem_SendsNoonLocalEpochDurationAndText_OmitsTypeWhenNull()
    {
        var (client, handler) = CreateClient();
        handler.Enqueue(HttpStatusCode.OK, CreatedWorkItemJson);

        var created = await client.CreateWorkItemAsync(
            new NewWorkItem("ALPHA-1", new DateOnly(2026, 7, 6), 90, TypeId: null, "did stuff")
        );

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.StartsWith(
            "https://yt.example.com/api/issues/ALPHA-1/timeTracking/workItems?fields=",
            request.Uri.AbsoluteUri
        );

        using var body = JsonDocument.Parse(request.Body!);
        var root = body.RootElement;
        // 2026-07-06 12:00 Europe/Zurich (CEST, +02:00) == 2026-07-06T10:00:00Z
        var expectedEpoch = new DateTimeOffset(
            2026,
            7,
            6,
            12,
            0,
            0,
            TimeSpan.FromHours(2)
        ).ToUnixTimeMilliseconds();
        Assert.Equal(expectedEpoch, root.GetProperty("date").GetInt64());
        Assert.Equal(90, root.GetProperty("duration").GetProperty("minutes").GetInt32());
        Assert.Equal("did stuff", root.GetProperty("text").GetString());
        Assert.False(root.TryGetProperty("type", out _));

        // Mapped response
        Assert.Equal("142-999", created.Id);
        Assert.Equal("ALPHA-1", created.IssueId);
        Assert.Equal("Some issue", created.IssueSummary);
        Assert.Equal(new DateOnly(2026, 7, 6), created.Date);
        Assert.Equal(90, created.Minutes);
        Assert.Equal("77-1", created.TypeId);
        Assert.Equal("Development", created.TypeName);
        Assert.Equal("gzw", created.AuthorLogin);
    }

    [Fact]
    public async Task CreateWorkItem_IncludesTypeIdWhenSet()
    {
        var (client, handler) = CreateClient();
        handler.Enqueue(HttpStatusCode.OK, CreatedWorkItemJson);

        await client.CreateWorkItemAsync(
            new NewWorkItem("ALPHA-1", new DateOnly(2026, 7, 6), 90, "77-1", "did stuff")
        );

        using var body = JsonDocument.Parse(handler.Requests[0].Body!);
        Assert.Equal("77-1", body.RootElement.GetProperty("type").GetProperty("id").GetString());
    }

    [Fact]
    public async Task GetMyWorkItems_FallsBackToIssueScopedPathOn404_AndFiltersByAuthorAndDate()
    {
        var (client, handler) = CreateClient();
        handler
            .Enqueue(
                HttpStatusCode.OK,
                """{ "id": "1-1", "login": "gzw", "fullName": "Gianluca Z" }"""
            )
            .Enqueue(HttpStatusCode.NotFound, """{ "error": "Not Found" }""")
            .Enqueue(HttpStatusCode.OK, """[ { "idReadable": "ALPHA-1", "summary": "S1" } ]""")
            .Enqueue(
                HttpStatusCode.OK,
                """
                [
                  {
                    "id": "142-1",
                    "date": 1783332000000,
                    "duration": { "minutes": 60, "presentation": "1h" },
                    "text": "mine, in range",
                    "issue": { "idReadable": "ALPHA-1", "summary": "S1" },
                    "author": { "login": "gzw" }
                  },
                  {
                    "id": "142-2",
                    "date": 1783332000000,
                    "duration": { "minutes": 30, "presentation": "30m" },
                    "text": "someone else",
                    "issue": { "idReadable": "ALPHA-1", "summary": "S1" },
                    "author": { "login": "other" }
                  },
                  {
                    "id": "142-3",
                    "date": 1780308000000,
                    "duration": { "minutes": 15, "presentation": "15m" },
                    "text": "mine, out of range (2026-06-01)",
                    "issue": { "idReadable": "ALPHA-1", "summary": "S1" },
                    "author": { "login": "gzw" }
                  }
                ]
                """
            );

        var from = new DateOnly(2026, 7, 1);
        var to = new DateOnly(2026, 7, 7);
        var items = await client.GetWorkItemsAsync(null, from, to);

        var item = Assert.Single(items);
        Assert.Equal("142-1", item.Id);
        Assert.Equal("gzw", item.AuthorLogin);
        Assert.Equal(new DateOnly(2026, 7, 6), item.Date);
        Assert.Equal("ALPHA-1", item.IssueId);
        Assert.Equal(60, item.Minutes);

        Assert.Equal(4, handler.Requests.Count);
        Assert.Contains("users/me?fields=id,login,fullName", handler.Requests[0].Uri.AbsoluteUri);
        Assert.Contains("/api/workItems?author=gzw", handler.Requests[1].Uri.AbsoluteUri);
        Assert.Contains("startDate=2026-07-01", handler.Requests[1].Uri.AbsoluteUri);
        Assert.Contains("endDate=2026-07-07", handler.Requests[1].Uri.AbsoluteUri);
        Assert.Contains(
            "query=work%20author%3A%20me%20work%20date%3A%202026-07-01%20..%202026-07-07",
            handler.Requests[2].Uri.AbsoluteUri
        );
        Assert.Contains(
            "/api/issues/ALPHA-1/timeTracking/workItems?",
            handler.Requests[3].Uri.AbsoluteUri
        );

        // Second call must not re-probe the top-level workItems endpoint.
        handler.Enqueue(HttpStatusCode.OK, "[]");
        await client.GetWorkItemsAsync(null, from, to);
        Assert.Equal(5, handler.Requests.Count);
        Assert.Contains("/api/issues?query=work%20author", handler.Requests[4].Uri.AbsoluteUri);
    }

    [Fact]
    public async Task GetMyWorkItems_UsesTopLevelEndpointWhenAvailable()
    {
        var (client, handler) = CreateClient();
        handler
            .Enqueue(HttpStatusCode.OK, """{ "id": "1-1", "login": "gzw", "fullName": "G" }""")
            .Enqueue(
                HttpStatusCode.OK,
                """
                [
                  {
                    "id": "142-1",
                    "date": 1783332000000,
                    "duration": { "minutes": 60, "presentation": "1h" },
                    "type": { "id": "77-1", "name": "Development" },
                    "text": "t",
                    "issue": { "idReadable": "ALPHA-1", "summary": "S1" },
                    "author": { "login": "gzw" }
                  }
                ]
                """
            );

        var items = await client.GetWorkItemsAsync(
            null,
            new DateOnly(2026, 7, 1),
            new DateOnly(2026, 7, 7)
        );

        var item = Assert.Single(items);
        Assert.Equal("Development", item.TypeName);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Contains("$top=500", handler.Requests[1].Uri.AbsoluteUri);
    }

    [Fact]
    public async Task GetIssueWithChildren_MapsOutwardSubtasksOnly()
    {
        var (client, handler) = CreateClient();
        handler.Enqueue(
            HttpStatusCode.OK,
            """
            {
              "idReadable": "ST6-1000",
              "summary": "Feature X",
              "resolved": null,
              "customFields": [ { "name": "Type", "value": { "name": "Feature" } } ],
              "links": [
                {
                  "direction": "OUTWARD",
                  "linkType": { "name": "Subtask" },
                  "issues": [
                    {
                      "idReadable": "ST6-1001",
                      "summary": "Umsetzung",
                      "resolved": 1751667321000,
                      "customFields": [ { "name": "Type", "value": { "name": "Task" } } ]
                    },
                    {
                      "idReadable": "ST6-1002",
                      "summary": "Sub-Feature",
                      "resolved": null,
                      "customFields": [ { "name": "Type", "value": { "name": "Feature" } } ]
                    }
                  ]
                },
                {
                  "direction": "INWARD",
                  "linkType": { "name": "Subtask" },
                  "issues": [ { "idReadable": "ST6-999", "summary": "parent" } ]
                },
                {
                  "direction": "OUTWARD",
                  "linkType": { "name": "Relates" },
                  "issues": [ { "idReadable": "ST6-500", "summary": "related" } ]
                }
              ]
            }
            """
        );

        var issue = await client.GetIssueWithChildrenAsync("ST6-1000");

        var request = Assert.Single(handler.Requests);
        Assert.StartsWith(
            "https://yt.example.com/api/issues/ST6-1000?fields=idReadable,summary,resolved,",
            request.Uri.AbsoluteUri
        );
        Assert.Contains(
            "links(direction,linkType(name),issues(idReadable,summary,resolved,customFields(name,value(name))))",
            Uri.UnescapeDataString(request.Uri.AbsoluteUri)
        );

        Assert.NotNull(issue);
        Assert.Equal("ST6-1000", issue.Id);
        Assert.Equal("Feature", issue.Type);
        // INWARD (parent) and non-Subtask links are ignored; the sub-feature is still listed
        // as a child (type filtering happens in the resolver).
        Assert.Equal(2, issue.Subtasks.Count);
        Assert.Equal("ST6-1001", issue.Subtasks[0].Id);
        Assert.True(issue.Subtasks[0].Resolved);
        Assert.Equal("Task", issue.Subtasks[0].Type);
        Assert.Equal("ST6-1002", issue.Subtasks[1].Id);
        Assert.False(issue.Subtasks[1].Resolved);
    }

    [Fact]
    public async Task GetIssueWithChildren_Returns404AsNull()
    {
        var (client, handler) = CreateClient();
        handler.Enqueue(HttpStatusCode.NotFound, """{ "error": "Not Found" }""");

        Assert.Null(await client.GetIssueWithChildrenAsync("ST6-404"));
    }

    [Fact]
    public async Task ListEndpoints_FollowSkipPagingUntilShortPage()
    {
        var (client, handler) = CreateClient();
        static string Page(int start, int count) =>
            "["
            + string.Join(
                ",",
                Enumerable
                    .Range(start, count)
                    .Select(i =>
                        $$"""{ "idReadable": "ALPHA-{{i}}", "summary": "S{{i}}", "updated": 0 }"""
                    )
            )
            + "]";
        // GetRecentlyActiveIssuesAsync pages with $top=50: one full page, then a short one.
        handler.Enqueue(HttpStatusCode.OK, Page(0, 50)).Enqueue(HttpStatusCode.OK, Page(50, 3));

        var issues = await client.GetRecentlyActiveIssuesAsync(
            null,
            new DateOnly(2026, 7, 1),
            new DateOnly(2026, 7, 5)
        );

        Assert.Equal(53, issues.Count);
        Assert.Equal("ALPHA-0", issues[0].Id);
        Assert.Equal("ALPHA-52", issues[52].Id);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Contains("$skip=0&$top=50", handler.Requests[0].Uri.AbsoluteUri);
        Assert.Contains("$skip=50&$top=50", handler.Requests[1].Uri.AbsoluteUri);
    }

    [Theory]
    [InlineData("ST6-1234", "issue id: ST6-1234")]
    [InlineData("  st6-1234  ", "issue id: st6-1234")] // trimmed; ids are case-insensitive in queries
    public async Task SearchIssues_IdShapedInput_SendsIssueIdQuery(string input, string expected)
    {
        var (client, handler) = CreateClient();
        handler.Enqueue(HttpStatusCode.OK, "[]");

        await client.SearchIssuesAsync(input, 25);

        var request = Assert.Single(handler.Requests);
        var unescaped = Uri.UnescapeDataString(request.Uri.AbsoluteUri);
        Assert.Contains($"query={expected}&$top=25", unescaped);
        Assert.Contains($"fields={IssueFieldsMask}", unescaped);
    }

    [Theory]
    [InlineData("klapp endpunkte")]
    [InlineData("project: ST6 #Unresolved sort by: updated desc")] // raw YouTrack syntax passthrough
    [InlineData("ST6-1234 klapp")] // id regex is anchored — not an exact-id lookup
    public async Task SearchIssues_FreeText_PassesRawQueryThrough(string input)
    {
        var (client, handler) = CreateClient();
        handler.Enqueue(HttpStatusCode.OK, "[]");

        await client.SearchIssuesAsync(input, 10);

        var request = Assert.Single(handler.Requests);
        Assert.Contains($"query={input}&$top=10", Uri.UnescapeDataString(request.Uri.AbsoluteUri));
    }

    [Fact]
    public async Task SearchIssues_MapsIssues_AndSurfaces400()
    {
        var (client, handler) = CreateClient();
        handler.Enqueue(HttpStatusCode.OK, OpenIssuesJson);
        var issues = await client.SearchIssuesAsync("klapp", 25);
        Assert.Equal(2, issues.Count);
        Assert.Equal("ALPHA-1238", issues[0].Id);

        handler.Enqueue(HttpStatusCode.BadRequest, """{ "error": "bad query" }""");
        var ex = await Assert.ThrowsAsync<YouTrackApiException>(() =>
            client.SearchIssuesAsync("(((", 25)
        );
        Assert.Equal(400, ex.StatusCode);
    }

    private const string IssueFieldsMask =
        "idReadable,summary,updated,project(shortName),customFields(name,value(name,minutes,presentation))";

    [Fact]
    public async Task NonSuccessStatus_ThrowsYouTrackApiExceptionWithDetails()
    {
        var (client, handler) = CreateClient();
        handler.Enqueue(HttpStatusCode.InternalServerError, "boom");

        var ex = await Assert.ThrowsAsync<YouTrackApiException>(() =>
            client.GetOpenIssuesAsync(null)
        );

        Assert.Equal(500, ex.StatusCode);
        Assert.Equal("boom", ex.ResponseBody);
        Assert.Contains("issues?query=", ex.RequestUrl);
        Assert.Contains("500", ex.Message);
        Assert.Contains("boom", ex.Message);
        Assert.Contains(ex.RequestUrl, ex.Message);
    }

    [Fact]
    public async Task GetWorkItemTypes_Returns403AsEmptyList()
    {
        var (client, handler) = CreateClient();
        handler.Enqueue(HttpStatusCode.Forbidden, """{ "error": "Forbidden" }""");

        var types = await client.GetWorkItemTypesAsync();

        Assert.Empty(types);
        Assert.Contains(
            "admin/timeTrackingSettings/workItemTypes?fields=id,name",
            handler.Requests[0].Uri.AbsoluteUri
        );
    }

    private static (YouTrackClient Client, StubHttpHandler Handler) CreateClientWithQueries(
        string? issueQuery,
        string? poolQuery
    )
    {
        var handler = new StubHttpHandler();
        var config = new AppConfig(
            new YouTrackConfig(
                "https://yt.example.com/",
                "https://yt.example.com",
                "TOKEN123",
                issueQuery,
                poolQuery
            ),
            new AnthropicConfig("key", "claude"),
            new WorkdayConfig(8.4, "Europe/Zurich", new[] { "In Progress" })
        );
        return (new YouTrackClient(new HttpClient(handler), config), handler);
    }

    [Fact]
    public async Task GetOpenIssues_UsesConfiguredQueryTemplateWithDevPlaceholder()
    {
        var (client, handler) = CreateClientWithQueries(
            "Board X: {Aktueller Sprint} Entwickler: $dev Sortieren nach: Status",
            null
        );
        handler.Enqueue(HttpStatusCode.OK, "[]");
        handler.Enqueue(HttpStatusCode.OK, "[]");

        await client.GetOpenIssuesAsync(null);
        await client.GetOpenIssuesAsync("VVO");

        Assert.Contains(
            "query=Board X: {Aktueller Sprint} Entwickler: me Sortieren nach: Status",
            Uri.UnescapeDataString(handler.Requests[0].Uri.AbsoluteUri)
        );
        Assert.Contains(
            "Entwickler: VVO",
            Uri.UnescapeDataString(handler.Requests[1].Uri.AbsoluteUri)
        );
    }

    [Fact]
    public async Task GetSprintPoolIssues_EmptyWithoutConfiguredQuery_QueriesWhenConfigured()
    {
        var (noPool, noPoolHandler) = CreateClientWithQueries(null, null);
        Assert.Empty(await noPool.GetSprintPoolIssuesAsync(null));
        Assert.Empty(noPoolHandler.Requests); // no HTTP call at all

        var (client, handler) = CreateClientWithQueries(null, "Board X: hat: -Entwickler");
        handler.Enqueue(HttpStatusCode.OK, "[]");
        await client.GetSprintPoolIssuesAsync(null);
        Assert.Contains(
            "query=Board X: hat: -Entwickler",
            Uri.UnescapeDataString(handler.Requests[0].Uri.AbsoluteUri)
        );
    }

    [Fact]
    public async Task GetOpenIssues_SubstitutesDevLoginIntoInvolvementQuery()
    {
        var (client, handler) = CreateClient();
        handler.Enqueue(HttpStatusCode.OK, "[]");

        await client.GetOpenIssuesAsync("VVO");

        var request = Assert.Single(handler.Requests);
        Assert.Contains(
            "query=for: VVO #Unresolved or work author: VVO #Unresolved",
            Uri.UnescapeDataString(request.Uri.AbsoluteUri)
        );
    }

    [Fact]
    public async Task GetWorkItems_UsesDevLoginAsAuthorWithoutResolvingCurrentUser()
    {
        var (client, handler) = CreateClient();
        handler.Enqueue(HttpStatusCode.OK, "[]");

        await client.GetWorkItemsAsync("VVO", new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 7));

        var request = Assert.Single(handler.Requests); // no users/me call
        Assert.Contains("/api/workItems?author=VVO", request.Uri.AbsoluteUri);
    }

    [Fact]
    public async Task GetUsers_MapsAndFiltersBanned_SortsByFullName()
    {
        var (client, handler) = CreateClient();
        handler.Enqueue(
            HttpStatusCode.OK,
            """
            [
              { "login": "zzz", "fullName": "Anders, Zoe", "banned": false },
              { "login": "old", "fullName": "Gone, User", "banned": true },
              { "login": "gzw", "fullName": "Zwahlen, Gian-Luca", "banned": false }
            ]
            """
        );

        var users = await client.GetUsersAsync();

        Assert.Equal(2, users.Count);
        Assert.Equal("zzz", users[0].Login);
        Assert.Equal("gzw", users[1].Login);
        Assert.Contains("users?fields=login,fullName,banned", handler.Requests[0].Uri.AbsoluteUri);
    }

    [Fact]
    public async Task GetUsers_ReturnsEmptyOnPermissionError()
    {
        var (client, handler) = CreateClient();
        handler.Enqueue(HttpStatusCode.Forbidden, """{ "error": "Forbidden" }""");

        Assert.Empty(await client.GetUsersAsync());
    }

    [Fact]
    public async Task GetCurrentUser_MapsLoginAndFullName()
    {
        var (client, handler) = CreateClient();
        handler.Enqueue(
            HttpStatusCode.OK,
            """{ "id": "1-1", "login": "gzw", "fullName": "Zwahlen, Gian-Luca" }"""
        );

        var me = await client.GetCurrentUserAsync();

        Assert.Equal("gzw", me.Login);
        Assert.Equal("Zwahlen, Gian-Luca", me.FullName);
    }

    [Fact]
    public async Task GetWorkItemTypes_MapsIdAndName()
    {
        var (client, handler) = CreateClient();
        handler.Enqueue(
            HttpStatusCode.OK,
            """[ { "id": "77-1", "name": "Development" }, { "id": "77-2", "name": "Testing" } ]"""
        );

        var types = await client.GetWorkItemTypesAsync();

        Assert.Equal(2, types.Count);
        Assert.Equal("77-1", types[0].Id);
        Assert.Equal("Development", types[0].Name);
    }
}
