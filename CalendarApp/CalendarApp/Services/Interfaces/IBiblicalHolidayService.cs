using CalendarApp.Models;

namespace CalendarApp.Services.Interfaces;

/// <summary>
/// Computes Biblical calendar holy days (appointed times / moedim) for display
/// on the calendar. All dates are returned as Gregorian calendar dates.
/// </summary>
public interface IBiblicalHolidayService
{
    /// <summary>
    /// Returns all Biblical holy days for the Biblical year that begins (i.e., whose
    /// Month 1 / Nisan starts) in the given Gregorian year.
    /// </summary>
    Task<IReadOnlyList<BiblicalHoliday>> GetHolidaysForGregorianYearAsync(int gregorianYear);

    /// <summary>
    /// Returns a mapping from each Gregorian date in [<paramref name="start"/>,
    /// <paramref name="end"/>] to the holy-day name that falls on that date.
    /// Adjacent Biblical years are checked so that boundary months (Jan–Mar, Dec)
    /// are handled correctly.
    /// </summary>
    Task<Dictionary<DateTime, List<string>>> GetHolidayDisplaysForRangeAsync(DateTime start, DateTime end);
}
