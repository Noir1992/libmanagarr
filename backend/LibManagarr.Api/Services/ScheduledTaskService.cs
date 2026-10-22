using LibManagarr.Api.Models;

namespace LibManagarr.Api.Services;

public sealed class ScheduledTaskService(IScheduledTaskStore store, ScheduledTaskCatalog catalog, TimeProvider clock)
{
    public async Task<IReadOnlyList<ScheduledTask>> GetAllAsync(CancellationToken token)
    {
        IReadOnlyList<ScheduledTask> tasks = await store.GetAllAsync(token);
        return tasks.Where(task => catalog.Find(task.TaskKey) is not null).Select(ApplyCapabilities).ToArray();
    }

    public async Task<ScheduledTask> GetAsync(string key, CancellationToken token)
    {
        RequireDefinition(key);
        return ApplyCapabilities(await store.GetAsync(key, token) ?? throw new ScheduledTaskException(404, "Task not found."));
    }

    public ScheduledTaskDefinition RequireDefinition(string key) => catalog.Find(key)
        ?? throw new ScheduledTaskException(404, "Task not found.");

    public async Task<ScheduledTask> UpdateAsync(string key, ScheduledTaskUpdateRequest request, CancellationToken token)
    {
        if (RequireDefinition(key).ReadOnly) throw new ScheduledTaskException(403, "This task is read-only.");
        if (Validate(request).Count > 0) throw new ScheduledTaskException(400, "Invalid task configuration.");
        ScheduledTask task = await GetAsync(key, token);
        if (task.Revision != request.Revision) throw new ScheduledTaskException(409, "The task was changed. Reload it before saving.");
        task.Enabled = request.Enabled;
        task.ScheduleMode = request.ScheduleMode;
        task.IntervalUnit = request.IntervalUnit;
        task.IntervalValue = request.IntervalValue;
        task.TimeHour = request.TimeHour;
        task.TimeMinute = request.TimeMinute;
        task.EveryNDays = request.EveryNDays;
        task.NextRunAt = task.Enabled ? ScheduleCalculator.GetFutureRunAt(task, clock.GetUtcNow(), clock.LocalTimeZone) : null;
        if (!await store.UpdateConfigurationAsync(task, request.Revision, token))
            throw new ScheduledTaskException(409, "The task was changed. Reload it before saving.");
        // Return the exact revision written, rather than a possibly newer concurrent update.
        task.Revision++;
        return task;
    }

    public static Dictionary<string, string[]> Validate(ScheduledTaskUpdateRequest request)
    {
        Dictionary<string, string[]> errors = [];
        if (!Enum.IsDefined(request.ScheduleMode)) errors["scheduleMode"] = ["Unknown schedule mode."];
        if (!Enum.IsDefined(request.IntervalUnit)) errors["intervalUnit"] = ["Unknown interval unit."];
        if (request.IntervalValue is < 1 or > 1000) errors["intervalValue"] = ["Use an interval between 1 and 1000."];
        if (request.TimeHour is < 0 or > 23) errors["timeHour"] = ["Use an hour between 0 and 23."];
        if (request.TimeMinute is < 0 or > 59) errors["timeMinute"] = ["Use a minute between 0 and 59."];
        if (request.EveryNDays is < 1 or > 3650) errors["everyNDays"] = ["Use a day interval between 1 and 3650."];
        if (request.Revision < 0) errors["revision"] = ["Invalid revision."];
        return errors;
    }

    private ScheduledTask ApplyCapabilities(ScheduledTask task)
    {
        ScheduledTaskDefinition definition = RequireDefinition(task.TaskKey);
        task.ReadOnly = definition.ReadOnly;
        task.CanRunManually = definition.CanRunManually;
        return task;
    }
}

public sealed class ScheduledTaskException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}
