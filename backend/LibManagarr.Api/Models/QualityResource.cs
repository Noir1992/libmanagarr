namespace LibManagarr.Api.Models;

public sealed class QualityResource
{
    public QualityInnerResource? Quality { get; set; }
    public QualityRevisionResource? Revision { get; set; }
}

public sealed record QualityRevisionResource(int Version, int Real, bool IsRepack);
