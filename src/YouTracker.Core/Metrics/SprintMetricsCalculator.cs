using YouTracker.Core.Abstractions;
using YouTracker.Core.Domain;

namespace YouTracker.Core.Metrics;

public enum HeatCellState
{
    Reached,
    Partial,
    Low,
    None,
    Today,
    Off,
    Future,
}

public sealed record HeatmapCell(DateOnly Date, int Minutes, HeatCellState State);

public sealed record DevHeatmapRow(
    string Login,
    string Name,
    IReadOnlyList<HeatmapCell> Cells,
    int TotalMinutes
);

public sealed record RoadmapGapRow(
    string Login,
    string Name,
    int RoadmapMinutes,
    int NonRoadmapMinutes,
    int UnknownMinutes,
    int TargetMinutes,
    int AttainmentPercent,
    int AvailableDays
);

public sealed record FeatureDeviation(
    string IssueId,
    string Summary,
    string? AssigneeLogin,
    string? Roadmapvorhaben,
    int? EstimateMinutes,
    int SpentMinutes,
    int GapMinutes,
    int? GapPercent
);

public enum AmpelStatus
{
    OnTrack,
    Achtung,
    Problem,
    Abwesend,
}

public sealed record DevVerdictFacts(
    string Login,
    string Name,
    AmpelStatus Ampel,
    int DaysWithBookings,
    int AvailableDays,
    int RoadmapMinutes,
    int TargetMinutes,
    int AttainmentPercent,
    int NonRoadmapMinutes,
    int UnknownMinutes,
    IReadOnlyList<string> Signals,
    IReadOnlyList<FeatureDeviation> OwnFeatures
);

public sealed record SprintDashboard(
    string SprintName,
    IReadOnlyList<DateOnly> Workdays,
    IReadOnlyList<DevHeatmapRow> Heatmap,
    IReadOnlyList<RoadmapGapRow> Gaps,
    IReadOnlyList<FeatureDeviation> Deviations,
    IReadOnlyList<DevVerdictFacts> Verdicts
);

/// <summary>
/// Pure sprint-dashboard metrics per the youtrack-sprint-zeitbuchungen skill: heatmap states,
/// roadmap booking gap (threshold × available days), feature estimation deviations (top 15,
/// ceremonies excluded) and the deterministic Ampel rules ("viel buchen ≠ viel liefern").
/// </summary>
public static class SprintMetricsCalculator
{
    /// <summary>Company-wide Roadmapvorhaben categories that count toward the roadmap target.</summary>
    public static readonly IReadOnlySet<string> RoadmapCategories = new HashSet<string>(
        ["Strategisches Projekt", "Optimierung", "Kundenprojekt"],
        StringComparer.OrdinalIgnoreCase
    );

    public const int TopDeviations = 15;

    public static SprintDashboard Build(
        TeamSprint sprint,
        IReadOnlyList<TeamMember> members,
        IReadOnlyDictionary<string, IReadOnlyList<WorkItem>> workItemsByLogin,
        IReadOnlyList<SprintTaskCategory> taskCategories,
        IReadOnlyList<SprintFeature> features,
        IReadOnlyList<string> ceremonyPatterns,
        DateOnly today
    )
    {
        var rmvByIssue = taskCategories
            .GroupBy(t => t.IssueId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => g.First().Roadmapvorhaben,
                StringComparer.OrdinalIgnoreCase
            );
        var deviations = BuildFeatureDeviations(features, ceremonyPatterns);

        var heatmap = new List<DevHeatmapRow>();
        var gaps = new List<RoadmapGapRow>();
        var verdicts = new List<DevVerdictFacts>();

        foreach (var member in members)
        {
            var items = workItemsByLogin.TryGetValue(member.Login, out var list)
                ? list
                : Array.Empty<WorkItem>();
            var availableDays = AvailableDays(sprint, member);
            heatmap.Add(BuildHeatmapRow(sprint, member, items, today));
            var gap = BuildRoadmapGap(member, items, rmvByIssue, availableDays.Count);
            gaps.Add(gap);
            verdicts.Add(BuildVerdict(member, gap, items, availableDays, deviations, today));
        }

        return new SprintDashboard(
            sprint.Name,
            sprint.Workdays,
            heatmap,
            gaps,
            deviations,
            verdicts
        );
    }

