using CalendarApp.Services.Calendar;

namespace CalendarApp.Tests;

[TestFixture]
public class GregorianCalendarServiceTests
{
    private GregorianCalendarService _service = null!;

    [SetUp]
    public void Setup()
    {
        _service = new GregorianCalendarService();
    }

    // ========== Mode ==========

    [Test]
    public void Mode_IsGregorian()
    {
        _service.Mode.Should().Be(CalendarMode.Gregorian);
    }

    // ========== GetDateForDateTime ==========

    [Test]
    public void GetDateForDateTime_ReturnsCorrectYearMonthDay()
    {
        var dt = new DateTime(2024, 3, 15, 14, 30, 0);
        var result = _service.GetDateForDateTime(dt);

        result.Year.Should().Be(2024);
        result.Month.Should().Be(3);
        result.Day.Should().Be(15);
        result.Mode.Should().Be(CalendarMode.Gregorian);
    }

    [Test]
    public void GetDateForDateTime_GregorianEquivalent_IsMidnight()
    {
        var dt = new DateTime(2024, 6, 21, 23, 59, 59);
        var result = _service.GetDateForDateTime(dt);

        result.GregorianEquivalent.Should().Be(new DateTime(2024, 6, 21));
    }

    [Test]
    public void GetDateForDateTime_NewYearDay()
    {
        var dt = new DateTime(2000, 1, 1);
        var result = _service.GetDateForDateTime(dt);

        result.Year.Should().Be(2000);
        result.Month.Should().Be(1);
        result.Day.Should().Be(1);
    }

    [Test]
    public void GetDateForDateTime_LeapDayFeb29()
    {
        var dt = new DateTime(2024, 2, 29);
        var result = _service.GetDateForDateTime(dt);

        result.Year.Should().Be(2024);
        result.Month.Should().Be(2);
        result.Day.Should().Be(29);
    }

    [Test]
    public void GetDateForDateTime_NoCrossReference()
    {
        var result = _service.GetDateForDateTime(new DateTime(2024, 1, 1));
        result.CrossReference.Should().BeNullOrEmpty();
    }

    // ========== GetGregorianDateTime ==========

    [Test]
    public void GetGregorianDateTime_RoundTrip_IsIdentity()
    {
        var original = new DateTime(2024, 5, 20);
        var calDate = _service.GetDateForDateTime(original);
        var roundTrip = _service.GetGregorianDateTime(calDate);

        roundTrip.Date.Should().Be(original.Date);
    }

    // ========== GetDayStart / GetDayEnd ==========

    [Test]
    public void GetDayStart_ReturnsMidnight()
    {
        var dt = new DateTime(2024, 4, 10, 15, 30, 0);
        var start = _service.GetDayStart(dt);

        start.Should().Be(new DateTime(2024, 4, 10, 0, 0, 0));
    }

    [Test]
    public void GetDayEnd_IsJustBeforeMidnight()
    {
        var dt = new DateTime(2024, 4, 10, 8, 0, 0);
        var end = _service.GetDayEnd(dt);

        end.Should().Be(new DateTime(2024, 4, 10, 23, 59, 59, 999).AddTicks(9999));
    }

    [Test]
    public void GetDayStart_IsBeforeDayEnd()
    {
        var dt = new DateTime(2024, 7, 4);
        _service.GetDayStart(dt).Should().BeBefore(_service.GetDayEnd(dt));
    }

    // ========== GetFirstDayOfMonth / GetLastDayOfMonth ==========

    [Test]
    public void GetFirstDayOfMonth_IsDay1()
    {
        var first = _service.GetFirstDayOfMonth(2024, 3);
        first.Day.Should().Be(1);
        first.Month.Should().Be(3);
        first.Year.Should().Be(2024);
    }

    [Test]
    public void GetLastDayOfMonth_February_LeapYear_IsDay29()
    {
        var last = _service.GetLastDayOfMonth(2024, 2);
        last.Day.Should().Be(29);
    }

    [Test]
    public void GetLastDayOfMonth_February_NonLeapYear_IsDay28()
    {
        var last = _service.GetLastDayOfMonth(2023, 2);
        last.Day.Should().Be(28);
    }

    [Test]
    public void GetLastDayOfMonth_December_IsDay31()
    {
        var last = _service.GetLastDayOfMonth(2024, 12);
        last.Day.Should().Be(31);
    }

    [Test]
    public void GetLastDayOfMonth_April_IsDay30()
    {
        var last = _service.GetLastDayOfMonth(2024, 4);
        last.Day.Should().Be(30);
    }

