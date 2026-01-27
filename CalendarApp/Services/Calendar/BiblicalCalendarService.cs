using CalendarApp.Models;
using CalendarApp.Services.Interfaces;

namespace CalendarApp.Services.Calendar;

/// <summary>
/// Calendar calculation service for the Biblical calendar.
/// Days begin at sunset (calculated from GPS or manually selected location).
/// Month begins on the first day following lunar conjunction (new moon).
/// Year begins with the first month that starts after the spring equinox.
/// Shows cross-reference to Gregorian calendar (dates at noon during daylight hours).
/// </summary>
public class BiblicalCalendarService : ICalendarCalculationService
{
    private readonly IAstronomicalService _astronomicalService;
    private readonly ILocationService _locationService;

    private static readonly string[] HebrewMonthNames =
    {
        "Nisan", "Iyyar", "Sivan", "Tammuz", "Av", "Elul",
        "Tishrei", "Cheshvan", "Kislev", "Tevet", "Shevat", "Adar", "Adar II"
    };

    public BiblicalCalendarService(
        IAstronomicalService astronomicalService,
        ILocationService locationService)
    {
        _astronomicalService = astronomicalService;
        _locationService = locationService;
    }

    public CalendarMode Mode => CalendarMode.Biblical;

    public CalendarDate GetDateForDateTime(DateTime gregorianDateTime, LocationInfo? location = null)
    {
        location ??= GetDefaultLocation();

        // Determine if we're before or after sunset
        var sunset = _astronomicalService.CalculateSunset(gregorianDateTime, location);
        var isAfterSunset = gregorianDateTime.TimeOfDay >= sunset.TimeOfDay;

        // Biblical day starts at sunset, so after sunset = next biblical day
        var biblicalGregorianDate = isAfterSunset
            ? gregorianDateTime.Date.AddDays(1)
            : gregorianDateTime.Date;

        // Calculate the biblical year, month, and day
        var (biblicalYear, biblicalMonth, biblicalDay) =
            CalculateBiblicalDate(biblicalGregorianDate, location);

        // Cross-reference shows Gregorian date at noon
        var crossRef = $"Gregorian: {gregorianDateTime:MMM d, yyyy} (at noon)";

        return new CalendarDate(
            biblicalYear,
            biblicalMonth,
            biblicalDay,
            CalendarMode.Biblical,
            gregorianDateTime.Date,
            crossRef);
    }

    public DateTime GetGregorianDateTime(CalendarDate calendarDate, LocationInfo? location = null)
    {
        location ??= GetDefaultLocation();

        // Find the Gregorian date that corresponds to this Biblical date
        // Start from the spring equinox of the year
        var equinox = _astronomicalService.CalculateVernalEquinox(calendarDate.Year);

        // Find the first new moon after the equinox (start of biblical year)
        var firstNewMoon = _astronomicalService.CalculateNextNewMoon(equinox);
        var firstDayOfYear = GetFirstDayAfterConjunction(firstNewMoon, location);

        // Add months
        var currentDate = firstDayOfYear;
        for (int month = 1; month < calendarDate.Month; month++)
        {
            var nextNewMoon = _astronomicalService.CalculateNextNewMoon(currentDate.AddDays(1));
            currentDate = GetFirstDayAfterConjunction(nextNewMoon, location);
        }

        // Add days
        return currentDate.AddDays(calendarDate.Day - 1);
    }

    public DateTime GetDayStart(DateTime gregorianDate, LocationInfo? location = null)
    {
        location ??= GetDefaultLocation();

        // Previous day's sunset is when this biblical day started
        var previousDay = gregorianDate.AddDays(-1);
        return _astronomicalService.CalculateSunset(previousDay, location);
    }

    public DateTime GetDayEnd(DateTime gregorianDate, LocationInfo? location = null)
    {
        location ??= GetDefaultLocation();
        return _astronomicalService.CalculateSunset(gregorianDate, location);
    }

    public CalendarDate GetFirstDayOfMonth(int year, int month)
    {
        // Find the spring equinox for the year
        var equinox = _astronomicalService.CalculateVernalEquinox(year);
        var location = GetDefaultLocation();

        // Find the first new moon after the equinox
        var firstNewMoon = _astronomicalService.CalculateNextNewMoon(equinox);
        var currentMonthStart = GetFirstDayAfterConjunction(firstNewMoon, location);

        // Advance to the requested month
        for (int m = 1; m < month; m++)
        {
            var nextNewMoon = _astronomicalService.CalculateNextNewMoon(currentMonthStart.AddDays(1));
            currentMonthStart = GetFirstDayAfterConjunction(nextNewMoon, location);
        }

        return GetDateForDateTime(currentMonthStart, location);
    }

    public CalendarDate GetLastDayOfMonth(int year, int month)
    {
        var firstDayNextMonth = GetFirstDayOfMonth(year, month == GetMonthsInYear(year) ? 1 : month + 1);
        var lastDay = GetGregorianDateTime(firstDayNextMonth).AddDays(-1);
        return GetDateForDateTime(lastDay);
    }

    public CalendarDate GetFirstDayOfYear(int year)
    {
        return GetFirstDayOfMonth(year, 1);
    }

    public string GetCrossReferenceDisplay(CalendarDate date)
    {
        // Show Gregorian date at noon (middle of daylight hours)
        return $"Gregorian: {date.GregorianEquivalent:MMMM d, yyyy} (at noon)";
    }

