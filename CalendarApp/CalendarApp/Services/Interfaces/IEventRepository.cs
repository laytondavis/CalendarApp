using CalendarApp.Models;

namespace CalendarApp.Services.Interfaces;

/// <summary>
/// Repository for calendar event data access.
/// </summary>
public interface IEventRepository
{
    /// <summary>
    /// Gets an event by its local ID.
    /// </summary>
    Task<CalendarEvent?> GetByIdAsync(int id);

    /// <summary>
    /// Gets an event by its Google Calendar ID.
    /// </summary>
    Task<CalendarEvent?> GetByGoogleIdAsync(string googleEventId);

    /// <summary>
    /// Gets events within a date range, filtered by scope visibility.
    /// Always includes EventScope.All events. When primaryMode is provided also includes
    /// events scoped to that mode. additionalVisibleModes includes events scoped to those
    /// modes as well (used by the cross-calendar visibility toggles).
    /// </summary>
    /// <param name="start">Start of the date range (UTC).</param>
    /// <param name="end">End of the date range (UTC).</param>
    /// <param name="primaryMode">The active calendar mode (its scoped events are always included).</param>
    /// <param name="additionalVisibleModes">Extra modes whose scoped events should also be shown.</param>
    Task<IEnumerable<CalendarEvent>> GetEventsForDateRangeAsync(
        DateTime start,
        DateTime end,
        CalendarMode? primaryMode = null,
        IEnumerable<CalendarMode>? additionalVisibleModes = null);

    /// <summary>
    /// Gets events that need to be synced to Google Calendar.
    /// </summary>
    Task<IEnumerable<CalendarEvent>> GetPendingSyncEventsAsync();

    /// <summary>
    /// Gets events with sync conflicts.
    /// </summary>
    Task<IEnumerable<CalendarEvent>> GetConflictedEventsAsync();

    /// <summary>
    /// Inserts a new event.
    /// </summary>
    /// <returns>The ID of the inserted event.</returns>
    Task<int> InsertAsync(CalendarEvent calendarEvent);

    /// <summary>
    /// Updates an existing event.
    /// </summary>
    Task UpdateAsync(CalendarEvent calendarEvent);

    /// <summary>
    /// Deletes an event permanently.
    /// </summary>
    Task DeleteAsync(int id);

    /// <summary>
    /// Marks an event as deleted (soft delete for sync).
    /// </summary>
    Task MarkAsDeletedAsync(int id);

    /// <summary>
    /// Updates the sync status of an event.
    /// </summary>
    Task UpdateSyncStatusAsync(int id, SyncStatus status, string? etag = null);

    /// <summary>
    /// Gets reminders for an event.
    /// </summary>
    Task<IEnumerable<Reminder>> GetRemindersForEventAsync(int eventId);

    /// <summary>
    /// Gets events with upcoming reminders.
    /// </summary>
    Task<IEnumerable<CalendarEvent>> GetEventsWithUpcomingRemindersAsync(DateTime until);

    /// <summary>
    /// Permanently deletes all events that originated from Google Calendar (GoogleEventId != "").
    /// Used before a ForceFullPull to clear stale records.
    /// </summary>
    Task DeleteGoogleEventsAsync();
}
