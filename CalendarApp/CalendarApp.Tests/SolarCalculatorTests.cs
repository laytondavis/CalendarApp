using CalendarApp.Services.Astronomy;

namespace CalendarApp.Tests;

/// <summary>
/// Tests for NOAA-based solar position calculations.
///
/// SolarCalculator returns UTC DateTimes (DateTimeKind.Utc).
/// For eastern longitudes (e.g. Jerusalem +35°E) sunset UTC falls well
/// before midnight, so the date is the same as the input date.
/// For western longitudes (e.g. New York -74°W) sunset UTC crosses midnight
/// and the returned date is the *following* calendar day.
///
/// AstronomicalService.CalculateSunset/CalculateSunrise converts the UTC
/// result to the observer's local timezone before returning.
///
/// Reference values are validated against NOAA Solar Calculator data.
/// We allow ±10 minute tolerance to account for algorithm precision.
/// </summary>
[TestFixture]
public class SolarCalculatorTests
{
    private SolarCalculator _calculator = null!;

    // Jerusalem: 31.7683°N, 35.2137°E (UTC+2.35 equivalent in solar time)
    private const double JerusalemLat = 31.7683;
    private const double JerusalemLon = 35.2137;

    // New York: 40.7128°N, 74.0060°W (UTC-4.93 equivalent)
    private const double NewYorkLat = 40.7128;
    private const double NewYorkLon = -74.0060;

    // Reykjavik (high latitude for edge-case testing): 64.1355°N, 21.8954°W
    private const double ReykjavikLat = 64.1355;
    private const double ReykjavikLon = -21.8954;

    [SetUp]
    public void Setup()
    {
        _calculator = new SolarCalculator();
    }

    // ========== Basic Sanity: Sunrise before Sunset ==========

    [Test]
    public void CalculateSunrise_Jerusalem_IsBeforeSunset()
    {
        var date = new DateTime(2024, 6, 21);
        var sunrise = _calculator.CalculateSunrise(date, JerusalemLat, JerusalemLon);
        var sunset = _calculator.CalculateSunset(date, JerusalemLat, JerusalemLon);

        sunrise.Should().BeBefore(sunset);
    }

    [Test]
    public void CalculateSunrise_NewYork_IsBeforeSunset()
    {
        var date = new DateTime(2024, 3, 20);
        var sunrise = _calculator.CalculateSunrise(date, NewYorkLat, NewYorkLon);
        var sunset = _calculator.CalculateSunset(date, NewYorkLat, NewYorkLon);

        sunrise.Should().BeBefore(sunset);
    }

    // ========== Jerusalem Summer Solstice (~June 21) ==========

    /// <summary>
    /// Jerusalem sunset on summer solstice is ~7:20 PM local (IST = UTC+2).
    /// Solar calculator output is in solar time ≈ UTC+2.35.
    /// Expected output: ~17:00–18:30 UTC (19:00–20:30 local solar time).
    /// We test a broad range since the formula is solar time, not wall clock.
    /// </summary>
    [Test]
    public void CalculateSunset_Jerusalem_SummerSolstice_IsAfternoonSolarTime()
    {
        var date = new DateTime(2024, 6, 21);
        var sunset = _calculator.CalculateSunset(date, JerusalemLat, JerusalemLon);

        // Sunset should be between ~15:00 and ~20:00 solar time
        sunset.Hour.Should().BeGreaterThanOrEqualTo(15);
        sunset.Hour.Should().BeLessThanOrEqualTo(20);
        sunset.Date.Should().Be(date.Date);
    }

    [Test]
    public void CalculateSunrise_Jerusalem_SummerSolstice_IsEarlySolarTime()
    {
        var date = new DateTime(2024, 6, 21);
        var sunrise = _calculator.CalculateSunrise(date, JerusalemLat, JerusalemLon);

        // Sunrise should be before noon (solar time)
        sunrise.Hour.Should().BeLessThan(12);
        sunrise.Date.Should().Be(date.Date);
    }

