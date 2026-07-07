using YouTracker.Core.Abstractions;
using YouTracker.Core.Ai;
using YouTracker.Core.Config;
using YouTracker.Core.Domain;
using YouTracker.Core.Metrics;
using YouTracker.Core.ReadModels;

namespace YouTracker.Core.Application.Handlers;

// AI handlers receive reader ports and IAiProvider only — structurally unable to write.
// `Dev` (null = current user) scopes which developer's tickets/bookings feed the prompts.

public sealed class DraftWorkLogQueryHandler(
    IIssueReader issues,
    IWorkItemReader workItems,
    ICommitActivityReader activity,
    IMeetingReader meetings,
    IAiProvider ai,
    AppConfig config,
    TimeProvider time
) : IQueryHandler<DraftWorkLogQuery, WorkLogDraftResult>
{
    public async Task<WorkLogDraftResult> HandleAsync(
        DraftWorkLogQuery query,
        CancellationToken ct = default
    )
    {
        var open = await issues.GetOpenIssuesAsync(query.Dev, ct).ConfigureAwait(false);
        var recent = await issues
            .GetRecentlyActiveIssuesAsync(query.Dev, query.Date.AddDays(-7), query.Date, ct)
            .ConfigureAwait(false);
        var known = await MeetingEvidence
            .AugmentWithRuleIssuesAsync(
                issues,
                Merge(open, recent),
                config.Calendar?.Rules,
                time.GetUtcNow(),
                ct
            )
            .ConfigureAwait(false);
        var types = await workItems.GetWorkItemTypesAsync(ct).ConfigureAwait(false);
        var booked = await workItems
            .GetWorkItemsAsync(query.Dev, query.Date, query.Date, ct)
            .ConfigureAwait(false);
        // Commits are local-machine evidence — only meaningful for the current user.
        IReadOnlyList<CommitActivity> commits = query.Dev is null
            ? await activity.GetCommitsAsync(query.Date, query.Date, ct).ConfigureAwait(false)
            : [];
        var calendar = await MeetingEvidence
            .LoadAsync(meetings, query.Dev, query.Date, query.Date, ct)
            .ConfigureAwait(false);

        var userPrompt =
            PromptBuilder.Workday(
                config.Today(time),
                config.TargetMinutesPerWorkday,
                config.Workday.Timezone
            )
            + PromptBuilder.IssueList(known)
            + PromptBuilder.WorkItemTypeList(types)
            + PromptBuilder.WorkItemList(booked)
            + (commits.Count > 0 ? PromptBuilder.Commits(commits, config.TimeZone) : "")
            + (calendar.Count > 0 ? PromptBuilder.Meetings(calendar, config.Calendar?.Rules) : "")
            + $"\n## Task\nThe user describes their work for {query.Date:yyyy-MM-dd}. "
            + "Map each described activity to one issue from the list and propose work items (drafts). "
            + (
                commits.Count > 0
                    ? "The git commits are factual evidence: use them to identify issues (ticket IDs like "
                        + "[XBOX-548] in commit messages or branch-style prefixes) and to corroborate what was worked on. "
                    : ""
            )
            + (
                calendar.Count > 0
                    ? "The calendar meetings are factual evidence too: when the user mentions a meeting, use its "
                        + "real duration, and when a configured target issue is stated, book it exactly there. "
                    : ""
            )
            + "Do not duplicate time that is already booked. Anything you cannot map goes into 'unmatched'. "
            + $"\n\n## User description\n{query.FreeText}";

        var json = await ai.CompleteJsonAsync(
                PromptBuilder.SystemBase,
                userPrompt,
                AiSchemas.WorkLogDrafts,
                ct
            )
            .ConfigureAwait(false);
        return AiResponseParser.ParseDrafts(json, known, query.Date);
    }

    internal static IReadOnlyList<Issue> Merge(
        IReadOnlyList<Issue> first,
        IReadOnlyList<Issue> second
    ) => [.. first.Concat(second).DistinctBy(i => i.Id, StringComparer.OrdinalIgnoreCase)];
}

