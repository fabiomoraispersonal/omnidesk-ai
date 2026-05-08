using omniDesk.Api.Domain.Departments;
using omniDesk.Api.Features.Distribution;
using Xunit;

namespace omniDesk.Api.Tests.Features.Distribution;

public class BusinessHoursEvaluatorTests
{
    private static DepartmentBusinessHours WeekdaysNineToSix()
        => DepartmentBusinessHours.Create(new TimeOnly(9, 0), new TimeOnly(18, 0), new[] { 1, 2, 3, 4, 5 })!;

    [Fact]
    public void IsAvailable_NullHours_ReturnsTrue_24x7()
    {
        var anytime = new DateTimeOffset(2026, 5, 8, 23, 30, 0, TimeSpan.Zero);
        Assert.True(BusinessHoursEvaluator.IsAvailable(anytime, null));
    }

    [Theory]
    [InlineData(2026, 5, 8, 10, 0, true)]   // Friday 10:00 → in
    [InlineData(2026, 5, 8, 18, 0, false)]  // Friday 18:00 → end is exclusive
    [InlineData(2026, 5, 8, 8, 30, false)]  // Friday before opening
    [InlineData(2026, 5, 9, 12, 0, false)]  // Saturday → not in days
    public void IsAvailable_RespectsHoursAndDays(int y, int m, int d, int h, int min, bool expected)
    {
        var hours = WeekdaysNineToSix();
        var instant = new DateTimeOffset(y, m, d, h, min, 0, TimeSpan.Zero);
        Assert.Equal(expected, BusinessHoursEvaluator.IsAvailable(instant, hours));
    }

    [Fact]
    public void ElapsedBusinessMinutes_PausesOutsideWindow()
    {
        var hours = WeekdaysNineToSix();
        // Ticket created Friday 17:50 → SLA continues only inside Mon-Fri 09:00-18:00.
        var start = new DateTimeOffset(2026, 5, 8, 17, 50, 0, TimeSpan.Zero);
        var nextDayInside = new DateTimeOffset(2026, 5, 11, 9, 30, 0, TimeSpan.Zero); // Monday 09:30
        var minutes = BusinessHoursEvaluator.ElapsedBusinessMinutes(start, nextDayInside, hours);
        // 17:50–18:00 (10) + Mon 09:00–09:30 (30) = 40 minutes
        Assert.Equal(40, minutes);
    }

    [Fact]
    public void ElapsedBusinessMinutes_NullHours_ReturnsTotalElapsed()
    {
        var start = new DateTimeOffset(2026, 5, 8, 8, 0, 0, TimeSpan.Zero);
        var now = new DateTimeOffset(2026, 5, 8, 9, 30, 0, TimeSpan.Zero);
        Assert.Equal(90, BusinessHoursEvaluator.ElapsedBusinessMinutes(start, now, null));
    }

    [Fact]
    public void NextBusinessWindowStart_SkipsWeekend()
    {
        var hours = WeekdaysNineToSix();
        var fridayLate = new DateTimeOffset(2026, 5, 8, 19, 0, 0, TimeSpan.Zero);
        var next = BusinessHoursEvaluator.NextBusinessWindowStart(fridayLate, hours);
        Assert.Equal(new DateTimeOffset(2026, 5, 11, 9, 0, 0, TimeSpan.Zero), next);
    }

    [Fact]
    public void NextBusinessWindowStart_TodayIfBeforeOpening()
    {
        var hours = WeekdaysNineToSix();
        var earlyMonday = new DateTimeOffset(2026, 5, 11, 7, 0, 0, TimeSpan.Zero);
        var next = BusinessHoursEvaluator.NextBusinessWindowStart(earlyMonday, hours);
        Assert.Equal(new DateTimeOffset(2026, 5, 11, 9, 0, 0, TimeSpan.Zero), next);
    }

    [Fact]
    public void NextBusinessWindowStart_NullHours_ReturnsNull()
    {
        Assert.Null(BusinessHoursEvaluator.NextBusinessWindowStart(DateTimeOffset.UtcNow, null));
    }
}