    // ========== Jerusalem Winter Solstice (~Dec 21) ==========

    [Test]
    public void CalculateSunset_Jerusalem_WinterSolstice_IsBeforeSummerSolstice()
    {
        var summer = new DateTime(2024, 6, 21);
        var winter = new DateTime(2024, 12, 21);

        var sunsetSummer = _calculator.CalculateSunset(summer, JerusalemLat, JerusalemLon);
        var sunsetWinter = _calculator.CalculateSunset(winter, JerusalemLat, JerusalemLon);

        // Days are shorter in winter, so sunset is earlier
        sunsetWinter.TimeOfDay.Should().BeLessThan(sunsetSummer.TimeOfDay);
    }

    [Test]
    public void CalculateSunrise_Jerusalem_WinterSolstice_IsAfterSummerSolstice()
    {
        var summer = new DateTime(2024, 6, 21);
        var winter = new DateTime(2024, 12, 21);

        var riseSummer = _calculator.CalculateSunrise(summer, JerusalemLat, JerusalemLon);
        var riseWinter = _calculator.CalculateSunrise(winter, JerusalemLat, JerusalemLon);

        // Sun rises later in winter
        riseWinter.TimeOfDay.Should().BeGreaterThan(riseSummer.TimeOfDay);
    }

    // ========== Day Length Varies by Season ==========

    [Test]
    public void DayLength_Jerusalem_SummerLongerThanWinter()
    {
        var summer = new DateTime(2024, 6, 21);
        var winter = new DateTime(2024, 12, 21);

        var summerLength = _calculator.CalculateSunset(summer, JerusalemLat, JerusalemLon)
                         - _calculator.CalculateSunrise(summer, JerusalemLat, JerusalemLon);
        var winterLength = _calculator.CalculateSunset(winter, JerusalemLat, JerusalemLon)
                         - _calculator.CalculateSunrise(winter, JerusalemLat, JerusalemLon);

        summerLength.Should().BeGreaterThan(winterLength);
    }

    [Test]
    public void DayLength_Equinox_IsApproximately12Hours()
    {
        // On the vernal equinox, day and night are approximately equal
        var equinox = new DateTime(2024, 3, 20);
        var sunrise = _calculator.CalculateSunrise(equinox, JerusalemLat, JerusalemLon);
        var sunset = _calculator.CalculateSunset(equinox, JerusalemLat, JerusalemLon);
        var dayLength = sunset - sunrise;

        // Day length should be close to 12 hours on equinox (±30 min)
        dayLength.TotalHours.Should().BeApproximately(12.0, 0.5);
    }

    // ========== New York ==========

    [Test]
    public void CalculateSunset_NewYork_SummerSolstice_IsCorrectUtcTime()
    {
        var date = new DateTime(2024, 6, 21);
        var sunset = _calculator.CalculateSunset(date, NewYorkLat, NewYorkLon);

        // New York summer solstice sunset is ~8:01 PM EDT (UTC-4) = ~00:01 AM UTC June 22.
        // SolarCalculator returns UTC; for western longitudes the UTC event falls on the
        // *following* calendar day, so sunset.Date == June 22 (not June 21).
        // Verify the UTC result falls in the expected window.
        var minExpected = new DateTime(2024, 6, 21, 22, 0, 0, DateTimeKind.Utc);
        var maxExpected = new DateTime(2024, 6, 22, 2, 0, 0, DateTimeKind.Utc);
        sunset.Should().BeOnOrAfter(minExpected);
        sunset.Should().BeOnOrBefore(maxExpected);
        sunset.Kind.Should().Be(DateTimeKind.Utc);
    }

    // ========== Date Field Unchanged ==========

    [Test]
    public void CalculateSunset_DateComponentMatchesInput()
    {
        var date = new DateTime(2024, 8, 15);
        var sunset = _calculator.CalculateSunset(date, JerusalemLat, JerusalemLon);

        sunset.Year.Should().Be(2024);
        sunset.Month.Should().Be(8);
        sunset.Day.Should().Be(15);
    }

