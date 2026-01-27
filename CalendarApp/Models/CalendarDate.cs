namespace CalendarApp.Models;

/// <summary>
/// Represents a date in any of the supported calendar systems.
/// </summary>
public record CalendarDate(
    int Year,
    int Month,
    int Day,
    CalendarMode Mode,
    DateTime GregorianEquivalent,
    string? CrossReference = null)
{
    /// <summary>
    /// Gets whether this date has a cross-reference to another calendar system.
    /// </summary>
    public bool HasCrossReference => !string.IsNullOrEmpty(CrossReference);

    /// <summary>
    /// Gets the day of week for this date based on the Gregorian equivalent.
    /// </summary>
    public DayOfWeek DayOfWeek => GregorianEquivalent.DayOfWeek;

    /// <summary>
    /// Creates a Gregorian calendar date from a DateTime.
    /// </summary>
    public static CalendarDate FromGregorian(DateTime date) =>
        new(date.Year, date.Month, date.Day, CalendarMode.Gregorian, date.Date);
}
