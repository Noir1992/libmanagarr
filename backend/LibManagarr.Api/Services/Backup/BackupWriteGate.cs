namespace LibManagarr.Api.Services.Backup;

/// <summary>
/// Serialisation gate for one storage owner that also announces its holder to the backup
/// coordinator. It keeps the WaitAsync and Release shape the stores already use, so taking part in
/// the writer coordination is one field instead of a change in every write method: the holder is
/// counted while it works, an export drains that count before it captures anything, and a restore
/// that waits for its restart refuses new work outright instead of queueing it behind a pause that
/// never lifts.
/// </summary>
public sealed class BackupWriteGate(BackupOperationCoordinator coordinator)
{
    private readonly SemaphoreSlim inner = new(1, 1);

    /// <summary>
    /// Scope of the holder. The inner semaphore lets one caller through at a time, so exactly the
    /// flow between WaitAsync and Release reads and clears this field. Write scopes stay inside
    /// that same flow, which is the only flow their per-flow counter can account for.
    /// </summary>
    private IWriteScope? held;

    public async Task WaitAsync(CancellationToken cancellationToken)
    {
        IWriteScope scope = await coordinator.EnterWriteAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await inner.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            scope.Dispose();
            throw;
        }

        held = scope;
    }

    public void Release()
    {
        inner.Release();
        IWriteScope? scope = held;
        held = null;
        scope?.Dispose();
    }
}
