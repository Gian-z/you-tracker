using System.Text.Json;
using YouTracker.Core.Abstractions;

namespace YouTracker.Infrastructure.Storage;

/// <summary>Persists the user settings as JSON in <c>settings.json</c> next to the config.</summary>
public sealed class FileUserSettingsStore : IUserSettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    private readonly string _path;

    public FileUserSettingsStore(string? directory = null) =>
        _path = Path.Combine(directory ?? StoragePaths.AppDataDir, "settings.json");

    public UserSettings Load()
    {
        if (!File.Exists(_path))
            return new UserSettings();
        try
        {
            return JsonSerializer.Deserialize<UserSettings>(File.ReadAllText(_path), Options)
                ?? new UserSettings();
        }
        catch (JsonException)
        {
            // Corrupt file: fall back to defaults instead of blocking the whole UI.
            return new UserSettings();
        }
    }

    public void Save(UserSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, JsonSerializer.Serialize(settings, Options));
    }
}
