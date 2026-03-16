using CalendarApp.Services.Astronomy;
using CalendarApp.Services.Calendar;
using CalendarApp.Services.Interfaces;

namespace CalendarApp.Tests;

/// <summary>
/// Integration tests for the Biblical calendar service using real astronomical
/// calculations (built-in NOAA/Meeus algorithms, no network required).
///
/// Key facts validated:
/// - Days begin at sunset (the previous evening's sunset starts the Biblical day)
/// - Months begin on the first day after lunar conjunction
/// - Year begins with the first month after the vernal equinox
/// - Biblical year has 12 or 13 months (intercalary Adar II in leap years)
/// </summary>
[TestFixture]
public class BiblicalCalendarServiceTests
{
    private BiblicalCalendarService _service = null!;
    private AstronomicalService _astroService = null!;

    // Jerusalem as the canonical test location
    private static readonly LocationInfo Jerusalem =
        new(31.7683, 35.2137, null, TimeZoneInfo.Local.Id);

    [SetUp]
    public async Task Setup()
    {
        _astroService = new AstronomicalService(new System.Net.Http.HttpClient());
        _service = new BiblicalCalendarService(_astroService, new StubLocationService(Jerusalem));
        // Initialize the location cache so the service uses the stub location (not Jerusalem IST fallback)
        await _service.InitializeLocationAsync();
    }

    // ========== Mode ==========

    [Test]
    public void Mode_IsBiblical()
    {
        _service.Mode.Should().Be(CalendarMode.Biblical);
    }

    // ========== GetDateForDateTime - Basic Sanity ==========

    [Test]
    public void GetDateForDateTime_ReturnsBiblicalMode()
    {
        var result = _service.GetDateForDateTime(new DateTime(2024, 4, 15, 12, 0, 0));
        result.Mode.Should().Be(CalendarMode.Biblical);
    }

    [Test]
    public void GetDateForDateTime_DayIsPositive()
    {
        var result = _service.GetDateForDateTime(new DateTime(2024, 6, 10, 12, 0, 0));
        result.Day.Should().BeGreaterThan(0);
    }

    [Test]
    public void GetDateForDateTime_MonthIsInRange()
    {
        var result = _service.GetDateForDateTime(new DateTime(2024, 6, 10, 12, 0, 0));
        result.Month.Should().BeGreaterThanOrEqualTo(1);
        result.Month.Should().BeLessThanOrEqualTo(13);
    }

    [Test]
    public void GetDateForDateTime_YearIsReasonable()
    {
        var result = _service.GetDateForDateTime(new DateTime(2024, 6, 10, 12, 0, 0));
        // Biblical year corresponding to 2024 Gregorian should be near 2024
        result.Year.Should().BeInRange(2023, 2025);
    }

    [Test]
    public void GetDateForDateTime_HasCrossReferenceToGregorian()
    {
        var result = _service.GetDateForDateTime(new DateTime(2024, 4, 15, 12, 0, 0));
        result.CrossReference.Should().NotBeNullOrEmpty();
        result.CrossReference.Should().Contain("Gregorian");
    }

    // ========== Day Boundary at Sunset ==========

    /// <summary>
    /// If two DateTime values straddle the sunset on the same calendar day,
    /// they should fall in different Biblical days (before and after sunset
    /// map to day N and day N+1 respectively).
    /// </summary>
    [Test]
    public void GetDateForDateTime_BeforeAndAfterSunset_AreDifferentBiblicalDays()
    {
        // Use a mid-summer date; sunset in Jerusalem is around 17:00 UTC
        var date = new DateTime(2024, 6, 15);
        var sunset = _astroService.CalculateSunset(date, Jerusalem);

        // One minute before sunset → current Biblical day
        var beforeSunset = new DateTime(date.Year, date.Month, date.Day,
            sunset.Hour, sunset.Minute, 0).AddMinutes(-1);

        // One minute after sunset → next Biblical day
        var afterSunset = new DateTime(date.Year, date.Month, date.Day,
            sunset.Hour, sunset.Minute, 0).AddMinutes(1);

        var dayBefore = _service.GetDateForDateTime(beforeSunset);
        var dayAfter = _service.GetDateForDateTime(afterSunset);

        // The two observations should be in different Biblical days
        dayBefore.Day.Should().NotBe(dayAfter.Day,
            because: $"crossing sunset (at {sunset:HH:mm:ss}) moves to the next Biblical day");
    }

    // ========== GetDayStart ==========

    [Test]
    public void GetDayStart_IsInPreviousEveningOrCurrentMorning()
    {
        // Biblical day starts at previous day's sunset.
        // For a date at noon, the day start (previous sunset) should be
        // sometime on the previous Gregorian calendar day.
        var today = new DateTime(2024, 7, 4, 12, 0, 0); // noon
        var dayStart = _service.GetDayStart(today);

        // The day start (previous day's sunset) should be before noon today
        dayStart.Should().BeBefore(today);
    }

