using YouTracker.Core.Abstractions;
using YouTracker.Core.Config;
using YouTracker.Infrastructure.Storage;

namespace YouTracker.Core.Tests.Storage;

public sealed class ConfigSaveTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    public ConfigSaveTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    private static AppConfig SampleConfig() =>
        new(
            new YouTrackConfig(
                "https://yt.example.com/youtrack",
                "https://yt.example.com",
                "perm:token",
                IssueQuery: "Entwickler: $dev",
                SprintPoolQuery: "hat: -Entwickler",
                FeatureTypes: ["Feature"],
                TaskTypes: ["Task", "Aufgabe"],
                SprintQuery: "Board X: {Aktueller Sprint}"
            ),
            new AnthropicConfig("sk-ant-real", "claude-opus-4-8", "claude"),
            new WorkdayConfig(8.4, "Europe/Zurich", ["In Arbeit"]),
            new GitConfig(["C:/repos"], "me@example.com"),
            new CalendarConfig(
                "https://outlook.example.com/cal.ics",
                [new MeetingRule("*Daily*", "AD-401", "Meeting", "Daily")]
            )
        );

    [Fact]
    public void Save_then_Load_roundtrips_every_section()
    {
        var store = new JsonConfigStore(_dir);
        store.Save(SampleConfig());

        var loaded = new JsonConfigStore(_dir).Load();
        Assert.Equal(SampleConfig().YouTrack.BaseUrl, loaded.YouTrack.BaseUrl);
        Assert.Equal(SampleConfig().YouTrack.Token, loaded.YouTrack.Token);
        Assert.Equal(SampleConfig().YouTrack.IssueQuery, loaded.YouTrack.IssueQuery);
        Assert.Equal(SampleConfig().YouTrack.SprintPoolQuery, loaded.YouTrack.SprintPoolQuery);
        Assert.Equal(SampleConfig().YouTrack.SprintQuery, loaded.YouTrack.SprintQuery);
        Assert.Equal(["Feature"], loaded.YouTrack.FeatureTypes);
        Assert.Equal(["Task", "Aufgabe"], loaded.YouTrack.TaskTypes);
        Assert.Equal(SampleConfig().Anthropic, loaded.Anthropic);
        Assert.Equal(SampleConfig().Workday.TargetHours, loaded.Workday.TargetHours);
        Assert.Equal(["In Arbeit"], loaded.Workday.InProgressStates);
        Assert.Equal(["C:/repos"], loaded.Git!.ScanRoots);
        Assert.Equal("me@example.com", loaded.Git!.Author);
        Assert.Equal("https://outlook.example.com/cal.ics", loaded.Calendar!.IcsUrl);
        var rule = Assert.Single(loaded.Calendar!.Rules!);
        Assert.Equal(new MeetingRule("*Daily*", "AD-401", "Meeting", "Daily"), rule);
    }

    [Fact]
    public void Save_leaves_no_temp_file_behind()
    {
        var store = new JsonConfigStore(_dir);
        store.Save(SampleConfig());
        store.Save(SampleConfig() with { Workday = new WorkdayConfig(7, "Europe/Zurich", ["X"]) });

        Assert.Single(Directory.GetFiles(_dir));
        Assert.Equal(7, new JsonConfigStore(_dir).Load().Workday.TargetHours);
    }

    [Fact]
    public void TeamConfig_without_activeSprint_field_still_loads()
    {
        File.WriteAllText(
            Path.Combine(_dir, "team.json"),
            """
            {
              "name": "ST6",
              "projects": ["ST6"],
              "taskQuery": "q1",
              "featureSprintQuery": "q2",
              "ceremonyPatterns": ["Daily"],
              "members": [ { "login": "GZW", "name": "Zwahlen", "thresholdMinutes": 420, "weekdays": ["Monday"] } ],
              "sprints": []
            }
            """
        );
        var team = new FileTeamConfigStore(_dir).Load();
        Assert.NotNull(team);
        Assert.Null(team!.ActiveSprint);
    }

    [Fact]
    public void TeamConfig_activeSprint_roundtrips()
    {
        var store = new FileTeamConfigStore(_dir);
        var team = new TeamConfig(
            "ST6",
            ["ST6"],
            "q1",
            "q2",
            ["Daily"],
            [new TeamMember("GZW", "Zwahlen", 420, [DayOfWeek.Monday])],
            [new TeamSprint("2026.07-1", [new DateOnly(2026, 7, 2)], [])],
            ActiveSprint: "2026.07-1"
        );
        store.Save(team);
        Assert.Equal("2026.07-1", new FileTeamConfigStore(_dir).Load()!.ActiveSprint);
    }
}
