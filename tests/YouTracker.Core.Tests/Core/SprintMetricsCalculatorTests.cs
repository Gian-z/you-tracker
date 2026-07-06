using YouTracker.Core.Abstractions;
using YouTracker.Core.Domain;
using YouTracker.Core.Metrics;

namespace YouTracker.Core.Tests.Core;

public sealed class SprintMetricsCalculatorTests
{
    private static readonly DateOnly Mo = new(2026, 6, 22); // Monday
    private static readonly DateOnly Di = new(2026, 6, 23);
    private static readonly DateOnly Mi = new(2026, 6, 24);
    private static readonly DateOnly Today = new(2026, 6, 24);

    private static TeamMember Member(
        string login = "GZW",
        int threshold = 420,
        params DayOfWeek[] weekdays
    ) =>
        new(
            login,
            $"Name {login}",
            threshold,
            weekdays.Length > 0
                ? weekdays
                :
                [
                    DayOfWeek.Monday,
                    DayOfWeek.Tuesday,
                    DayOfWeek.Wednesday,
                    DayOfWeek.Thursday,
                    DayOfWeek.Friday,
                ]
        );

    private static TeamSprint Sprint(params TeamAbsence[] absences) =>
        new("S1", [Mo, Di, Mi], absences);

    private static WorkItem Item(string issueId, DateOnly date, int minutes) =>
        new(Guid.NewGuid().ToString("N"), issueId, issueId, date, minutes, null, null, null, null);

    [Fact]
    public void AvailableDays_respects_weekday_model_and_absences()
    {
        var member = Member(
            "BEM",
            weekdays: [DayOfWeek.Tuesday, DayOfWeek.Wednesday] // Monday off
        );
        var sprint = Sprint(new TeamAbsence("BEM", Mi, Mi));

        var days = SprintMetricsCalculator.AvailableDays(sprint, member);

        Assert.Equal([Di], days); // Mo = weekday off, Mi = absent
    }

    [Fact]
    public void Heatmap_states_follow_threshold_bands()
    {
        var member = Member();
        var sprint = Sprint();
        var items = new[]
        {
            Item("A-1", Mo, 420), // reached
            Item("A-1", Di, 200), // partial (>= 210 would be 50%) -> 200 < 210 => Low
        };

        var row = SprintMetricsCalculator.BuildHeatmapRow(sprint, member, items, Today);

        Assert.Equal(HeatCellState.Reached, row.Cells[0].State);
        Assert.Equal(HeatCellState.Low, row.Cells[1].State);
        Assert.Equal(HeatCellState.Today, row.Cells[2].State); // Mi == today
        Assert.Equal(620, row.TotalMinutes);
    }

    [Fact]
    public void Heatmap_marks_unavailable_and_future_days()
    {
        var member = Member("BEM", weekdays: [DayOfWeek.Tuesday, DayOfWeek.Wednesday]);
        var sprint = Sprint();

        var row = SprintMetricsCalculator.BuildHeatmapRow(
            sprint,
            member,
            [],
            new DateOnly(2026, 6, 22)
        );

        Assert.Equal(HeatCellState.Off, row.Cells[0].State); // Monday off
        Assert.Equal(HeatCellState.Future, row.Cells[1].State);
        Assert.Equal(HeatCellState.Future, row.Cells[2].State);
    }

    [Fact]
    public void RoadmapGap_classifies_rmv_categories_and_computes_attainment()
    {
        var member = Member();
        var rmv = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["A-1"] = "Strategisches Projekt",
            ["A-2"] = "Support und Unterstützung",
        };
        var items = new[]
        {
            Item("A-1", Mo, 420), // roadmap
            Item("A-2", Di, 100), // non-roadmap
            Item("A-3", Di, 50), // unknown (not in map)
        };

        var gap = SprintMetricsCalculator.BuildRoadmapGap(member, items, rmv, availableDays: 2);

