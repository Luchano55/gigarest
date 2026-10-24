using GigaRest.Core.Settings;

namespace GigaRest.Core.Tests;

public class ReturnScheduleTests
{
    private static readonly ReturnSchedule Weekdays9 = new() { Enabled = true, Time = "09:00" };

    [Fact]
    public void NextIsTomorrowMorningOnWeekday()
    {
        // Пятница, 23 октября 2026, вечер. Следующее — понедельник 26-го, 9:00.
        var friday = new DateTime(2026, 10, 23, 22, 0, 0);
        Assert.Equal(DayOfWeek.Friday, friday.DayOfWeek);
        Assert.Equal(new DateTime(2026, 10, 26, 9, 0, 0), Weekdays9.NextAfter(friday));
        Assert.Equal(new DateTime(2026, 10, 23, 9, 0, 0), Weekdays9.NextAfter(new DateTime(2026, 10, 23, 8, 59, 0)));
    }

    [Fact]
    public void DueWhenCrossed()
    {
        var monday = new DateTime(2026, 10, 26, 8, 59, 30);
        Assert.False(Weekdays9.IsDue(monday, monday.AddSeconds(20)));
        Assert.True(Weekdays9.IsDue(monday, monday.AddSeconds(40)));
        // Уже предложили — следующая проверка не срабатывает снова.
        Assert.False(Weekdays9.IsDue(monday.AddSeconds(40), monday.AddSeconds(70)));
    }

    [Fact]
    public void AfterSleepOfferedIfNotTooLate()
    {
        var sunday = new DateTime(2026, 10, 25, 23, 0, 0);
        Assert.True(Weekdays9.IsDue(sunday, new DateTime(2026, 10, 26, 10, 30, 0)));   // проснулся в 10:30
        Assert.False(Weekdays9.IsDue(sunday, new DateTime(2026, 10, 26, 22, 0, 0)));   // вечером уже не предлагаем
    }

    [Fact]
    public void DisabledOrNoDaysNeverDue()
    {
        var off = new ReturnSchedule { Enabled = false };
        Assert.Null(off.NextAfter(DateTime.Now));
        var noDays = new ReturnSchedule { Enabled = true, Days = [] };
        Assert.False(noDays.IsDue(new DateTime(2026, 10, 26, 8, 0, 0), new DateTime(2026, 10, 26, 10, 0, 0)));
    }

    [Theory]
    [InlineData("08:30", "по будням в 08:30")]
    [InlineData("ерунда", "по будням в 09:00")]
    [InlineData("25:00", "по будням в 09:00")]
    public void DescribesInRussian(string time, string expected) =>
        Assert.Equal(expected, new ReturnSchedule { Enabled = true, Time = time }.Describe());

    [Fact]
    public void DescribesCustomDays()
    {
        Assert.Equal("пн, ср в 10:00", new ReturnSchedule { Enabled = true, Time = "10:00", Days = [DayOfWeek.Wednesday, DayOfWeek.Monday] }.Describe());
        Assert.Equal("каждый день в 09:00", new ReturnSchedule { Enabled = true, Days = [.. ReturnSchedule.Order] }.Describe());
        Assert.Equal("выключено", new ReturnSchedule().Describe());
    }
}
