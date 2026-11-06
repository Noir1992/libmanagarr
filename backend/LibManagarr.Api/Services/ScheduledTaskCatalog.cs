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
        services.AddSingleton(new ScheduledTaskDefinition("arrDataSync", false, true, () => new()));
        services.AddSingleton(new ScheduledTaskDefinition("cleanup", true, true, () => new()
        {
            Enabled = true, ScheduleMode = ScheduleMode.DailyAtTime
        }));
        // Nobody asked for a health pass before the feature existed, so it comes in switched off and the
        // operator turns it on once the media tree is set up. Three in the local morning is the offer the
        // plan makes: quiet hours for a pass that walks the shares.
        services.AddSingleton(new ScheduledTaskDefinition("symlinkHealthCheck", false, true, () => new()
        {
            Enabled = false, ScheduleMode = ScheduleMode.DailyAtTime, TimeHour = 3, TimeMinute = 0
        }));
        services.AddScoped<IScheduledTaskHandler, ArrDataSyncTaskHandler>();
        services.AddScoped<IScheduledTaskHandler, LogCleanupTaskHandler>();
        services.AddScoped<IScheduledTaskHandler, SymlinkHealthCheckTaskHandler>();
        return services;
    }
}