    [Test]
    public void GetDayStart_IsBeforeGetDayEnd()
    {
        var date = new DateTime(2024, 5, 20, 12, 0, 0);
        var start = _service.GetDayStart(date);
        var end = _service.GetDayEnd(date);

        start.Should().BeBefore(end);
    }

    // ========== GetMonthsInYear ==========

    [Test]
    public void GetMonthsInYear_IsBetween12And13()
    {
        // Test a range of years
        foreach (var year in new[] { 2023, 2024, 2025, 2026, 2027 })
        {
            var months = _service.GetMonthsInYear(year);
            months.Should().BeInRange(12, 13,
                because: $"Biblical year {year} must have 12 or 13 months");
        }
    }

    [Test]
    public void GetMonthsInYear_OverSeveralYears_HasBothLeapAndNonLeap()
    {
        // Over an 8-year span (approximately 3 Metonic cycle segments),
        // some years should have 13 months (leap years)
        var monthCounts = Enumerable.Range(2020, 8)
            .Select(y => _service.GetMonthsInYear(y))
            .ToList();

        monthCounts.Should().Contain(12, because: "there are regular (12-month) Biblical years");
        monthCounts.Should().Contain(13, because: "there are intercalary (13-month) Biblical years");
    }

    // ========== GetFirstDayOfMonth ==========

    [Test]
    public void GetFirstDayOfMonth_DayIsAlways1()
    {
        var first = _service.GetFirstDayOfMonth(2024, 1);
        first.Day.Should().Be(1);
    }

    [Test]
    public void GetFirstDayOfMonth_MonthMatchesInput()
    {
        var first = _service.GetFirstDayOfMonth(2024, 3);
        first.Month.Should().Be(3);
    }

    [Test]
    public void GetFirstDayOfMonth_IsBiblicalMode()
    {
        var first = _service.GetFirstDayOfMonth(2024, 1);
        first.Mode.Should().Be(CalendarMode.Biblical);
    }

    // ========== GetLastDayOfMonth ==========

    [Test]
    public void GetLastDayOfMonth_DayIsBetween29And30()
    {
        // Biblical months are 29 or 30 days (one synodic month)
        var last = _service.GetLastDayOfMonth(2024, 1);
        last.Day.Should().BeInRange(29, 30);
    }

    // ========== GetFirstDayOfYear ==========

    [Test]
    public void GetFirstDayOfYear_IsMonth1Day1()
    {
        var first = _service.GetFirstDayOfYear(2024);
        first.Month.Should().Be(1);
        first.Day.Should().Be(1);
    }

    [Test]
    public void GetFirstDayOfYear_GregorianEquivalentIsAfterVernalEquinox()
    {
        // The Biblical year starts with the new moon AFTER the vernal equinox
        var firstDay = _service.GetFirstDayOfYear(2024);
        var equinox = _astroService.CalculateVernalEquinox(2024);

        firstDay.GregorianEquivalent.Should().BeOnOrAfter(equinox,
            because: "the Biblical year starts after the vernal equinox");
    }

    [Test]
    public void GetFirstDayOfYear_GregorianEquivalentIsInSpring()
    {
        // The first day of the Biblical year (Nisan 1) falls in March or April
        var firstDay = _service.GetFirstDayOfYear(2024);
        var gregorian = firstDay.GregorianEquivalent;

        gregorian.Month.Should().BeInRange(3, 4,
            because: "Biblical Nisan 1 falls in March or April");
    }

    // ========== GetMonthGrid ==========

    [Test]
    public void GetMonthGrid_Returns42Cells()
    {
        var grid = _service.GetMonthGrid(2024, 1).ToList();
        grid.Should().HaveCount(42);
    }

    [Test]
    public void GetMonthGrid_FirstCellIsSunday()
    {
        var grid = _service.GetMonthGrid(2024, 1).ToList();
        grid[0].GregorianEquivalent.DayOfWeek.Should().Be(DayOfWeek.Sunday);
    }

    [Test]
    public void GetMonthGrid_ContainsDay1OfRequestedMonth()
    {
        var grid = _service.GetMonthGrid(2024, 2).ToList();
        grid.Should().Contain(d => d.Month == 2 && d.Day == 1);
    }

    [Test]
    public void GetMonthGrid_DatesAreConsecutiveGregorianDays()
    {
        var grid = _service.GetMonthGrid(2024, 1).ToList();
        for (int i = 1; i < grid.Count; i++)
        {
            var diff = (grid[i].GregorianEquivalent - grid[i - 1].GregorianEquivalent).TotalDays;
            diff.Should().BeApproximately(1.0, 0.001,
                because: "each cell in the grid represents one consecutive day");
        }
    }

