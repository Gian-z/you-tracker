using YouTracker.Core.Abstractions;
using YouTracker.Infrastructure.Storage;

namespace YouTracker.Core.Tests.Storage;

public sealed class FileTimerStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    private string TimerFile => Path.Combine(_dir, "timer.json");

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void Save_then_Load_round_trips_the_state()
    {
        var store = new FileTimerStore(_dir);
        var state = new TimerState(
            "ABC-1",
            "Fix the flux capacitor",
            new DateTimeOffset(2026, 7, 6, 9, 30, 0, TimeSpan.Zero)
        );

        store.Save(state);
        var loaded = store.Load();

        Assert.Equal(state, loaded);
        Assert.True(File.Exists(TimerFile));
    }

    [Fact]
    public void Load_accepts_old_three_field_json_with_defaults()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(
            TimerFile,
            """{ "issueId": "ABC-1", "issueSummary": "x", "startedUtc": "2026-07-06T09:30:00+00:00" }"""
        );
        var store = new FileTimerStore(_dir);

        var loaded = store.Load();

        Assert.NotNull(loaded);
        Assert.Equal("ABC-1", loaded.IssueId);
        Assert.Equal(0, loaded.AccumulatedSeconds);
        Assert.Null(loaded.PausedAtUtc);
        Assert.False(loaded.IsPaused);
    }

    [Fact]
    public void Save_then_Load_round_trips_paused_state()
    {
        var store = new FileTimerStore(_dir);
        var state = new TimerState(
            "ABC-1",
            "x",
            new DateTimeOffset(2026, 7, 6, 9, 30, 0, TimeSpan.Zero),
            AccumulatedSeconds: 2700,
            PausedAtUtc: new DateTimeOffset(2026, 7, 6, 10, 15, 0, TimeSpan.Zero)
        );

        store.Save(state);

        Assert.Equal(state, store.Load());
    }

    [Fact]
    public void Clear_removes_the_persisted_state()
    {
        var store = new FileTimerStore(_dir);
        store.Save(new TimerState("ABC-1", "x", DateTimeOffset.UtcNow));

        store.Clear();

        Assert.Null(store.Load());
        Assert.False(File.Exists(TimerFile));
    }

    [Fact]
    public void Load_returns_null_when_no_file_exists()
    {
        var store = new FileTimerStore(_dir);

        Assert.Null(store.Load());
    }

    [Fact]
    public void Load_returns_null_and_deletes_corrupt_file()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(TimerFile, "{{{ definitely not json");
        var store = new FileTimerStore(_dir);

        Assert.Null(store.Load());
        Assert.False(File.Exists(TimerFile));
    }
}