        Assert.Equal(420, gap.RoadmapMinutes);
        Assert.Equal(100, gap.NonRoadmapMinutes);
        Assert.Equal(50, gap.UnknownMinutes);
        Assert.Equal(840, gap.TargetMinutes);
        Assert.Equal(50, gap.AttainmentPercent);
    }

    [Fact]
    public void Deviations_exclude_ceremonies_and_rank_by_absolute_gap()
    {
        var features = new List<SprintFeature>
        {
            new("F-1", "Daily Standup", null, null, 600, 1200),
            new("F-2", "Import handler", null, "GZW", 600, 1500), // +900
            new("F-3", "Admin UI", null, null, 600, 300), // -300
        };

        var deviations = SprintMetricsCalculator.BuildFeatureDeviations(features, ["Daily"]);

        Assert.Equal(2, deviations.Count);
        Assert.Equal("F-2", deviations[0].IssueId);
        Assert.Equal(900, deviations[0].GapMinutes);
        Assert.Equal(150, deviations[0].GapPercent);
    }

    private static DevVerdictFacts Verdict(
        int attainment,
        int roadmap = 0,
        int nonRoadmap = 0,
        int unknown = 0,
        int availableDays = 10,
        IReadOnlyList<FeatureDeviation>? own = null,
        string login = "GZW"
    )
    {
        var member = Member(login);
        var gap = new RoadmapGapRow(
            login,
            member.Name,
            roadmap,
            nonRoadmap,
            unknown,
            member.ThresholdMinutes * availableDays,
            attainment,
            availableDays
        );
        var days = Enumerable.Range(0, availableDays).Select(i => Mo.AddDays(i)).ToList();
        return SprintMetricsCalculator.BuildVerdict(member, gap, [], days, own ?? [], Today);
    }

    [Fact]
    public void Ampel_abwesend_when_no_available_days()
    {
        var facts = Verdict(attainment: 0, availableDays: 0);
        Assert.Equal(AmpelStatus.Abwesend, facts.Ampel);
    }

    [Fact]
    public void Ampel_problem_below_50_percent()
    {
        Assert.Equal(AmpelStatus.Problem, Verdict(attainment: 42).Ampel);
    }

    [Fact]
    public void Ampel_problem_when_unknown_exceeds_known()
    {
        var facts = Verdict(attainment: 85, roadmap: 100, nonRoadmap: 0, unknown: 200);
        Assert.Equal(AmpelStatus.Problem, facts.Ampel);
    }

    [Fact]
    public void Ampel_achtung_between_50_and_79()
    {
        Assert.Equal(AmpelStatus.Achtung, Verdict(attainment: 66, roadmap: 100).Ampel);
    }

    [Fact]
    public void Ampel_achtung_when_own_feature_doubles_estimate_despite_high_attainment()
    {
        var own = new List<FeatureDeviation> { new("F-1", "X", "GZW", null, 600, 1320, 720, 120) };
        var facts = Verdict(attainment: 85, roadmap: 500, own: own);
        Assert.Equal(AmpelStatus.Achtung, facts.Ampel); // viel buchen != viel liefern
    }

    [Fact]
    public void Ampel_achtung_when_support_exceeds_roadmap()
    {
        var facts = Verdict(attainment: 85, roadmap: 100, nonRoadmap: 200);
        Assert.Equal(AmpelStatus.Achtung, facts.Ampel);
    }

    [Fact]
    public void Ampel_on_track_at_80_percent_without_overdrawn_features()
    {
        var facts = Verdict(attainment: 85, roadmap: 500);
        Assert.Equal(AmpelStatus.OnTrack, facts.Ampel);
    }

    [Fact]
    public void Ampel_achtung_when_on_track_percent_but_feature_over_50_percent()
    {
        var own = new List<FeatureDeviation> { new("F-1", "X", "GZW", null, 600, 960, 360, 60) };
        var facts = Verdict(attainment: 85, roadmap: 500, own: own);
        Assert.Equal(AmpelStatus.Achtung, facts.Ampel);
    }
}
