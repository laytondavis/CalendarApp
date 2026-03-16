using CalendarApp.Models;
using CalendarApp.Services.Interfaces;

namespace CalendarApp.Services.Astronomy;

/// <summary>
/// Service for astronomical calculations including sunset, lunar phases, and equinoxes.
/// Supports both built-in algorithms and external API.
/// </summary>
public class AstronomicalService : IAstronomicalService
{
    private readonly HttpClient _httpClient;
    private readonly SolarCalculator _solarCalculator;
    private readonly LunarCalculator _lunarCalculator;

    public AstronomyCalculationMode Mode { get; set; } = AstronomyCalculationMode.BuiltIn;

    public AstronomicalService(HttpClient httpClient)
    {
        _httpClient = httpClient;
        _solarCalculator = new SolarCalculator();
        _lunarCalculator = new LunarCalculator();
    }

    public DateTime CalculateSunrise(DateTime date, LocationInfo location)
    {
        var utc = _solarCalculator.CalculateSunrise(date, location.Latitude, location.Longitude);
        return TimeZoneInfo.ConvertTimeFromUtc(utc, location.TimeZone);
    }

    public DateTime CalculateSunset(DateTime date, LocationInfo location)
    {
        var utc = _solarCalculator.CalculateSunset(date, location.Latitude, location.Longitude);
        return TimeZoneInfo.ConvertTimeFromUtc(utc, location.TimeZone);
    }

    public async Task<DateTime> CalculateSunriseAsync(DateTime date, LocationInfo location)
    {
        if (Mode == AstronomyCalculationMode.WebApi)
        {
            return await CalculateSunTimeViaApiAsync(date, location, isSunrise: true);
        }
        return CalculateSunrise(date, location); // already returns local time
    }

    public async Task<DateTime> CalculateSunsetAsync(DateTime date, LocationInfo location)
    {
        if (Mode == AstronomyCalculationMode.WebApi)
        {
            return await CalculateSunTimeViaApiAsync(date, location, isSunrise: false);
        }
        return CalculateSunset(date, location); // already returns local time
    }

    public DateTime CalculateNextNewMoon(DateTime afterDate)
    {
        return _lunarCalculator.CalculateNextNewMoon(afterDate);
    }

    public DateTime CalculatePreviousNewMoon(DateTime beforeDate)
    {
        return _lunarCalculator.CalculatePreviousNewMoon(beforeDate);
    }

    public LunarPhase GetLunarPhase(DateTime date)
    {
        return _lunarCalculator.GetLunarPhase(date);
    }

    public double GetLunarIllumination(DateTime date)
    {
        return _lunarCalculator.GetLunarIllumination(date);
    }

    public DateTime CalculateVernalEquinox(int year)
    {
        return _solarCalculator.CalculateVernalEquinox(year);
    }

    public DateTime CalculateAutumnalEquinox(int year)
    {
        return _solarCalculator.CalculateAutumnalEquinox(year);
    }

    public DateTime CalculateSummerSolstice(int year)
    {
        return _solarCalculator.CalculateSummerSolstice(year);
    }

    public DateTime CalculateWinterSolstice(int year)
    {
        return _solarCalculator.CalculateWinterSolstice(year);
    }

    private async Task<DateTime> CalculateSunTimeViaApiAsync(DateTime date, LocationInfo location, bool isSunrise)
    {
        try
        {
            // Using Sunrise-Sunset.org API
            var url = $"https://api.sunrise-sunset.org/json?" +
                      $"lat={location.Latitude}&lng={location.Longitude}" +
                      $"&date={date:yyyy-MM-dd}&formatted=0";

            var response = await _httpClient.GetStringAsync(url);
            var json = System.Text.Json.JsonDocument.Parse(response);
            var results = json.RootElement.GetProperty("results");

            var timeString = isSunrise
                ? results.GetProperty("sunrise").GetString()
                : results.GetProperty("sunset").GetString();

            if (DateTime.TryParse(timeString, out var utcTime))
            {
                // Convert to local time for the location
                var tz = TimeZoneInfo.FindSystemTimeZoneById(location.TimeZoneId);
                return TimeZoneInfo.ConvertTimeFromUtc(utcTime, tz);
            }
        }
        catch
        {
            // Fall back to built-in calculation
        }

        return isSunrise
            ? CalculateSunrise(date, location)
            : CalculateSunset(date, location);
    }
}
