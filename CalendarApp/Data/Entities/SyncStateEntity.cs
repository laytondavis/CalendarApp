using CalendarApp.Models;
using SQLite;

namespace CalendarApp.Data.Entities;

/// <summary>
/// Database entity for tracking sync state with Google Calendar.
/// </summary>
[Table("SyncState")]
public class SyncStateEntity
{
    [PrimaryKey]
    [MaxLength(100)]
    public string CalendarId { get; set; } = string.Empty;

    /// <summary>
    /// Google Calendar sync token for incremental sync.
    /// </summary>
    [MaxLength(500)]
    public string SyncToken { get; set; } = string.Empty;

    public DateTime LastSyncUtc { get; set; }

    public int StatusValue { get; set; } = (int)SyncStatus.Synced;

    [Ignore]
    public SyncStatus Status
    {
        get => (SyncStatus)StatusValue;
        set => StatusValue = (int)value;
    }

    public string LastError { get; set; } = string.Empty;
}
