namespace LibManagarr.Api.Models;

public class ScheduledTaskSettings
{
    public bool Enabled { get; set; }

    public bool ReadOnly { get; set; }

    public ScheduleMode ScheduleMode { get; set; } = ScheduleMode.Interval;

    public ScheduleIntervalUnit IntervalUnit { get; set; } = ScheduleIntervalUnit.Hour;

    public int IntervalValue { get; set; } = 1;

    public int TimeHour { get; set; }

    public int TimeMinute { get; set; }

    public int EveryNDays { get; set; } = 1;

    public DateTimeOffset? LastRunAt { get; set; }

    public DateTimeOffset? NextRunAt { get; set; }
}
