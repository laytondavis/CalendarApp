namespace CalendarApp.Models;

/// <summary>
/// Represents a Biblical calendar holy day (appointed time / moed) with its
/// computed Gregorian date.
/// </summary>
public class BiblicalHoliday
{
    public BiblicalHoliday(string name, DateTime gregorianDate)
    {
        Name = name;
        GregorianDate = gregorianDate;
    }

    /// <summary>Display name for the holy day.</summary>
    public string Name { get; }

    /// <summary>The Gregorian calendar date on which this holy day falls.</summary>
    public DateTime GregorianDate { get; }
}
