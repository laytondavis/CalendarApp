using CalendarApp.Models;
using Google.Apis.Calendar.v3;

namespace CalendarApp.Services.Interfaces;

/// <summary>
/// Service for interacting with the Google Calendar API.
/// </summary>
public interface IGoogleCalendarService
{
    /// <summary>
    /// Gets a list of the user's calendars.
    /// </summary>
    Task<IEnumerable<GoogleCalendarInfo>> GetCalendarListAsync();

    /// <summary>
    /// Gets events from a calendar using the primary (writable) account,
    /// optionally using incremental sync.
    /// </summary>
    Task<GoogleSyncResult> GetEventsAsync(
        string calendarId = "primary",
        string? syncToken = null,
        DateTime? timeMin = null,
        DateTime? timeMax = null);

    /// <summary>
    /// Gets events from a calendar using an explicit CalendarService instance.
    /// Used by SyncService to pull events from read-only secondary accounts.
    /// </summary>
    Task<GoogleSyncResult> GetEventsFromServiceAsync(
        CalendarService calendarService,
        string calendarId = "primary",
        string? syncToken = null,
        DateTime? timeMin = null,
        DateTime? timeMax = null);

    /// <summary>
    /// Creates an event on Google Calendar.
    /// </summary>
    Task<GoogleEventResult> CreateEventAsync(CalendarEvent localEvent, string calendarId = "primary");

    /// <summary>
    /// Updates an event on Google Calendar.
    /// </summary>
    Task<GoogleEventResult> UpdateEventAsync(CalendarEvent localEvent, string calendarId = "primary");

    /// <summary>
    /// Deletes an event from Google Calendar.
    /// </summary>
    Task<bool> DeleteEventAsync(string googleEventId, string calendarId = "primary");
}

/// <summary>
/// Information about a Google Calendar.
/// </summary>
public class GoogleCalendarInfo
{
    public string Id { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string ColorHex { get; set; } = string.Empty;
    public bool IsPrimary { get; set; }
    public string AccessRole { get; set; } = string.Empty;
}

/// <summary>
/// Result of a Google Calendar sync operation.
/// </summary>
public class GoogleSyncResult
{
    public List<CalendarEvent> Events { get; set; } = new();
    public List<string> DeletedEventIds { get; set; } = new();
    public string? NextSyncToken { get; set; }
    public bool IsFullSync { get; set; }
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Result of creating/updating an event on Google.
/// </summary>
public class GoogleEventResult
{
    public bool Success { get; set; }
    public string? GoogleEventId { get; set; }
    public string? ETag { get; set; }
    public string? ErrorMessage { get; set; }
}
