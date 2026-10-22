namespace LibManagarr.Api.Models;

public sealed class TestConnectionRequest
{
    public ArrApplicationKind Kind { get; set; }

    public ArrInstanceSettings Instance { get; set; } = new();
}
