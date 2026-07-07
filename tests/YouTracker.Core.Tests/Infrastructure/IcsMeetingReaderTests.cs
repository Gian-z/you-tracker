using YouTracker.Infrastructure.Calendar;

namespace YouTracker.Core.Tests.Infrastructure;

public sealed class IcsMeetingReaderTests
{
    private static readonly TimeZoneInfo Zurich = TimeZoneInfo.FindSystemTimeZoneById(
        "Europe/Zurich"
    );

    // Shaped like an M365 published feed: Windows VTIMEZONE id, a recurring daily with
    // EXDATE, a TZID-local single event, a UTC event, an all-day event, a cancelled and a
    // busystatus-FREE event.
    private const string Ics = """
        BEGIN:VCALENDAR
        PRODID:Microsoft Exchange Server 2010
        VERSION:2.0
        BEGIN:VTIMEZONE
        TZID:W. Europe Standard Time
        BEGIN:STANDARD
        DTSTART:16010101T030000
        TZOFFSETFROM:+0200
        TZOFFSETTO:+0100
        RRULE:FREQ=YEARLY;INTERVAL=1;BYDAY=-1SU;BYMONTH=10
        END:STANDARD
        BEGIN:DAYLIGHT
        DTSTART:16010101T020000
        TZOFFSETFROM:+0100
        TZOFFSETTO:+0200
        RRULE:FREQ=YEARLY;INTERVAL=1;BYDAY=-1SU;BYMONTH=3
        END:DAYLIGHT
        END:VTIMEZONE
        BEGIN:VEVENT
        UID:daily-1
        SUMMARY:Daily Standup
        DTSTART;TZID=W. Europe Standard Time:20260701T090000
        DTEND;TZID=W. Europe Standard Time:20260701T091500
        RRULE:FREQ=DAILY;COUNT=20
        EXDATE;TZID=W. Europe Standard Time:20260707T090000
        END:VEVENT
        BEGIN:VEVENT
        UID:single-1
        SUMMARY:Sprint Retro
        DTSTART;TZID=W. Europe Standard Time:20260706T140000
        DTEND;TZID=W. Europe Standard Time:20260706T150000
        END:VEVENT
        BEGIN:VEVENT
        UID:utc-1
        SUMMARY:Architektur-Sync
        DTSTART:20260706T080000Z
        DTEND:20260706T083000Z
        END:VEVENT
        BEGIN:VEVENT
        UID:allday-1
        SUMMARY:Ferien
        DTSTART;VALUE=DATE:20260706
        DTEND;VALUE=DATE:20260707
        END:VEVENT
        BEGIN:VEVENT
        UID:cancelled-1
        SUMMARY:Abgesagtes Meeting
        STATUS:CANCELLED
        DTSTART;TZID=W. Europe Standard Time:20260706T100000
        DTEND;TZID=W. Europe Standard Time:20260706T110000
        END:VEVENT
        BEGIN:VEVENT
        UID:free-1
        SUMMARY:Optionaler Termin
        X-MICROSOFT-CDO-BUSYSTATUS:FREE
        DTSTART;TZID=W. Europe Standard Time:20260706T160000
        DTEND;TZID=W. Europe Standard Time:20260706T163000
        END:VEVENT
        END:VCALENDAR
        """;

    private static IReadOnlyList<YouTracker.Core.Abstractions.CalendarMeeting> Parse(
        DateOnly day
    ) => IcsMeetingReader.ParseMeetings(Ics, day, day, Zurich);

    [Fact]
    public void Recurring_daily_materializes_on_a_day_after_dtstart()
    {
        // 2026-07-06 is five days after the RRULE master's DTSTART.
        var meetings = Parse(new DateOnly(2026, 7, 6));

        var daily = Assert.Single(meetings, m => m.Title == "Daily Standup");
        Assert.Equal(new DateOnly(2026, 7, 6), DateOnly.FromDateTime(daily.Start.DateTime));
        Assert.Equal(new TimeOnly(9, 0), TimeOnly.FromDateTime(daily.Start.DateTime));
        Assert.Equal(15, (int)(daily.End - daily.Start).TotalMinutes);
    }

    [Fact]
    public void Exdate_day_yields_no_daily_occurrence()
    {
        var meetings = Parse(new DateOnly(2026, 7, 7));

        Assert.DoesNotContain(meetings, m => m.Title == "Daily Standup");
    }

    [Fact]
    public void Utc_and_tzid_events_land_at_correct_zurich_wall_time()
    {
        var meetings = Parse(new DateOnly(2026, 7, 6));

        var retro = Assert.Single(meetings, m => m.Title == "Sprint Retro");
        Assert.Equal(new TimeOnly(14, 0), TimeOnly.FromDateTime(retro.Start.DateTime));

        // 08:00Z == 10:00 Europe/Zurich in July (CEST).
        var sync = Assert.Single(meetings, m => m.Title == "Architektur-Sync");
        Assert.Equal(new TimeOnly(10, 0), TimeOnly.FromDateTime(sync.Start.DateTime));
        Assert.Equal(30, (int)(sync.End - sync.Start).TotalMinutes);
    }

    [Fact]
    public void All_day_cancelled_and_free_events_are_flagged()
    {
        var meetings = Parse(new DateOnly(2026, 7, 6));

        Assert.True(Assert.Single(meetings, m => m.Title == "Ferien").IsAllDay);
        Assert.True(Assert.Single(meetings, m => m.Title == "Abgesagtes Meeting").IsDeclined);
        Assert.True(Assert.Single(meetings, m => m.Title == "Optionaler Termin").IsDeclined);
        Assert.False(Assert.Single(meetings, m => m.Title == "Sprint Retro").IsDeclined);
    }

    [Fact]
    public void Invalid_ics_throws_a_german_error()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            IcsMeetingReader.ParseMeetings(
                "definitely not a calendar",
                new DateOnly(2026, 7, 6),
                new DateOnly(2026, 7, 6),
                Zurich
            )
        );
        Assert.Contains("Kalender", ex.Message);
    }
}
