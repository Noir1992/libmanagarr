namespace LibManagarr.Api.Services.Backup;

/// <summary>
/// Serialises backup work against the rest of the application. Ordinary writers hold a short
/// write scope; an export or a restore drains those scopes and then keeps the pause, so every
/// captured file comes from one point in time instead of a torn mix of concurrent writes.
/// Write scopes are reentrant per async flow, which stops a settings save that also writes
/// through the task store from waiting on itself behind a queued pause.
/// </summary>
public sealed class BackupOperationCoordinator(TimeProvider clock)
{
    /// <summary>
    /// Per flow counter of the write scopes in use. The depth lives inside a cell object because
    /// an async local written after a suspension is not seen by the calling frame, while mutating
    /// an object that was placed into the flow before the first await is.
    /// </summary>
    private static readonly AsyncLocal<WriteDepthCell?> WriteDepth = new();

    private readonly SemaphoreSlim operationGate = new(1, 1);
    private readonly object sync = new();

    private TaskCompletionSource<bool> resumeSignal = NewResumeSignal();
    private int activeWriters;
    private bool paused;

    /// <summary>Set while a restore is staged and the application is waiting to restart.</summary>
    public bool IsInMaintenanceMode
    {
        get
        {
            lock (sync)
            {
                return paused && maintenanceMode;
            }
        }
    }

    private bool maintenanceMode;

    public async Task<IWriteScope> EnterWriteAsync(CancellationToken cancellationToken)
    {
        WriteDepthCell? existing = WriteDepth.Value;
        if (existing is not null && existing.Depth > 0)
        {
            existing.Depth++;
            return NestedScope.Instance;
        }

        WriteDepthCell cell = new() { Depth = 1 };
        WriteDepth.Value = cell;
        try
        {
            await AwaitTurnAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Reachable through the cell, so the caller sees the cancelled attempt even though
            // this frame already suspended.
            cell.Depth = 0;
            throw;
        }

        return new WriteScope(this, cell);
    }

    /// <summary>
    /// Takes the single backup operation slot and pauses new writers until the lease is released.
    /// In-flight writers get <paramref name="drainTimeout"/> to finish; a pause that cannot drain
    /// is aborted with the writers running again, so an export never reads a half written state
    /// and a takeover never starts behind one.
    /// </summary>
    public async Task<IOperationLease> PauseWritersAsync(TimeSpan drainTimeout, bool keepPausedAfterRelease, CancellationToken cancellationToken)
    {
        if (!await operationGate.WaitAsync(TimeSpan.Zero, cancellationToken))
        {
            throw new BackupException(BackupErrorCodes.OperationInProgress, "Another backup operation is already running.")
            {
                StatusCode = StatusCodes.Status409Conflict
            };
        }

        lock (sync)
        {
            paused = true;
            if (keepPausedAfterRelease)
            {
                maintenanceMode = true;
            }

            resumeSignal = NewResumeSignal();
        }

        DateTimeOffset deadline = clock.GetUtcNow() + drainTimeout;
        while (true)
        {
            int inFlight;
            lock (sync)
            {
                inFlight = activeWriters;
            }

            if (inFlight == 0)
            {
                return new OperationLease(this, keepPausedAfterRelease);
            }

            if (clock.GetUtcNow() >= deadline)
            {
                ResumeWriters(keepPausedAfterRelease);
                throw new BackupException(BackupErrorCodes.WritersNotPaused,
                    $"Running operations did not finish within {(int)drainTimeout.TotalSeconds} seconds, so nothing was captured.")
                {
                    StatusCode = StatusCodes.Status503ServiceUnavailable
                };
            }

            await Task.Delay(TimeSpan.FromMilliseconds(25), clock, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Waits until an ordinary writer may proceed. A restore never lifts its pause, so waiting
    /// there would hang the request until it times out; it answers immediately instead and says
    /// the installation is waiting for the restart that finishes the import.
    /// </summary>
    private async Task AwaitTurnAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            Task resume;
            lock (sync)
            {
                if (maintenanceMode)
                {
                    throw new BackupException(BackupErrorCodes.RestorePending,
                        "A restore is waiting to be applied, so no further changes are accepted.")
                    {
                        StatusCode = StatusCodes.Status503ServiceUnavailable
                    };
                }

                if (!paused)
                {
                    activeWriters++;
                    return;
                }

                resume = resumeSignal.Task;
            }

            await resume.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static TaskCompletionSource<bool> NewResumeSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private void ExitWriteScope(WriteDepthCell cell)
    {
        cell.Depth--;
        lock (sync)
        {
            activeWriters--;
        }
    }

    /// <summary>
    /// Hands the operation slot back and, unless the pause exists to hold the application in
    /// maintenance mode until a restart, lets writers run again. The same path undoes a drain
    /// that timed out, so a failed pause never leaves the application frozen.
    /// </summary>
    private void ResumeWriters(bool keepPausedAfterRelease)
    {
        lock (sync)
        {
            if (!keepPausedAfterRelease)
            {
                paused = false;
                maintenanceMode = false;
                resumeSignal.SetResult(true);
            }
        }

        operationGate.Release();
    }

    private sealed class WriteDepthCell
    {
        public int Depth;
    }

    private sealed class WriteScope(BackupOperationCoordinator owner, WriteDepthCell cell) : IWriteScope
    {
        private int disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0)
            {
                return;
            }

            if (cell.Depth == 1)
            {
                WriteDepth.Value = null;
            }

            owner.ExitWriteScope(cell);
        }
    }

    private sealed class NestedScope : IWriteScope
    {
        public static readonly NestedScope Instance = new();

        public void Dispose()
        {
            WriteDepthCell? cell = WriteDepth.Value;
            if (cell is not null && cell.Depth > 0)
            {
                cell.Depth--;
            }
        }
    }

    private sealed class OperationLease(BackupOperationCoordinator owner, bool keepPausedAfterRelease) : IOperationLease
    {
        private int disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0)
            {
                return;
            }

            owner.ResumeWriters(keepPausedAfterRelease);
        }
    }
}

/// <summary>Short scope held by any request or task that persists state.</summary>
public interface IWriteScope : IDisposable
{
}

/// <summary>Held for the duration of one export or restore preparation while writers stay paused.</summary>
public interface IOperationLease : IDisposable
{
}
