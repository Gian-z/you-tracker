using YouTracker.Core.Abstractions;
using YouTracker.Core.Application;
using YouTracker.Core.Application.Handlers;
using YouTracker.Core.Config;

namespace YouTracker.Core.Tests.Core;

public sealed class InMemoryConfigStore(AppConfig? config = null) : IConfigStore
{
    public AppConfig? Config { get; set; } = config;
    public int SaveCount { get; private set; }

    public string ConfigPath => "memory://config.json";
    public bool Exists => Config is not null;
    public string Template => "{}";

    public AppConfig Load() => Config ?? throw new InvalidOperationException("No config in store.");

    public void Save(AppConfig config)
    {
        Config = config;
        SaveCount++;
    }
}

public sealed class InMemoryUserSettingsStore : IUserSettingsStore
{
    public UserSettings Settings { get; set; } = new();

    public UserSettings Load() => Settings;

    public void Save(UserSettings settings) => Settings = settings;
}

public sealed class InMemoryDayStateStore : IDayStateStore
{
    public Dictionary<DateOnly, DayState> States { get; } = [];

    public IReadOnlyDictionary<DateOnly, DayState> Load() => States;

    public void Save(DateOnly date, DayState state) => States[date] = state;
}

public sealed class SettingsHandlerTests
{
    private static AppConfig ValidConfig() =>
        new(
            new YouTrackConfig("https://yt.example.com", "https://yt.example.com", "perm:t"),
            new AnthropicConfig("", "claude-opus-4-8"),
            new WorkdayConfig(8.0, "Europe/Zurich", ["In Arbeit"])
        );

    [Fact]
    public async Task SaveAppConfig_persists_and_returns_the_reloaded_config()
    {
        var store = new InMemoryConfigStore();
        var handler = new SaveAppConfigCommandHandler(store);

        var result = await handler.HandleAsync(new SaveAppConfigCommand(ValidConfig()));

        Assert.Equal(1, store.SaveCount);
        Assert.Equal(ValidConfig().YouTrack, result.YouTrack);
        Assert.Equal(ValidConfig().Anthropic, result.Anthropic);
        Assert.Equal(["In Arbeit"], result.Workday.InProgressStates);
    }

    [Theory]
    [InlineData("", "perm:t", "youTrack.baseUrl")]
    [InlineData("https://yt", "", "youTrack.token")]
    public async Task SaveAppConfig_rejects_missing_required_fields(
        string baseUrl,
        string token,
        string expectedField
    )
    {
        var config = ValidConfig() with
        {
            YouTrack = new YouTrackConfig(baseUrl, "https://yt.example.com", token),
        };
        var handler = new SaveAppConfigCommandHandler(new InMemoryConfigStore());

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.HandleAsync(new SaveAppConfigCommand(config))
        );
        Assert.Contains(expectedField, ex.Message);
    }

    [Fact]
    public async Task SaveAppConfig_rejects_unknown_timezone()
    {
        var config = ValidConfig() with
        {
            Workday = new WorkdayConfig(8.0, "Mars/Olympus", ["In Arbeit"]),
        };
        var handler = new SaveAppConfigCommandHandler(new InMemoryConfigStore());

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.HandleAsync(new SaveAppConfigCommand(config))
        );
        Assert.Contains("Zeitzone", ex.Message);
    }

    [Fact]
    public async Task SaveUserSettings_rejects_invalid_rounding()
    {
        var handler = new SaveUserSettingsCommandHandler(new InMemoryUserSettingsStore());
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.HandleAsync(new SaveUserSettingsCommand(new UserSettings(RoundingMinutes: 7)))
        );
    }

    [Fact]
    public async Task SaveUserSettings_persists_valid_settings()
    {
        var store = new InMemoryUserSettingsStore();
        var handler = new SaveUserSettingsCommandHandler(store);
        var settings = new UserSettings(
            UsePresence: false,
            TargetMinutes: 504,
            RoundingMinutes: 15
        );

        await handler.HandleAsync(new SaveUserSettingsCommand(settings));

        Assert.Equal(settings, store.Settings);
    }

    [Fact]
    public async Task SaveDayState_validates_clock_format()
    {
        var handler = new SaveDayStateCommandHandler(new InMemoryDayStateStore());
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.HandleAsync(
                new SaveDayStateCommand(new DateOnly(2026, 7, 16), new DayState(Come: "morgens"))
            )
        );
        Assert.Contains("Komme", ex.Message);
    }

    [Fact]
    public async Task SaveDayState_accepts_single_digit_hours_and_persists()
    {
        var store = new InMemoryDayStateStore();
        var handler = new SaveDayStateCommandHandler(store);
        var date = new DateOnly(2026, 7, 16);

        await handler.HandleAsync(
            new SaveDayStateCommand(date, new DayState("7:45", "17:00", 30, DayAbsence.None))
        );

        Assert.Equal("7:45", store.States[date].Come);
    }

    [Fact]
    public async Task GetDayStates_filters_to_the_requested_range()
    {
        var store = new InMemoryDayStateStore();
        store.States[new DateOnly(2026, 7, 10)] = new DayState("08:00");
        store.States[new DateOnly(2026, 7, 16)] = new DayState("07:45");
        var handler = new GetDayStatesQueryHandler(store);

        var result = await handler.HandleAsync(
            new GetDayStatesQuery(new DateOnly(2026, 7, 13), new DateOnly(2026, 7, 17))
        );

        var entry = Assert.Single(result);
        Assert.Equal(new DateOnly(2026, 7, 16), entry.Key);
    }

    [Fact]
    public async Task SaveTeamConfig_rejects_unknown_active_sprint()
    {
        var team = new TeamConfig(
            "ST6",
            ["ST6"],
            "q1",
            "q2",
            [],
            [new TeamMember("GZW", "Zwahlen", 420, [DayOfWeek.Monday])],
            [new TeamSprint("2026.07-1", [new DateOnly(2026, 7, 2)], [])],
            ActiveSprint: "nope"
        );
        var handler = new SaveTeamConfigCommandHandler(new InMemoryTeamConfigStore());

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.HandleAsync(new SaveTeamConfigCommand(team))
        );
        Assert.Contains("nope", ex.Message);
    }

    [Fact]
    public async Task SaveTeamConfig_replaces_the_whole_config()
    {
        var store = new InMemoryTeamConfigStore();
        var team = new TeamConfig(
            "ST6",
            ["ST6", "XBOX"],
            "q1",
            "q2",
            ["Daily"],
            [new TeamMember("GZW", "Zwahlen", 420, [DayOfWeek.Monday])],
            [new TeamSprint("2026.07-1", [new DateOnly(2026, 7, 2)], [])],
            ActiveSprint: "2026.07-1"
        );
        var handler = new SaveTeamConfigCommandHandler(store);

        var result = await handler.HandleAsync(new SaveTeamConfigCommand(team));

        Assert.Equal(team, store.Config);
        Assert.Equal(team, result);
    }
}
