using LibManagarr.Api.Models;

namespace LibManagarr.Api.Services;

public interface ISettingsService
{
    Task<AppSettings> GetAsync(CancellationToken cancellationToken);

    // The published snapshot is shared and must only be read. Use GetAsync for edits.
    Task<AppSettings> GetSnapshotAsync(CancellationToken cancellationToken) => GetAsync(cancellationToken);

    Task<IDisposable> BeginUpdateAsync(CancellationToken cancellationToken) =>
        SettingsUpdateCoordinator.EnterAsync(this, cancellationToken);

    async Task<AppSettings> UpdateAsync(Action<AppSettings> update, CancellationToken cancellationToken)
    {
        using IDisposable lease = await BeginUpdateAsync(cancellationToken);
        AppSettings settings = await GetAsync(cancellationToken);
        update(settings);
        return await SaveAsync(settings, cancellationToken);
    }

    Task<AppSettings> SaveAsync(AppSettings settings, CancellationToken cancellationToken);
}
