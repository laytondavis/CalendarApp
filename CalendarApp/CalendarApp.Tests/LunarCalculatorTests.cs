using CalendarApp.Services.Astronomy;
using CalendarApp.Services.Interfaces;

namespace CalendarApp.Tests;

/// <summary>
/// Tests for Jean Meeus lunar phase calculations.
///
/// Reference new moon dates (UTC) from NASA/USNO:
///   Jan  6, 2000 18:14  (the algorithm's internal reference lunation k=0)
///   Jan 13, 2021 05:00
///   Feb 11, 2021 19:06
///   Mar 24, 2020 09:28
///   Jan 17, 2023 20:53
///
/// We allow ±12 hours on exact timestamps and ±1 day on date-only checks
/// to account for the simplified algorithm versus full ephemeris tables.
/// </summary>
[TestFixture]
public class LunarCalculatorTests
{
    private LunarCalculator _calculator = null!;

    [SetUp]
    public void Setup()
    {
        _calculator = new LunarCalculator();
    }

    // ========== CalculateNextNewMoon ==========

    [Test]
    public void CalculateNextNewMoon_IsAlwaysStrictlyAfterInput()
    {
        var testDates = new[]
        {
            new DateTime(2024, 1, 1),
            new DateTime(2024, 6, 15),
            new DateTime(2000, 1, 7), // day after the reference new moon
        };

        foreach (var date in testDates)
        {
            var nextMoon = _calculator.CalculateNextNewMoon(date);
            nextMoon.Should().BeAfter(date, because: $"next new moon after {date:d} must be in the future");
        }
    }

    /// <summary>
    /// The reference lunation k=0 is Jan 6, 2000 18:14 UTC.
    /// Asking for the next new moon after Jan 5, 2000 should return ~Jan 6.
    /// </summary>
    [Test]
    public void CalculateNextNewMoon_AfterJan5_2000_IsJan6_2000()
    {
        var afterDate = new DateTime(2000, 1, 5, 0, 0, 0, DateTimeKind.Utc);
        var newMoon = _calculator.CalculateNextNewMoon(afterDate);

        newMoon.Date.Should().Be(new DateTime(2000, 1, 6));
    }

    /// <summary>
    /// NASA data: new moon on January 13, 2021 at ~05:00 UTC.
    /// </summary>
    [Test]
    public void CalculateNextNewMoon_AfterJan12_2021_IsJan13()
    {
        var afterDate = new DateTime(2021, 1, 12);
        var newMoon = _calculator.CalculateNextNewMoon(afterDate);

        // Allow ±1 day tolerance
        newMoon.Date.Should().BeOnOrAfter(new DateTime(2021, 1, 12));
        newMoon.Date.Should().BeOnOrBefore(new DateTime(2021, 1, 14));
    }

    /// <summary>
    /// NASA data: new moon on March 24, 2020 at ~09:28 UTC.
    /// </summary>
    [Test]
    public void CalculateNextNewMoon_AfterMar23_2020_IsAroundMar24()
    {
        var afterDate = new DateTime(2020, 3, 23);
        var newMoon = _calculator.CalculateNextNewMoon(afterDate);

        newMoon.Date.Should().BeOnOrAfter(new DateTime(2020, 3, 23));
        newMoon.Date.Should().BeOnOrBefore(new DateTime(2020, 3, 25));
    }

    [Test]
    public void CalculateNextNewMoon_IntervalIsApproximately29To30Days()
    {
        var start = new DateTime(2024, 1, 1);
        var moon1 = _calculator.CalculateNextNewMoon(start);
        var moon2 = _calculator.CalculateNextNewMoon(moon1.AddDays(1));

        var interval = (moon2 - moon1).TotalDays;

        // Synodic month is 29.53 days; allow ±1 day
        interval.Should().BeGreaterThan(28.0);
        interval.Should().BeLessThan(31.0);
    }

