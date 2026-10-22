namespace LibManagarr.Api.Models;

public sealed class SettingsInstancesResponse
{
    public List<ArrInstanceSettings> SonarrInstances { get; set; } = [];

    public List<ArrInstanceSettings> RadarrInstances { get; set; } = [];
}
