using SQLite;

namespace CalendarApp.Data.Entities;

/// <summary>
/// Persists the user's Google Calendar list so individual calendars can be
/// enabled or disabled for sync without hitting the API every time.
/// </summary>
[Table("GoogleCalendars")]
public class GoogleCalendarListEntity
{
    /// <summary>Google-assigned calendar ID, e.g. "primary" or "en.usa#holiday@group.v.calendar.google.com".</summary>
    [PrimaryKey]
    [MaxLength(500)]
    public string CalendarId { get; set; } = string.Empty;

    /// <summary>Display name shown in the Google Calendar UI.</summary>
    [MaxLength(200)]
    public string Summary { get; set; } = string.Empty;

    /// <summary>Hex color string as returned by the Google Calendar API, e.g. "#0B8043".</summary>
    [MaxLength(10)]
    public string ColorHex { get; set; } = string.Empty;

    /// <summary>True for the user's default "primary" calendar.</summary>
    public bool IsPrimary { get; set; }

    /// <summary>Access role reported by Google: "owner", "writer", "reader", or "freeBusyReader".</summary>
    [MaxLength(30)]
    public string AccessRole { get; set; } = string.Empty;

    /// <summary>
    /// Whether to include this calendar in sync.
    /// Defaults to true for the primary calendar, false for all others.
    /// </summary>
    public bool IsEnabled { get; set; }

    /// <summary>
    /// User-selected override color hex string (e.g. "#FF5733").
    /// Empty means "use the Google-provided ColorHex".
    /// </summary>
    [MaxLength(10)]
    public string UserColorHex { get; set; } = string.Empty;
}
