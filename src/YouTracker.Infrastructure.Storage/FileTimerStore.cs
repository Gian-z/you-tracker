using System.Text.Json;
using YouTracker.Core.Abstractions;

namespace YouTracker.Infrastructure.Storage;

/// <summary>
/// Persists the single running timer as JSON in <c>timer.json</c> so it survives app restarts.
/// A missing or corrupt file is treated as "no timer running" (corrupt files are deleted).
/// </summary>
public sealed class FileTimerStore : ITimerStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    private readonly string _directory;
    private readonly string _filePath;

    public FileTimerStore(string? directory = null)
    {
        _directory = directory ?? StoragePaths.AppDataDir;
        _filePath = Path.Combine(_directory, "timer.json");
    }

    public TimerState? Load()
    {
        if (!File.Exists(_filePath))
            return null;

        try
        {
            var state = JsonSerializer.Deserialize<TimerState>(
                File.ReadAllText(_filePath),
                Options
            );
            if (state is null || string.IsNullOrWhiteSpace(state.IssueId))
            {
                TryDelete();
                return null;
            }
            return state;
        }
        catch (Exception ex) when (ex is JsonException or IOException or FormatException)
        {
            TryDelete();
            return null;
        }
    }

    public void Save(TimerState state)
    {
        StoragePaths.EnsureCreated(_directory);
        File.WriteAllText(_filePath, JsonSerializer.Serialize(state, Options));
    }

    public void Clear() => TryDelete();

    private void TryDelete()
    {
        try
        {
            if (File.Exists(_filePath))
                File.Delete(_filePath);
        }
        catch (IOException)
        {
            // Best effort — a locked file will simply be retried on the next operation.
        }
    }
}
