using LibManagarr.Api.Models;

namespace LibManagarr.Api.Services;

public sealed record ScheduledTaskDefinition(
    string TaskKey, bool ReadOnly, bool CanRunManually, Func<ScheduledTaskSettings> CreateDefaults);

public sealed class ScheduledTaskCatalog(IEnumerable<ScheduledTaskDefinition> definitions)
{
    public IReadOnlyList<ScheduledTaskDefinition> Definitions { get; } = definitions.ToArray();

    public ScheduledTaskDefinition? Find(string key) => Definitions.SingleOrDefault(task => task.TaskKey == key);

    public static IServiceCollection AddBuiltInScheduledTasks(IServiceCollection services)
    {
        // A fresh installation gets a working clock from the first start: the sync runs hourly and skips
        // itself with one info line while no instance is configured, so an empty list costs no failed runs.
        services.AddSingleton(new ScheduledTaskDefinition("arrDataSync", false, true, () => new()
        {
            Enabled = true, ScheduleMode = ScheduleMode.Interval, IntervalUnit = ScheduleIntervalUnit.Hour, IntervalValue = 1
        }));
        services.AddSingleton(new ScheduledTaskDefinition("cleanup", true, true, () => new()
        {
            Enabled = true, ScheduleMode = ScheduleMode.DailyAtTime
        }));
        // The health pass comes in switched on with the feature; the run itself stays quiet while the media
        // tree is unproven and skips with one info line instead of walking shares that are not there.
        // Three in the local morning is the offer the plan makes: quiet hours for a pass that walks the shares.
        services.AddSingleton(new ScheduledTaskDefinition("symlinkHealthCheck", false, true, () => new()
        {
            Enabled = true, ScheduleMode = ScheduleMode.DailyAtTime, TimeHour = 3, TimeMinute = 0
        }));
        services.AddScoped<IScheduledTaskHandler, ArrDataSyncTaskHandler>();
        services.AddScoped<IScheduledTaskHandler, LogCleanupTaskHandler>();
        services.AddScoped<IScheduledTaskHandler, SymlinkHealthCheckTaskHandler>();
        return services;
    }
}
