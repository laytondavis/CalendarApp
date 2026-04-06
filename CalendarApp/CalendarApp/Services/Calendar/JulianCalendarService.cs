using CalendarApp.Models;
using CalendarApp.Services.Interfaces;
using System.Globalization;

namespace CalendarApp.Services.Calendar;

/// <summary>
/// Calendar calculation service for the Julian calendar.
/// Days begin and end at midnight. Year begins on January 1.
/// Shows cross-reference to Gregorian dates.
/// </summary>
public class JulianCalendarService : ICalendarCalculationService
{
    private readonly JulianCalendar _julianCalendar = new();

    private static readonly string[] MonthNames =
    {
        "January", "February", "March", "April", "May", "June",
        "July", "August", "September", "October", "November", "December"
    };

    public CalendarMode Mode => CalendarMode.Julian;

    public CalendarDate GetDateForDateTime(DateTime gregorianDateTime, LocationInfo? location = null)
    {
        var julianYear = _julianCalendar.GetYear(gregorianDateTime);
        var julianMonth = _julianCalendar.GetMonth(gregorianDateTime);
        var julianDay = _julianCalendar.GetDayOfMonth(gregorianDateTime);

        var crossRef = $"Greg {gregorianDateTime:M/d/yy}";

        return new CalendarDate(
            julianYear,
            julianMonth,
            julianDay,
            CalendarMode.Julian,
            gregorianDateTime.Date,
            crossRef);
    }

    public DateTime GetGregorianDateTime(CalendarDate calendarDate, LocationInfo? location = null)
    {
        return _julianCalendar.ToDateTime(
            calendarDate.Year,
            calendarDate.Month,
            calendarDate.Day,
            0, 0, 0, 0);
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
        var gregorianDate = _julianCalendar.ToDateTime(year, month, 1, 0, 0, 0, 0);
        return GetDateForDateTime(gregorianDate);
    }

    public CalendarDate GetLastDayOfMonth(int year, int month)
    {
        var daysInMonth = _julianCalendar.GetDaysInMonth(year, month);
        var gregorianDate = _julianCalendar.ToDateTime(year, month, daysInMonth, 0, 0, 0, 0);
        return GetDateForDateTime(gregorianDate);
    }

    public CalendarDate GetFirstDayOfYear(int year)
    {
        var gregorianDate = _julianCalendar.ToDateTime(year, 1, 1, 0, 0, 0, 0);
        return GetDateForDateTime(gregorianDate);
    }

    public string GetCrossReferenceDisplay(CalendarDate date)
    {
        return $"G:{date.GregorianEquivalent.Day}";
    }

    public IEnumerable<CalendarDate> GetMonthGrid(int year, int month)
    {
        var firstDayGregorian = _julianCalendar.ToDateTime(year, month, 1, 0, 0, 0, 0);

        // Start from the Sunday of the week containing the first day
        var firstDayOfWeek = (int)firstDayGregorian.DayOfWeek;
        var startDate = firstDayGregorian.AddDays(-firstDayOfWeek);

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
        return _julianCalendar.GetDaysInMonth(year, month);
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
