using YouTracker.Core.Abstractions;
using YouTracker.Infrastructure.Storage;

namespace YouTracker.Core.Tests.Storage;

public sealed class PersonalStateStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    public PersonalStateStoreTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void UserSettings_default_when_file_missing()
    {
        var store = new FileUserSettingsStore(_dir);
        var settings = store.Load();
        Assert.True(settings.UsePresence);
        Assert.Null(settings.TargetMinutes);
        Assert.Equal(0, settings.RoundingMinutes);
    }

    [Fact]
    public void UserSettings_roundtrip()
    {
        var store = new FileUserSettingsStore(_dir);
        var settings = new UserSettings(
            UsePresence: false,
            TargetMinutes: 504,
            DefaultIssueId: "ST6-124",
            DefaultIssueSummary: "Login-Flow",
            DefaultTypeId: "77-1",
            DefaultTypeName: "Entwicklung",
            RoundingMinutes: 15
        );
        store.Save(settings);
        Assert.Equal(settings, new FileUserSettingsStore(_dir).Load());
    }

    [Fact]
    public void UserSettings_corrupt_file_falls_back_to_defaults()
    {
        File.WriteAllText(Path.Combine(_dir, "settings.json"), "{ not json");
        Assert.Equal(new UserSettings(), new FileUserSettingsStore(_dir).Load());
    }

    [Fact]
    public void DayState_roundtrip_keyed_by_date()
    {
        var store = new FileDayStateStore(_dir);
        var date = new DateOnly(2026, 7, 16);
        var state = new DayState("07:45", "17:10", 30, DayAbsence.Half);
        store.Save(date, state);
        store.Save(date.AddDays(1), new DayState(Absence: DayAbsence.Full));

        var all = new FileDayStateStore(_dir).Load();
        Assert.Equal(state, all[date]);
        Assert.Equal(DayAbsence.Full, all[date.AddDays(1)].Absence);
    }

    [Fact]
    public void DayState_default_state_prunes_the_day()
    {
        var store = new FileDayStateStore(_dir);
        var date = new DateOnly(2026, 7, 16);
        store.Save(date, new DayState("07:45"));
        store.Save(date, new DayState());
        Assert.Empty(store.Load());
    }

    [Fact]
    public void DayState_enum_serializes_as_string()
    {
        var store = new FileDayStateStore(_dir);
        store.Save(new DateOnly(2026, 7, 16), new DayState(Absence: DayAbsence.Half));
        var json = File.ReadAllText(Path.Combine(_dir, "day-state.json"));
        Assert.Contains("\"half\"", json);
        Assert.Contains("2026-07-16", json);
    }
}