public sealed class SuggestGapFillsQueryHandler(
    IIssueReader issues,
    IWorkItemReader workItems,
    ICommitActivityReader activity,
    IMeetingReader meetings,
    IAiProvider ai,
    AppConfig config,
    TimeProvider time
) : IQueryHandler<SuggestGapFillsQuery, WorkLogDraftResult>
{
    public async Task<WorkLogDraftResult> HandleAsync(
        SuggestGapFillsQuery query,
        CancellationToken ct = default
    )
    {
        var today = config.Today(time);
        var items = await workItems
            .GetWorkItemsAsync(query.Dev, query.From, query.To, ct)
            .ConfigureAwait(false);
        var overview = MetricsCalculator.BuildOverview(
            items,
            query.From,
            query.To,
            today,
            config.TargetMinutesPerWorkday
        );
        var gaps = MetricsCalculator.FindGaps(overview, today);
        if (gaps.Count == 0)
            return new WorkLogDraftResult([], []);

        var open = await issues.GetOpenIssuesAsync(query.Dev, ct).ConfigureAwait(false);
        var recent = await issues
            .GetRecentlyActiveIssuesAsync(query.Dev, query.From.AddDays(-7), query.To, ct)
            .ConfigureAwait(false);
        var known = await MeetingEvidence
            .AugmentWithRuleIssuesAsync(
                issues,
                DraftWorkLogQueryHandler.Merge(open, recent),
                config.Calendar?.Rules,
                time.GetUtcNow(),
                ct
            )
            .ConfigureAwait(false);
        var types = await workItems.GetWorkItemTypesAsync(ct).ConfigureAwait(false);
        // Commits are local-machine evidence — only meaningful for the current user.
        IReadOnlyList<CommitActivity> commits = query.Dev is null
            ? await activity.GetCommitsAsync(query.From, query.To, ct).ConfigureAwait(false)
            : [];
        var calendar = await MeetingEvidence
            .LoadAsync(meetings, query.Dev, query.From, query.To, ct)
            .ConfigureAwait(false);

        var gapLines = string.Join(
            "\n",
            gaps.Select(g => $"{g.Date:yyyy-MM-dd}: {g.GapMinutes} min missing")
        );
        var userPrompt =
            PromptBuilder.Workday(today, config.TargetMinutesPerWorkday, config.Workday.Timezone)
            + PromptBuilder.IssueList(known)
            + PromptBuilder.WorkItemTypeList(types)
            + PromptBuilder.WorkItemList(items)
            + (commits.Count > 0 ? PromptBuilder.Commits(commits, config.TimeZone) : "")
            + (calendar.Count > 0 ? PromptBuilder.Meetings(calendar, config.Calendar?.Rules) : "")
            + $"\n## Gap days\n{gapLines}\n"
            + "\n## Task\nPropose plausible work items (drafts) that fill each gap day. "
            + (
                commits.Count > 0 || calendar.Count > 0
                    ? "PRIORITIZE the factual evidence: calendar meetings show attended meetings with exact "
                        + "durations (book them on the stated configured issue when one is given), and git commit "
                        + "timestamps show which days the dev worked on what — ticket IDs in the messages "
                        + "(e.g. [XBOX-548]) identify the issue directly. Such proposals deserve high confidence. "
                        + "Fall back to recently updated/booked issues only for gaps without evidence. "
                    : "Base them on which issues the user recently worked on or updated. "
                        + "Prefer issues already booked adjacent days. "
            )
            + "Mark every proposal with honest confidence (these are guesses to review, not facts). "
            + "Do not exceed the missing minutes per day. Unexplainable gaps go into 'unmatched'.";

        var json = await ai.CompleteJsonAsync(
                PromptBuilder.SystemBase,
                userPrompt,
                AiSchemas.WorkLogDrafts,
                ct
            )
            .ConfigureAwait(false);
        return AiResponseParser.ParseDrafts(json, known, gaps[0].Date);
    }
}

public sealed class SummarizePeriodQueryHandler(
    IWorkItemReader workItems,
    ICommitActivityReader activity,
    IAiProvider ai,
    AppConfig config,
    TimeProvider time
) : IQueryHandler<SummarizePeriodQuery, string>
{
    public async Task<string> HandleAsync(
        SummarizePeriodQuery query,
        CancellationToken ct = default
    )
    {
        var items = await workItems
            .GetWorkItemsAsync(query.Dev, query.From, query.To, ct)
            .ConfigureAwait(false);
        if (items.Count == 0)
            return $"Keine Buchungen im Zeitraum {query.From:yyyy-MM-dd} – {query.To:yyyy-MM-dd}.";

        var overview = MetricsCalculator.BuildOverview(
            items,
            query.From,
            query.To,
            config.Today(time),
            config.TargetMinutesPerWorkday
        );

        IReadOnlyList<CommitActivity> commits = query.Dev is null
            ? await activity.GetCommitsAsync(query.From, query.To, ct).ConfigureAwait(false)
            : [];

        var userPrompt =
            PromptBuilder.Workday(
                config.Today(time),
                config.TargetMinutesPerWorkday,
                config.Workday.Timezone
            )
            + PromptBuilder.WorkItemList(items)
            + (commits.Count > 0 ? PromptBuilder.Commits(commits, config.TimeZone) : "")
            + PromptBuilder.Metrics(overview)
            + $"\n## Task\nWrite a standup-ready recap of {query.From:yyyy-MM-dd} – {query.To:yyyy-MM-dd}: "
            + "2-5 sentences overall, then one bullet per issue with the booked time. "
            + (
                commits.Count > 0
                    ? "Use the git commits to make the recap concrete (what was actually built/fixed). "
                    : ""
            )
            + "Mention notable gaps or focus observations in one sentence, without moralizing.";

        return await ai.CompleteTextAsync(PromptBuilder.SystemBase, userPrompt, ct)
            .ConfigureAwait(false);
    }
}

