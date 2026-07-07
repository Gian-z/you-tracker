using YouTracker.Core.Abstractions;
using YouTracker.Core.Application;
using YouTracker.Core.Application.Handlers;
using YouTracker.Core.Domain;
using YouTracker.Core.ReadModels;

namespace YouTracker.Core.Tests.Core;

/// <summary>Write-side safety net of the "bookings land on tasks" rule.</summary>
public class BookingCommandHandlerTests
{
    private static readonly DateOnly Date = new(2026, 7, 6);

    private static (
        CreateWorkItemCommandHandler Handler,
        FakeWorkItemWriter Writer,
        FakeIssueReader Issues,
        EventBus Events
    ) CreateHandler()
    {
        var writer = new FakeWorkItemWriter();
        var issues = new FakeIssueReader();
        var events = new EventBus();
        return (
            new CreateWorkItemCommandHandler(writer, issues, TestData.Config(), events),
            writer,
            issues,
            events
        );
    }

    private static IssueWithChildren Feature(string id, params IssueChild[] subtasks) =>
        new(id, "Feature summary", "Feature", subtasks);

    [Fact]
    public async Task Booking_on_feature_with_single_task_is_redirected_to_the_task()
    {
        var (handler, writer, issues, events) = CreateHandler();
        issues.Children["FEAT-1"] = Feature(
            "FEAT-1",
            new IssueChild("TASK-2", "Umsetzung", "Task", false)
        );
        WorkItemCreated? published = null;
        using var sub = events.Subscribe<WorkItemCreated>(e => published = e);

        var created = await handler.HandleAsync(
            new CreateWorkItemCommand("FEAT-1", Date, 60, null, null)
        );

        Assert.Equal("TASK-2", Assert.Single(writer.Created).IssueId);
        Assert.Equal("TASK-2", created.IssueId); // the caller sees the real target
        Assert.Equal("TASK-2", published?.IssueId);
    }

    [Fact]
    public async Task Booking_on_ambiguous_feature_throws_unless_explicitly_allowed()
    {
        var (handler, writer, issues, _) = CreateHandler();
        issues.Children["FEAT-1"] = Feature(
            "FEAT-1",
            new IssueChild("TASK-2", "a", "Task", false),
            new IssueChild("TASK-3", "b", "Task", false)
        );

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.HandleAsync(new CreateWorkItemCommand("FEAT-1", Date, 60, null, null))
        );
        Assert.Contains("TASK-2", ex.Message);
        Assert.Contains("TASK-3", ex.Message);
        Assert.Empty(writer.Created);

        await handler.HandleAsync(
            new CreateWorkItemCommand("FEAT-1", Date, 60, null, null, AllowFeature: true)
        );
        Assert.Equal("FEAT-1", Assert.Single(writer.Created).IssueId);
    }

    [Fact]
    public async Task Booking_on_feature_without_task_throws_unless_explicitly_allowed()
    {
        var (handler, writer, issues, _) = CreateHandler();
        issues.Children["FEAT-1"] = Feature("FEAT-1");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.HandleAsync(new CreateWorkItemCommand("FEAT-1", Date, 60, null, null))
        );
        Assert.Empty(writer.Created);
    }

    [Fact]
    public async Task Booking_on_plain_task_is_unchanged()
    {
        var (handler, writer, issues, _) = CreateHandler();
        issues.Children["TASK-1"] = new IssueWithChildren("TASK-1", "s", "Task", []);

        await handler.HandleAsync(new CreateWorkItemCommand("TASK-1", Date, 60, null, null));

        Assert.Equal("TASK-1", Assert.Single(writer.Created).IssueId);
    }

    [Fact]
    public async Task Commit_drafts_redirects_with_note_and_skips_ambiguous_with_error()
    {
        var writer = new FakeWorkItemWriter();
        var reader = new FakeWorkItemReader();
        var issues = new FakeIssueReader();
        issues.Children["FEAT-1"] = Feature(
            "FEAT-1",
            new IssueChild("TASK-2", "Umsetzung", "Task", false)
        );
        issues.Children["FEAT-9"] = Feature(
            "FEAT-9",
            new IssueChild("TASK-8", "a", "Task", false),
            new IssueChild("TASK-7", "b", "Task", false)
        );
        var handler = new CommitWorkLogDraftsCommandHandler(
            writer,
            reader,
            issues,
            TestData.Config(),
            new EventBus()
        );
        WorkLogDraft Draft(string issueId) => new(issueId, null, "high", Date, 30, null, "c", null);

        var result = await handler.HandleAsync(
            new CommitWorkLogDraftsCommand([Draft("FEAT-1"), Draft("FEAT-9")], null)
        );

        Assert.Equal(1, result.Created);
        Assert.Equal("TASK-2", Assert.Single(writer.Created).IssueId);
        Assert.Contains("TASK-2", Assert.Single(result.Notes));
        Assert.Contains("FEAT-9", Assert.Single(result.Errors));
    }
}
