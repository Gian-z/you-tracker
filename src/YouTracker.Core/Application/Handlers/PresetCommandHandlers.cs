using YouTracker.Core.Abstractions;

namespace YouTracker.Core.Application.Handlers;

public sealed class SavePresetCommandHandler(IPresetStore store)
    : ICommandHandler<SavePresetCommand, BookingPreset>
{
    public Task<BookingPreset> HandleAsync(
        SavePresetCommand command,
        CancellationToken ct = default
    )
    {
        var preset = command.Preset;
        if (string.IsNullOrWhiteSpace(preset.IssueId))
            throw new ArgumentException("Preset needs an issue id.", nameof(command));
        if (preset.Minutes <= 0)
            throw new ArgumentException("Preset duration must be positive.", nameof(command));
        if (string.IsNullOrWhiteSpace(preset.Name))
            preset = preset with { Name = $"{preset.IssueId} ({preset.Minutes}m)" };
        if (string.IsNullOrWhiteSpace(preset.Id))
            preset = preset with { Id = Guid.NewGuid().ToString("N") };

        var existing = store.Load();
        var updated = existing
            .Where(p => !string.Equals(p.Id, preset.Id, StringComparison.Ordinal))
            .Append(preset)
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        store.Save(updated);
        return Task.FromResult(preset);
    }
}

public sealed class DeletePresetCommandHandler(IPresetStore store)
    : ICommandHandler<DeletePresetCommand, bool>
{
    public Task<bool> HandleAsync(DeletePresetCommand command, CancellationToken ct = default)
    {
        var existing = store.Load();
        var remaining = existing
            .Where(p => !string.Equals(p.Id, command.Id, StringComparison.Ordinal))
            .ToList();
        if (remaining.Count == existing.Count)
            return Task.FromResult(false);
        store.Save(remaining);
        return Task.FromResult(true);
    }
}
