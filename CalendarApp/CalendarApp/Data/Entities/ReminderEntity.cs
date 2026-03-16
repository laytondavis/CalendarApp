using CalendarApp.Models;
using SQLite;

namespace CalendarApp.Data.Entities;

/// <summary>
/// Database entity for event reminders.
/// </summary>
[Table("Reminders")]
public class ReminderEntity
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    public int EventId { get; set; }

    public int MethodValue { get; set; } = (int)ReminderMethod.Notification;

    [Ignore]
    public ReminderMethod Method
    {
        get => (ReminderMethod)MethodValue;
        set => MethodValue = (int)value;
    }

    public int MinutesBefore { get; set; }

    /// <summary>
    /// Platform-specific notification ID for cancellation.
    /// </summary>
    [MaxLength(200)]
    public string NotificationId { get; set; } = string.Empty;
}