    public IEnumerable<CalendarDate> GetMonthGrid(int year, int month)
    {
        var location = GetDefaultLocation();
        var firstDay = GetFirstDayOfMonth(year, month);
        var firstDayGregorian = GetGregorianDateTime(firstDay, location);

        // Start from the Sunday of the week containing the first day
        var firstDayOfWeek = (int)firstDayGregorian.DayOfWeek;
        var startDate = firstDayGregorian.AddDays(-firstDayOfWeek);

        // Generate 6 weeks (42 days) to ensure full grid
        for (int i = 0; i < 42; i++)
        {
            var date = startDate.AddDays(i);
            yield return GetDateForDateTime(date, location);
        }
    }

    public IEnumerable<CalendarDate> GetWeekDays(CalendarDate anyDayInWeek)
    {
        var location = GetDefaultLocation();
        var gregorianDate = GetGregorianDateTime(anyDayInWeek, location);
        var dayOfWeek = (int)gregorianDate.DayOfWeek;
        var sunday = gregorianDate.AddDays(-dayOfWeek);

        for (int i = 0; i < 7; i++)
        {
            yield return GetDateForDateTime(sunday.AddDays(i), location);
        }
    }

    public int GetDaysInMonth(int year, int month)
    {
        var firstDay = GetFirstDayOfMonth(year, month);
        var firstDayGregorian = GetGregorianDateTime(firstDay);

        // Find the next new moon to determine month length
        var nextNewMoon = _astronomicalService.CalculateNextNewMoon(firstDayGregorian.AddDays(1));
        var location = GetDefaultLocation();
        var nextMonthStart = GetFirstDayAfterConjunction(nextNewMoon, location);

        return (int)(nextMonthStart - firstDayGregorian).TotalDays;
    }

    public int GetMonthsInYear(int year)
    {
        // Biblical calendar can have 12 or 13 months (leap year with Adar II)
        // Determine by checking if there's a 13th month before the next year's equinox
        var thisYearEquinox = _astronomicalService.CalculateVernalEquinox(year);
        var nextYearEquinox = _astronomicalService.CalculateVernalEquinox(year + 1);
        var location = GetDefaultLocation();

        var firstNewMoon = _astronomicalService.CalculateNextNewMoon(thisYearEquinox);
        var currentDate = GetFirstDayAfterConjunction(firstNewMoon, location);
        var monthCount = 1;

        while (true)
        {
            var nextNewMoon = _astronomicalService.CalculateNextNewMoon(currentDate.AddDays(1));
            var nextMonthStart = GetFirstDayAfterConjunction(nextNewMoon, location);

            if (nextMonthStart >= nextYearEquinox)
            {
                // This new moon is after the next equinox, so it starts the new year
                break;
            }

            monthCount++;
            currentDate = nextMonthStart;

            if (monthCount > 13) // Safety check
                break;
        }

        return monthCount;
    }

    public string GetMonthName(int month)
    {
        if (month < 1 || month > 13)
            throw new ArgumentOutOfRangeException(nameof(month));
        return HebrewMonthNames[month - 1];
    }

    private (int Year, int Month, int Day) CalculateBiblicalDate(DateTime gregorianDate, LocationInfo location)
    {
        // Find the relevant spring equinox (this year or previous year)
        var thisYearEquinox = _astronomicalService.CalculateVernalEquinox(gregorianDate.Year);
        var prevYearEquinox = _astronomicalService.CalculateVernalEquinox(gregorianDate.Year - 1);

        // Find the first new moon after each equinox to determine year start
        var thisYearFirstNewMoon = _astronomicalService.CalculateNextNewMoon(thisYearEquinox);
        var thisYearStart = GetFirstDayAfterConjunction(thisYearFirstNewMoon, location);

        int biblicalYear;
        DateTime yearStart;

        if (gregorianDate >= thisYearStart)
        {
            biblicalYear = gregorianDate.Year;
            yearStart = thisYearStart;
        }
        else
        {
            biblicalYear = gregorianDate.Year - 1;
            var prevYearFirstNewMoon = _astronomicalService.CalculateNextNewMoon(prevYearEquinox);
            yearStart = GetFirstDayAfterConjunction(prevYearFirstNewMoon, location);
        }

        // Calculate month and day
        var (month, day) = CalculateMonthAndDay(gregorianDate, yearStart, location);

        return (biblicalYear, month, day);
    }

    private (int Month, int Day) CalculateMonthAndDay(DateTime gregorianDate, DateTime yearStart, LocationInfo location)
    {
        var month = 1;
        var currentMonthStart = yearStart;

        while (true)
        {
            var nextNewMoon = _astronomicalService.CalculateNextNewMoon(currentMonthStart.AddDays(1));
            var nextMonthStart = GetFirstDayAfterConjunction(nextNewMoon, location);

            if (gregorianDate < nextMonthStart)
            {
                // gregorianDate is in this month
                var day = (int)(gregorianDate - currentMonthStart).TotalDays + 1;
                return (month, day);
            }

            month++;
            currentMonthStart = nextMonthStart;

            if (month > 13) // Safety check
            {
                return (13, 1);
            }
        }
    }

    private DateTime GetFirstDayAfterConjunction(DateTime conjunction, LocationInfo location)
    {
        // The first day after sunset following the conjunction
        var sunset = _astronomicalService.CalculateSunset(conjunction, location);

        if (conjunction.TimeOfDay < sunset.TimeOfDay)
        {
            // Conjunction before sunset = day starts this evening
            return conjunction.Date;
        }
        else
        {
            // Conjunction after sunset = day starts next evening
            return conjunction.Date.AddDays(1);
        }
    }

    private LocationInfo GetDefaultLocation()
    {
        // Try to get the default location synchronously
        var task = _locationService.GetDefaultLocationAsync();
        task.Wait();
        return task.Result ?? LocationInfo.FromCoordinates(31.7683, 35.2137); // Jerusalem as fallback
    }
}
