using System.Text.Json.Serialization;

namespace LibManagarr.Api.Models;

public sealed class ScheduledTask : ScheduledTaskSettings
{
    public string TaskKey { get; set; } = "";
    public long Revision { get; set; }
    public bool CanRunManually { get; set; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class ScheduledTaskUpdateRequest
{
    public required long Revision { get; init; }
    public required bool Enabled { get; init; }
    public required ScheduleMode ScheduleMode { get; init; }
    public required ScheduleIntervalUnit IntervalUnit { get; init; }
    public required int IntervalValue { get; init; }
    public required int TimeHour { get; init; }
    public required int TimeMinute { get; init; }
    public required int EveryNDays { get; init; }
}
