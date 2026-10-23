namespace LibManagarr.Api.Models;

public sealed class InstancesSettingsDto
{
    public List<ArrInstanceSettings> SonarrInstances { get; set; } = [];

    public List<ArrInstanceSettings> RadarrInstances { get; set; } = [];
}
