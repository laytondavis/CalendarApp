using CalendarApp.Services.Calendar;

namespace CalendarApp.Tests;

/// <summary>
/// Tests for the Julian calendar service.
/// Key fact: In the 21st century (2000–2099), the Julian calendar
/// is exactly 13 days BEHIND the Gregorian calendar.
/// So Feb 14, 2024 (Gregorian) = Feb 1, 2024 (Julian).
/// </summary>
[TestFixture]
public class JulianCalendarServiceTests
{
    private JulianCalendarService _service = null!;

    [SetUp]
    public void Setup()
    {
        _service = new JulianCalendarService();
    }

    // ========== Mode ==========

    [Test]
    public void Mode_IsJulian()
    {
        _service.Mode.Should().Be(CalendarMode.Julian);
    }

    // ========== GetDateForDateTime - Julian/Gregorian offset ==========

    /// <summary>
    /// In the 21st century the Julian calendar is 13 days behind Gregorian.
    /// Gregorian March 1, 2024 = Julian February 17, 2024.
    /// </summary>
    [Test]
    public void GetDateForDateTime_GregorianMarch1_2024_IsJulianFeb17()
    {
        var gregorian = new DateTime(2024, 3, 1);
        var result = _service.GetDateForDateTime(gregorian);

        result.Month.Should().Be(2);
        result.Day.Should().Be(17);
        result.Year.Should().Be(2024);
        result.Mode.Should().Be(CalendarMode.Julian);
    }

    /// <summary>
    /// Gregorian January 14, 2024 = Julian January 1, 2024 (Julian New Year).
    /// </summary>
    [Test]
    public void GetDateForDateTime_GregorianJan14_2024_IsJulianJan1()
    {
        var gregorian = new DateTime(2024, 1, 14);
        var result = _service.GetDateForDateTime(gregorian);

        result.Month.Should().Be(1);
        result.Day.Should().Be(1);
        result.Year.Should().Be(2024);
    }

    /// <summary>
    /// Julian Feb 29 exists in Julian leap years.
    /// 2024 is a Julian leap year (divisible by 4 with no century exception).
    /// Gregorian March 13, 2024 = Julian Feb 29, 2024.
    /// </summary>
    [Test]
    public void GetDateForDateTime_JulianLeapDay2024_Exists()
    {
        // Julian Feb 29, 2024 -> Gregorian March 13, 2024
        var gregorian = new DateTime(2024, 3, 13);
        var result = _service.GetDateForDateTime(gregorian);

        result.Month.Should().Be(2);
        result.Day.Should().Be(29);
        result.Year.Should().Be(2024);
    }

    /// <summary>
    /// 1900 is a Gregorian non-leap year but IS a Julian leap year.
    /// Julian Feb 29, 1900 exists.
    /// </summary>
    [Test]
    public void GetDaysInMonth_February1900_JulianHas29Days()
    {
        // 1900 is a Julian leap year (div by 4), but NOT a Gregorian leap year (century rule)
        _service.GetDaysInMonth(1900, 2).Should().Be(29);
    }

    [Test]
    public void GetDateForDateTime_HasCrossReferenceToGregorian()
    {
        var gregorian = new DateTime(2024, 3, 1);
        var result = _service.GetDateForDateTime(gregorian);

        result.CrossReference.Should().NotBeNullOrEmpty();
        result.CrossReference.Should().Contain("Gregorian");
    }

    // ========== GetGregorianDateTime ==========

    [Test]
    public void GetGregorianDateTime_JulianJan1_2024_IsGregorianJan14()
    {
        var julianDate = new CalendarDate(2024, 1, 1, CalendarMode.Julian,
            DateTime.MinValue); // GregorianEquivalent placeholder
        var result = _service.GetGregorianDateTime(julianDate);

        result.Year.Should().Be(2024);
        result.Month.Should().Be(1);
        result.Day.Should().Be(14);
    }

    [Test]
    public void GetGregorianDateTime_RoundTrip()
    {
        var originalGregorian = new DateTime(2024, 6, 15);
        var julianDate = _service.GetDateForDateTime(originalGregorian);
        var backToGregorian = _service.GetGregorianDateTime(julianDate);

        backToGregorian.Date.Should().Be(originalGregorian.Date);
    }

    // ========== GetFirstDayOfMonth / GetLastDayOfMonth ==========

    [Test]
    public void GetFirstDayOfMonth_Julian_IsDay1()
    {
        var first = _service.GetFirstDayOfMonth(2024, 3);
        first.Day.Should().Be(1);
        first.Month.Should().Be(3);
        first.Mode.Should().Be(CalendarMode.Julian);
    }

