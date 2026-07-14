namespace YouTracker.Core.Abstractions;

// Personal (per-user, local) state powering the web GUI: user settings, daily presence
// stamps and personal day absences. All of it lives in %APPDATA%/you-tracker — none of
// it ever reaches YouTrack.

/// <summary>
/// User-level settings from the settings dialog. <paramref name="UsePresence"/> switches the
/// booking-gap semantics: true → gap measures against the recorded presence (everything present
/// should be booked), false → against the fixed daily target. <paramref name="TargetMinutes"/>
/// null falls back to the config's workday target. <paramref name="RoundingMinutes"/> 0 = no
/// rounding of timer/quick bookings.
/// </summary>
public sealed record UserSettings(
    bool UsePresence = true,
    int? TargetMinutes = null,
    string? DefaultIssueId = null,
    string? DefaultIssueSummary = null,
    string? DefaultTypeId = null,
    string? DefaultTypeName = null,
    int RoundingMinutes = 0
);

/// <summary>Persistence port for the user settings (local file; replaceable).</summary>
public interface IUserSettingsStore
{
    UserSettings Load();
    void Save(UserSettings settings);
}

public enum DayAbsence
{
    None,
    Half,
    Full,
}

/// <summary>
/// One day's presence stamps and personal absence. <paramref name="Come"/>/<paramref name="Go"/>
/// are wall-clock strings ("HH:mm", empty = not stamped); the gap/saldo math lives in the
/// frontend so the live "Gehe offen → Präsenz läuft mit" behavior needs no server ticking.
/// </summary>
public sealed record DayState(
    string? Come = null,
    string? Go = null,
    int PauseMinutes = 0,
    DayAbsence Absence = DayAbsence.None
);

/// <summary>Persistence port for per-day presence/absence state (local file; replaceable).</summary>
public interface IDayStateStore
{
    IReadOnlyDictionary<DateOnly, DayState> Load();
    void Save(DateOnly date, DayState state);
}