    /// <summary>Sprint workdays on which the member is available (weekday model minus absences).</summary>
    public static IReadOnlyList<DateOnly> AvailableDays(TeamSprint sprint, TeamMember member) =>
        [
            .. sprint.Workdays.Where(d =>
                member.Weekdays.Contains(d.DayOfWeek)
                && !sprint.Absences.Any(a =>
                    string.Equals(a.Login, member.Login, StringComparison.OrdinalIgnoreCase)
                    && d >= a.From
                    && d <= a.To
                )
            ),
        ];

    public static DevHeatmapRow BuildHeatmapRow(
        TeamSprint sprint,
        TeamMember member,
        IReadOnlyList<WorkItem> items,
        DateOnly today
    )
    {
        var available = AvailableDays(sprint, member).ToHashSet();
        var cells = sprint
            .Workdays.Select(date =>
            {
                var minutes = items.Where(w => w.Date == date).Sum(w => w.Minutes);
                var state =
                    !available.Contains(date) ? HeatCellState.Off
                    : date > today ? HeatCellState.Future
                    : date == today ? HeatCellState.Today
                    : minutes >= member.ThresholdMinutes ? HeatCellState.Reached
                    : minutes >= member.ThresholdMinutes / 2 ? HeatCellState.Partial
                    : minutes > 0 ? HeatCellState.Low
                    : HeatCellState.None;
                return new HeatmapCell(date, minutes, state);
            })
            .ToList();
        return new DevHeatmapRow(member.Login, member.Name, cells, cells.Sum(c => c.Minutes));
    }

    public static RoadmapGapRow BuildRoadmapGap(
        TeamMember member,
        IReadOnlyList<WorkItem> items,
        IReadOnlyDictionary<string, string?> rmvByIssue,
        int availableDays
    )
    {
        int roadmap = 0,
            nonRoadmap = 0,
            unknown = 0;
        foreach (var item in items)
        {
            var rmv = rmvByIssue.TryGetValue(item.IssueId, out var value) ? value : null;
            if (rmv is null)
                unknown += item.Minutes;
            else if (RoadmapCategories.Contains(rmv))
                roadmap += item.Minutes;
            else
                nonRoadmap += item.Minutes;
        }

        var target = member.ThresholdMinutes * availableDays;
        var attainment = target == 0 ? 0 : (int)Math.Round(100.0 * roadmap / target);
        return new RoadmapGapRow(
            member.Login,
            member.Name,
            roadmap,
            nonRoadmap,
            unknown,
            target,
            attainment,
            availableDays
        );
    }

    public static IReadOnlyList<FeatureDeviation> BuildFeatureDeviations(
        IReadOnlyList<SprintFeature> features,
        IReadOnlyList<string> ceremonyPatterns
    ) =>
        [
            .. features
                .Where(f =>
                    !ceremonyPatterns.Any(p =>
                        f.Summary.Contains(p, StringComparison.OrdinalIgnoreCase)
                    )
                )
                .Select(f =>
                {
                    var spent = f.SpentMinutes ?? 0;
                    var gap = spent - (f.EstimateMinutes ?? 0);
                    int? gapPercent = f.EstimateMinutes is > 0
                        ? (int)Math.Round(100.0 * gap / f.EstimateMinutes.Value)
                        : null;
                    return new FeatureDeviation(
                        f.Id,
                        f.Summary,
                        f.AssigneeLogin,
                        f.Roadmapvorhaben,
                        f.EstimateMinutes,
                        spent,
                        gap,
                        gapPercent
                    );
                })
                .Where(d => d.EstimateMinutes is > 0 || d.SpentMinutes > 0)
                .OrderByDescending(d => Math.Abs(d.GapMinutes))
                .Take(TopDeviations),
        ];

