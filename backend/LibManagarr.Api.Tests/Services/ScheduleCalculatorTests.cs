using LibManagarr.Api.Models;
using LibManagarr.Api.Services;

namespace LibManagarr.Api.Tests.Services;

public sealed class ScheduleCalculatorTests
{
    [Theory]
    [InlineData("2026-03-29T00:00:00Z", "2026-03-29T01:00:00Z")]
    [InlineData("2026-10-25T00:00:00Z", "2026-10-25T00:30:00Z")]
    [InlineData("2026-10-25T00:30:00Z", "2026-10-26T01:30:00Z")]
    [InlineData("2026-10-25T01:10:00Z", "2026-10-26T01:30:00Z")]
    public void CalendarSchedules_HandleSkippedAndRepeatedLocalTimes(string instant, string expected)
    {
        TimeZoneInfo berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");
        ScheduledTaskSettings task = new() { ScheduleMode = ScheduleMode.DailyAtTime, TimeHour = 2, TimeMinute = 30 };
        Assert.Equal(DateTimeOffset.Parse(expected), ScheduleCalculator.GetFutureRunAt(task, DateTimeOffset.Parse(instant), berlin));
    }

    [Fact]
    public void MultiDaySchedule_UsesLocalCalendarAcrossDst()
    {
        ScheduledTaskSettings task = new() { ScheduleMode = ScheduleMode.EveryNDaysAtTime, TimeHour = 10, EveryNDays = 3 };
        Assert.Equal(DateTimeOffset.Parse("2026-03-30T08:00:00Z"), ScheduleCalculator.GetFutureRunAt(task,
            DateTimeOffset.Parse("2026-03-27T12:00:00Z"), TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin")));
    }

    [Theory]
    [InlineData(ScheduleIntervalUnit.Minute, "2026-01-31T12:01:00Z")]
    [InlineData(ScheduleIntervalUnit.Hour, "2026-01-31T13:00:00Z")]
    [InlineData(ScheduleIntervalUnit.Day, "2026-02-01T12:00:00Z")]
    [InlineData(ScheduleIntervalUnit.Week, "2026-02-07T12:00:00Z")]
    [InlineData(ScheduleIntervalUnit.Month, "2026-02-28T12:00:00Z")]
    [InlineData(ScheduleIntervalUnit.Year, "2027-01-31T12:00:00Z")]
    public void IntervalUnits_HaveStableFutureDueTimes(ScheduleIntervalUnit unit, string expected)
    {
        ScheduledTaskSettings task = new() { IntervalUnit = unit };
        Assert.Equal(DateTimeOffset.Parse(expected), ScheduleCalculator.GetFutureRunAt(task, DateTimeOffset.Parse("2026-01-31T12:00:00Z")));
    }

    [Fact]
    public void IsDue_ShouldReturnTrue_WhenIntervalRunIsOverdue()
    {
        DateTimeOffset now = new(2026, 5, 25, 10, 30, 0, TimeSpan.Zero);
        ScheduledTaskSettings settings = new()
        {
            Enabled = true,
            ScheduleMode = ScheduleMode.Interval,
            IntervalUnit = ScheduleIntervalUnit.Hour,
            IntervalValue = 1,
            LastRunAt = new DateTimeOffset(2026, 5, 25, 8, 0, 0, TimeSpan.Zero)
        };

        Assert.True(ScheduleCalculator.GetNextRunAt(settings, now, TimeZoneInfo.Utc) <= now);
        Assert.Equal(now, ScheduleCalculator.GetNextRunAt(settings, now, TimeZoneInfo.Utc));
    }

    [Fact]
    public void GetNextRunAt_ShouldReturnNextDailyRun_WhenPreviousRunAlreadyHappened()
    {
        DateTimeOffset now = new(2026, 5, 25, 11, 0, 0, TimeSpan.Zero);
        ScheduledTaskSettings settings = new()
        {
            Enabled = true,
            ScheduleMode = ScheduleMode.DailyAtTime,
            TimeHour = 10,
            TimeMinute = 15,
            LastRunAt = new DateTimeOffset(2026, 5, 25, 10, 15, 0, TimeSpan.Zero)
        };

        DateTimeOffset expectedNextRunAt = new(2026, 5, 26, 10, 15, 0, TimeSpan.Zero);

        Assert.True(ScheduleCalculator.GetNextRunAt(settings, now, TimeZoneInfo.Utc) > now);
        Assert.Equal(expectedNextRunAt, ScheduleCalculator.GetNextRunAt(settings, now, TimeZoneInfo.Utc));
    }

    [Fact]
    public void GetNextRunAt_ShouldReturnNow_WhenEveryNDaysRunIsOverdue()
    {
        DateTimeOffset now = new(2026, 5, 30, 9, 0, 0, TimeSpan.Zero);
        ScheduledTaskSettings settings = new()
        {
            Enabled = true,
            ScheduleMode = ScheduleMode.EveryNDaysAtTime,
            EveryNDays = 3,
            TimeHour = 8,
            TimeMinute = 0,
            LastRunAt = new DateTimeOffset(2026, 5, 26, 8, 0, 0, TimeSpan.Zero)
        };

        Assert.True(ScheduleCalculator.GetNextRunAt(settings, now, TimeZoneInfo.Utc) <= now);
        Assert.Equal(now, ScheduleCalculator.GetNextRunAt(settings, now, TimeZoneInfo.Utc));
    }
}
