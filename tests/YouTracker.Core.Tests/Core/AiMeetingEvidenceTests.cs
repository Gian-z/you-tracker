using YouTracker.Core.Abstractions;
using YouTracker.Core.Application;
using YouTracker.Core.Application.Handlers;
using YouTracker.Core.Config;

namespace YouTracker.Core.Tests.Core;

/// <summary>Calendar meetings flow into the AI draft/gap-fill prompts as evidence.</summary>
public class AiMeetingEvidenceTests
{
    private static readonly DateOnly Day = new(2026, 7, 6);
    private static readonly TimeSpan Cest = TimeSpan.FromHours(2);
    private static readonly DateTimeOffset Now = new(2026, 7, 6, 10, 0, 0, TimeSpan.Zero);

    private static CalendarMeeting Meeting(string title, bool declined = false)
    {
        var start = new DateTimeOffset(2026, 7, 6, 9, 0, 0, Cest);
        return new CalendarMeeting(title, start, start.AddMinutes(15), false, declined);
    }

    private static (
        DraftWorkLogQueryHandler Handler,
        FakeMeetingReader Meetings,
        FakeAiProvider Ai
    ) CreateDraftHandler()
    {
        var meetings = new FakeMeetingReader();
        var ai = new FakeAiProvider("""{ "drafts": [], "unmatched": [] }""");
        var config = TestData.Config(
            new CalendarConfig(
                "https://example.com/cal.ics",
                [new MeetingRule("Daily*", "AD-4711", null, null)]
            )
        );
        var handler = new DraftWorkLogQueryHandler(
            new FakeIssueReader(TestData.Issue("ABC-1")),
            new FakeWorkItemReader(),
            new FakeCommitActivityReader(),
            meetings,
            ai,
            config,
            new FakeTimeProvider(Now)
        );
        return (handler, meetings, ai);
    }

    [Fact]
    public async Task Draft_prompt_contains_meetings_with_configured_target_issue()
    {
        var (handler, meetings, ai) = CreateDraftHandler();
        meetings.Meetings.Add(Meeting("Daily Standup"));
        meetings.Meetings.Add(Meeting("Kundentermin"));

        await handler.HandleAsync(new DraftWorkLogQuery("Tag beschreiben", Day));

        Assert.NotNull(ai.LastUserPrompt);
        Assert.Contains("Calendar meetings", ai.LastUserPrompt);
        Assert.Contains("Daily Standup", ai.LastUserPrompt);
        Assert.Contains("AD-4711 (config rule)", ai.LastUserPrompt);
        Assert.Contains("Kundentermin", ai.LastUserPrompt);
    }

    [Fact]
    public async Task Declined_meetings_and_foreign_dev_views_produce_no_calendar_section()
    {
        var (handler, meetings, ai) = CreateDraftHandler();
        meetings.Meetings.Add(Meeting("Daily Standup", declined: true));

        await handler.HandleAsync(new DraftWorkLogQuery("Tag beschreiben", Day));
        Assert.DoesNotContain("Calendar meetings", ai.LastUserPrompt);

        meetings.Meetings.Add(Meeting("Daily Standup"));
        await handler.HandleAsync(new DraftWorkLogQuery("Tag beschreiben", Day, Dev: "VVO"));
        Assert.DoesNotContain("Calendar meetings", ai.LastUserPrompt);
    }

    [Fact]
    public async Task Rule_tickets_outside_the_issue_scope_survive_draft_validation()
    {
        // AD-4711 is NOT in the dev's issue query — without augmentation the parser would
        // reject the AI's proposal as "unknown issue id 'AD-4711'".
        var meetings = new FakeMeetingReader();
        meetings.Meetings.Add(Meeting("Daily Standup"));
        var ai = new FakeAiProvider(
            """
            {
              "drafts": [
                { "issueId": "AD-4711", "date": "2026-07-06", "minutes": 15,
                  "confidence": "high", "comment": "Daily" }
              ],
              "unmatched": []
            }
            """
        );
        var handler = new DraftWorkLogQueryHandler(
            new FakeIssueReader(TestData.Issue("ABC-1")),
            new FakeWorkItemReader(),
            new FakeCommitActivityReader(),
            meetings,
            ai,
            TestData.Config(
                new CalendarConfig(
                    "https://example.com/cal.ics",
                    [new MeetingRule("Daily*", "AD-4711", null, null)]
                )
            ),
            new FakeTimeProvider(Now)
        );

        var result = await handler.HandleAsync(new DraftWorkLogQuery("Daily gemacht", Day));

        var draft = Assert.Single(result.Drafts);
        Assert.Equal("AD-4711", draft.IssueId);
        Assert.Empty(result.Unmatched);
        // The augmented issue is also offered to the model in the issue list.
        Assert.Contains("AD-4711", ai.LastUserPrompt);
    }

    [Fact]
    public async Task Gapfill_prompt_contains_meetings_for_gap_days()
    {
        var meetings = new FakeMeetingReader();
        meetings.Meetings.Add(Meeting("Daily Standup"));
        var ai = new FakeAiProvider("""{ "drafts": [], "unmatched": [] }""");
        var handler = new SuggestGapFillsQueryHandler(
            new FakeIssueReader(TestData.Issue("ABC-1")),
            new FakeWorkItemReader(), // no bookings → the whole day is a gap
            new FakeCommitActivityReader(),
            meetings,
            ai,
            TestData.Config(
                new CalendarConfig(
                    "https://example.com/cal.ics",
                    [new MeetingRule("Daily*", "AD-4711", null, null)]
                )
            ),
            new FakeTimeProvider(Now)
        );

        await handler.HandleAsync(new SuggestGapFillsQuery(Day, Day));

        Assert.NotNull(ai.LastUserPrompt);
        Assert.Contains("Daily Standup", ai.LastUserPrompt);
        Assert.Contains("AD-4711 (config rule)", ai.LastUserPrompt);
        Assert.Contains("PRIORITIZE the factual evidence", ai.LastUserPrompt);
    }
}