public sealed class TriageIssuesQueryHandler(
    IIssueReader issues,
    IWorkItemReader workItems,
    IAiProvider ai,
    AppConfig config,
    TimeProvider time
) : IQueryHandler<TriageIssuesQuery, TriageResult>
{
    public async Task<TriageResult> HandleAsync(
        TriageIssuesQuery query,
        CancellationToken ct = default
    )
    {
        var today = config.Today(time);
        var open = await issues.GetOpenIssuesAsync(query.Dev, ct).ConfigureAwait(false);
        if (open.Count == 0)
            return new TriageResult([], "Keine offenen Tickets.", []);
        var recent = await workItems
            .GetWorkItemsAsync(query.Dev, today.AddDays(-14), today, ct)
            .ConfigureAwait(false);
        var hygiene = MetricsCalculator.Hygiene(
            open,
            recent,
            [.. config.Workday.InProgressStates],
            today,
            config.TimeZone
        );

        // Sprint pool: other devs' / unassigned sprint tasks the dev could pick up (config-driven).
        var poolRaw = await issues.GetSprintPoolIssuesAsync(query.Dev, ct).ConfigureAwait(false);
        var openIds = open.Select(i => i.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var pool = poolRaw.Where(i => !openIds.Contains(i.Id)).ToList();

        var userPrompt =
            PromptBuilder.Workday(today, config.TargetMinutesPerWorkday, config.Workday.Timezone)
            + PromptBuilder.IssueList(open, "## The dev's issues")
            + PromptBuilder.Hygiene(hygiene)
            + PromptBuilder.WorkItemList(recent)
            + (
                pool.Count > 0
                    ? PromptBuilder.IssueList(
                        pool,
                        "## Sprint pool (tasks on the sprint NOT belonging to the dev)"
                    )
                    : ""
            )
            + "\n## Task\nRank ALL of the dev's issues by what deserves focus today. "
            + "Tie-break deterministically: priority first, then staleness (older update = more urgent), then spent-vs-estimate. "
            + "Give 1-3 short reasons per issue and one focusSuggestion sentence naming the top 1-2 issues. "
            + (
                pool.Count > 0
                    ? "Additionally pick up to 5 sprint-pool tasks that best match the dev's recent focus "
                        + "(projects, components and topics visible in their booked work items) into sprintSuggestions, "
                        + "each with reasons referencing that focus. Only IDs from the sprint pool; empty array if none fit."
                    : "Return an empty sprintSuggestions array."
            );

        var json = await ai.CompleteJsonAsync(
                PromptBuilder.SystemBase,
                userPrompt,
                AiSchemas.Triage,
                ct
            )
            .ConfigureAwait(false);
        return AiResponseParser.ParseTriage(json, open, pool);
    }
}

/// <summary>
/// Calendar meetings as optional AI evidence: like git commits they describe the machine
/// owner's day, so they only apply when viewing yourself; all-day/declined entries are
/// noise, and a broken/unconfigured feed must never fail an AI request — it just means
/// "no calendar evidence".
/// </summary>
internal static class MeetingEvidence
{
    public static async Task<IReadOnlyList<CalendarMeeting>> LoadAsync(
        IMeetingReader meetings,
        string? dev,
        DateOnly from,
        DateOnly to,
        CancellationToken ct
    )
    {
        if (dev is not null)
            return [];
        try
        {
            var all = await meetings.GetMeetingsAsync(from, to, ct).ConfigureAwait(false);
            return [.. all.Where(m => m is { IsAllDay: false, IsDeclined: false })];
        }
        catch
        {
            return [];
        }
    }

    /// <summary>
    /// The meeting-rule tickets (e.g. AD-…) usually live OUTSIDE the dev's issue-query scope,
    /// but AiResponseParser validates proposed ids against the known list — without this,
    /// meeting drafts die as "unknown issue id". Summaries are fetched best-effort.
    /// </summary>
    public static async Task<IReadOnlyList<Issue>> AugmentWithRuleIssuesAsync(
        IIssueReader issues,
        IReadOnlyList<Issue> known,
        IReadOnlyList<Config.MeetingRule>? rules,
        DateTimeOffset now,
        CancellationToken ct
    )
    {
        if (rules is not { Count: > 0 })
            return known;
        var missing = rules
            .Select(r => r.IssueId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(id =>
                !known.Any(k => string.Equals(k.Id, id, StringComparison.OrdinalIgnoreCase))
            )
            .ToList();
        if (missing.Count == 0)
            return known;

        var extra = new List<Issue>();
        foreach (var id in missing)
        {
            string? summary = null;
            try
            {
                summary = (
                    await issues.GetIssueWithChildrenAsync(id, ct).ConfigureAwait(false)
                )?.Summary;
            }
            catch
            {
                // best effort — the id stays bookable even without a summary
            }
            var dash = id.IndexOf('-');
            extra.Add(
                new Issue(
                    id,
                    summary ?? "(aus Kalender-Regel)",
                    dash > 0 ? id[..dash] : id,
                    Type: null,
                    State: null,
                    Priority: null,
                    EstimateMinutes: null,
                    SpentMinutes: null,
                    Updated: now
                )
            );
        }
        return [.. known, .. extra];
    }
}