    [Test]
    public void CalculateNextNewMoon_12ConsecutiveMoons_SpanApproximately354To355Days()
    {
        // 12 synodic months ≈ 354.37 days
        var current = new DateTime(2024, 1, 1);
        var firstMoon = _calculator.CalculateNextNewMoon(current);
        var moonDate = firstMoon;

        for (int i = 0; i < 12; i++)
        {
            moonDate = _calculator.CalculateNextNewMoon(moonDate.AddDays(1));
        }

        var totalDays = (moonDate - firstMoon).TotalDays;
        totalDays.Should().BeGreaterThan(350.0);
        totalDays.Should().BeLessThan(360.0);
    }

    // ========== CalculatePreviousNewMoon ==========

    [Test]
    public void CalculatePreviousNewMoon_IsAlwaysStrictlyBeforeInput()
    {
        var testDates = new[]
        {
            new DateTime(2024, 3, 15),
            new DateTime(2024, 6, 21),
            new DateTime(2000, 1, 7),
        };

        foreach (var date in testDates)
        {
            var prevMoon = _calculator.CalculatePreviousNewMoon(date);
            prevMoon.Should().BeBefore(date,
                because: $"previous new moon before {date:d} must be in the past");
        }
    }

    /// <summary>
    /// Jan 6, 2000 was a new moon. Asking for the previous new moon before
    /// Jan 7, 2000 should return Jan 6, 2000.
    /// </summary>
    [Test]
    public void CalculatePreviousNewMoon_BeforeJan7_2000_IsJan6_2000()
    {
        var beforeDate = new DateTime(2000, 1, 7, 23, 0, 0, DateTimeKind.Utc);
        var newMoon = _calculator.CalculatePreviousNewMoon(beforeDate);

        newMoon.Date.Should().Be(new DateTime(2000, 1, 6));
    }

    [Test]
    public void CalculatePreviousNewMoon_ThenNextNewMoon_ReturnsNextCycle()
    {
        var referenceDate = new DateTime(2024, 5, 15);
        var prev = _calculator.CalculatePreviousNewMoon(referenceDate);
        var next = _calculator.CalculateNextNewMoon(referenceDate);

        // prev should be before referenceDate
        prev.Should().BeBefore(referenceDate);
        // next should be after referenceDate
        next.Should().BeAfter(referenceDate);
        // They should be roughly one synodic month apart
        var interval = (next - prev).TotalDays;
        interval.Should().BeGreaterThan(28.0);
        interval.Should().BeLessThan(31.0);
    }

    // ========== GetLunarPhase ==========

    [Test]
    public void GetLunarPhase_DayAfterKnownNewMoon_IsNewMoon()
    {
        // Jan 6, 2000 is a new moon (algorithm reference). The day after is still "new moon" phase.
        var dayOfNewMoon = new DateTime(2000, 1, 6, 20, 0, 0, DateTimeKind.Utc);
        var phase = _calculator.GetLunarPhase(dayOfNewMoon);

        phase.Should().Be(LunarPhase.NewMoon);
    }

    [Test]
    public void GetLunarPhase_HalfwaythroughCycle_IsFullMoonOrNear()
    {
        // ~14-15 days after a new moon should yield full moon or waxing gibbous
        var newMoon = new DateTime(2000, 1, 6, 18, 14, 0, DateTimeKind.Utc);
        var halfCycle = newMoon.AddDays(14.75); // roughly half synodic month

        var phase = _calculator.GetLunarPhase(halfCycle);

        phase.Should().BeOneOf(LunarPhase.FullMoon, LunarPhase.WaxingGibbous, LunarPhase.WaningGibbous);
    }

    [Test]
    public void GetLunarPhase_FirstQuarter_AtAbout7Days()
    {
        var newMoon = _calculator.CalculateNextNewMoon(new DateTime(2024, 1, 1));
        var firstQuarterDate = newMoon.AddDays(7.4);

        var phase = _calculator.GetLunarPhase(firstQuarterDate);

        phase.Should().BeOneOf(LunarPhase.FirstQuarter, LunarPhase.WaxingCrescent, LunarPhase.WaxingGibbous);
    }

