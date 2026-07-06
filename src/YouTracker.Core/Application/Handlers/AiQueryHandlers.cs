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
        var known = Merge(open, recent);
        var types = await workItems.GetWorkItemTypesAsync(ct).ConfigureAwait(false);
        var booked = await workItems
            .GetWorkItemsAsync(query.Dev, query.Date, query.Date, ct)
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
            + $"\n## Task\nThe user describes their work for {query.Date:yyyy-MM-dd}. "
            + "Map each described activity to one issue from the list and propose work items (drafts). "
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
        var known = DraftWorkLogQueryHandler.Merge(open, recent);
        var types = await workItems.GetWorkItemTypesAsync(ct).ConfigureAwait(false);

        var gapLines = string.Join(
            "\n",
            gaps.Select(g => $"{g.Date:yyyy-MM-dd}: {g.GapMinutes} min missing")
        );
        var userPrompt =
            PromptBuilder.Workday(today, config.TargetMinutesPerWorkday, config.Workday.Timezone)
            + PromptBuilder.IssueList(known)
            + PromptBuilder.WorkItemTypeList(types)
            + PromptBuilder.WorkItemList(items)
            + $"\n## Gap days\n{gapLines}\n"
            + "\n## Task\nPropose plausible work items (drafts) that fill each gap day, based on which issues "
            + "the user recently worked on or updated. Prefer issues already booked adjacent days. "
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

        var userPrompt =
            PromptBuilder.Workday(
                config.Today(time),
                config.TargetMinutesPerWorkday,
                config.Workday.Timezone
            )
            + PromptBuilder.WorkItemList(items)
            + PromptBuilder.Metrics(overview)
            + $"\n## Task\nWrite a standup-ready recap of {query.From:yyyy-MM-dd} – {query.To:yyyy-MM-dd}: "
            + "2-5 sentences overall, then one bullet per issue with the booked time. "
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
            return new TriageResult([], "Keine offenen Tickets.");
        var recent = await workItems
            .GetWorkItemsAsync(query.Dev, today.AddDays(-14), today, ct)
            .ConfigureAwait(false);
        var hygiene = MetricsCalculator.Hygiene(
            open,
            recent,
            [.. config.Workday.InProgressStates],
            today
        );

        var userPrompt =
            PromptBuilder.Workday(today, config.TargetMinutesPerWorkday, config.Workday.Timezone)
            + PromptBuilder.IssueList(open)
            + PromptBuilder.Hygiene(hygiene)
            + "\n## Task\nRank ALL issues by what deserves focus today. "
            + "Tie-break deterministically: priority first, then staleness (older update = more urgent), then spent-vs-estimate. "
            + "Give 1-3 short reasons per issue and one focusSuggestion sentence naming the top 1-2 issues.";

        var json = await ai.CompleteJsonAsync(
                PromptBuilder.SystemBase,
                userPrompt,
                AiSchemas.Triage,
                ct
            )
            .ConfigureAwait(false);
        return AiResponseParser.ParseTriage(json, open);
    }
}