    [Test]
    public void GetLastDayOfMonth_February_JulianLeapYear_IsDay29()
    {
        var last = _service.GetLastDayOfMonth(2024, 2);
        last.Day.Should().Be(29);
        last.Month.Should().Be(2);
    }

    [Test]
    public void GetLastDayOfMonth_February_JulianNonLeapYear_IsDay28()
    {
        // 2023 is not divisible by 4, so not a Julian leap year
        var last = _service.GetLastDayOfMonth(2023, 2);
        last.Day.Should().Be(28);
    }

    [Test]
    public void GetLastDayOfMonth_December_IsDay31()
    {
        var last = _service.GetLastDayOfMonth(2024, 12);
        last.Day.Should().Be(31);
    }

    // ========== GetFirstDayOfYear ==========

    [Test]
    public void GetFirstDayOfYear_IsJanuaryFirst_Julian()
    {
        var first = _service.GetFirstDayOfYear(2024);
        first.Year.Should().Be(2024);
        first.Month.Should().Be(1);
        first.Day.Should().Be(1);
        first.Mode.Should().Be(CalendarMode.Julian);
    }

    // ========== GetMonthGrid ==========

    [Test]
    public void GetMonthGrid_Returns42Days()
    {
        var grid = _service.GetMonthGrid(2024, 3).ToList();
        grid.Should().HaveCount(42);
    }

    [Test]
    public void GetMonthGrid_FirstCellIsSunday()
    {
        var grid = _service.GetMonthGrid(2024, 3).ToList();
        grid[0].GregorianEquivalent.DayOfWeek.Should().Be(DayOfWeek.Sunday);
    }

    [Test]
    public void GetMonthGrid_ContainsJulianDay1()
    {
        var grid = _service.GetMonthGrid(2024, 3).ToList();
        grid.Should().Contain(d => d.Month == 3 && d.Day == 1);
    }

    [Test]
    public void GetMonthGrid_DatesAreConsecutive()
    {
        var grid = _service.GetMonthGrid(2024, 1).ToList();
        for (int i = 1; i < grid.Count; i++)
        {
            var diff = (grid[i].GregorianEquivalent - grid[i - 1].GregorianEquivalent).TotalDays;
            diff.Should().BeApproximately(1.0, 0.001);
        }
    }

    // ========== GetWeekDays ==========

    [Test]
    public void GetWeekDays_Returns7Days()
    {
        var julianDate = _service.GetDateForDateTime(new DateTime(2024, 3, 13));
        var week = _service.GetWeekDays(julianDate).ToList();
        week.Should().HaveCount(7);
    }

    [Test]
    public void GetWeekDays_StartsOnSunday()
    {
        var julianDate = _service.GetDateForDateTime(new DateTime(2024, 3, 13));
        var week = _service.GetWeekDays(julianDate).ToList();
        week[0].GregorianEquivalent.DayOfWeek.Should().Be(DayOfWeek.Sunday);
    }

    // ========== GetDaysInMonth ==========

    [Test]
    public void GetDaysInMonth_January_Returns31()
    {
        _service.GetDaysInMonth(2024, 1).Should().Be(31);
    }

    [Test]
    public void GetDaysInMonth_April_Returns30()
    {
        _service.GetDaysInMonth(2024, 4).Should().Be(30);
    }

    [Test]
    public void GetDaysInMonth_February_2000_JulianLeapYear_Returns29()
    {
        // 2000 is divisible by 4 -> Julian leap year
        _service.GetDaysInMonth(2000, 2).Should().Be(29);
    }

    // ========== GetMonthsInYear ==========

    [Test]
    public void GetMonthsInYear_AlwaysReturns12()
    {
        _service.GetMonthsInYear(2024).Should().Be(12);
        _service.GetMonthsInYear(2000).Should().Be(12);
    }

    // ========== GetMonthName ==========

    [Test]
    public void GetMonthName_ReturnsCorrectNames()
    {
        _service.GetMonthName(1).Should().Be("January");
        _service.GetMonthName(6).Should().Be("June");
        _service.GetMonthName(12).Should().Be("December");
    }

    [Test]
    public void GetMonthName_InvalidMonth_Throws()
    {
        var act = () => _service.GetMonthName(0);
        act.Should().Throw<ArgumentOutOfRangeException>();

        var act2 = () => _service.GetMonthName(13);
        act2.Should().Throw<ArgumentOutOfRangeException>();
    }

    // ========== GetCrossReferenceDisplay ==========

    [Test]
    public void GetCrossReferenceDisplay_IncludesGregorianDate()
    {
        var gregorian = new DateTime(2024, 3, 1);
        var julianDate = _service.GetDateForDateTime(gregorian);
        var crossRef = _service.GetCrossReferenceDisplay(julianDate);

        crossRef.Should().Contain("Gregorian");
    }
}
