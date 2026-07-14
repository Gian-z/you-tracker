using YouTracker.Core.Abstractions;
using YouTracker.Core.Config;

namespace YouTracker.Core.Application.Handlers;

// Handlers for the settings dialog: app config, user settings, per-day presence state and
// the full team config. All of them read/write local files only — no YouTrack access.

public sealed class GetAppConfigQueryHandler(IConfigStore store)
    : IQueryHandler<GetAppConfigQuery, AppConfig>
{
    public Task<AppConfig> HandleAsync(GetAppConfigQuery query, CancellationToken ct = default) =>
        Task.FromResult(store.Load());
}

public sealed class SaveAppConfigCommandHandler(IConfigStore store)
    : ICommandHandler<SaveAppConfigCommand, AppConfig>
{
    public Task<AppConfig> HandleAsync(SaveAppConfigCommand command, CancellationToken ct = default)
    {
        var config = command.Config;
        RequireField(config.YouTrack.BaseUrl, "youTrack.baseUrl");
        RequireField(config.YouTrack.WebBaseUrl, "youTrack.webBaseUrl");
        RequireField(config.YouTrack.Token, "youTrack.token");
        RequireField(config.Workday.Timezone, "workday.timezone");
        if (config.Workday.TargetHours is <= 0 or > 24)
            throw new InvalidOperationException(
                "workday.targetHours muss zwischen 0 und 24 liegen."
            );
        try
        {
            _ = config.TimeZone;
        }
        catch (TimeZoneNotFoundException)
        {
            throw new InvalidOperationException(
                $"Unbekannte Zeitzone '{config.Workday.Timezone}'."
            );
        }

        store.Save(config);
        // Re-load so env-var overrides and defaulting behave exactly like at startup.
        return Task.FromResult(store.Load());
    }

    private static void RequireField(string? value, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"'{field}' darf nicht leer sein.");
    }
}

public sealed class GetUserSettingsQueryHandler(IUserSettingsStore store)
    : IQueryHandler<GetUserSettingsQuery, UserSettings>
{
    public Task<UserSettings> HandleAsync(
        GetUserSettingsQuery query,
        CancellationToken ct = default
    ) => Task.FromResult(store.Load());
}

public sealed class SaveUserSettingsCommandHandler(IUserSettingsStore store)
    : ICommandHandler<SaveUserSettingsCommand, UserSettings>
{
    public Task<UserSettings> HandleAsync(
        SaveUserSettingsCommand command,
        CancellationToken ct = default
    )
    {
        var settings = command.Settings;
        if (settings.TargetMinutes is < 60 or > 24 * 60)
            throw new InvalidOperationException("Tagesziel muss zwischen 1:00 und 24:00 liegen.");
        if (settings.RoundingMinutes is not (0 or 5 or 15))
            throw new InvalidOperationException("Rundung muss 0, 5 oder 15 Minuten sein.");
        store.Save(settings);
        return Task.FromResult(settings);
    }
}

public sealed class GetDayStatesQueryHandler(IDayStateStore store)
    : IQueryHandler<GetDayStatesQuery, IReadOnlyDictionary<DateOnly, DayState>>
{
    public Task<IReadOnlyDictionary<DateOnly, DayState>> HandleAsync(
        GetDayStatesQuery query,
        CancellationToken ct = default
    )
    {
        IReadOnlyDictionary<DateOnly, DayState> result = store
            .Load()
            .Where(kv => kv.Key >= query.From && kv.Key <= query.To)
            .ToDictionary(kv => kv.Key, kv => kv.Value);
        return Task.FromResult(result);
    }
}

public sealed class SaveDayStateCommandHandler(IDayStateStore store)
    : ICommandHandler<SaveDayStateCommand, DayState>
{
    public Task<DayState> HandleAsync(SaveDayStateCommand command, CancellationToken ct = default)
    {
        var state = command.State;
        ValidateClock(state.Come, "Komme");
        ValidateClock(state.Go, "Gehe");
        if (state.PauseMinutes is < 0 or > 24 * 60)
            throw new InvalidOperationException("Pause muss zwischen 0 und 24h liegen.");
        store.Save(command.Date, state);
        return Task.FromResult(state);
    }

    private static void ValidateClock(string? value, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;
        if (!TimeOnly.TryParseExact(value, "H:mm", out _))
            throw new InvalidOperationException($"'{field}' braucht das Format HH:MM.");
    }
}

public sealed class SaveTeamConfigCommandHandler(ITeamConfigStore store)
    : ICommandHandler<SaveTeamConfigCommand, TeamConfig>
{
    public Task<TeamConfig> HandleAsync(
        SaveTeamConfigCommand command,
        CancellationToken ct = default
    )
    {
        var config = command.Config;
        if (string.IsNullOrWhiteSpace(config.Name))
            throw new InvalidOperationException("Team-Name darf nicht leer sein.");
        if (config.Members.Any(m => string.IsNullOrWhiteSpace(m.Login)))
            throw new InvalidOperationException("Jedes Mitglied braucht ein Login.");
        var dupes = config
            .Sprints.GroupBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();
        if (dupes.Count > 0)
            throw new InvalidOperationException($"Sprint '{dupes[0]}' existiert mehrfach.");
        if (
            config.ActiveSprint is { Length: > 0 } active
            && !config.Sprints.Any(s =>
                string.Equals(s.Name, active, StringComparison.OrdinalIgnoreCase)
            )
        )
            throw new InvalidOperationException($"Aktiver Sprint '{active}' ist unbekannt.");

        store.Save(config);
        return Task.FromResult(config);
    }
}
