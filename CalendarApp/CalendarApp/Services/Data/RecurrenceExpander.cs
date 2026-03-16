using CalendarApp.Models;

namespace CalendarApp.Services.Data;

/// <summary>
/// Expands a recurring CalendarEvent into individual occurrences within a date range.
/// Supports DAILY, WEEKLY (BYDAY), MONTHLY (BYDAY with ordinals, BYMONTHDAY, or same day),
/// and YEARLY frequencies, plus COUNT and UNTIL limits.
/// </summary>
internal static class RecurrenceExpander
{
    private static readonly Dictionary<string, DayOfWeek> DayCodeMap =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["SU"] = DayOfWeek.Sunday,  ["MO"] = DayOfWeek.Monday,
            ["TU"] = DayOfWeek.Tuesday, ["WE"] = DayOfWeek.Wednesday,
            ["TH"] = DayOfWeek.Thursday,["FR"] = DayOfWeek.Friday,
            ["SA"] = DayOfWeek.Saturday
        };

    /// <summary>
    /// Returns occurrences of <paramref name="baseEvent"/> that overlap [rangeStart, rangeEnd].
    /// </summary>
    public static IEnumerable<CalendarEvent> Expand(
        CalendarEvent baseEvent,
        DateTime rangeStart,
        DateTime rangeEnd)
    {
        var rule = baseEvent.RecurrenceRule;
        if (rule == null) yield break;

        var baseStart   = baseEvent.StartDateTime;
        var duration    = baseEvent.EndDateTime - baseEvent.StartDateTime;
        var byDays      = ParseByDay(rule.ByDay);
        var interval    = Math.Max(rule.Interval, 1);

        // The generator stops at the smaller of UNTIL and rangeEnd.
        // COUNT is handled by a separate counter below.
        var effectiveUntil = rule.UntilDateUtc.HasValue
            ? new DateTime(Math.Min(rule.UntilDateUtc.Value.Ticks, rangeEnd.Ticks))
            : rangeEnd;

        int maxCount  = rule.Count ?? int.MaxValue;
        int generated = 0; // total occurrences counted from baseStart (for COUNT)

        var occurrences = rule.Frequency switch
        {
            RecurrenceFrequency.Daily   => DailyOccurrences(baseStart, effectiveUntil, interval),
            RecurrenceFrequency.Weekly  => WeeklyOccurrences(baseStart, effectiveUntil, interval, byDays),
            RecurrenceFrequency.Monthly => MonthlyOccurrences(baseStart, effectiveUntil, interval, byDays, rule.ByMonthDay),
            RecurrenceFrequency.Yearly  => YearlyOccurrences(baseStart, effectiveUntil, interval),
            _                           => Enumerable.Empty<DateTime>()
        };

        foreach (var occStart in occurrences)
        {
            if (generated >= maxCount) break;
            generated++;

            var occEnd = occStart + duration;
            // Yield only if this occurrence overlaps the requested range
            if (occStart <= rangeEnd && occEnd >= rangeStart)
                yield return MakeOccurrence(baseEvent, occStart, occEnd);
        }
    }

    // ── Frequency generators ────────────────────────────────────────────────

    private static IEnumerable<DateTime> DailyOccurrences(
        DateTime start, DateTime until, int interval)
    {
        for (var d = start; d <= until; d = d.AddDays(interval))
            yield return d;
    }

    private static IEnumerable<DateTime> WeeklyOccurrences(
        DateTime start, DateTime until, int interval,
        List<(int? ordinal, DayOfWeek dow)> byDays)
    {
        if (byDays.Count == 0)
        {
            // No BYDAY → repeat on the same day of week as the base event
            for (var d = start; d <= until; d = d.AddDays(interval * 7))
                yield return d;
            yield break;
        }

        // Align to the Sunday of the week containing baseStart
        var weekStart = start.Date;
        while (weekStart.DayOfWeek != DayOfWeek.Sunday)
            weekStart = weekStart.AddDays(-1);

        var timeOfDay = start.TimeOfDay;

        for (var ws = weekStart; ws <= until.Date; ws = ws.AddDays(interval * 7))
        {
            foreach (var (_, dow) in byDays.OrderBy(b => (int)b.dow))
            {
                int offset = ((int)dow - (int)DayOfWeek.Sunday + 7) % 7;
                var occ = ws.AddDays(offset).Add(timeOfDay);
                if (occ >= start && occ <= until)
                    yield return occ;
            }
        }
    }

    private static IEnumerable<DateTime> MonthlyOccurrences(
        DateTime start, DateTime until, int interval,
        List<(int? ordinal, DayOfWeek dow)> byDays,
        string byMonthDay)
    {
        var timeOfDay  = start.TimeOfDay;
        var monthStart = new DateTime(start.Year, start.Month, 1);
        var untilMonth = new DateTime(until.Year, until.Month, 1);

        for (var month = monthStart; month <= untilMonth; month = month.AddMonths(interval))
        {
            if (byDays.Count > 0)
            {
                // Collect all dates this BYDAY rule produces for this month, sorted
                var candidates = new List<DateTime>();
                foreach (var (ordinal, dow) in byDays)
                {
                    if (ordinal.HasValue)
                    {
                        // Ordinal form: 1SA, 4SA, -1MO, …
                        var d = FindOrdinalDay(month, ordinal.Value, dow);
                        if (d.HasValue) candidates.Add(d.Value.Add(timeOfDay));
                    }
                    else
                    {
                        // No ordinal → every occurrence of that weekday in the month
                        var d = month;
                        while (d.DayOfWeek != dow) d = d.AddDays(1);
                        while (d.Month == month.Month)
                        {
                            candidates.Add(d.Add(timeOfDay));
                            d = d.AddDays(7);
                        }
                    }
                }

                foreach (var occ in candidates.OrderBy(d => d))
                    if (occ >= start && occ <= until)
                        yield return occ;
            }
            else if (!string.IsNullOrEmpty(byMonthDay))
            {
                // BYMONTHDAY: comma-separated day numbers (positive or negative)
                foreach (var part in byMonthDay.Split(','))
                {
                    if (!int.TryParse(part.Trim(), out int mday)) continue;
                    int daysInMonth = DateTime.DaysInMonth(month.Year, month.Month);
                    int day = mday > 0
                        ? Math.Min(mday, daysInMonth)
                        : daysInMonth + mday + 1; // -1 = last day
                    if (day < 1 || day > daysInMonth) continue;
                    var occ = new DateTime(month.Year, month.Month, day).Add(timeOfDay);
                    if (occ >= start && occ <= until)
                        yield return occ;
                }
            }
            else
            {
                // Same day-of-month as base event (clamped to month length)
                int targetDay = Math.Min(start.Day, DateTime.DaysInMonth(month.Year, month.Month));
                var occ = new DateTime(month.Year, month.Month, targetDay).Add(timeOfDay);
                if (occ >= start && occ <= until)
                    yield return occ;
            }
        }
    }

    private static IEnumerable<DateTime> YearlyOccurrences(
        DateTime start, DateTime until, int interval)
    {
        for (int year = start.Year; year <= until.Year; year += interval)
        {
            int days = DateTime.DaysInMonth(year, start.Month);
            int day  = Math.Min(start.Day, days);
            var occ  = new DateTime(year, start.Month, day).Add(start.TimeOfDay);
            if (occ >= start && occ <= until)
                yield return occ;
        }
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    /// <summary>Finds the Nth (ordinal) occurrence of a weekday in the given month.</summary>
    /// <param name="monthStart">First day of the target month.</param>
    /// <param name="ordinal">Positive = from start (1 = first, 2 = second …); negative = from end (-1 = last …).</param>
    private static DateTime? FindOrdinalDay(DateTime monthStart, int ordinal, DayOfWeek dow)
    {
        if (ordinal == 0) ordinal = 1;
        int daysInMonth = DateTime.DaysInMonth(monthStart.Year, monthStart.Month);

        if (ordinal > 0)
        {
            // Walk forward to first occurrence of dow, then add (ordinal-1) weeks
            var first = monthStart;
            while (first.DayOfWeek != dow) first = first.AddDays(1);
            var result = first.AddDays((ordinal - 1) * 7);
            return result.Month == monthStart.Month ? result : null;
        }
        else
        {
            // Walk backward from last day of month
            var last = new DateTime(monthStart.Year, monthStart.Month, daysInMonth);
            while (last.DayOfWeek != dow) last = last.AddDays(-1);
            // ordinal=-1 → no shift; ordinal=-2 → subtract 1 week
            var result = last.AddDays((ordinal + 1) * 7);
            return result.Month == monthStart.Month ? result : null;
        }
    }

    /// <summary>
    /// Parses a BYDAY string (e.g. "MO,WE,FR" or "1SA,4SA" or "-1MO") into
    /// (optional ordinal, DayOfWeek) tuples.
    /// </summary>
    private static List<(int? ordinal, DayOfWeek dow)> ParseByDay(string byDay)
    {
        var result = new List<(int?, DayOfWeek)>();
        if (string.IsNullOrEmpty(byDay)) return result;

        foreach (var part in byDay.Split(','))
        {
            var s = part.Trim();
            if (s.Length < 2) continue;

            // Last 2 chars are always the day code (MO, TU, WE, TH, FR, SA, SU)
            var code   = s[^2..];
            var prefix = s[..^2];

            if (!DayCodeMap.TryGetValue(code, out var dow)) continue;

            int? ordinal = null;
            if (!string.IsNullOrEmpty(prefix) && int.TryParse(prefix, out var o))
                ordinal = o;

            result.Add((ordinal, dow));
        }
        return result;
    }

    private static CalendarEvent MakeOccurrence(CalendarEvent src, DateTime start, DateTime end) => new()
    {
        Id                 = src.Id,
        GoogleEventId      = src.GoogleEventId,
        Title              = src.Title,
        Description        = src.Description,
        IsAllDay           = src.IsAllDay,
        Location           = src.Location,
        CalendarId         = src.CalendarId,
        ColorHex           = src.ColorHex,
        CalendarMode       = src.CalendarMode,
        EventScope         = src.EventScope,
        GoogleAccountAlias = src.GoogleAccountAlias,
        SyncStatus         = src.SyncStatus,
        ETag               = src.ETag,
        LastModifiedUtc    = src.LastModifiedUtc,
        Reminders          = src.Reminders,
        RecurrenceRule     = src.RecurrenceRule,
        StartDateTime      = start,
        EndDateTime        = end
    };
}