    /// <summary>
    /// Deterministic Ampel per the skill's criteria. Signals explain the classification and
    /// feed the AI Fazit as immutable facts.
    /// </summary>
    public static DevVerdictFacts BuildVerdict(
        TeamMember member,
        RoadmapGapRow gap,
        IReadOnlyList<WorkItem> items,
        IReadOnlyList<DateOnly> availableDays,
        IReadOnlyList<FeatureDeviation> deviations,
        DateOnly today
    )
    {
        var signals = new List<string>();
        var own = deviations
            .Where(d =>
                string.Equals(d.AssigneeLogin, member.Login, StringComparison.OrdinalIgnoreCase)
            )
            .ToList();
        var daysWithBookings = items.Select(w => w.Date).Distinct().Count();

        // Abwesend: no availability at all in this sprint.
        if (availableDays.Count == 0)
        {
            return Facts(AmpelStatus.Abwesend, ["Ganzen Sprint abwesend."]);
        }

        var overdrawn = own.Where(d => d.GapPercent is > 50).ToList();
        var doubled = own.Where(d => d.GapPercent is >= 100).ToList();
        var barelyStarted = own.Where(d =>
                d.EstimateMinutes is > 0 && d.SpentMinutes < 0.2 * d.EstimateMinutes.Value
            )
            .ToList();
        var knownMinutes = gap.RoadmapMinutes + gap.NonRoadmapMinutes;

        if (gap.AttainmentPercent < 50)
            signals.Add($"Roadmap-Erreichung {gap.AttainmentPercent}% (< 50%).");
        if (gap.UnknownMinutes > knownMinutes && gap.UnknownMinutes > 0)
            signals.Add(
                $"Nicht zuordenbare Buchungen ({DurationFormat.ToPresentation(gap.UnknownMinutes)}) übersteigen Sprint-Buchungen."
            );
        var problemOverdraw = doubled.Count >= 2 && barelyStarted.Count > 0;
        if (problemOverdraw)
            signals.Add("Mehrere Features stark überzogen, andere kaum begonnen.");

        if (signals.Count > 0)
            return Facts(AmpelStatus.Problem, signals);

        if (gap.AttainmentPercent is >= 50 and < 80)
            signals.Add($"Roadmap-Erreichung {gap.AttainmentPercent}% (50–79%).");
        foreach (var d in doubled)
            signals.Add($"{d.IssueId} läuft auf ~{100 + d.GapPercent}% der Schätzung.");
        foreach (var d in barelyStarted)
            signals.Add($"{d.IssueId} kaum begonnen (< 20% der Schätzung).");
        if (gap.NonRoadmapMinutes > gap.RoadmapMinutes && knownMinutes > 0)
            signals.Add("Support/Admin überwiegt Roadmap-Buchungen.");

        if (signals.Count > 0)
            return Facts(AmpelStatus.Achtung, signals);

        // On Track requires: attainment >= 80 AND no own feature > +50%.
        if (gap.AttainmentPercent >= 80 && overdrawn.Count == 0)
            return Facts(AmpelStatus.OnTrack, ["Roadmap-Ziel erreicht, Schätzungen eingehalten."]);

        foreach (var d in overdrawn)
            signals.Add($"{d.IssueId} überzogen (+{d.GapPercent}%).");
        return Facts(AmpelStatus.Achtung, signals);

        DevVerdictFacts Facts(AmpelStatus status, IReadOnlyList<string> reasons) =>
            new(
                member.Login,
                member.Name,
                status,
                daysWithBookings,
                availableDays.Count,
                gap.RoadmapMinutes,
                gap.TargetMinutes,
                gap.AttainmentPercent,
                gap.NonRoadmapMinutes,
                gap.UnknownMinutes,
                reasons,
                own
            );
    }
}
