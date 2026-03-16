using CalendarApp.Models;

namespace CalendarApp.Services.Interfaces;

/// <summary>
/// Service for calculating dates in different calendar systems.
/// </summary>
public interface ICalendarCalculationService
{
    /// <summary>
    /// Gets the calendar mode this service handles.
    /// </summary>
    CalendarMode Mode { get; }

    /// <summary>
    /// Converts a Gregorian DateTime to a CalendarDate in this calendar system.
    /// </summary>
    /// <param name="gregorianDateTime">The Gregorian date/time to convert.</param>
    /// <param name="location">Optional location for sunset calculations (Biblical calendar).</param>
    /// <returns>The date in this calendar system.</returns>
    CalendarDate GetDateForDateTime(DateTime gregorianDateTime, LocationInfo? location = null);

    /// <summary>
    /// Converts a CalendarDate back to a Gregorian DateTime.
    /// </summary>
    /// <param name="calendarDate">The calendar date to convert.</param>
    /// <param name="location">Optional location for sunset calculations (Biblical calendar).</param>
    /// <returns>The Gregorian DateTime.</returns>
    DateTime GetGregorianDateTime(CalendarDate calendarDate, LocationInfo? location = null);

    /// <summary>
    /// Gets the start of a day in this calendar system.
    /// </summary>
    /// <param name="gregorianDate">The Gregorian date.</param>
    /// <param name="location">Optional location for sunset calculations (Biblical calendar).</param>
    /// <returns>The DateTime when the day starts.</returns>
    DateTime GetDayStart(DateTime gregorianDate, LocationInfo? location = null);

    /// <summary>
    /// Gets the end of a day in this calendar system.
    /// </summary>
    /// <param name="gregorianDate">The Gregorian date.</param>
    /// <param name="location">Optional location for sunset calculations (Biblical calendar).</param>
    /// <returns>The DateTime when the day ends.</returns>
    DateTime GetDayEnd(DateTime gregorianDate, LocationInfo? location = null);

    /// <summary>
    /// Gets the first day of a month.
    /// </summary>
    CalendarDate GetFirstDayOfMonth(int year, int month);

    /// <summary>
    /// Gets the last day of a month.
    /// </summary>
    CalendarDate GetLastDayOfMonth(int year, int month);

    /// <summary>
    /// Gets the first day of a year.
    /// </summary>
    CalendarDate GetFirstDayOfYear(int year);

    /// <summary>
    /// Gets the cross-reference display string for a date.
    /// </summary>
    string GetCrossReferenceDisplay(CalendarDate date);

    /// <summary>
    /// Generates the grid of dates for a month view.
    /// </summary>
    /// <param name="year">The year in this calendar system.</param>
    /// <param name="month">The month in this calendar system.</param>
    /// <returns>A 6-week grid of dates (42 days).</returns>
    IEnumerable<CalendarDate> GetMonthGrid(int year, int month);

    /// <summary>
    /// Gets the days of a week containing the specified date.
    /// </summary>
    IEnumerable<CalendarDate> GetWeekDays(CalendarDate anyDayInWeek);

    /// <summary>
    /// Gets the number of days in a month.
    /// </summary>
    int GetDaysInMonth(int year, int month);

    /// <summary>
    /// Gets the number of months in a year.
    /// </summary>
    int GetMonthsInYear(int year);

    /// <summary>
    /// Gets the name of a month.
    /// </summary>
    string GetMonthName(int month);
}
