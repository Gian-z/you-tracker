using System.Text.Json;
using System.Text.Json.Serialization;
using YouTracker.Core.Abstractions;

namespace YouTracker.Infrastructure.Storage;

/// <summary>
/// Persists per-day presence/absence state as JSON in <c>day-state.json</c> next to the config —
/// a dictionary keyed by ISO date ("2026-07-16"). Days whose state is fully default get pruned
/// on save so the file doesn't grow forever.
/// </summary>
public sealed class FileDayStateStore : IDayStateStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly string _path;

    public FileDayStateStore(string? directory = null) =>
        _path = Path.Combine(directory ?? StoragePaths.AppDataDir, "day-state.json");

    public IReadOnlyDictionary<DateOnly, DayState> Load()
    {
        if (!File.Exists(_path))
            return new Dictionary<DateOnly, DayState>();
        try
        {
            return JsonSerializer.Deserialize<Dictionary<DateOnly, DayState>>(
                    File.ReadAllText(_path),
                    Options
                ) ?? new Dictionary<DateOnly, DayState>();
        }
        catch (JsonException)
        {
            // Corrupt file: presence stamps are re-enterable; don't block the UI.
            return new Dictionary<DateOnly, DayState>();
        }
    }

    public void Save(DateOnly date, DayState state)
    {
        var all = new Dictionary<DateOnly, DayState>(Load());
        if (state == new DayState())
            all.Remove(date);
        else
            all[date] = state;

        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, JsonSerializer.Serialize(all, Options));
    }
}
