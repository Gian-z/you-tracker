using System.Text.Json;
using System.Text.Json.Serialization;
using YouTracker.Core.Abstractions;

namespace YouTracker.Infrastructure.Storage;

/// <summary>Persists the scrum-team configuration as JSON in <c>team.json</c> next to the config.</summary>
public sealed class FileTeamConfigStore : ITeamConfigStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public FileTeamConfigStore(string? directory = null) =>
        ConfigPath = Path.Combine(directory ?? StoragePaths.AppDataDir, "team.json");

    public string ConfigPath { get; }

    public TeamConfig? Load()
    {
        if (!File.Exists(ConfigPath))
            return null;
        try
        {
            return JsonSerializer.Deserialize<TeamConfig>(File.ReadAllText(ConfigPath), Options);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"Team config at '{ConfigPath}' is not valid JSON: {ex.Message}",
                ex
            );
        }
    }

    public void Save(TeamConfig config)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
        File.WriteAllText(ConfigPath, JsonSerializer.Serialize(config, Options));
    }
}
