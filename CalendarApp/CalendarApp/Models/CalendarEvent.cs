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
    /// Controls which calendar types display this event.
    /// Defaults to All so existing events remain visible everywhere.
    /// </summary>
    public EventScope EventScope { get; set; } = EventScope.All;

    /// <summary>
    /// The alias of the Google account this event was synced from.
    /// Empty string for the primary (writable) account.
    /// </summary>
    public string GoogleAccountAlias { get; set; } = string.Empty;

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

/// <summary>
/// Controls which calendar type(s) display a local event.
/// All = default, visible on every calendar type.
/// </summary>
public enum EventScope
{
    All = 0,
    Gregorian = 1,
    Julian = 2,
    Biblical = 3
}
