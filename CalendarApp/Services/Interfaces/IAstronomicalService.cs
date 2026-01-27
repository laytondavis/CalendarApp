using CalendarApp.Models;

namespace CalendarApp.Services.Interfaces;

/// <summary>
/// Service for astronomical calculations including sunset, lunar phases, and equinoxes.
/// </summary>
public interface IAstronomicalService
{
    /// <summary>
    /// Gets or sets the calculation mode (built-in or web API).
    /// </summary>
    AstronomyCalculationMode Mode { get; set; }

    /// <summary>
    /// Calculates the sunrise time for a given date and location.
    /// </summary>
    DateTime CalculateSunrise(DateTime date, LocationInfo location);

    /// <summary>
    /// Calculates the sunset time for a given date and location.
    /// </summary>
    DateTime CalculateSunset(DateTime date, LocationInfo location);

    /// <summary>
    /// Calculates the next new moon (lunar conjunction) after a given date.
    /// </summary>
    DateTime CalculateNextNewMoon(DateTime afterDate);

    /// <summary>
    /// Calculates the previous new moon (lunar conjunction) before a given date.
    /// </summary>
    DateTime CalculatePreviousNewMoon(DateTime beforeDate);

    /// <summary>
    /// Gets the lunar phase for a given date.
    /// </summary>
    LunarPhase GetLunarPhase(DateTime date);

    /// <summary>
    /// Gets the lunar illumination percentage (0-100) for a given date.
    /// </summary>
    double GetLunarIllumination(DateTime date);

    /// <summary>
    /// Calculates the vernal (spring) equinox for a given year.
    /// </summary>
    DateTime CalculateVernalEquinox(int year);

    /// <summary>
    /// Calculates the autumnal equinox for a given year.
    /// </summary>
    DateTime CalculateAutumnalEquinox(int year);

    /// <summary>
    /// Calculates the summer solstice for a given year.
    /// </summary>
    DateTime CalculateSummerSolstice(int year);

    /// <summary>
    /// Calculates the winter solstice for a given year.
    /// </summary>
    DateTime CalculateWinterSolstice(int year);

    /// <summary>
    /// Calculates the sunrise time asynchronously (for API mode).
    /// </summary>
    Task<DateTime> CalculateSunriseAsync(DateTime date, LocationInfo location);

    /// <summary>
    /// Calculates the sunset time asynchronously (for API mode).
    /// </summary>
    Task<DateTime> CalculateSunsetAsync(DateTime date, LocationInfo location);
}

/// <summary>
/// The method used for astronomical calculations.
/// </summary>
public enum AstronomyCalculationMode
{
    /// <summary>
    /// Use built-in algorithms (NOAA/Jean Meeus).
    /// </summary>
    BuiltIn,

    /// <summary>
    /// Use external web API.
    /// </summary>
    WebApi
}

/// <summary>
/// Represents the phase of the moon.
/// </summary>
public enum LunarPhase
{
    NewMoon,
    WaxingCrescent,
    FirstQuarter,
    WaxingGibbous,
    FullMoon,
    WaningGibbous,
    ThirdQuarter,
    WaningCrescent
}
