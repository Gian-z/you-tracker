using YouTracker.Core.Abstractions;
using YouTracker.Core.Application;
using YouTracker.Core.Application.Handlers;

namespace YouTracker.Core.Tests.Core;

public class WorkItemEditCommandHandlerTests
{
    private static readonly DateOnly Date = new(2026, 7, 6);

    [Fact]
    public async Task Update_writes_full_replacement_and_publishes_WorkItemsChanged()
    {
        var writer = new FakeWorkItemWriter();
        var events = new EventBus();
        WorkItemsChanged? published = null;
        using var sub = events.Subscribe<WorkItemsChanged>(e => published = e);
        var handler = new UpdateWorkItemCommandHandler(writer, events);

        var updated = await handler.HandleAsync(
            new UpdateWorkItemCommand("ABC-1", "142-9", Date, 90, "77-1", "korrigiert")
        );

        var (issueId, workItemId, update) = Assert.Single(writer.Updated);
        Assert.Equal("ABC-1", issueId);
        Assert.Equal("142-9", workItemId);
        Assert.Equal(new WorkItemUpdate(Date, 90, "77-1", "korrigiert"), update);
        Assert.Equal(90, updated.Minutes);
        Assert.Equal("ABC-1", published?.IssueId);
    }

    [Fact]
    public async Task Update_with_non_positive_minutes_is_rejected_before_writing()
    {
        var writer = new FakeWorkItemWriter();
        var handler = new UpdateWorkItemCommandHandler(writer, new EventBus());

        await Assert.ThrowsAsync<ArgumentException>(() =>
            handler.HandleAsync(new UpdateWorkItemCommand("ABC-1", "142-9", Date, 0, null, null))
        );
        Assert.Empty(writer.Updated);
    }

    [Fact]
    public async Task Delete_deletes_and_publishes_WorkItemsChanged()
    {
        var writer = new FakeWorkItemWriter();
        var events = new EventBus();
        WorkItemsChanged? published = null;
        using var sub = events.Subscribe<WorkItemsChanged>(e => published = e);
        var handler = new DeleteWorkItemCommandHandler(writer, events);

        Assert.True(await handler.HandleAsync(new DeleteWorkItemCommand("ABC-1", "142-9")));

        Assert.Equal(("ABC-1", "142-9"), Assert.Single(writer.Deleted));
        Assert.Equal("ABC-1", published?.IssueId);
    }

    [Fact]
    public async Task Failed_delete_does_not_publish()
    {
        var writer = new FakeWorkItemWriter { ThrowOnDelete = new InvalidOperationException("x") };
        var events = new EventBus();
        var raised = false;
        using var sub = events.Subscribe<WorkItemsChanged>(_ => raised = true);
        var handler = new DeleteWorkItemCommandHandler(writer, events);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.HandleAsync(new DeleteWorkItemCommand("ABC-1", "142-9"))
        );
        Assert.False(raised);
    }
}
