using YouTracker.Core.Abstractions;
using YouTracker.Core.Config;
using YouTracker.Core.ReadModels;

namespace YouTracker.Core.Application.Handlers;

/// <summary>
/// Deterministic meeting→draft mapping (no AI): the day's calendar meetings are matched
/// against the configured title-pattern rules and returned as confirm-before-book drafts
/// for the existing DraftReviewDialog. The commit path (incl. the Feature→Task redirect)
/// is the unchanged CommitWorkLogDraftsCommand.
/// </summary>
public sealed class GetMeetingDraftsQueryHandler(
    IMeetingReader meetings,
    IWorkItemReader workItems,
    IIssueReader issues,
    AppConfig config
) : IQueryHandler<GetMeetingDraftsQuery, WorkLogDraftResult>
{
    public async Task<WorkLogDraftResult> HandleAsync(
        GetMeetingDraftsQuery query,
        CancellationToken ct = default
    )
    {
        var rules = config.Calendar?.Rules;
        if (!config.CalendarEnabled || rules is not { Count: > 0 })
            throw new InvalidOperationException(
                "Kalender ist nicht konfiguriert – 'calendar.icsUrl' und 'calendar.rules' in config.json setzen."
            );

        var all = await meetings.GetMeetingsAsync(query.Date, query.Date, ct).ConfigureAwait(false);
        // All-day and declined/free meetings are noise, not "unmatched" — filter silently.
        var relevant = all.Where(m => m is { IsAllDay: false, IsDeclined: false }).ToList();

        var booked = await workItems
            .GetWorkItemsAsync(null, query.Date, query.Date, ct)
            .ConfigureAwait(false);

        var drafts = new List<WorkLogDraft>();
        var unmatched = new List<UnmatchedActivity>();
        var summaries = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        foreach (var meeting in relevant.OrderBy(m => m.Start))
        {
            var label =
                $"{meeting.Title} ({meeting.Start:HH:mm}–{meeting.End:HH:mm}, "
                + $"{MeetingMinutes(meeting)}m)";

            var rule = Match(rules, meeting.Title);
            if (rule is null)
            {
                unmatched.Add(new UnmatchedActivity(label, "Keine Regel in config.json passt"));
                continue;
            }

            var minutes = MeetingMinutes(meeting);
            var comment = rule.Comment ?? meeting.Title;
            if (IsAlreadyBooked(booked, rule.IssueId, comment, minutes))
            {
                unmatched.Add(new UnmatchedActivity(label, "Bereits gebucht"));
                continue;
            }

            if (!summaries.TryGetValue(rule.IssueId, out var summary))
            {
                summary = await LoadSummaryAsync(rule.IssueId, ct).ConfigureAwait(false);
                summaries[rule.IssueId] = summary;
            }

            drafts.Add(
                new WorkLogDraft(
                    rule.IssueId,
                    summary,
                    Confidence: "high", // deterministic rule, not a guess
                    query.Date,
                    minutes,
                    rule.WorkTypeName,
                    comment,
                    Reasoning: $"Meeting «{meeting.Title}» {meeting.Start:HH:mm}–{meeting.End:HH:mm} → Regel «{rule.Pattern}»"
                )
            );
        }

        return new WorkLogDraftResult(drafts, unmatched);
    }

    /// <summary>First matching rule wins (wildcard semantics live on MeetingRule.Matches).</summary>
    internal static MeetingRule? Match(IReadOnlyList<MeetingRule> rules, string title) =>
        rules.FirstOrDefault(r => r.Matches(title));

    /// <summary>
    /// Pragmatic dedup: a meeting counts as already booked when a work item exists on the
    /// same date and issue whose comment equals the draft comment (case-insensitive) OR
    /// whose minutes cover the meeting's duration.
    /// </summary>
    private static bool IsAlreadyBooked(
        IReadOnlyList<Domain.WorkItem> booked,
        string issueId,
        string comment,
        int minutes
    ) =>
        booked.Any(w =>
            string.Equals(w.IssueId, issueId, StringComparison.OrdinalIgnoreCase)
            && (
                string.Equals(w.Text, comment, StringComparison.OrdinalIgnoreCase)
                || w.Minutes >= minutes
            )
        );

    private static int MeetingMinutes(CalendarMeeting meeting) =>
        Math.Max(1, (int)Math.Round((meeting.End - meeting.Start).TotalMinutes));

    private async Task<string?> LoadSummaryAsync(string issueId, CancellationToken ct)
    {
        try
        {
            var issue = await issues.GetIssueWithChildrenAsync(issueId, ct).ConfigureAwait(false);
            return issue?.Summary;
        }
        catch
        {
            return null; // best-effort — the draft works without a summary
        }
    }
}
