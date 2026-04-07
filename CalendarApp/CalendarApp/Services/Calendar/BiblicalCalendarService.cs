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
    private LocationInfo? _cachedLocation;

    private static readonly string[] HebrewMonthNames =
    {
        "Nisan (Abib)", "Iyyar", "Sivan", "Tammuz", "Av", "Elul",
        "Tishrei", "Cheshvan", "Kislev", "Tevet", "Shevat", "Adar", "Adar II"
    };

    // Jerusalem as default fallback location — use Israel's real timezone, not the computer's local zone.
    private static readonly LocationInfo JerusalemFallback =
        new(31.7683, 35.2137, null, GetJerusalemTimeZoneId());

    private static string GetJerusalemTimeZoneId()
    {
        // Try Windows ID first, then IANA. Both are accepted on .NET 9 on all platforms.
        foreach (var id in new[] { "Israel Standard Time", "Asia/Jerusalem" })
            if (TimeZoneInfo.TryFindSystemTimeZoneById(id, out _))
                return id;
        return TimeZoneInfo.Local.Id; // last-resort fallback
    }

    public BiblicalCalendarService(
        IAstronomicalService astronomicalService,
        ILocationService locationService)
    {
        _astronomicalService = astronomicalService;
        _locationService = locationService;
    }

    /// <summary>
    /// Fetches the effective location and caches it.
    /// Uses GetCurrentLocationAsync so that when LocationMode is GpsWithManualFallback
    /// and a GPS fix is available, the GPS coordinates are used for all calculations
    /// rather than the manually-pinned location.
    /// Safe to call repeatedly — re-fetches each time so changes made in Settings
    /// are always picked up on the next calendar refresh.
    /// </summary>
    public async Task InitializeLocationAsync()
    {
        try
        {
            _cachedLocation = await _locationService.GetCurrentLocationAsync();
        }
        catch
        {
            // Ignore — will use Jerusalem fallback
        }
    }

    public CalendarMode Mode => CalendarMode.Biblical;

    public CalendarDate GetDateForDateTime(DateTime gregorianDateTime, LocationInfo? location = null)
    {
        location ??= GetDefaultLocation();

        // Determine if we're before or after sunset.
        // Use the DATE component only so the sunset time is always computed for midnight
        // of the given date, keeping it consistent regardless of the time-of-day passed in.
        var sunset = _astronomicalService.CalculateSunset(gregorianDateTime.Date, location);
        var isAfterSunset = gregorianDateTime.TimeOfDay >= sunset.TimeOfDay;

        // Biblical day starts at sunset, so after sunset = next biblical day
        var biblicalGregorianDate = isAfterSunset
            ? gregorianDateTime.Date.AddDays(1)
            : gregorianDateTime.Date;

        // Calculate the biblical year, month, and day
        var (biblicalYear, biblicalMonth, biblicalDay) =
            CalculateBiblicalDate(biblicalGregorianDate, location);

        var crossRef = gregorianDateTime.ToString("M/d/yyyy");

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
        return date.GregorianEquivalent.ToString("M/d/yyyy");
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
        // Biblical calendar can have 12 or 13 months (leap year with Adar II).
        // Count how many new-moon months fit between this year's equinox and the next.
        var thisYearEquinox = _astronomicalService.CalculateVernalEquinox(year);   // UTC
        var nextYearEquinox = _astronomicalService.CalculateVernalEquinox(year + 1); // UTC
        var location = GetDefaultLocation();

        // Convert next-year equinox to local time so we can compare with local month-start dates
        var nextYearEquinoxLocal = TimeZoneInfo.ConvertTimeFromUtc(nextYearEquinox, location.TimeZone);

        var firstNewMoon = _astronomicalService.CalculateNextNewMoon(thisYearEquinox);
        var currentDate = GetFirstDayAfterConjunction(firstNewMoon, location); // local midnight
        var monthCount = 1;

        while (true)
        {
            var nextNewMoon = _astronomicalService.CalculateNextNewMoon(currentDate.AddDays(1));
            var nextMonthStart = GetFirstDayAfterConjunction(nextNewMoon, location); // local midnight

            if (nextMonthStart >= nextYearEquinoxLocal.Date)
            {
                // This new moon starts on or after the next equinox → it belongs to the new year
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
            // We are in the biblical year that started after this year's equinox
            // The biblical year number corresponds to the Gregorian year of the equinox
            // that started this biblical year (i.e. the current Gregorian year minus 1
            // if the equinox year convention is "year before Nisan starts").
            // Per user requirement: biblical year = Gregorian year - 1 when before equinox of next year
            biblicalYear = gregorianDate.Year;
            yearStart = thisYearStart;
        }
        else
        {
            // We are before this year's biblical new year, so still in previous biblical year
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

    internal DateTime GetFirstDayAfterConjunction(DateTime conjunctionUtc, LocationInfo location)
    {
        // The new Biblical month begins at the first sunset after the conjunction
        // where the moon is at least 1% illuminated.
        //
        // Month-grid cells are keyed by Gregorian midnight (00:00 local).  The first
        // grid cell that belongs to the new month is the one whose midnight falls after
        // the opening sunset.
        //
        // All comparisons must be in the OBSERVER'S LOCAL time:
        //   • conjunctionUtc comes from LunarCalculator (UTC, DateTimeKind.Utc)
        //   • CalculateSunset now returns local time at the observer's location
        //
        // Biblical day = sunset → next sunset.  The Gregorian "label" for a Biblical
        // day is the calendar date that comes after the opening sunset:
        //
        //   Conjunction BEFORE sunset on local day D:
        //     Month opens at D's sunset → first Biblical day = D sunset → D+1 sunset
        //     → first grid cell = D+1
        //
        //   Conjunction AFTER sunset on local day D:
        //     D's sunset has already passed; month opens at D+1's sunset
        //     → first Biblical day = D+1 sunset → D+2 sunset
        //     → first grid cell = D+2
        //
        // Additionally, the moon must be at least 1% illuminated at sunset on the
        // first day of the month. If not, the month is delayed one day at a time
        // until the condition is met. In practice the moon reaches 1% within
        // ~22.6 hours of conjunction, so at most one extra day is ever needed.

        var tz = location.TimeZone;

        // Convert conjunction to the observer's local clock
        var conjunctionLocal = TimeZoneInfo.ConvertTimeFromUtc(conjunctionUtc, tz);

        // Sunset on the same local calendar day as the conjunction (returned in local time)
        var sunsetLocal = _astronomicalService.CalculateSunset(conjunctionLocal.Date, location);

        DateTime candidateDate;
        if (conjunctionLocal < sunsetLocal)
        {
            // Conjunction before sunset: new month's first grid cell = next local day
            candidateDate = conjunctionLocal.Date.AddDays(1);
        }
        else
        {
            // Conjunction after sunset: new month's first grid cell = two local days ahead
            candidateDate = conjunctionLocal.Date.AddDays(2);
        }

        // Illumination rule: the moon must show at least 0.14% illumination at the
        // OPENING sunset of the new month's first day. The opening sunset for grid
        // cell D+1 is sunset on D (the day before). Advance one day at a time until
        // the condition is met. 0.14% ≈ 8.7 h post-conjunction — low enough to
        // accept a genuine daytime-conjunction crescent (~16 h, ~0.5%) while still
        // filtering out the near-zero illumination when the conjunction falls within
        // minutes of the preceding sunset (~38 min → ~0.001%).
        const int maxAdvanceDays = 5; // safety cap
        for (int i = 0; i < maxAdvanceDays; i++)
        {
            // Opening sunset = sunset on the day BEFORE the candidate grid cell
            var openingSunsetLocal = _astronomicalService.CalculateSunset(candidateDate.AddDays(-1), location);
            // Strip Kind before converting so we always treat the value as being in `tz`
            var openingSunsetUtc = TimeZoneInfo.ConvertTimeToUtc(
                DateTime.SpecifyKind(openingSunsetLocal, DateTimeKind.Unspecified), tz);
            if (_astronomicalService.GetLunarIllumination(openingSunsetUtc) >= 0.14)
                return candidateDate;
            candidateDate = candidateDate.AddDays(1);
        }

        return candidateDate; // fallback after safety cap
    }

    /// <summary>
    /// Returns the location currently being used for calculations (for display/timezone purposes).
    /// </summary>
    public LocationInfo GetCurrentLocation() => GetDefaultLocation();

    private LocationInfo GetDefaultLocation()
    {
        // Use cached location (initialized via InitializeLocationAsync) or Jerusalem fallback
        return _cachedLocation ?? JerusalemFallback;
    }
}
