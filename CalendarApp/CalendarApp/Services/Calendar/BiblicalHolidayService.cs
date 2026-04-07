using CalendarApp.Models;
using CalendarApp.Services.Interfaces;

namespace CalendarApp.Services.Calendar;

/// <summary>
/// Calculates Biblical calendar holy days (appointed times / moedim) for display
/// on the calendar. Dates are derived from:
///   • The vernal (spring) equinox → start of Biblical year (Month 1)
///   • Lunar conjunctions → month boundaries
///   • Local sunset times → day boundaries
///
/// Holy days computed:
///   Month 1: Passover (14th), Unleavened Bread (15th–21st), Wave Sheaf, Pentecost
///   Month 7: Trumpets (1st), Atonement (10th), Tabernacles (15th–21st), Last Great Day (22nd)
/// </summary>
public class BiblicalHolidayService : IBiblicalHolidayService
{
    private readonly IAstronomicalService _astronomicalService;
    private readonly BiblicalCalendarService _biblicalCalendarService;

    public BiblicalHolidayService(
        IAstronomicalService astronomicalService,
        BiblicalCalendarService biblicalCalendarService)
    {
        _astronomicalService = astronomicalService;
        _biblicalCalendarService = biblicalCalendarService;
    }

    // ── Public API ───────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<BiblicalHoliday>> GetHolidaysForGregorianYearAsync(int gregorianYear)
    {
        var location = _biblicalCalendarService.GetCurrentLocation();
        return await Task.Run(() => ComputeHolidays(gregorianYear, location));
    }

    public async Task<Dictionary<DateTime, List<string>>> GetHolidayDisplaysForRangeAsync(DateTime start, DateTime end)
    {
        var result = new Dictionary<DateTime, List<string>>();

        // Check adjacent Gregorian years: the Biblical year starting in spring of year G
        // produces holidays from March–October of year G.  Year G-1 is checked so that
        // a view anchored in early spring of G also picks up the previous year's fall
        // holidays if they bleed into Jan/Feb of G (rare but possible).
        var years = new HashSet<int> { start.Year - 1, start.Year, end.Year, end.Year + 1 };

        foreach (var year in years)
        {
            if (year < 1 || year > 9998) continue;

            var holidays = await GetHolidaysForGregorianYearAsync(year);
            foreach (var h in holidays)
            {
                if (h.GregorianDate.Date >= start.Date && h.GregorianDate.Date <= end.Date)
                {
                    var dateKey = h.GregorianDate.Date;
                    if (!result.ContainsKey(dateKey))
                        result[dateKey] = new List<string>();
                    result[dateKey].Add(h.Name);
                }
            }
        }

        return result;
    }

    // ── Core computation ─────────────────────────────────────────────────────

    private IReadOnlyList<BiblicalHoliday> ComputeHolidays(int gregorianYear, LocationInfo location)
    {
        var holidays = new List<BiblicalHoliday>();

        // ── Spring holy days: Month 1 (Nisan / Abib) ─────────────────────────

        // Month 1 begins after the first new moon following the vernal equinox.
        var equinox = _astronomicalService.CalculateVernalEquinox(gregorianYear);
        var firstConjunction = _astronomicalService.CalculateNextNewMoon(equinox);
        var month1Start = GetFirstDayAfterConjunction(firstConjunction, location);

        // Passover — 14th of Month 1
        holidays.Add(new BiblicalHoliday("Passover", month1Start.AddDays(13)));

        // Feast of Unleavened Bread — 15th through 21st of Month 1 (7 days)
        var ubStart = month1Start.AddDays(14);
        for (int d = 0; d < 7; d++)
        {
            var ubDay = ubStart.AddDays(d);
            var name = d == 0 ? "Unleavened Bread (1st day)"
                     : d == 6 ? "Unleavened Bread (7th day)"
                     : $"Unleavened Bread (day {d + 1})";
            holidays.Add(new BiblicalHoliday(name, ubDay));
        }

        // Wave Sheaf — Sunday following the Sabbath within Unleavened Bread.
        // The Sabbath (Saturday) always falls within the 7-day UB window.
        // That Sabbath is the "1st Sabbath" of the Omer count.
        var waveSheaf = FindWaveSheaf(ubStart);
        holidays.Add(new BiblicalHoliday("Wave Sheaf", waveSheaf));

        // Pentecost — Sunday after the 8th Sabbath (= Wave Sheaf + 49 days).
        // Counting: Wave Sheaf = Day 1, so Day 50 = Wave Sheaf + 49 days.
        // 8 Sabbaths × 7 days/week = 49 days after Wave Sheaf = Sunday.
        holidays.Add(new BiblicalHoliday("Pentecost", waveSheaf.AddDays(49)));

        // ── Fall holy days: Month 7 (Tishrei) ────────────────────────────────

        // Advance from Month 1 through six more lunar months to reach Month 7.
        var monthStart = month1Start;
        for (int m = 1; m < 7; m++)
        {
            // AddDays(1) moves us safely past the current month's conjunction;
            // CalculateNextNewMoon then finds the *next* conjunction (~29.5 days later).
            var nextConj = _astronomicalService.CalculateNextNewMoon(monthStart.AddDays(1));
            monthStart = GetFirstDayAfterConjunction(nextConj, location);
        }
        var month7Start = monthStart;

        // Trumpets (Yom Teruah) — 1st of Month 7
        holidays.Add(new BiblicalHoliday("Trumpets", month7Start));

        // Atonement (Yom Kippur) — 10th of Month 7
        holidays.Add(new BiblicalHoliday("Atonement", month7Start.AddDays(9)));

        // Feast of Tabernacles (Sukkot) — 15th through 21st of Month 7 (7 days)
        var tabStart = month7Start.AddDays(14);
        for (int d = 0; d < 7; d++)
        {
            var tabDay = tabStart.AddDays(d);
            var name = d == 0 ? "Tabernacles (1st day)"
                     : d == 6 ? "Tabernacles (7th day)"
                     : $"Tabernacles (day {d + 1})";
            holidays.Add(new BiblicalHoliday(name, tabDay));
        }

        // Last Great Day — 22nd of Month 7 (day following Tabernacles)
        holidays.Add(new BiblicalHoliday("Last Great Day", month7Start.AddDays(21)));

        return holidays;
    }

    /// <summary>
    /// Finds Wave Sheaf Sunday: the Sunday immediately after the one Saturday
    /// (Sabbath) that falls within the 7-day Unleavened Bread window.
    /// Any 7-consecutive-day span always contains exactly one Saturday.
    /// </summary>
    private static DateTime FindWaveSheaf(DateTime ubStart)
    {
        for (int d = 0; d < 7; d++)
        {
            if (ubStart.AddDays(d).DayOfWeek == DayOfWeek.Saturday)
                return ubStart.AddDays(d + 1); // the following Sunday
        }

        // Unreachable in practice — a 7-day window always has one Saturday.
        return ubStart.AddDays(8);
    }

    /// <summary>
    /// Delegates to <see cref="BiblicalCalendarService.GetFirstDayAfterConjunction"/> so that
    /// holiday dates use exactly the same month-start logic (including the 1% illumination rule)
    /// as the calendar grid itself.
    /// </summary>
    private DateTime GetFirstDayAfterConjunction(DateTime conjunctionUtc, LocationInfo location)
        => _biblicalCalendarService.GetFirstDayAfterConjunction(conjunctionUtc, location);
}
