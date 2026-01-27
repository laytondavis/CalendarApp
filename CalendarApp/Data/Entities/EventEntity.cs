using CalendarApp.Models;
using SQLite;

namespace CalendarApp.Data.Entities;

/// <summary>
/// Database entity for calendar events.
/// </summary>
[Table("Events")]
public class EventEntity
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    [MaxLength(500)]
    public string GoogleEventId { get; set; } = string.Empty;

    [MaxLength(500)]
    public string Title { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    [Indexed]
    public DateTime StartDateTimeUtc { get; set; }

    public DateTime EndDateTimeUtc { get; set; }

    public bool IsAllDay { get; set; }

    /// <summary>
    /// For Biblical calendar: store the sunset-based start time.
    /// </summary>
    public DateTime? BiblicalStartDateTimeUtc { get; set; }

    [MaxLength(500)]
    public string Location { get; set; } = string.Empty;

    /// <summary>
    /// Reference to recurrence rule, if this is a recurring event.
    /// </summary>
    public int? RecurrenceId { get; set; }

    [Indexed]
    [MaxLength(100)]
    public string CalendarId { get; set; } = "primary";

    [MaxLength(10)]
    public string ColorHex { get; set; } = "#1a73e8";

    /// <summary>
    /// The calendar mode this event was created in.
    /// </summary>
    public int CalendarModeValue { get; set; } = (int)CalendarMode.Gregorian;

    [Ignore]
    public CalendarMode CalendarMode
    {
        get => (CalendarMode)CalendarModeValue;
        set => CalendarModeValue = (int)value;
    }

    // Sync metadata
    public DateTime LastModifiedUtc { get; set; }

    public int SyncStatusValue { get; set; } = (int)SyncStatus.Synced;

    [Ignore]
    public SyncStatus SyncStatus
    {
        get => (SyncStatus)SyncStatusValue;
        set => SyncStatusValue = (int)value;
    }

    [MaxLength(500)]
    public string ETag { get; set; } = string.Empty;

    public bool IsDeleted { get; set; }
}
