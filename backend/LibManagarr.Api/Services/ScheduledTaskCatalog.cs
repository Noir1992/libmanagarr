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
        services.AddSingleton(new ScheduledTaskDefinition("cleanup", true, false, () => new()
        {
            Enabled = true, ScheduleMode = ScheduleMode.DailyAtTime
        }));
        services.AddScoped<IScheduledTaskHandler, ArrDataSyncTaskHandler>();
        services.AddScoped<IScheduledTaskHandler, LogCleanupTaskHandler>();
        return services;
    }
}
