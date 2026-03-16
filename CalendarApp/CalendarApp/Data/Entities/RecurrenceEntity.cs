using CalendarApp.Models;
using SQLite;

namespace CalendarApp.Data.Entities;

/// <summary>
/// Database entity for recurrence rules.
/// </summary>
[Table("RecurrenceRules")]
public class RecurrenceEntity
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    public int EventId { get; set; }

    /// <summary>
    /// RFC 5545 RRULE string.
    /// </summary>
    [MaxLength(1000)]
    public string RRule { get; set; } = string.Empty;

    public int FrequencyValue { get; set; }

    [Ignore]
    public RecurrenceFrequency Frequency
    {
        get => (RecurrenceFrequency)FrequencyValue;
        set => FrequencyValue = (int)value;
    }

    public int Interval { get; set; } = 1;

    public int? Count { get; set; }

    public DateTime? UntilDateUtc { get; set; }

    /// <summary>
    /// BYDAY values (e.g., "MO,WE,FR" or "1MO" for first Monday).
    /// </summary>
    [MaxLength(100)]
    public string ByDay { get; set; } = string.Empty;

    /// <summary>
    /// BYMONTH values (e.g., "1,6,12").
    /// </summary>
    [MaxLength(50)]
    public string ByMonth { get; set; } = string.Empty;

    /// <summary>
    /// BYMONTHDAY values (e.g., "1,15,-1").
    /// </summary>
    [MaxLength(100)]
    public string ByMonthDay { get; set; } = string.Empty;

    /// <summary>
    /// BYSETPOS values.
    /// </summary>
    [MaxLength(50)]
    public string BySetPos { get; set; } = string.Empty;

    /// <summary>
    /// EXDATE values - dates to exclude from recurrence (comma-separated ISO dates).
    /// </summary>
    public string ExceptionDates { get; set; } = string.Empty;
}
