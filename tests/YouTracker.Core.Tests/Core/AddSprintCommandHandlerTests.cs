using YouTracker.Core.Abstractions;
using YouTracker.Core.Application;
using YouTracker.Core.Application.Handlers;

namespace YouTracker.Core.Tests.Core;

public class AddSprintCommandHandlerTests
{
    private static TeamConfig Team(params TeamSprint[] sprints) =>
        new("ST6", ["ST6"], "task {SPRINT}", "feature {SPRINT}", [], [], sprints);

    [Fact]
    public async Task Adds_sprint_with_weekday_workdays_only()
    {
        var store = new InMemoryTeamConfigStore(Team());
        var handler = new AddSprintCommandHandler(store);

        // Do 2026-07-02 .. Mi 2026-07-08 spans a weekend.
        var sprint = await handler.HandleAsync(
            new AddSprintCommand("2026.07-1", new DateOnly(2026, 7, 2), new DateOnly(2026, 7, 8))
        );

        Assert.Equal("2026.07-1", sprint.Name);
        Assert.Equal(
            [
                new DateOnly(2026, 7, 2),
                new DateOnly(2026, 7, 3),
                new DateOnly(2026, 7, 6),
                new DateOnly(2026, 7, 7),
                new DateOnly(2026, 7, 8),
            ],
            sprint.Workdays
        );
        Assert.Empty(sprint.Absences);
        Assert.Equal(sprint, Assert.Single(store.Config!.Sprints));
    }

    [Fact]
    public async Task Duplicate_name_is_rejected_case_insensitively()
    {
        var existing = new TeamSprint("2026.07-1", [new DateOnly(2026, 7, 2)], []);
        var handler = new AddSprintCommandHandler(new InMemoryTeamConfigStore(Team(existing)));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.HandleAsync(
                new AddSprintCommand(
                    "2026.07-1 ",
                    new DateOnly(2026, 8, 1),
                    new DateOnly(2026, 8, 5)
                )
            )
        );
    }

    [Fact]
    public async Task From_after_to_is_rejected()
    {
        var handler = new AddSprintCommandHandler(new InMemoryTeamConfigStore(Team()));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.HandleAsync(
                new AddSprintCommand("x", new DateOnly(2026, 7, 10), new DateOnly(2026, 7, 2))
            )
        );
    }

    [Fact]
    public async Task Weekend_only_range_is_rejected()
    {
        var handler = new AddSprintCommandHandler(new InMemoryTeamConfigStore(Team()));

        // Sa 2026-07-04 .. So 2026-07-05
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.HandleAsync(
                new AddSprintCommand("x", new DateOnly(2026, 7, 4), new DateOnly(2026, 7, 5))
            )
        );
    }

    [Fact]
    public async Task Missing_team_config_throws()
    {
        var handler = new AddSprintCommandHandler(new InMemoryTeamConfigStore());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.HandleAsync(
                new AddSprintCommand("x", new DateOnly(2026, 7, 2), new DateOnly(2026, 7, 3))
            )
        );
    }
}