    // ========== GetWeekDays ==========

    [Test]
    public void GetWeekDays_Returns7Days()
    {
        var midApril = _service.GetDateForDateTime(new DateTime(2024, 4, 15, 12, 0, 0));
        var week = _service.GetWeekDays(midApril).ToList();
        week.Should().HaveCount(7);
    }

    [Test]
    public void GetWeekDays_StartsOnSunday()
    {
        var midApril = _service.GetDateForDateTime(new DateTime(2024, 4, 15, 12, 0, 0));
        var week = _service.GetWeekDays(midApril).ToList();
        week[0].GregorianEquivalent.DayOfWeek.Should().Be(DayOfWeek.Sunday);
    }

    // ========== GetDaysInMonth ==========

    [Test]
    public void GetDaysInMonth_IsBetween29And30()
    {
        // Biblical months track the synodic month (~29.53 days)
        // so months are either 29 or 30 days
        for (int m = 1; m <= 3; m++)
        {
            var days = _service.GetDaysInMonth(2024, m);
            days.Should().BeInRange(29, 30,
                because: $"Biblical month {m} must be 29 or 30 days");
        }
    }

    // ========== GetMonthName ==========

    [Test]
    public void GetMonthName_Month1_IsNisan()
    {
        _service.GetMonthName(1).Should().Contain("Nisan");
    }

    [Test]
    public void GetMonthName_Month7_IsTishrei()
    {
        _service.GetMonthName(7).Should().Be("Tishrei");
    }

    [Test]
    public void GetMonthName_Month13_IsAdarII()
    {
        _service.GetMonthName(13).Should().Contain("Adar");
    }

    [Test]
    public void GetMonthName_InvalidMonth_Throws()
    {
        var act = () => _service.GetMonthName(0);
        act.Should().Throw<ArgumentOutOfRangeException>();

        var act2 = () => _service.GetMonthName(14);
        act2.Should().Throw<ArgumentOutOfRangeException>();
    }

    // ========== GetCrossReferenceDisplay ==========

    [Test]
    public void GetCrossReferenceDisplay_IncludesGregorian()
    {
        var date = _service.GetDateForDateTime(new DateTime(2024, 5, 1, 12, 0, 0));
        var display = _service.GetCrossReferenceDisplay(date);
        display.Should().Contain("Gregorian");
    }

    // ========== Consistency: first day of successive months are ~29-30 days apart ==========

    [Test]
    public void SuccessiveMonthStarts_AreApproximatelySynodicMonthApart()
    {
        var firstDayM1 = _service.GetFirstDayOfMonth(2024, 1);
        var firstDayM2 = _service.GetFirstDayOfMonth(2024, 2);

        var daysBetween = (firstDayM2.GregorianEquivalent - firstDayM1.GregorianEquivalent).TotalDays;

        daysBetween.Should().BeInRange(29, 31,
            because: "Biblical months span one synodic month (~29.53 days)");
    }
}

// ========== Test Infrastructure ==========

/// <summary>
/// Minimal stub for ILocationService that always returns a fixed location.
/// Used so BiblicalCalendarService tests don't need a database or GPS.
/// </summary>
internal class StubLocationService : ILocationService
{
    private readonly LocationInfo _location;

    public StubLocationService(LocationInfo location)
    {
        _location = location;
    }

    public LocationMode Mode { get; set; } = LocationMode.ManualOnly;
    public bool IsLocationEnabled => true;

    public Task<LocationInfo?> GetCurrentLocationAsync() => Task.FromResult<LocationInfo?>(_location);
    public Task<LocationInfo?> GetLastKnownLocationAsync() => Task.FromResult<LocationInfo?>(_location);
    public Task<LocationInfo?> GetDefaultLocationAsync() => Task.FromResult<LocationInfo?>(_location);
    public Task<bool> RequestPermissionAsync() => Task.FromResult(true);
    public Task SaveDefaultLocationAsync(LocationInfo location, string name,
        string? city = null, string? zipCode = null, string? county = null, string? state = null)
        => Task.CompletedTask;
    public Task<IEnumerable<SavedLocation>> GetSavedLocationsAsync()
        => Task.FromResult(Enumerable.Empty<SavedLocation>());
    public Task<IEnumerable<LocationSuggestion>> SearchLocationsAsync(string query)
        => Task.FromResult(Enumerable.Empty<LocationSuggestion>());
    public Task<int> AddLocationAsync(LocationInfo location, string name,
        string? city = null, string? zipCode = null, string? county = null, string? state = null)
        => Task.FromResult(0);
    public Task SetActiveLocationAsync(int id) => Task.CompletedTask;
    public Task DeleteLocationAsync(int id) => Task.CompletedTask;
}
