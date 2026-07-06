using System.Text.Json;
using YouTracker.Core.Abstractions;

namespace YouTracker.Infrastructure.Storage;

/// <summary>Persists booking presets as JSON in <c>presets.json</c> next to the config.</summary>
public sealed class FilePresetStore : IPresetStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    private readonly string _path;

    public FilePresetStore(string? directory = null) =>
        _path = Path.Combine(directory ?? StoragePaths.AppDataDir, "presets.json");

    public IReadOnlyList<BookingPreset> Load()
    {
        if (!File.Exists(_path))
            return Array.Empty<BookingPreset>();
        try
        {
            return JsonSerializer.Deserialize<List<BookingPreset>>(File.ReadAllText(_path), Options)
                ?? [];
        }
        catch (JsonException)
        {
            // Corrupt file: don't destroy it silently — presets are user-authored.
            return Array.Empty<BookingPreset>();
        }
    }

    public void Save(IReadOnlyList<BookingPreset> presets)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, JsonSerializer.Serialize(presets, Options));
    }
}
