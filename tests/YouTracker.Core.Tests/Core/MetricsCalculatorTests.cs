using YouTracker.Core.Metrics;

namespace YouTracker.Core.Tests.Core;

public class MetricsCalculatorTests
{
    // 2026-07-06 is a Monday.
    private static readonly DateOnly Monday = new(2026, 7, 6);

    [Theory]
    [InlineData(1, 1.0, 100)]
    [InlineData(2, 1.0, 100)]
    [InlineData(3, 1.0, 88)]
    [InlineData(5, 0.5, 49)]
    [InlineData(50, 0.0, 0)] // clamped to 0
    public void FokusScore_matches_formula_and_clamps(
        int switches,
        double deepShare,
        int expected
    ) => Assert.Equal(expected, MetricsCalculator.FokusScore(switches, deepShare));

    [Fact]
    public void BuildOverview_computes_booked_switches_deep_share_and_gap()
    {
        var day = Monday;
        var items = new[]
        {
            TestData.WorkItem("ABC-1", day, 30),
            TestData.WorkItem("ABC-2", day, 90),
        };

        var overview = MetricsCalculator.BuildOverview(items, day, day, today: day, 480);

        var summary = Assert.Single(overview.Days);
        Assert.True(summary.IsWorkday);
        Assert.Equal(120, summary.BookedMinutes);
        Assert.Equal(2, summary.ContextSwitches);
        Assert.Equal(0.75, summary.DeepWorkShare);
        Assert.Equal(480 - 120, summary.GapMinutes);
    }

    [Fact]
    public void BuildOverview_weekend_day_is_not_workday_and_has_no_gap()
    {
        var saturday = new DateOnly(2026, 7, 4);

        var overview = MetricsCalculator.BuildOverview([], saturday, saturday, Monday, 480);

        var summary = Assert.Single(overview.Days);
        Assert.False(summary.IsWorkday);
        Assert.Equal(0, summary.GapMinutes);
        Assert.Equal(0, summary.TargetMinutes);
    }

    [Fact]
    public void BuildOverview_future_days_have_no_gap()
    {
        var tomorrow = Monday.AddDays(1); // Tuesday, after "today"

        var overview = MetricsCalculator.BuildOverview([], tomorrow, tomorrow, today: Monday, 480);

        var summary = Assert.Single(overview.Days);
        Assert.True(summary.IsWorkday);
        Assert.Equal(0, summary.GapMinutes);
    }

    [Fact]
    public void BuildOverview_day_without_bookings_has_null_fokus_score()
    {
        var overview = MetricsCalculator.BuildOverview([], Monday, Monday, Monday, 480);

        Assert.Null(Assert.Single(overview.Days).FokusScore);
        Assert.Null(overview.AverageFokusScore);
    }

    [Fact]
    public void BuildOverview_exactly_sixty_minute_item_counts_as_deep_work()
    {
        var items = new[] { TestData.WorkItem("ABC-1", Monday, 60) };

        var overview = MetricsCalculator.BuildOverview(items, Monday, Monday, Monday, 480);

        Assert.Equal(1.0, Assert.Single(overview.Days).DeepWorkShare);
    }

    [Fact]
    public void FindGaps_skips_weekends_and_future_days()
    {
        // Fri 2026-07-03 .. Tue 2026-07-07, today = Monday 2026-07-06, no bookings at all.
        var from = new DateOnly(2026, 7, 3);
        var to = new DateOnly(2026, 7, 7);
        var overview = MetricsCalculator.BuildOverview([], from, to, today: Monday, 480);

        var gaps = MetricsCalculator.FindGaps(overview, Monday);

        Assert.Equal([new DateOnly(2026, 7, 3), Monday], gaps.Select(g => g.Date).ToArray());
    }

    [Fact]
    public void AddWorkdays_minus_three_from_monday_is_previous_wednesday()
    {
        var result = MetricsCalculator.AddWorkdays(Monday, -3);

        Assert.Equal(new DateOnly(2026, 7, 1), result);
        Assert.Equal(DayOfWeek.Wednesday, result.DayOfWeek);
    }

    [Fact]
    public void Hygiene_flags_in_progress_issue_without_recent_booking()
    {
        var issue = TestData.Issue("ABC-1", state: "In Bearbeitung");

        var findings = MetricsCalculator.Hygiene(
            [issue],
            [],
            ["In Bearbeitung", "In Arbeit"],
            Monday
        );

        var finding = Assert.Single(findings);
        Assert.Equal(HygieneKind.NoRecentBooking, finding.Kind);
        Assert.Equal("ABC-1", finding.IssueId);
    }

    [Fact]
    public void Hygiene_does_not_flag_when_recent_booking_exists()
    {
        var issue = TestData.Issue("ABC-1", state: "In Bearbeitung");
        var booking = TestData.WorkItem("ABC-1", Monday, 60); // today, i.e. within threshold

        var findings = MetricsCalculator.Hygiene(
            [issue],
            [booking],
            ["In Bearbeitung", "In Arbeit"],
            Monday
        );

        Assert.Empty(findings);
    }

    [Fact]
    public void Hygiene_flags_spent_over_estimate()
    {
        var issue = TestData.Issue("ABC-2", state: "Open", estimate: 60, spent: 120);

        var findings = MetricsCalculator.Hygiene([issue], [], ["In Bearbeitung"], Monday);

        var finding = Assert.Single(findings);
        Assert.Equal(HygieneKind.OverEstimate, finding.Kind);
    }

    [Fact]
    public void Hygiene_flags_issue_not_updated_for_more_than_14_days()
    {
        var issue = TestData.Issue(
            "ABC-3",
            state: "Open",
            updated: new DateTimeOffset(2026, 6, 1, 8, 0, 0, TimeSpan.Zero)
        );

        var findings = MetricsCalculator.Hygiene([issue], [], ["In Bearbeitung"], Monday);

        var finding = Assert.Single(findings);
        Assert.Equal(HygieneKind.Stale, finding.Kind);
    }

    [Fact]
    public void Hygiene_matches_in_progress_state_case_insensitively()
    {
        var issue = TestData.Issue("ABC-4", state: "in bearbeitung");

        var findings = MetricsCalculator.Hygiene(
            [issue],
            [],
            ["In Bearbeitung", "In Arbeit"],
            Monday
        );

        var finding = Assert.Single(findings);
        Assert.Equal(HygieneKind.NoRecentBooking, finding.Kind);
    }
}
