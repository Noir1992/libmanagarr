namespace LibManagarr.Api.Models;

/// <summary>
/// Common identity of a fetched instance snapshot, used to index persisted snapshots per instance.
/// </summary>
public interface IInstanceSnapshot
{
    string InstanceId { get; }

    string InstanceName { get; }

    DateTimeOffset SyncedAt { get; }
}
