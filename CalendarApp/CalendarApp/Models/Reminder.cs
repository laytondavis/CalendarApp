namespace CalendarApp.Models;

/// <summary>
/// Represents a reminder for a calendar event.
/// </summary>
public class Reminder
{
    public int Id { get; set; }
    public int EventId { get; set; }
    public ReminderMethod Method { get; set; } = ReminderMethod.Notification;
    public int MinutesBefore { get; set; }

    /// <summary>
    /// Platform-specific notification ID for cancellation.
    /// </summary>
    public string NotificationId { get; set; } = string.Empty;

    /// <summary>
    /// Gets a human-readable description of the reminder timing.
    /// </summary>
    public string TimingDescription
    {
        get
        {
            if (MinutesBefore == 0)
                return "At time of event";
            if (MinutesBefore < 60)
                return $"{MinutesBefore} minutes before";
            if (MinutesBefore < 1440)
            {
                var hours = MinutesBefore / 60;
                return hours == 1 ? "1 hour before" : $"{hours} hours before";
            }
            var days = MinutesBefore / 1440;
            return days == 1 ? "1 day before" : $"{days} days before";
        }
    }
}

/// <summary>
/// Reminder notification method.
/// </summary>
public enum ReminderMethod
{
    Notification,
    Email
}
