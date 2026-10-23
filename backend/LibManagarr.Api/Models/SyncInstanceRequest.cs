namespace LibManagarr.Api.Models;

public sealed class SyncInstanceRequest
{
    public ArrApplicationKind Kind { get; set; }

    public string InstanceId { get; set; } = string.Empty;
}
