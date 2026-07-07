using System.Text;
using YouTracker.Core.Abstractions;
using YouTracker.Core.Domain;
using YouTracker.Core.Metrics;

namespace YouTracker.Core.Ai;

/// <summary>
/// Builds deterministic, compact context blocks from already-fetched data.
/// The AI never queries YouTrack itself — everything it may reference is in these prompts.
/// </summary>
public static class PromptBuilder
{
    public const string SystemBase =
        "You are the assistant inside 'you-tracker', a personal YouTrack time-tracking tool. "
        + "You only ever PROPOSE; the user confirms every write in the UI. "
        + "Only reference issue IDs that appear in the provided issue list — never invent IDs. "
        + "Respond in the language the user's own text/data is written in (default: German).";

    public static string IssueList(IEnumerable<Issue> issues, string heading = "## Issues")
    {
        var sb = new StringBuilder(
            $"{heading} (id | project | type | state | priority | estimate | spent | updated | summary)\n"
        );
        foreach (var i in issues.OrderBy(x => x.Id, StringComparer.Ordinal))
        {
            sb.Append(i.Id)
                .Append(" | ")
                .Append(i.ProjectKey)
                .Append(" | ")
                .Append(i.Type ?? "-")
                .Append(" | ")
                .Append(i.State ?? "-")
                .Append(" | ")
                .Append(i.Priority ?? "-")
                .Append(" | ")
                .Append(i.EstimateMinutes is { } e ? DurationFormat.ToPresentation(e) : "-")
                .Append(" | ")
                .Append(i.SpentMinutes is { } s ? DurationFormat.ToPresentation(s) : "-")
                .Append(" | ")
                .Append(i.Updated.ToString("yyyy-MM-dd"))
                .Append(" | ")
                .AppendLine(i.Summary);
        }
        return sb.ToString();
    }

    public static string WorkItemList(IEnumerable<WorkItem> items)
    {
        var sb = new StringBuilder(
            "## Booked work items (date | issue | minutes | type | comment)\n"
        );
        foreach (
            var w in items
                .OrderBy(x => x.Date)
                .ThenBy(x => x.IssueId, StringComparer.Ordinal)
                .ThenBy(x => x.Id, StringComparer.Ordinal)
        )
        {
            sb.Append(w.Date.ToString("yyyy-MM-dd"))
                .Append(" | ")
                .Append(w.IssueId)
                .Append(" | ")
                .Append(w.Minutes)
                .Append(" | ")
                .Append(w.TypeName ?? "-")
                .Append(" | ")
                .AppendLine(
                    string.IsNullOrWhiteSpace(w.Text) ? "-" : w.Text.ReplaceLineEndings(" ")
                );
        }
        return sb.ToString();
    }

    /// <summary>
    /// Calendar meetings as factual evidence; when a configured rule maps a title to an
    /// issue, the target is stated so the model books it there directly.
    /// </summary>
    public static string Meetings(
        IEnumerable<CalendarMeeting> meetings,
        IReadOnlyList<Config.MeetingRule>? rules
    )
    {
        var sb = new StringBuilder(
            "## Calendar meetings in the period — factual evidence of attended meetings "
                + "(local time | duration | title | configured target issue)\n"
        );
        foreach (var m in meetings.OrderBy(x => x.Start))
        {
            var minutes = Math.Max(1, (int)Math.Round((m.End - m.Start).TotalMinutes));
            var rule = rules?.FirstOrDefault(r => r.Matches(m.Title));
            sb.Append(m.Start.ToString("yyyy-MM-dd HH:mm"))
                .Append(" | ")
                .Append(minutes)
                .Append("m | ")
                .Append(m.Title.ReplaceLineEndings(" "))
                .Append(" | ")
                .AppendLine(rule is null ? "-" : $"{rule.IssueId} (config rule)");
        }
        return sb.ToString();
    }

    public static string Commits(IEnumerable<CommitActivity> commits, TimeZoneInfo timeZone)
    {
        var sb = new StringBuilder(
            "## Git commits in the period — factual evidence of the dev's work (local time | repo | message)\n"
        );
        foreach (var c in commits.OrderBy(x => x.Timestamp))
        {
            var local = TimeZoneInfo.ConvertTime(c.Timestamp, timeZone);
            sb.Append(local.ToString("yyyy-MM-dd HH:mm"))
                .Append(" | ")
                .Append(c.Repo)
                .Append(" | ")
                .AppendLine(c.Message.ReplaceLineEndings(" "));
        }
        return sb.ToString();
    }

    public static string WorkItemTypeList(IEnumerable<WorkItemType> types)
    {
        var names = types.Select(t => t.Name).OrderBy(n => n, StringComparer.Ordinal).ToList();
        return names.Count == 0
            ? "## Work item types: (none available)\n"
            : $"## Work item types: {string.Join(", ", names)}\n";
    }

    public static string Workday(DateOnly today, int targetMinutesPerWorkday, string timezone) =>
        $"## Context\nToday: {today:yyyy-MM-dd} ({today.DayOfWeek}). Timezone: {timezone}. "
        + $"Working days are Monday-Friday with a target of {targetMinutesPerWorkday} minutes/day.\n";

    public static string Metrics(TimeOverview overview)
    {
        var sb = new StringBuilder(
            "## Metrics per day (date | booked min | target min | gap min | fokus score | context switches)\n"
        );
        foreach (var d in overview.Days)
        {
            sb.Append(d.Date.ToString("yyyy-MM-dd"))
                .Append(" | ")
                .Append(d.BookedMinutes)
                .Append(" | ")
                .Append(d.TargetMinutes)
                .Append(" | ")
                .Append(d.GapMinutes)
                .Append(" | ")
                .Append(d.FokusScore?.ToString() ?? "-")
                .Append(" | ")
                .Append(d.ContextSwitches)
                .AppendLine();
        }
        sb.Append("Totals: booked ")
            .Append(overview.TotalBookedMinutes)
            .Append(" min of ")
            .Append(overview.TotalTargetMinutes)
            .Append(" min target; average Fokus-Score ")
            .Append(overview.AverageFokusScore?.ToString() ?? "n/a")
            .AppendLine(".")
            .AppendLine(
                "Definitions: Fokus-Score = clamp(100 - 12*max(0, contextSwitches-2) - 30*(1-deepWorkShare), 0, 100); "
                    + "deep work = share of booked minutes in bookings >= 60 min."
            );
        return sb.ToString();
    }

    public static string Hygiene(IReadOnlyList<HygieneFinding> findings)
    {
        if (findings.Count == 0)
            return "## Hygiene findings: none\n";
        var sb = new StringBuilder("## Hygiene findings (kind | issue | detail)\n");
        foreach (
            var f in findings.OrderBy(x => x.IssueId, StringComparer.Ordinal).ThenBy(x => x.Kind)
        )
            sb.Append(f.Kind).Append(" | ").Append(f.IssueId).Append(" | ").AppendLine(f.Detail);
        return sb.ToString();
    }
}