    [Test]
    public void GetLunarPhase_ThirdQuarter_AtAbout22Days()
    {
        var newMoon = _calculator.CalculateNextNewMoon(new DateTime(2024, 1, 1));
        var thirdQuarterDate = newMoon.AddDays(22.1);

        var phase = _calculator.GetLunarPhase(thirdQuarterDate);

        phase.Should().BeOneOf(LunarPhase.ThirdQuarter, LunarPhase.WaningGibbous, LunarPhase.WaningCrescent);
    }

    [Test]
    public void GetLunarPhase_AllValuesValidEnum()
    {
        var validPhases = Enum.GetValues<LunarPhase>();

        // Sweep one full synodic month and confirm all returned phases are valid enum values
        var newMoon = _calculator.CalculateNextNewMoon(new DateTime(2024, 1, 1));
        for (int day = 0; day < 30; day++)
        {
            var phase = _calculator.GetLunarPhase(newMoon.AddDays(day));
            validPhases.Should().Contain(phase);
        }
    }

    // ========== GetLunarIllumination ==========

    [Test]
    public void GetLunarIllumination_AtNewMoon_IsNearZero()
    {
        var newMoon = new DateTime(2000, 1, 6, 18, 14, 0, DateTimeKind.Utc);
        var illumination = _calculator.GetLunarIllumination(newMoon);

        // At new moon illumination should be very low (< 5%)
        illumination.Should().BeLessThan(5.0);
    }

    [Test]
    public void GetLunarIllumination_AtFullMoon_IsNearHundred()
    {
        var newMoon = new DateTime(2000, 1, 6, 18, 14, 0, DateTimeKind.Utc);
        var fullMoonApprox = newMoon.AddDays(14.75);
        var illumination = _calculator.GetLunarIllumination(fullMoonApprox);

        // At full moon illumination should be very high (> 95%)
        illumination.Should().BeGreaterThan(95.0);
    }

    [Test]
    public void GetLunarIllumination_IsAlwaysBetween0And100()
    {
        var start = new DateTime(2024, 1, 1);
        var moon = _calculator.CalculateNextNewMoon(start);

        for (int day = 0; day < 30; day++)
        {
            var illumination = _calculator.GetLunarIllumination(moon.AddDays(day));
            illumination.Should().BeGreaterThanOrEqualTo(0.0);
            illumination.Should().BeLessThanOrEqualTo(100.0);
        }
    }

    [Test]
    public void GetLunarIllumination_IncreasesFromNewToFull()
    {
        var newMoon = _calculator.CalculateNextNewMoon(new DateTime(2024, 3, 1));
        var at1Day = _calculator.GetLunarIllumination(newMoon.AddDays(1));
        var at7Days = _calculator.GetLunarIllumination(newMoon.AddDays(7));
        var at14Days = _calculator.GetLunarIllumination(newMoon.AddDays(14));

        at1Day.Should().BeLessThan(at7Days);
        at7Days.Should().BeLessThan(at14Days);
    }

    [Test]
    public void GetLunarIllumination_DecreasesFromFullToNew()
    {
        var newMoon = _calculator.CalculateNextNewMoon(new DateTime(2024, 3, 1));
        var at14Days = _calculator.GetLunarIllumination(newMoon.AddDays(14));
        var at21Days = _calculator.GetLunarIllumination(newMoon.AddDays(21));
        var at28Days = _calculator.GetLunarIllumination(newMoon.AddDays(28));

        at14Days.Should().BeGreaterThan(at21Days);
        at21Days.Should().BeGreaterThan(at28Days);
    }

    // ========== Consistency checks ==========

    [Test]
    public void NextThenPreviousNewMoon_ReturnSameLunation()
    {
        var date = new DateTime(2024, 7, 10);
        var next = _calculator.CalculateNextNewMoon(date);
        var prevOfNext = _calculator.CalculatePreviousNewMoon(next.AddHours(1));

        // Previous new moon just after "next" should land back on "next"
        (next - prevOfNext).TotalDays.Should().BeLessThan(1.0);
    }

