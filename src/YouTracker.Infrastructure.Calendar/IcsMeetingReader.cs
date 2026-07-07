using Ical.Net.CalendarComponents;
using Ical.Net.DataTypes;
using YouTracker.Core.Abstractions;
using YouTracker.Core.Config;

namespace YouTracker.Infrastructure.Calendar;

/// <summary>
/// Reads the user's published Outlook ICS feed. Recurring meetings arrive as un-expanded
/// RRULE masters (plus EXDATE/RECURRENCE-ID overrides) — Ical.Net expands them; naive line
/// parsing would show a daily standup on exactly one day of its lifetime.
/// </summary>
public sealed class IcsMeetingReader(HttpClient http, AppConfig config) : IMeetingReader
{
    public async Task<IReadOnlyList<CalendarMeeting>> GetMeetingsAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken ct = default
    )
    {
        var url = config.Calendar?.IcsUrl;
        if (string.IsNullOrWhiteSpace(url))
            return [];

        string ics;
        try
        {
            using var response = await http.GetAsync(url, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            ics = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
            when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            throw new InvalidOperationException(
                "Kalender-Feed konnte nicht geladen werden: "
                    + ex.Message
                    + " – URL in config.json prüfen ('Kalender veröffentlichen' in Outlook).",
                ex
            );
        }

        return ParseMeetings(ics, from, to, config.TimeZone);
    }

    /// <summary>Pure and testable (mirrors GitCommitActivityReader.ParseGitLog).</summary>
    internal static IReadOnlyList<CalendarMeeting> ParseMeetings(
        string ics,
        DateOnly from,
        DateOnly to,
        TimeZoneInfo timeZone
    )
    {
        Ical.Net.Calendar calendar;
        try
        {
            calendar =
                Ical.Net.Calendar.Load(ics)
                ?? throw new InvalidOperationException("Leerer Kalender.");
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "Der Kalender-Feed ist kein gültiger iCalendar-Inhalt: " + ex.Message,
                ex
            );
        }

        var rangeStart = new CalDateTime(from.Year, from.Month, from.Day);
        // One buffer day on each side of the UTC window; the wall-clock check below is exact.
        var rangeEnd = to.AddDays(2);
        var endUtc = new DateTime(
            rangeEnd.Year,
            rangeEnd.Month,
            rangeEnd.Day,
            0,
            0,
            0,
            DateTimeKind.Utc
        );

        var meetings = new List<CalendarMeeting>();
        var occurrences = calendar
            .GetOccurrences(rangeStart)
            .TakeWhile(o => o.Period.StartTime.AsUtc < endUtc);
        foreach (var occurrence in occurrences)
        {
            if (occurrence.Source is not CalendarEvent evt)
                continue;

            var start = ToConfiguredZone(occurrence.Period.StartTime, timeZone);
            var endTime = occurrence.Period.EffectiveEndTime;
            var end = endTime is null ? start : ToConfiguredZone(endTime, timeZone);

            // The expansion window is UTC-ish; re-check the wall-clock date in the configured zone.
            var localDate = DateOnly.FromDateTime(start.DateTime);
            if (localDate < from || localDate > to)
                continue;

            meetings.Add(
                new CalendarMeeting(
                    evt.Summary ?? "(ohne Titel)",
                    start,
                    end,
                    IsAllDay: evt.IsAllDay,
                    IsDeclined: IsDeclined(evt)
                )
            );
        }
        return meetings;
    }

    /// <summary>
    /// Published feeds strip ATTENDEE/PARTSTAT; a declined-but-kept meeting is exported with
    /// X-MICROSOFT-CDO-BUSYSTATUS:FREE, a cancelled one with STATUS:CANCELLED.
    /// </summary>
    private static bool IsDeclined(CalendarEvent evt)
    {
        if (string.Equals(evt.Status, "CANCELLED", StringComparison.OrdinalIgnoreCase))
            return true;
        var busyStatus = evt.Properties.Get<string>("X-MICROSOFT-CDO-BUSYSTATUS");
        return string.Equals(busyStatus, "FREE", StringComparison.OrdinalIgnoreCase);
    }

    private static DateTimeOffset ToConfiguredZone(CalDateTime value, TimeZoneInfo timeZone)
    {
        // AsUtc handles UTC DTSTARTs, TZID-local values (incl. Windows tz ids from
        // VTIMEZONE) and floating times alike.
        var utc = new DateTimeOffset(value.AsUtc, TimeSpan.Zero);
        return TimeZoneInfo.ConvertTime(utc, timeZone);
    }
}
