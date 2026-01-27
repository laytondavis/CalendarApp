namespace CalendarApp.Models;

/// <summary>
/// Represents the three calendar systems supported by the application.
/// </summary>
public enum CalendarMode
{
    /// <summary>
    /// Standard Gregorian calendar. Days begin and end at midnight.
    /// Year begins on January 1.
    /// </summary>
    Gregorian,

    /// <summary>
    /// Julian calendar. Days begin and end at midnight.
    /// Year begins on January 1.
    /// Shows cross-reference to Gregorian dates.
    /// </summary>
    Julian,

    /// <summary>
    /// Biblical calendar. Days begin at sunset.
    /// Month begins on the first day following lunar conjunction.
    /// Year begins with the first month after spring equinox.
    /// Shows cross-reference to Gregorian dates (at noon during daylight hours).
    /// </summary>
    Biblical
}
