using LibManagarr.Api.Models;

namespace LibManagarr.Api.Services;

public static class ScheduleCalculator
{
    public static DateTimeOffset GetNextRunAt(ScheduledTaskSettings settings, DateTimeOffset now,
        TimeZoneInfo? timeZone = null)
    {
        DateTimeOffset anchor = settings.LastRunAt ?? now;
        DateTimeOffset candidate = Calculate(settings, anchor, timeZone ?? TimeZoneInfo.Local);
        return candidate < now ? now : candidate;
    }

    public static DateTimeOffset GetFutureRunAt(ScheduledTaskSettings settings, DateTimeOffset now,
        TimeZoneInfo? timeZone = null) => Calculate(settings, now, timeZone ?? TimeZoneInfo.Local);

    private static DateTimeOffset Calculate(ScheduledTaskSettings settings, DateTimeOffset anchor, TimeZoneInfo zone)
    {
        if (settings.ScheduleMode == ScheduleMode.Interval)
        {
            return settings.IntervalUnit switch
            {
                ScheduleIntervalUnit.Minute => anchor.AddMinutes(settings.IntervalValue),
                ScheduleIntervalUnit.Hour => anchor.AddHours(settings.IntervalValue),
                ScheduleIntervalUnit.Day => anchor.AddDays(settings.IntervalValue),
                ScheduleIntervalUnit.Week => anchor.AddDays(settings.IntervalValue * 7),
                ScheduleIntervalUnit.Month => anchor.AddMonths(settings.IntervalValue),
                ScheduleIntervalUnit.Year => anchor.AddYears(settings.IntervalValue),
                _ => throw new ArgumentOutOfRangeException(nameof(settings))
            };
        }

        DateTime local = TimeZoneInfo.ConvertTime(anchor, zone).DateTime;
        DateTime date = local.Date.AddHours(settings.TimeHour).AddMinutes(settings.TimeMinute);
        int days = settings.ScheduleMode == ScheduleMode.EveryNDaysAtTime ? settings.EveryNDays : 1;
        // Compare wall time too: the second occurrence of a repeated time is never another run.
        if (date <= local) date = date.AddDays(days);
        DateTimeOffset candidate = ToInstant(date, zone);
        if (candidate <= anchor) candidate = ToInstant(date.AddDays(days), zone);
        return candidate;
    }

    private static DateTimeOffset ToInstant(DateTime date, TimeZoneInfo zone)
    {
        date = DateTime.SpecifyKind(date, DateTimeKind.Unspecified);
        while (zone.IsInvalidTime(date)) date = date.AddMinutes(1);
        TimeSpan offset = zone.IsAmbiguousTime(date)
            ? zone.GetAmbiguousTimeOffsets(date).Max()
            : zone.GetUtcOffset(date);
        return new DateTimeOffset(date, offset).ToUniversalTime();
    }
}
