using CalendarApp.Models;
using CalendarApp.Services.Interfaces;
using System.Globalization;

namespace CalendarApp.Services.Calendar;

/// <summary>
/// Calendar calculation service for the standard Gregorian calendar.
/// Days begin and end at midnight. Year begins on January 1.
/// </summary>
public class GregorianCalendarService : ICalendarCalculationService
{
    private static readonly string[] MonthNames =
    {
        "January", "February", "March", "April", "May", "June",
        "July", "August", "September", "October", "November", "December"
    };

    public CalendarMode Mode => CalendarMode.Gregorian;

    public CalendarDate GetDateForDateTime(DateTime gregorianDateTime, LocationInfo? location = null)
    {
        return new CalendarDate(
            gregorianDateTime.Year,
            gregorianDateTime.Month,
            gregorianDateTime.Day,
            CalendarMode.Gregorian,
            gregorianDateTime.Date);
    }

    public DateTime GetGregorianDateTime(CalendarDate calendarDate, LocationInfo? location = null)
    {
        return new DateTime(calendarDate.Year, calendarDate.Month, calendarDate.Day);
    }

    public DateTime GetDayStart(DateTime gregorianDate, LocationInfo? location = null)
    {
        return gregorianDate.Date; // Midnight
    }

    public DateTime GetDayEnd(DateTime gregorianDate, LocationInfo? location = null)
    {
        return gregorianDate.Date.AddDays(1).AddTicks(-1);
    }

    public CalendarDate GetFirstDayOfMonth(int year, int month)
    {
        var date = new DateTime(year, month, 1);
        return GetDateForDateTime(date);
    }

    public CalendarDate GetLastDayOfMonth(int year, int month)
    {
        var daysInMonth = DateTime.DaysInMonth(year, month);
        var date = new DateTime(year, month, daysInMonth);
        return GetDateForDateTime(date);
    }

    public CalendarDate GetFirstDayOfYear(int year)
    {
        return GetDateForDateTime(new DateTime(year, 1, 1));
    }

    public string GetCrossReferenceDisplay(CalendarDate date)
    {
        // Gregorian calendar doesn't need cross-reference to itself
        return string.Empty;
    }

    public IEnumerable<CalendarDate> GetMonthGrid(int year, int month)
    {
        var firstDay = new DateTime(year, month, 1);

        // Start from the Sunday of the week containing the first day
        var firstDayOfWeek = (int)firstDay.DayOfWeek;
        var startDate = firstDay.AddDays(-firstDayOfWeek);

        // Generate 6 weeks (42 days) to ensure full grid
        for (int i = 0; i < 42; i++)
        {
            var date = startDate.AddDays(i);
            yield return GetDateForDateTime(date);
        }
    }

    public IEnumerable<CalendarDate> GetWeekDays(CalendarDate anyDayInWeek)
    {
        var gregorianDate = GetGregorianDateTime(anyDayInWeek);
        var dayOfWeek = (int)gregorianDate.DayOfWeek;
        var sunday = gregorianDate.AddDays(-dayOfWeek);

        for (int i = 0; i < 7; i++)
        {
            yield return GetDateForDateTime(sunday.AddDays(i));
        }
    }

    public int GetDaysInMonth(int year, int month)
    {
        return DateTime.DaysInMonth(year, month);
    }

    public int GetMonthsInYear(int year)
    {
        return 12;
    }

    public string GetMonthName(int month)
    {
        if (month < 1 || month > 12)
            throw new ArgumentOutOfRangeException(nameof(month));
        return MonthNames[month - 1];
    }
}
