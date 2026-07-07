using YouTracker.Core.Application;
using YouTracker.Core.Domain;
using YouTracker.Core.ReadModels;

namespace YouTracker.Core.Tests.Core;

public class BookingTargetResolverTests
{
    private static readonly string[] FeatureTypes = ["Feature"];
    private static readonly string[] TaskTypes = ["Task", "Aufgabe"];

    private static BookingTarget Resolve(IssueWithChildren? issue) =>
        BookingTargetResolver.Resolve(issue?.Id ?? "ABC-1", issue, FeatureTypes, TaskTypes);

    private static IssueWithChildren Feature(params IssueChild[] subtasks) =>
        new("ABC-1", "Feature summary", "Feature", subtasks);

    [Fact]
    public void Unknown_issue_resolves_direct()
    {
        var target = BookingTargetResolver.Resolve("ABC-9", null, FeatureTypes, TaskTypes);

        Assert.Equal(BookingTargetKind.Direct, target.Kind);
        Assert.Equal("ABC-9", target.TargetIssueId);
    }

    [Fact]
    public void Non_feature_type_resolves_direct()
    {
        var target = Resolve(new IssueWithChildren("ABC-1", "s", "Task", []));

        Assert.Equal(BookingTargetKind.Direct, target.Kind);
    }

    [Fact]
    public void Feature_with_single_task_redirects_even_when_resolved()
    {
        var target = Resolve(Feature(new IssueChild("ABC-2", "Umsetzung", "Task", Resolved: true)));

        Assert.Equal(BookingTargetKind.Redirected, target.Kind);
        Assert.Equal("ABC-2", target.TargetIssueId);
        Assert.True(target.TargetResolved);
    }

    [Fact]
    public void Feature_with_several_tasks_but_one_unresolved_redirects_to_it()
    {
        var target = Resolve(
            Feature(
                new IssueChild("ABC-2", "done", "Task", Resolved: true),
                new IssueChild("ABC-3", "open", "Aufgabe", Resolved: false)
            )
        );

        Assert.Equal(BookingTargetKind.Redirected, target.Kind);
        Assert.Equal("ABC-3", target.TargetIssueId);
    }

    [Fact]
    public void Feature_with_several_unresolved_tasks_is_ambiguous()
    {
        var target = Resolve(
            Feature(
                new IssueChild("ABC-2", "a", "Task", Resolved: false),
                new IssueChild("ABC-3", "b", "Task", Resolved: false)
            )
        );

        Assert.Equal(BookingTargetKind.Ambiguous, target.Kind);
        Assert.Equal(["ABC-2", "ABC-3"], target.Candidates.Select(c => c.IssueId).ToArray());
        Assert.Equal("ABC-1", target.TargetIssueId);
    }

    [Fact]
    public void Feature_typed_subtasks_are_not_candidates()
    {
        var target = Resolve(Feature(new IssueChild("ABC-2", "sub-feature", "Feature", false)));

        Assert.Equal(BookingTargetKind.NoTask, target.Kind);
    }

    [Fact]
    public void Feature_without_subtasks_is_no_task()
    {
        var target = Resolve(Feature());

        Assert.Equal(BookingTargetKind.NoTask, target.Kind);
        Assert.Equal("ABC-1", target.TargetIssueId);
    }

    [Fact]
    public void Type_matching_is_case_insensitive()
    {
        var issue = new IssueWithChildren(
            "ABC-1",
            "s",
            "FEATURE",
            [new IssueChild("ABC-2", "t", "aufgabe", false)]
        );

        var target = Resolve(issue);

        Assert.Equal(BookingTargetKind.Redirected, target.Kind);
        Assert.Equal("ABC-2", target.TargetIssueId);
    }
}