    // ========== Diagnostic: 2025 new moons ==========

    /// <summary>
    /// NASA/USNO 2025 new moons: Aug 23 08:06 UTC, Sep 21 19:54 UTC, Oct 21 00:25 UTC.
    /// This test reveals exactly what the algorithm computes (tolerance ±2 days).
    /// </summary>
    [Test]
    public void CalculateNextNewMoon_Sep2025_ExactDayAndTime()
    {
        // Ask for the next new moon starting from Sep 1
        var newMoon = _calculator.CalculateNextNewMoon(new DateTime(2025, 9, 1, 0, 0, 0, DateTimeKind.Utc));

        // NASA: Sep 21 19:54 UTC. Assert exact day = 21 to reveal the real computed value.
        newMoon.Day.Should().Be(21,
            because: $"algorithm returned {newMoon:yyyy-MM-dd HH:mm} UTC");
        newMoon.Hour.Should().BeInRange(14, 23,
            because: $"algorithm returned {newMoon:yyyy-MM-dd HH:mm} UTC");
    }

    /// <summary>
    /// Simulates what GetLunarConjunctionDisplay does for Sep 21, 2025 in CDT (UTC-5).
    /// startOfLocalDayUtc = Sep 21 05:00 UTC.
    /// The next new moon after that must still be in September CDT for it to display.
    /// </summary>
    [Test]
    public void CalculateNextNewMoon_FromSep21StartCdt_IsStillSep21LocalTime()
    {
        // CDT = UTC-5; Sep 21 00:00 CDT = Sep 21 05:00 UTC
        var startOfLocalDayUtc = new DateTime(2025, 9, 21, 5, 0, 0, DateTimeKind.Utc);
        var newMoon = _calculator.CalculateNextNewMoon(startOfLocalDayUtc);

        // Convert to CDT for local-day check
        var cdt = TimeZoneInfo.FindSystemTimeZoneById("Central Standard Time"); // CST/CDT
        var newMoonLocal = TimeZoneInfo.ConvertTimeFromUtc(newMoon, cdt);

        // Fail with useful info showing the computed UTC and local times
        newMoonLocal.Date.Should().Be(new DateTime(2025, 9, 21),
            because: $"algorithm returned {newMoon:yyyy-MM-dd HH:mm} UTC = {newMoonLocal:yyyy-MM-dd HH:mm} CDT");
    }

    [Test]
    public void CalculateNextNewMoon_Aug2025_IsInAugust()
    {
        var newMoon = _calculator.CalculateNextNewMoon(new DateTime(2025, 8, 1, 0, 0, 0, DateTimeKind.Utc));

        // NASA: Aug 23 08:06 UTC
        newMoon.Year.Should().Be(2025,
            because: $"algorithm returned {newMoon:yyyy-MM-dd HH:mm} UTC");
        newMoon.Month.Should().Be(8,
            because: $"algorithm returned {newMoon:yyyy-MM-dd HH:mm} UTC");
        newMoon.Day.Should().BeInRange(21, 25,
            because: $"algorithm returned {newMoon:yyyy-MM-dd HH:mm} UTC");
    }

    [Test]
    public void CalculateNextNewMoon_Oct2025_IsInOctober()
    {
        var newMoon = _calculator.CalculateNextNewMoon(new DateTime(2025, 10, 1, 0, 0, 0, DateTimeKind.Utc));

        // NASA: Oct 21 00:25 UTC
        newMoon.Year.Should().Be(2025,
            because: $"algorithm returned {newMoon:yyyy-MM-dd HH:mm} UTC");
        newMoon.Month.Should().Be(10,
            because: $"algorithm returned {newMoon:yyyy-MM-dd HH:mm} UTC");
        newMoon.Day.Should().BeInRange(19, 23,
            because: $"algorithm returned {newMoon:yyyy-MM-dd HH:mm} UTC");
    }
}