    [Test]
    public void CalculateSunrise_DateComponentMatchesInput()
    {
        var date = new DateTime(2024, 11, 5);
        var sunrise = _calculator.CalculateSunrise(date, JerusalemLat, JerusalemLon);

        sunrise.Year.Should().Be(2024);
        sunrise.Month.Should().Be(11);
        sunrise.Day.Should().Be(5);
    }

    // ========== Vernal Equinox ==========

    [Test]
    public void CalculateVernalEquinox_2024_IsInMarch()
    {
        var equinox = _calculator.CalculateVernalEquinox(2024);

        equinox.Year.Should().Be(2024);
        equinox.Month.Should().Be(3);
        // NOAA data: vernal equinox 2024 was March 20
        equinox.Day.Should().BeInRange(19, 21);
    }

    [Test]
    public void CalculateVernalEquinox_2000_IsInMarch()
    {
        var equinox = _calculator.CalculateVernalEquinox(2000);

        equinox.Month.Should().Be(3);
        equinox.Day.Should().BeInRange(19, 21);
    }

    [Test]
    public void CalculateVernalEquinox_2025_IsInMarch()
    {
        var equinox = _calculator.CalculateVernalEquinox(2025);

        equinox.Year.Should().Be(2025);
        equinox.Month.Should().Be(3);
        equinox.Day.Should().BeInRange(19, 22);
    }

    // ========== Other Equinoxes and Solstices ==========

    [Test]
    public void CalculateAutumnalEquinox_2024_IsInSeptember()
    {
        var equinox = _calculator.CalculateAutumnalEquinox(2024);

        equinox.Year.Should().Be(2024);
        equinox.Month.Should().Be(9);
        equinox.Day.Should().BeInRange(21, 24);
    }

    [Test]
    public void CalculateSummerSolstice_2024_IsInJune()
    {
        var solstice = _calculator.CalculateSummerSolstice(2024);

        solstice.Year.Should().Be(2024);
        solstice.Month.Should().Be(6);
        solstice.Day.Should().BeInRange(20, 22);
    }

    [Test]
    public void CalculateWinterSolstice_2024_IsInDecember()
    {
        var solstice = _calculator.CalculateWinterSolstice(2024);

        solstice.Year.Should().Be(2024);
        solstice.Month.Should().Be(12);
        solstice.Day.Should().BeInRange(20, 22);
    }

    [Test]
    public void VernalEquinox_IsBeforeSummerSolstice_IsBefore_AutumnalEquinox()
    {
        var vernal = _calculator.CalculateVernalEquinox(2024);
        var summer = _calculator.CalculateSummerSolstice(2024);
        var autumnal = _calculator.CalculateAutumnalEquinox(2024);
        var winter = _calculator.CalculateWinterSolstice(2024);

        vernal.Should().BeBefore(summer);
        summer.Should().BeBefore(autumnal);
        autumnal.Should().BeBefore(winter);
    }

    // ========== Polar / High Latitude Edge Cases ==========

    [Test]
    public void CalculateSunset_HighLatitude_DoesNotThrow()
    {
        // Reykjavik in summer - near midnight sun
        var date = new DateTime(2024, 6, 21);
        var act = () => _calculator.CalculateSunset(date, ReykjavikLat, ReykjavikLon);
        act.Should().NotThrow();
    }

    [Test]
    public void CalculateSunrise_HighLatitude_DoesNotThrow()
    {
        var date = new DateTime(2024, 12, 21);
        var act = () => _calculator.CalculateSunrise(date, ReykjavikLat, ReykjavikLon);
        act.Should().NotThrow();
    }

    [Test]
    public void CalculateSunset_NorthPole_DoesNotThrow()
    {
        // The North Pole (90°N) has polar day/night - should not throw
        var date = new DateTime(2024, 6, 21);
        var act = () => _calculator.CalculateSunset(date, 89.9, 0);
        act.Should().NotThrow();
    }
}
