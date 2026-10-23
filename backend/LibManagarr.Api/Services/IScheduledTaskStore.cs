using LibManagarr.Api.Models;

namespace LibManagarr.Api.Services;

public interface IScheduledTaskStore
{
    Task InitializeAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<ScheduledTask>> GetAllAsync(CancellationToken cancellationToken);
    Task<ScheduledTask?> GetAsync(string taskKey, CancellationToken cancellationToken);
    Task<bool> UpdateConfigurationAsync(ScheduledTask task, long expectedRevision, CancellationToken cancellationToken);
    Task<bool> UpdateRunAsync(string taskKey, long expectedRevision, DateTimeOffset? lastRunAt,
        DateTimeOffset? nextRunAt, CancellationToken cancellationToken);
}
