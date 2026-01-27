namespace CalendarApp.Models;

/// <summary>
/// Represents a calendar event.
/// </summary>
public class CalendarEvent
{
    public int Id { get; set; }
    public string GoogleEventId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTime StartDateTime { get; set; }
    public DateTime EndDateTime { get; set; }
    public bool IsAllDay { get; set; }
    public DateTime? BiblicalStartDateTime { get; set; }
    public string Location { get; set; } = string.Empty;
    public string CalendarId { get; set; } = "primary";
    public string ColorHex { get; set; } = "#1a73e8";
    public SyncStatus SyncStatus { get; set; } = SyncStatus.Synced;
    public string ETag { get; set; } = string.Empty;
    public DateTime LastModifiedUtc { get; set; }
    public bool IsDeleted { get; set; }

    /// <summary>
    /// The calendar mode this event was created in.
    /// </summary>
    public CalendarMode CalendarMode { get; set; } = CalendarMode.Gregorian;

    /// <summary>
    /// Recurrence rule for this event, if any.
    /// </summary>
    public RecurrenceRule? RecurrenceRule { get; set; }

    /// <summary>
    /// Reminders associated with this event.
    /// </summary>
    public List<Reminder> Reminders { get; set; } = new();

    /// <summary>
    /// Gets the duration of the event.
    /// </summary>
    public TimeSpan Duration => EndDateTime - StartDateTime;
}

/// <summary>
/// Represents the sync status of an event.
/// </summary>
public enum SyncStatus
{
    Synced,
    PendingUpload,
    PendingDownload,
    Conflict,
    Error
}
