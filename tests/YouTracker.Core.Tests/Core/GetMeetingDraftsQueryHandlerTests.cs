using YouTracker.Core.Abstractions;
using YouTracker.Core.Application;
using YouTracker.Core.Application.Handlers;
using YouTracker.Core.Config;

namespace YouTracker.Core.Tests.Core;

public class GetMeetingDraftsQueryHandlerTests
{
    private static readonly DateOnly Day = new(2026, 7, 6);
    private static readonly TimeSpan Cest = TimeSpan.FromHours(2);

    private static CalendarMeeting Meeting(
        string title,
        int hour,
        int minutes = 15,
        bool allDay = false,
        bool declined = false
    )
    {
        var start = new DateTimeOffset(2026, 7, 6, hour, 0, 0, Cest);
        return new CalendarMeeting(title, start, start.AddMinutes(minutes), allDay, declined);
    }

    private static (
        GetMeetingDraftsQueryHandler Handler,
        FakeMeetingReader Meetings,
        FakeWorkItemReader WorkItems
    ) CreateHandler(params MeetingRule[] rules)
    {
        var meetings = new FakeMeetingReader();
        var workItems = new FakeWorkItemReader();
        var config = TestData.Config(new CalendarConfig("https://example.com/cal.ics", [.. rules]));
        return (
            new GetMeetingDraftsQueryHandler(meetings, workItems, new FakeIssueReader(), config),
            meetings,
            workItems
        );
    }

    [Fact]
    public async Task Matching_meeting_becomes_a_high_confidence_draft()
    {
        var (handler, meetings, _) = CreateHandler(
            new MeetingRule("Daily*", "AD-4711", "Meeting", "Daily")
        );
        meetings.Meetings.Add(Meeting("Daily Standup", 9));

        var result = await handler.HandleAsync(new GetMeetingDraftsQuery(Day));

        var draft = Assert.Single(result.Drafts);
        Assert.Equal("AD-4711", draft.IssueId);
        Assert.Equal(15, draft.Minutes);
        Assert.Equal("Daily", draft.Comment);
        Assert.Equal("Meeting", draft.WorkTypeName);
        Assert.Equal("high", draft.Confidence);
        Assert.Equal(Day, draft.Date);
        Assert.Contains("Daily*", draft.Reasoning);
        Assert.Empty(result.Unmatched);
    }

    [Fact]
    public async Task First_matching_rule_wins_and_matching_is_case_insensitive()
    {
        var (handler, meetings, _) = CreateHandler(
            new MeetingRule("*retro*", "AD-1", null, null),
            new MeetingRule("*", "AD-2", null, null)
        );
        meetings.Meetings.Add(Meeting("Sprint RETRO Q3", 14, 60));
        meetings.Meetings.Add(Meeting("Planning", 10, 60));

        var result = await handler.HandleAsync(new GetMeetingDraftsQuery(Day));

        Assert.Equal(2, result.Drafts.Count);
        Assert.Equal("AD-2", result.Drafts[0].IssueId); // Planning (10:00) via catch-all
        Assert.Equal("AD-1", result.Drafts[1].IssueId); // Retro (14:00) via first rule
    }

    [Fact]
    public async Task Unmatched_meeting_gets_a_german_reason()
    {
        var (handler, meetings, _) = CreateHandler(new MeetingRule("Daily*", "AD-1", null, null));
        meetings.Meetings.Add(Meeting("1:1 mit Chef", 11, 30));

        var result = await handler.HandleAsync(new GetMeetingDraftsQuery(Day));

        Assert.Empty(result.Drafts);
        var unmatched = Assert.Single(result.Unmatched);
        Assert.Contains("1:1 mit Chef", unmatched.Text);
        Assert.Contains("Keine Regel", unmatched.Reason);
    }

    [Fact]
    public async Task Already_booked_meetings_are_deduped_by_comment_or_minutes()
    {
        var (handler, meetings, workItems) = CreateHandler(
            new MeetingRule("Daily*", "AD-1", null, "Daily"),
            new MeetingRule("Retro*", "AD-2", null, null)
        );
        meetings.Meetings.Add(Meeting("Daily Standup", 9)); // 15m, comment "Daily"
        meetings.Meetings.Add(Meeting("Retro", 14, 60)); // 60m
        // Same issue+comment → dedup by comment.
        workItems.WorkItems.Add(TestData.WorkItem("AD-1", Day, 15) with { Text = "daily" });
        // Same issue, minutes cover the meeting → dedup by minutes.
        workItems.WorkItems.Add(TestData.WorkItem("AD-2", Day, 90));

        var result = await handler.HandleAsync(new GetMeetingDraftsQuery(Day));

        Assert.Empty(result.Drafts);
        Assert.Equal(2, result.Unmatched.Count);
        Assert.All(result.Unmatched, u => Assert.Equal("Bereits gebucht", u.Reason));
    }

    [Fact]
    public async Task All_day_and_declined_meetings_are_silently_skipped()
    {
        var (handler, meetings, _) = CreateHandler(new MeetingRule("*", "AD-1", null, null));
        meetings.Meetings.Add(Meeting("Ferien", 0, 60 * 24, allDay: true));
        meetings.Meetings.Add(Meeting("Abgesagt", 9, declined: true));

        var result = await handler.HandleAsync(new GetMeetingDraftsQuery(Day));

        Assert.Empty(result.Drafts);
        Assert.Empty(result.Unmatched); // noise, not "unmatched"
    }

    [Fact]
    public async Task Unconfigured_calendar_throws_a_german_error()
    {
        var handler = new GetMeetingDraftsQueryHandler(
            new FakeMeetingReader(),
            new FakeWorkItemReader(),
            new FakeIssueReader(),
            TestData.Config() // no calendar config
        );

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.HandleAsync(new GetMeetingDraftsQuery(Day))
        );
        Assert.Contains("config.json", ex.Message);
    }

    [Theory]
    [InlineData("Daily", "Daily Standup", false)] // no wildcard = exact match
    [InlineData("Daily", "Daily", true)]
    [InlineData("*Standup", "Daily Standup", true)]
    public async Task Wildcard_semantics_match_the_full_title(
        string pattern,
        string title,
        bool expectDraft
    )
    {
        var (handler, meetings, _) = CreateHandler(new MeetingRule(pattern, "AD-1", null, null));
        meetings.Meetings.Add(Meeting(title, 9));

        var result = await handler.HandleAsync(new GetMeetingDraftsQuery(Day));

        Assert.Equal(expectDraft ? 1 : 0, result.Drafts.Count);
    }
}
