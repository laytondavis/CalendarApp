namespace CalendarApp.Models;

/// <summary>
/// Represents a recurrence rule for calendar events (RFC 5545 RRULE).
/// </summary>
public class RecurrenceRule
{
    public int Id { get; set; }
    public int EventId { get; set; }

    /// <summary>
    /// The RFC 5545 RRULE string.
    /// </summary>
    public string RRule { get; set; } = string.Empty;

    public RecurrenceFrequency Frequency { get; set; }
    public int Interval { get; set; } = 1;
    public int? Count { get; set; }
    public DateTime? UntilDateUtc { get; set; }

    /// <summary>
    /// BYDAY values (e.g., "MO,WE,FR" or "1MO" for first Monday).
    /// </summary>
    public string ByDay { get; set; } = string.Empty;

    /// <summary>
    /// BYMONTH values (e.g., "1,6,12").
    /// </summary>
    public string ByMonth { get; set; } = string.Empty;

    /// <summary>
    /// BYMONTHDAY values (e.g., "1,15,-1" for 1st, 15th, and last day).
    /// </summary>
    public string ByMonthDay { get; set; } = string.Empty;

    /// <summary>
    /// BYSETPOS values for selecting specific occurrences.
    /// </summary>
    public string BySetPos { get; set; } = string.Empty;

    /// <summary>
    /// EXDATE values - dates to exclude from recurrence.
    /// </summary>
    public string ExceptionDates { get; set; } = string.Empty;

    /// <summary>
    /// Generates the RRULE string from the properties.
    /// </summary>
    public string ToRRuleString()
    {
        var parts = new List<string>
        {
            $"FREQ={Frequency.ToString().ToUpperInvariant()}"
        };

        if (Interval > 1)
            parts.Add($"INTERVAL={Interval}");

        if (Count.HasValue)
            parts.Add($"COUNT={Count.Value}");

        if (UntilDateUtc.HasValue)
            parts.Add($"UNTIL={UntilDateUtc.Value:yyyyMMddTHHmmssZ}");

        if (!string.IsNullOrEmpty(ByDay))
            parts.Add($"BYDAY={ByDay}");

        if (!string.IsNullOrEmpty(ByMonth))
            parts.Add($"BYMONTH={ByMonth}");

        if (!string.IsNullOrEmpty(ByMonthDay))
            parts.Add($"BYMONTHDAY={ByMonthDay}");

        if (!string.IsNullOrEmpty(BySetPos))
            parts.Add($"BYSETPOS={BySetPos}");

        return $"RRULE:{string.Join(";", parts)}";
    }
}

/// <summary>
/// Recurrence frequency types.
/// </summary>
public enum RecurrenceFrequency
{
    Daily,
    Weekly,
    Monthly,
    Yearly
}
