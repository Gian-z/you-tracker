using YouTracker.Core.Domain;

namespace YouTracker.Core.Metrics;

public sealed record DaySummary(
    DateOnly Date,
    int BookedMinutes,
    int TargetMinutes,
    bool IsWorkday,
    int GapMinutes,
    int? FokusScore,
    int ContextSwitches,
    double DeepWorkShare,
    IReadOnlyList<WorkItem> Items
);

public sealed record TimeOverview(
    DateOnly From,
    DateOnly To,
    IReadOnlyList<DaySummary> Days,
    int TotalBookedMinutes,
    int TotalTargetMinutes,
    int? AverageFokusScore
);

public enum HygieneKind
{
    NoRecentBooking,
    OverEstimate,
    Stale,
}

public sealed record HygieneFinding(
    HygieneKind Kind,
    string IssueId,
    string Summary,
    string Detail
);

/// <summary>
/// Pure, deterministic metric functions (no I/O). Definitions match the proven
/// cmi-my-fokus-dashboard rules: 8h/day Mon-Fri target, Fokus-Score, deep-work >= 1h,
/// hygiene checks (no recent booking / over estimate / stale).
/// </summary>
public static class MetricsCalculator
{
    public const int StaleAfterDays = 14;
    public const int NoBookingWorkdays = 3;

    /// <summary>Fokus-Score = clamp(100 − 12×max(0, switches−2) − 30×(1−deepWorkShare), 0, 100).</summary>
    public static int FokusScore(int contextSwitches, double deepWorkShare) =>
        Math.Clamp(
            (int)
                Math.Round(
                    100 - 12.0 * Math.Max(0, contextSwitches - 2) - 30.0 * (1 - deepWorkShare)
                ),
            0,
            100
        );

    public static TimeOverview BuildOverview(
        IReadOnlyList<WorkItem> workItems,
        DateOnly from,
        DateOnly to,
        DateOnly today,
        int targetMinutesPerWorkday
    )
    {
        var days = new List<DaySummary>();
        for (var date = from; date <= to; date = date.AddDays(1))
        {
            var items = workItems
                .Where(w => w.Date == date)
                .OrderBy(w => w.IssueId)
                .ThenBy(w => w.Id)
                .ToList();
            var booked = items.Sum(w => w.Minutes);
            var isWorkday = date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday);
            var target = isWorkday ? targetMinutesPerWorkday : 0;
            var gap = isWorkday && date <= today ? Math.Max(0, target - booked) : 0;
            var switches = items.Select(w => w.IssueId).Distinct().Count();
            var deepShare =
                booked == 0
                    ? 0
                    : (double)items.Where(w => w.Minutes >= 60).Sum(w => w.Minutes) / booked;
            int? score = booked == 0 ? null : FokusScore(switches, deepShare);
            days.Add(
                new DaySummary(
                    date,
                    booked,
                    target,
                    isWorkday,
                    gap,
                    score,
                    switches,
                    deepShare,
                    items
                )
            );
        }

        var totalTarget = days.Where(d => d.IsWorkday && d.Date <= today).Sum(d => d.TargetMinutes);
        var scores = days.Where(d => d.FokusScore is not null)
            .Select(d => d.FokusScore!.Value)
            .ToList();
        return new TimeOverview(
            from,
            to,
            days,
            days.Sum(d => d.BookedMinutes),
            totalTarget,
            scores.Count == 0 ? null : (int)Math.Round(scores.Average())
        );
    }

    /// <summary>Workdays up to today with less time booked than the target.</summary>
    public static IReadOnlyList<DaySummary> FindGaps(TimeOverview overview, DateOnly today) =>
        [.. overview.Days.Where(d => d.IsWorkday && d.Date <= today && d.GapMinutes > 0)];

    public static IReadOnlyList<HygieneFinding> Hygiene(
        IReadOnlyList<Issue> openIssues,
        IReadOnlyList<WorkItem> recentWorkItems,
        IReadOnlyCollection<string> inProgressStates,
        DateOnly today
    )
    {
        var findings = new List<HygieneFinding>();
        var bookingThreshold = AddWorkdays(today, -NoBookingWorkdays);
        var staleThreshold = today.AddDays(-StaleAfterDays);

        foreach (var issue in openIssues)
        {
            if (
                issue.State is not null
                && inProgressStates.Contains(issue.State, StringComparer.OrdinalIgnoreCase)
                && !recentWorkItems.Any(w => w.IssueId == issue.Id && w.Date >= bookingThreshold)
            )
            {
                findings.Add(
                    new(
                        HygieneKind.NoRecentBooking,
                        issue.Id,
                        issue.Summary,
                        $"State '{issue.State}' but no booking since {bookingThreshold:yyyy-MM-dd}"
                    )
                );
            }

            if (
                issue is { EstimateMinutes: > 0, SpentMinutes: not null }
                && issue.SpentMinutes > issue.EstimateMinutes
            )
            {
                findings.Add(
                    new(
                        HygieneKind.OverEstimate,
                        issue.Id,
                        issue.Summary,
                        $"Spent {DurationFormat.ToPresentation(issue.SpentMinutes.Value)} > estimate {DurationFormat.ToPresentation(issue.EstimateMinutes.Value)}"
                    )
                );
            }

            if (DateOnly.FromDateTime(issue.Updated.UtcDateTime) < staleThreshold)
            {
                findings.Add(
                    new(
                        HygieneKind.Stale,
                        issue.Id,
                        issue.Summary,
                        $"No update since {issue.Updated:yyyy-MM-dd}"
                    )
                );
            }
        }

        return findings;
    }

    /// <summary>Adds (or subtracts) working days, skipping Saturdays and Sundays.</summary>
    public static DateOnly AddWorkdays(DateOnly date, int workdays)
    {
        var step = Math.Sign(workdays);
        var remaining = Math.Abs(workdays);
        var current = date;
        while (remaining > 0)
        {
            current = current.AddDays(step);
            if (current.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
                remaining--;
        }
        return current;
    }
}
