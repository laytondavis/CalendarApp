using CalendarApp.Models;

namespace CalendarApp.Services.Interfaces;

/// <summary>
/// Service for two-way syncing events between local storage and Google Calendar.
/// </summary>
public interface ISyncService
{
    /// <summary>
    /// Gets whether a sync is currently in progress.
    /// </summary>
    bool IsSyncing { get; }

    /// <summary>
    /// Gets the last sync time.
    /// </summary>
    DateTime? LastSyncTimeUtc { get; }

    /// <summary>
    /// Performs a full two-way sync with Google Calendar.
    /// </summary>
    Task<SyncResult> SyncAsync(string calendarId = "primary");

    /// <summary>
    /// Uploads any pending local changes to Google Calendar.
    /// </summary>
    Task<SyncResult> PushLocalChangesAsync(string calendarId = "primary");

    /// <summary>
    /// Downloads changes from Google Calendar.
    /// </summary>
    Task<SyncResult> PullRemoteChangesAsync(string calendarId = "primary");

    /// <summary>
    /// Clears the stored sync token and re-downloads all events from Google Calendar.
    /// Use this when events appear to be missing or out of date.
    /// </summary>
    Task<SyncResult> ForceFullPullAsync(string calendarId = "primary");

    /// <summary>
    /// Resolves a sync conflict for a specific event.
    /// </summary>
    Task ResolveConflictAsync(int localEventId, ConflictResolution resolution);

    /// <summary>
    /// Fetches the user's Google calendar list and saves it to the local database.
    /// Preserves any existing IsEnabled choices; newly discovered calendars default
    /// to enabled=true for the primary calendar and enabled=false for all others.
    /// </summary>
    Task RefreshCalendarListAsync();

    /// <summary>
    /// Returns the cached list of Google calendars from the local database (no network call).
    /// </summary>
    Task<IEnumerable<GoogleCalendarInfo>> GetCachedCalendarListAsync();

    /// <summary>
    /// Event raised when sync status changes.
    /// </summary>
    event EventHandler<SyncStatusChangedEventArgs>? SyncStatusChanged;
}

/// <summary>
/// Result of a sync operation.
/// </summary>
public class SyncResult
{
    public bool Success { get; set; }
    public int EventsUploaded { get; set; }
    public int EventsDownloaded { get; set; }
    public int EventsDeleted { get; set; }
    public int Conflicts { get; set; }
    public List<string> Errors { get; set; } = new();

    public string Summary =>
        Success
            ? $"Synced: {EventsUploaded} up, {EventsDownloaded} down, {EventsDeleted} deleted"
            : $"Sync failed: {string.Join("; ", Errors)}";
}

/// <summary>
/// How to resolve a sync conflict.
/// </summary>
public enum ConflictResolution
{
    KeepLocal,
    KeepRemote,
    KeepNewest
}

/// <summary>
/// Event args for sync status changes.
/// </summary>
public class SyncStatusChangedEventArgs : EventArgs
{
    public SyncStatusChangedEventArgs(string message, bool isError = false)
    {
        Message = message;
        IsError = isError;
    }

    public string Message { get; }
    public bool IsError { get; }
}
