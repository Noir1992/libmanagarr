namespace LibManagarr.Api.Models;

public sealed class SyncInstanceResult
{
    public string Message { get; set; } = string.Empty;

    public DateTimeOffset? SyncedAt { get; set; }

    public int ItemCount { get; set; }
}