    // ========== GetFirstDayOfYear ==========

    [Test]
    public void GetFirstDayOfYear_IsJanuaryFirst()
    {
        var first = _service.GetFirstDayOfYear(2024);
        first.Year.Should().Be(2024);
        first.Month.Should().Be(1);
        first.Day.Should().Be(1);
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
    public void GetMonthGrid_ContainsFirstOfMonth()
    {
        var grid = _service.GetMonthGrid(2024, 3).ToList();
        grid.Should().Contain(d => d.Year == 2024 && d.Month == 3 && d.Day == 1);
    }

    [Test]
    public void GetMonthGrid_ContainsLastOfMonth()
    {
        var grid = _service.GetMonthGrid(2024, 3).ToList();
        grid.Should().Contain(d => d.Year == 2024 && d.Month == 3 && d.Day == 31);
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

    [Test]
    public void GetMonthGrid_February2024_IsLeapYear_ContainsDay29()
    {
        var grid = _service.GetMonthGrid(2024, 2).ToList();
        grid.Should().Contain(d => d.Month == 2 && d.Day == 29);
    }

    // ========== GetWeekDays ==========

    [Test]
    public void GetWeekDays_Returns7Days()
    {
        var wednesday = _service.GetDateForDateTime(new DateTime(2024, 3, 13));
        var week = _service.GetWeekDays(wednesday).ToList();
        week.Should().HaveCount(7);
    }

    [Test]
    public void GetWeekDays_StartsOnSunday()
    {
        var wednesday = _service.GetDateForDateTime(new DateTime(2024, 3, 13));
        var week = _service.GetWeekDays(wednesday).ToList();
        week[0].GregorianEquivalent.DayOfWeek.Should().Be(DayOfWeek.Sunday);
    }

    [Test]
    public void GetWeekDays_EndsOnSaturday()
    {
        var wednesday = _service.GetDateForDateTime(new DateTime(2024, 3, 13));
        var week = _service.GetWeekDays(wednesday).ToList();
        week[6].GregorianEquivalent.DayOfWeek.Should().Be(DayOfWeek.Saturday);
    }

    [Test]
    public void GetWeekDays_ContainsInputDay()
    {
        var inputDate = new DateTime(2024, 3, 13); // Wednesday
        var calDate = _service.GetDateForDateTime(inputDate);
        var week = _service.GetWeekDays(calDate).ToList();
        week.Should().Contain(d => d.Day == 13 && d.Month == 3 && d.Year == 2024);
    }

    // ========== GetDaysInMonth ==========

    [Test]
    public void GetDaysInMonth_January_Returns31()
    {
        _service.GetDaysInMonth(2024, 1).Should().Be(31);
    }

    [Test]
    public void GetDaysInMonth_February_LeapYear_Returns29()
    {
        _service.GetDaysInMonth(2024, 2).Should().Be(29);
    }

    [Test]
    public void GetDaysInMonth_February_NonLeapYear_Returns28()
    {
        _service.GetDaysInMonth(2023, 2).Should().Be(28);
    }

    [Test]
    public void GetDaysInMonth_April_Returns30()
    {
        _service.GetDaysInMonth(2024, 4).Should().Be(30);
    }

    // ========== GetMonthsInYear ==========

    [Test]
    public void GetMonthsInYear_Always12()
    {
        _service.GetMonthsInYear(2024).Should().Be(12);
        _service.GetMonthsInYear(2000).Should().Be(12);
        _service.GetMonthsInYear(1900).Should().Be(12);
    }

    // ========== GetMonthName ==========

    [Test]
    public void GetMonthName_ReturnsCorrectNames()
    {
        _service.GetMonthName(1).Should().Be("January");
        _service.GetMonthName(2).Should().Be("February");
        _service.GetMonthName(3).Should().Be("March");
        _service.GetMonthName(4).Should().Be("April");
        _service.GetMonthName(5).Should().Be("May");
        _service.GetMonthName(6).Should().Be("June");
        _service.GetMonthName(7).Should().Be("July");
        _service.GetMonthName(8).Should().Be("August");
        _service.GetMonthName(9).Should().Be("September");
        _service.GetMonthName(10).Should().Be("October");
        _service.GetMonthName(11).Should().Be("November");
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
    public void GetCrossReferenceDisplay_ReturnsEmpty()
    {
        var date = _service.GetDateForDateTime(new DateTime(2024, 1, 1));
        _service.GetCrossReferenceDisplay(date).Should().BeEmpty();
    }
}
