using YouTracker.Core.Abstractions;
using YouTracker.Core.Application;
using YouTracker.Core.Application.Handlers;

namespace YouTracker.Core.Tests.Core;

public sealed class PresetHandlerTests
{
    private static BookingPreset Preset(
        string id = "",
        string name = "Daily",
        int minutes = 15,
        string issueId = "CMI-1"
    ) => new(id, name, issueId, "Standup", minutes, null, null, "Daily standup");

    [Fact]
    public async Task Save_assigns_id_and_persists_sorted_by_name()
    {
        var store = new InMemoryPresetStore();
        var handler = new SavePresetCommandHandler(store);

        var saved = await handler.HandleAsync(new SavePresetCommand(Preset(name: "Zebra")));
        await handler.HandleAsync(new SavePresetCommand(Preset(name: "Alpha")));

        Assert.False(string.IsNullOrWhiteSpace(saved.Id));
        Assert.Equal(2, store.Presets.Count);
        Assert.Equal("Alpha", store.Presets[0].Name);
    }

    [Fact]
    public async Task Save_with_existing_id_replaces_instead_of_duplicating()
    {
        var store = new InMemoryPresetStore();
        var handler = new SavePresetCommandHandler(store);
        var saved = await handler.HandleAsync(new SavePresetCommand(Preset()));

        var updated = await handler.HandleAsync(new SavePresetCommand(saved with { Minutes = 30 }));

        Assert.Equal(saved.Id, updated.Id);
        var single = Assert.Single(store.Presets);
        Assert.Equal(30, single.Minutes);
    }

    [Theory]
    [InlineData("", 15)]
    [InlineData("CMI-1", 0)]
    public async Task Save_rejects_invalid_presets(string issueId, int minutes)
    {
        var handler = new SavePresetCommandHandler(new InMemoryPresetStore());
        await Assert.ThrowsAsync<ArgumentException>(() =>
            handler.HandleAsync(new SavePresetCommand(Preset(issueId: issueId, minutes: minutes)))
        );
    }

    [Fact]
    public async Task Delete_removes_by_id_and_reports_unknown()
    {
        var store = new InMemoryPresetStore();
        var save = new SavePresetCommandHandler(store);
        var saved = await save.HandleAsync(new SavePresetCommand(Preset()));
        var handler = new DeletePresetCommandHandler(store);

        Assert.True(await handler.HandleAsync(new DeletePresetCommand(saved.Id)));
        Assert.Empty(store.Presets);
        Assert.False(await handler.HandleAsync(new DeletePresetCommand("nope")));
    }
}
