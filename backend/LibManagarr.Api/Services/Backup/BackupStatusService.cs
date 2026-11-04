namespace LibManagarr.Api.Services.Backup;

/// <summary>
/// The phase a backup operation is in, as shown to the signed-in UI. Values are stable strings so
/// the frontend can translate them instead of reacting to wording.
/// </summary>
public enum BackupOperationPhase
{
    Idle,

    Exporting,

    Validating,

    PreparingRestore,

    RestartPending,

    /// <summary>A prepared import was taken over by this start; reported once to the settings UI.</summary>
    Restored,

    /// <summary>A prepared import failed its checks on the next start and the previous state is active again.</summary>
    RestoreRolledBack
}

/// <summary>
/// In-memory view of the current backup operation for the settings UI. It never carries archive
/// contents, passwords or file paths of the captured data, only the phase, a stable code and the
/// moment it changed, which is all the screen needs and nothing a caller could read secrets from.
/// </summary>
public sealed class BackupStatusService(TimeProvider clock)
{
    private readonly object sync = new();
    private BackupOperationPhase phase = BackupOperationPhase.Idle;
    private string? code;
    private string? operationId;
    private DateTimeOffset changedAt;

    public BackupOperationPhase Phase
    {
        get
        {
            lock (sync)
            {
                return phase;
            }
        }
    }

    public string? Code
    {
        get
        {
            lock (sync)
            {
                return code;
            }
        }
    }

    public DateTimeOffset ChangedAt
    {
        get
        {
            lock (sync)
            {
                return changedAt;
            }
        }
    }

    public void Update(BackupOperationPhase newPhase, string? errorCode = null, string? operationId = null)
    {
        lock (sync)
        {
            phase = newPhase;
            code = errorCode;
            this.operationId = operationId;
            changedAt = clock.GetUtcNow();
        }
    }

    public BackupStatusDto Snapshot()
    {
        lock (sync)
        {
            return new BackupStatusDto(phase.ToString(), code, changedAt, operationId);
        }
    }
}

public sealed record BackupStatusDto(string Phase, string? Code, DateTimeOffset ChangedAt, string? OperationId = null);
